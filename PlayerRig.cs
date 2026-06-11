using Candide;                                                  // Globals (GraphicsDevice)
using Candide.Database;                                         // SpacDataBase
using Candide.Entities.Animations;                              // PlayerSpacState
using Candide.Graphics;                                         // DeferredRenderer
using CandideCreator.Shared.Graphics;                           // SpriteSheet
using CandideCreator.Shared.Models.SpritesAnimationCollections; // Spac
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;                         // Texture2D, SurfaceFormat
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;                          // ConditionalWeakTable

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Player BODY rig repaint (pack.playerSkins.rig) — the sibling of RigSwap (creatures) and NpcBodies (satyr
// NPCs), for the player character itself: 1:1 PNG repaints of the player's body animation sheets, plus an
// optional [r,g,b] tint baked over them. Same frame layout required; only textures change.
//
// HOW THE PLAYER BODY RENDERS (all verified in rdll):
//   - PlayerAnimationController.EntityInitialize builds an AnimationStateMachine from
//     SpacDataBase.GetSpacOrNull("player_new_animations") with 47 SpacAnimationArguments; each state is a
//     PlayerSpacState holding "[Unmanaged] public readonly Spac Spac" (the same shared spac instance).
//   - Every frame PlayerAnimationController.BeforeRender registers the CURRENT state:
//       "DeferredRenderer.RegisterPlayerSpac(Entity, playerSpac, Skins, args);"   (the ONLY call site)
//     and DeferredRenderer.DrawEntitySpacs draws it THROUGH THE STATE'S Spac FIELD:
//       "DrawSpacFrame(effect, playerSpacState.Spac, currentFrame, in args, ...)"
//     where body sprites sample "spac.SpriteSheets[sprite.SpriteSheetIndex]". Worn skins (hair, clothes,
//     held items) draw from their own SpacSkin sheets in the same loop — a body-sheet swap never touches
//     them, and PlayerSkinsFeature keeps owning that layer.
//   - So, exactly like RigSwap: prefix RegisterPlayerSpac and replace the state's readonly Spac with a
//     repainted clone built via Spac.GetCopyWithOtherSpriteSheets (SpritesAnimations shared by reference —
//     all 47 states' timing/frames keep working, only SpriteSheets[] differ). Each PlayerSpacState is
//     swapped the first time it renders; afterwards the prefix is a single reference-equality no-op.
//
// CITIZENS ARE UNTOUCHED even though they animate from the SAME "player_new_animations" spac
// (rdll CitizenController: "public static Spac Spac => SpacDataBase.GetSpacOrNull(\"player_new_animations\")"):
// citizens run an EntitySpacAnimationStateMachine registered via RegisterEntitySpac — a different hook, a
// different state type — and we never modify the base spac itself, only which spac the PLAYER states point
// at. For the same reason UI previews (character creation / character select render via CandideSpacDisplay,
// not RegisterPlayerSpac) keep vanilla body art — in-world rendering only.
//
// TINT: there is no cheap render-time tint path for the player body. Creatures tint via General[0].Color →
// Args.UseEntityColors, but RegisterPlayerSpac never applies entity colors, and args.TintColor would tint
// the WHOLE draw (clothing, held items) and is already multiplied by the light-source code in BeforeRender.
// So `tint` is baked at build time instead: a one-time per-pixel multiply over the body sheets (the same
// GetData/SetData uint ops PlayerSkinsFeature.SeedRawImage uses). With `tint` but no `sheets`, the VANILLA
// textures are copied + tinted (guarded: only SurfaceFormat.Color textures are CPU-readable; anything else
// warns and stays untinted). Zero per-frame cost either way.
//
// TINT MUST ALSO COVER THE WORN SKIN LAYERS — on a normally-dressed character the base body sheets are
// NEVER DRAWN: every visible pixel (skin tone included — it is a palette-swapped "body" skin slice) comes
// from SpacSkin layers, and DrawSpacFrame HIDES the base sprite whenever a skin with HideBase covers its
// tag ("if (item.HideBase) { flag2 = true; }" → base skipped; rdll DrawSpacFrame). So baking tint into
// spac.SpriteSheets alone is invisible under clothes. Each skin sprite, however, multiplies its OWN
// per-layer color: "DrawSpacSprite(..., item2.Color, ...)" — and PlayerSkinSliceCollection.SetSkins builds
// those SpacSkin structs FRESH PER PLAYER (new per-tag List<SpacSkin> on every rebuild, sheets shared by
// reference but Color per instance). So the prefix also multiplies `tint` into every SpacSkin.Color of the
// player's SpacSkins — safe (never touches the shared sheet textures), idempotent per rebuilt list (tracked
// via ConditionalWeakTable so equipment changes re-tint exactly once), and zero-cost once processed.
//
// DISCOVERY: the player spac's sheet names/count aren't documented anywhere — the first build logs every
// base sheet's name + texture size + frame size (same convention as RigSwap/NpcBodies), so authors configure
// a placeholder rig, read the log, then extract & repaint the real sheets.
//
// SCOPE v1: ONE rig globally (last pack wins, conflict warned) for ALL player entities this client renders —
// local AND remote players. Per-character selection is future work. Co-op peers without the pack just render
// vanilla bodies (nothing is serialized; purely a client-side render swap).
// ---------------------------------------------------------------------------------------------------------

internal static class PlayerRigFeature
{
    // The active rig (one globally; see header). Filled by ContentLoader.BuildPack at parse time.
    private static Dictionary<string, string> _pngBySheet;   // vanilla sheet basename -> abs png path
    private static int[] _tint;                               // [r,g,b] 0-255, baked into the sheets at build
    private static string _pack;                              // pack file name (for logs)

    private static Spac _clone;        // built rig spac (shares SpritesAnimations with the base)
    private static Spac _builtFrom;    // the base instance the clone was built from (DB reload => new base => rebuild)
    private static Spac _failedFrom;   // base we failed to build from (cached so we never retry per frame)

    private static readonly Dictionary<string, Texture2D> _textureCache = new();   // cache key -> final (tinted) texture
    private static readonly HashSet<string> _warnedOnce = new();

    // Worn-skin lists already tinted. SetSkins REPLACES each per-tag List<SpacSkin> wholesale on every skin
    // rebuild (equipment change), so "list seen" == "list tinted"; dead lists fall out of the table on GC.
    private static readonly ConditionalWeakTable<List<SpacSkin>, object> _tintedSkinLists = new();
    private static readonly object TintedMark = new();
    private static bool _loggedSkinTint;

    // PlayerSpacState.Spac is "[Unmanaged] public readonly Spac Spac" — FieldRef writes through readonly
    // safely (the same trick RigSwap uses on AbstractSpacAnimationStateMachine.Spac).
    private static readonly AccessTools.FieldRef<PlayerSpacState, Spac> SpacRef =
        AccessTools.FieldRefAccess<PlayerSpacState, Spac>("Spac");

    // Called from ContentLoader.BuildPack when a pack carries playerSkins.rig. One rig globally: the last
    // pack to register wins (warned), matching how single-slot pack features behave elsewhere.
    public static void Register(SkinRigDef def, string packDir, string pack)
    {
        try
        {
            if (def == null) return;
            bool hasSheets = def.Sheets != null && def.Sheets.Count > 0;
            bool hasTint = def.Tint != null && def.Tint.Length >= 3;
            if (!hasSheets && !hasTint)
            { Plugin.Log.LogWarning($"[{pack}] playerSkins.rig skipped (needs `sheets` and/or `tint`)."); return; }

            if (_pngBySheet != null || _tint != null)
                Plugin.Log.LogWarning($"[{pack}] playerSkins.rig: a player rig from '{_pack}' is already active — replacing it (one rig globally).");

            Dictionary<string, string> map = null;
            if (hasSheets)
            {
                map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in def.Sheets)
                    if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        map[kv.Key] = Path.Combine(packDir, kv.Value);
                if (map.Count == 0) map = null;
            }
            _pngBySheet = map;
            _tint = hasTint ? def.Tint : null;
            _pack = pack;
            _clone = null; _builtFrom = null; _failedFrom = null;   // a new spec invalidates any previous build
            _tintedSkinLists.Clear();                               // worn-skin layers re-tint under the new spec
            Plugin.Log.LogInfo($"Content Forge: [{pack}] player body rig registered "
                + $"({(map != null ? map.Count + " sheet(s)" : "no sheets")}{(hasTint ? ", tinted" : "")}).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[{pack}] playerSkins.rig setup failed: {e.Message}"); }
    }

    // Render-time swap point, called by PlayerRigPatch for every player entity every frame. After the first
    // successful swap of a given PlayerSpacState this is two reference checks plus (with `tint`) a handful
    // of already-tinted skin-list lookups — no allocation.
    internal static void OnRegisterPlayerSpac(PlayerSpacState playerSpac, SpacSkins skins)
    {
        if (_pngBySheet == null && _tint == null) return;
        if (_tint != null && skins != null) TintSkins(skins);   // worn layers (see header: clothes hide the base)
        if (playerSpac == null) return;
        Spac current = playerSpac.Spac;
        if (current == null || ReferenceEquals(current, _clone) || ReferenceEquals(current, _failedFrom)) return;
        Spac swapped = ReferenceEquals(current, _builtFrom) ? _clone : GetOrBuild(current);
        if (!ReferenceEquals(current, swapped)) SpacRef(playerSpac) = swapped;
    }

    // Multiply the tint into every SpacSkin.Color of this player's skins (hair, clothes, held items — every
    // visible pixel of a dressed character). SpacSkin is a struct: write the modified copy back into the
    // list. Each rebuilt list is processed exactly once (_tintedSkinLists), so the multiply never compounds.
    private static void TintSkins(SpacSkins skins)
    {
        float r = Clamp255(_tint[0]) / 255f, g = Clamp255(_tint[1]) / 255f, b = Clamp255(_tint[2]) / 255f;
        foreach (var kv in skins.Skins)
        {
            // Held ITEMS are skin layers too (tag 8 = main-hand, 11 = offhand) — a body tint must not
            // recolor the player's sword/shield (it masked the palette tests). Clothes/hair/body only.
            if (kv.Key == 8 || kv.Key == 11) continue;
            List<SpacSkin> list = kv.Value;
            if (list == null || _tintedSkinLists.TryGetValue(list, out _)) continue;
            _tintedSkinLists.Add(list, TintedMark);
            for (int i = 0; i < list.Count; i++)
            {
                SpacSkin s = list[i];
                s.Color = new Microsoft.Xna.Framework.Vector4(s.Color.X * r, s.Color.Y * g, s.Color.Z * b, s.Color.W);
                list[i] = s;
            }
            if (!_loggedSkinTint)
            {
                _loggedSkinTint = true;
                Plugin.Log.LogInfo($"Content Forge: [{_pack}] player rig tint also applied to worn skin layers "
                    + "(clothes/hair cover the base body; held items are excluded).");
            }
        }
    }

    // Build (once; rebuilt only if a DB reload re-creates the base spac instance) the repainted player spac.
    // Mirrors RigSwap.GetOrBuild: clone the LIVE spac the controller picked, swap/tint the matching sheets,
    // keep the base's normal/metal maps + frame sizes, register the clone in SpacDataBase.SpacMap.
    private static Spac GetOrBuild(Spac baseSpac)
    {
        if (Globals.GraphicsDevice == null) return baseSpac;   // too early; retry next frame (don't cache)

        try
        {
            SpriteSheet[] bs = baseSpac.SpriteSheets;
            if (bs == null || bs.Length == 0) { _failedFrom = baseSpac; return baseSpac; }

            // Tell authors exactly what to repaint (sheet names, texture + frame sizes) — discovery log.
            Plugin.Log.LogInfo($"Content Forge: [{_pack}] building player rig from spac '{baseSpac.Name}' — base sheets: "
                + string.Join(", ", BaseSheetSummaries(bs)));

            var sheets = (SpriteSheet[])bs.Clone();
            var unmatched = _pngBySheet != null
                ? new HashSet<string>(_pngBySheet.Keys, StringComparer.OrdinalIgnoreCase) : null;
            int replaced = 0;
            for (int i = 0; i < sheets.Length; i++)
            {
                Texture2D baseTex = bs[i].Texture;
                Texture2D tex = null;

                string png = ResolvePng(baseTex, out string matchedKey);
                if (png != null)
                {
                    unmatched.Remove(matchedKey);
                    if (!File.Exists(png)) { WarnOnce(png, $"[{_pack}] player rig: sheet file not found: {png}"); }
                    else
                    {
                        tex = LoadTexture(png);   // author PNGs are always CPU-readable -> tint bakes in-place
                        if (baseTex != null && (tex.Width != baseTex.Width || tex.Height != baseTex.Height))
                            WarnOnce("dim" + i, $"[{_pack}] player rig: '{Path.GetFileName(png)}' is {tex.Width}x{tex.Height} but base sheet "
                                + $"'{baseTex.Name}' is {baseTex.Width}x{baseTex.Height} — frames will sample the wrong cells (visual garbage). "
                                + "Repaint the vanilla sheet 1:1 (same size, same grid).");
                    }
                }
                else if (_tint != null && baseTex != null)
                    tex = TintedCopy(baseTex);   // tint-only (or tint over unswapped sheets); null = unreadable

                if (tex == null) continue;
                // Keep the base's normal/metal maps, frame size AND offset: a 1:1 repaint stays lit + aligned.
                sheets[i] = new SpriteSheet(tex, bs[i].NormalMap, bs[i].MetalMap, bs[i].SpriteWidth, bs[i].SpriteHeight, bs[i].Offset);
                replaced++;
            }

            if (unmatched != null && unmatched.Count > 0)
                WarnOnce("keys", $"[{_pack}] player rig: `sheets` key(s) [{string.Join(", ", unmatched)}] matched no base sheet — "
                    + "key entries by the base sheet basenames listed above.");
            if (replaced == 0)
            {
                WarnOnce("none", $"[{_pack}] player rig: nothing to apply on spac '{baseSpac.Name}' — keeping vanilla art.");
                _failedFrom = baseSpac;
                return baseSpac;
            }

            Spac clone = baseSpac.GetCopyWithOtherSpriteSheets(sheets);   // shares SpritesAnimations: states keep working
            clone.Name = "cf_playerrig__" + baseSpac.Name;
            SpacDataBase.SpacMap[clone.Name] = clone;
            _clone = clone;
            _builtFrom = baseSpac;
            _failedFrom = null;
            Plugin.Log.LogInfo($"Content Forge: [{_pack}] player rig built ({replaced}/{sheets.Length} sheet(s) "
                + $"{(_tint != null ? "repainted/tinted" : "repainted")}) as spac '{clone.Name}'.");
            return clone;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[{_pack}] player rig build from '{baseSpac.Name}' failed: {e.Message}");
            _failedFrom = baseSpac;   // cache the fallback so we don't retry (or log) every frame
            return baseSpac;
        }
    }

    // Which author PNG (if any) replaces this base sheet? Matched by the vanilla texture's basename, exactly
    // like RigSwap (e.g. "skeletonidle" for "media/sprites/creatures/hostiles/skeleton/skeletonidle").
    private static string ResolvePng(Texture2D baseTex, out string matchedKey)
    {
        matchedKey = null;
        if (_pngBySheet == null) return null;
        string name = baseTex?.Name;
        if (name == null) return null;
        int slash = name.LastIndexOf('/');
        string basename = slash >= 0 ? name.Substring(slash + 1) : name;
        if (!_pngBySheet.TryGetValue(basename, out string png)) return null;
        matchedKey = basename;
        return png;
    }

    // Author PNG -> GPU texture, with the tint (if any) baked into its pixels. Cached by path+tint so DB
    // reloads (or several sheets sharing one PNG) load it once. Same FromStream pattern as RigSwap/NpcBodies.
    private static Texture2D LoadTexture(string path)
    {
        string key = path + TintKey();
        if (_textureCache.TryGetValue(key, out Texture2D tex)) return tex;
        using (var fs = File.OpenRead(path))
            tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
        tex.Name = path;
        if (_tint != null) MultiplyPixels(tex);   // FromStream textures are SurfaceFormat.Color — always readable
        _textureCache[key] = tex;
        return tex;
    }

    // Tinted COPY of a vanilla sheet texture (never mutate the original — citizens/UI share it). Only
    // SurfaceFormat.Color textures are CPU-readable; anything else warns once and stays untinted.
    private static Texture2D TintedCopy(Texture2D baseTex)
    {
        string key = "cf:tint:" + (baseTex.Name ?? baseTex.GetHashCode().ToString()) + TintKey();
        if (_textureCache.TryGetValue(key, out Texture2D cached)) return cached;
        if (baseTex.Format != SurfaceFormat.Color)
        {
            WarnOnce(key, $"[{_pack}] player rig: base sheet '{baseTex.Name}' is {baseTex.Format} (not CPU-readable) — tint skipped for it.");
            return null;
        }
        var copy = new Texture2D(Globals.GraphicsDevice, baseTex.Width, baseTex.Height);
        var px = new uint[baseTex.Width * baseTex.Height];
        baseTex.GetData(px);
        copy.SetData(px);
        copy.Name = key;
        MultiplyPixels(copy);
        _textureCache[key] = copy;
        return copy;
    }

    // One-time per-pixel multiply (packed RGBA: R low byte, A high byte — same packing SeedRawImage relies on).
    private static void MultiplyPixels(Texture2D tex)
    {
        float r = Clamp255(_tint[0]) / 255f, g = Clamp255(_tint[1]) / 255f, b = Clamp255(_tint[2]) / 255f;
        var px = new uint[tex.Width * tex.Height];
        tex.GetData(px);
        for (int i = 0; i < px.Length; i++)
        {
            uint v = px[i];
            uint cr = (uint)((v & 0xFF) * r);
            uint cg = (uint)(((v >> 8) & 0xFF) * g);
            uint cb = (uint)(((v >> 16) & 0xFF) * b);
            px[i] = (v & 0xFF000000) | (cb << 16) | (cg << 8) | cr;
        }
        tex.SetData(px);
    }

    private static string TintKey() => _tint == null ? "" : $"|{Clamp255(_tint[0])},{Clamp255(_tint[1])},{Clamp255(_tint[2])}";

    private static int Clamp255(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);

    private static IEnumerable<string> BaseSheetSummaries(SpriteSheet[] bs)
    {
        for (int i = 0; i < bs.Length; i++)
        {
            Texture2D t = bs[i].Texture;
            yield return $"[{i}] '{t?.Name ?? "?"}' {t?.Width ?? 0}x{t?.Height ?? 0} (frame {bs[i].SpriteWidth}x{bs[i].SpriteHeight})";
        }
    }

    private static void WarnOnce(string key, string message)
    {
        if (_warnedOnce.Add(key)) Plugin.Log.LogWarning(message);
    }
}

// Prefix runs before the registration the renderer draws from, so even the player's first visible frame is
// already repainted. Patched unconditionally from Plugin.Load (PatchAll(typeof(PlayerRigPatch))); with no
// rig configured the hook is a single null check per player per frame.
internal static class PlayerRigPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(DeferredRenderer), nameof(DeferredRenderer.RegisterPlayerSpac))]
    private static void Prefix(PlayerSpacState playerSpac, SpacSkins skin)
    {
        try { PlayerRigFeature.OnRegisterPlayerSpac(playerSpac, skin); }
        catch (Exception e) { Plugin.Log.LogWarning($"player rig render hook failed: {e.Message}"); }
    }
}
