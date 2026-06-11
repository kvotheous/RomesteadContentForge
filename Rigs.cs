using Candide;                                                  // Globals (GraphicsDevice)
using Candide.Database;                                         // SpacDataBase
using Candide.Entities.Animations;                              // EntitySpacAnimationStateMachine
using Candide.Graphics;                                         // DeferredRenderer
using CandideCreator.Shared.Graphics;                           // SpriteSheet
using CandideCreator.Shared.Models.SpritesAnimationCollections; // Spac
using CandideCreator.Shared.SpacSystem;                         // AbstractSpacAnimationStateMachine
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;                         // Texture2D
using Shared.Entity;                                            // EntitySystem, EntityWrapper
using System;
using System.Collections.Generic;
using System.IO;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Custom creature RIGS (Tier 1 = REPAINT-the-rig): a creature def can ship PNG repaint(s) of its base
// creature's animation sheet(s), so a clone stops looking like "a tinted skeleton" and looks like new art.
// The author's PNG must keep the base sheet's exact LAYOUT (same pixel size, same frame grid, frames in the
// same cells); we only swap textures — all animation timing/frames/states are reused untouched. A different
// layout / frame count / new skeleton ("a real new rig") is explicitly OUT OF SCOPE for v1.
//
// HOW CREATURES ANIMATE (rdll SkeletonWarriorController.EntityInitialize):
//     "Spac spacOrCrash = SpacDataBase.GetSpacOrCrash(flag ? "skeleton_ash_animations" : "skeleton_animations");
//      _animationStateMachine = new EntitySpacAnimationStateMachine(spacOrCrash, ...); Entity.UsesSpac = true;"
// i.e. creatures are SPAC-based, and the spac id is HARDCODED inside each client controller — it is NOT a
// component field we could repoint on our cloned prototype (only a few controllers read it from parameters,
// e.g. the deer's `Parameters.GetString("spacId", "deer_female_animations")`; the skeleton does not).
//
// SO THE SWAP HAPPENS AT RENDER REGISTRATION instead. Every spac creature draws via
//     "public void Draw(EntityWrapper entity) { ... DeferredRenderer.RegisterEntitySpac(entity, this); }"
// and the renderer fetches the texture THROUGH THE STATE MACHINE'S Spac FIELD each frame
// (rdll DeferredRenderer.DrawEntitySpacs: "DrawSpacFrame(effect, entitySpacAnimationStateMachine2.Spac, ...)",
//  DrawSpacFrame: "SpriteSheet spriteSheet = spac.SpriteSheets[sprite.SpriteSheetIndex];").
// We prefix RegisterEntitySpac: if the entity's BaseGuid is one of our rig'd creatures, we replace the
// machine's readonly `Spac` with a repainted clone built via the game's own skinning hook
// (cc Spac.GetCopyWithOtherSpriteSheets — also used by vanilla "explosive_turtle"/"lava_snake" reskins and
// our weapon-trail feature). The clone SHARES SpritesAnimations with the base, so every state the machine
// already built keeps working; only SpriteSheets[] differ. The swap is reference-stable, so after the first
// frame the prefix is a no-op dictionary hit.
//
// Server side: visuals are client-only. The spac id lives in client controller CODE, not in any serialized
// component (the prototype's ControllerComponent only carries the controller NAME + parameters, which
// DeepCopyEntity already copies), so the server prototype needs no work at all — Apply() returns early.
//
// Multi-sheet spacs: a creature spac usually has one sheet PER ANIMATION — e.g. skeleton_animations has 3:
//   media/.../skeleton/skeletonidle   (320x400, 80x80 frames)
//   media/.../skeleton/skeletonwalk   (640x400, 80x80 frames)
//   media/.../skeleton/skeletonattack (480x400, 80x80 frames)
// so `sheet` (one PNG) only fully reskins single-sheet creatures; for the common multi-sheet case authors
// use `sheets`: { "<vanilla sheet basename>": "<png>", ... }. The first build logs every base sheet's name
// + dimensions so authors know exactly what to repaint (same discovery pattern as the weapon-trail log).
//
// A WRONG-SIZE PNG never crashes: SpriteSheet.CalculateRowCount derives Row/ColumnCount from the texture,
// and GetVectorFrame is pure UV math — frames just sample the wrong cells (visual garbage). We warn loudly.
internal static class RigSwap
{
    private sealed class RigSpec
    {
        public string DefId;                            // creature id (for cache keys + logs)
        public string SinglePng;                        // creature.sheet  -> replaces SpriteSheets[0]
        public Dictionary<string, string> PngBySheet;   // creature.sheets -> per-sheet, keyed by vanilla basename
    }

    private static readonly Dictionary<Guid, RigSpec> _rigs = new();          // creature prototype Guid -> spec
    private static readonly Dictionary<string, Spac> _spacCache = new();      // "defId|baseSpacName" -> built spac
    private static readonly Dictionary<string, Texture2D> _textureCache = new(); // abs png path -> GPU texture
    private static readonly HashSet<string> _warnedOnce = new();              // de-dups per-frame warnings
    private static bool _patched;

    // The machine's Spac is `public readonly` on the abstract base (cc AbstractSpacAnimationStateMachine:
    // "[Unmanaged] public readonly Spac Spac;") — FieldRef writes through readonly safely.
    private static readonly AccessTools.FieldRef<AbstractSpacAnimationStateMachine, Spac> SpacRef =
        AccessTools.FieldRefAccess<AbstractSpacAnimationStateMachine, Spac>("Spac");

    // Called from ContentLoader.RegisterCreatures for EVERY creature clone, on both registries (host runs
    // both). Records the rig spec once (client side only) and lazily installs the render hook. Never throws.
    public static void Apply(EntitySystem clone, CreatureDef def, string packDir, bool serverSide)
    {
        try
        {
            if (def == null) return;
            bool hasSingle = !string.IsNullOrWhiteSpace(def.Sheet);
            bool hasMap = def.Sheets != null && def.Sheets.Count > 0;
            if (!hasSingle && !hasMap) return;     // no custom rig requested
            if (serverSide) return;                // visuals are client-only; nothing is serialized (see header)

            Guid guid = clone.General[0].Id;
            if (_rigs.ContainsKey(guid)) return;   // double-registration guard (DB reloads re-run RegisterCreatures)

            var spec = new RigSpec { DefId = def.Id, SinglePng = hasSingle ? Path.Combine(packDir, def.Sheet) : null };
            if (hasMap)
            {
                spec.PngBySheet = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in def.Sheets)
                    if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        spec.PngBySheet[kv.Key] = Path.Combine(packDir, kv.Value);
            }
            _rigs[guid] = spec;

            if (!_patched)   // self-patch so Plugin.Load needs no extra PatchAll line; only ever runs if used
            {
                new Harmony(MyPluginInfo.PLUGIN_GUID + ".rigs").PatchAll(typeof(RigRenderPatch));
                _patched = true;
            }
            Plugin.Log.LogInfo($"Content Forge: creature '{def.Id}' has a custom rig ({(hasMap ? def.Sheets.Count + " sheet(s)" : "1 sheet")}).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"creature '{def?.Id}' rig setup failed: {e.Message}"); }
    }

    // Render-time swap point. Runs before the registration the renderer draws from, so even the creature's
    // very first visible frame is already repainted.
    internal static class RigRenderPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DeferredRenderer), nameof(DeferredRenderer.RegisterEntitySpac))]
        private static void Prefix(EntityWrapper entity, EntitySpacAnimationStateMachine entitySpac)
        {
            try
            {
                if (_rigs.Count == 0 || entitySpac == null) return;
                if (!_rigs.TryGetValue(entity.BaseGuid, out RigSpec spec)) return;   // BaseGuid = our prototype guid
                Spac current = entitySpac.Spac;
                if (current == null) return;
                Spac swapped = GetOrBuild(spec, current);
                if (!ReferenceEquals(current, swapped)) SpacRef(entitySpac) = swapped;
            }
            catch (Exception e) { WarnOnce("prefix", $"Custom rig render hook failed: {e.Message}"); }
        }
    }

    // Build (once, then cache) the repainted spac for one (creature, base spac) pair. Mirrors the weapon
    // trail's EnsureTrailSpac: clone the LIVE spac the controller actually picked (so e.g. an is_ashen
    // parameter variant clones the ash spac), swap the matching sprite sheet textures, keep the base's
    // normal/metal maps and frame size, register the clone in SpacDataBase.SpacMap under a new id.
    // Any failure caches the base spac so we never retry (or log) every frame.
    private static Spac GetOrBuild(RigSpec spec, Spac baseSpac)
    {
        string key = spec.DefId + "|" + baseSpac.Name;
        if (_spacCache.TryGetValue(key, out Spac built)) return built;
        // Already one of our builds (the controller re-picked the swapped spac) — never rebuild from a clone.
        if (baseSpac.Name != null && baseSpac.Name.StartsWith("cf_rig_", StringComparison.Ordinal))
        { _spacCache[key] = baseSpac; return baseSpac; }
        if (Globals.GraphicsDevice == null) return baseSpac;   // too early; retry next frame (don't cache)

        try
        {
            SpriteSheet[] bs = baseSpac.SpriteSheets;
            if (bs == null || bs.Length == 0) { _spacCache[key] = baseSpac; return baseSpac; }

            // Tell authors exactly what to repaint (sheet names, texture + frame sizes) — discovery log.
            Plugin.Log.LogInfo($"Content Forge: building rig '{spec.DefId}' from spac '{baseSpac.Name}' — base sheets: "
                + string.Join(", ", BaseSheetSummaries(bs)));

            var sheets = (SpriteSheet[])bs.Clone();
            int replaced = 0;
            for (int i = 0; i < sheets.Length; i++)
            {
                string png = ResolvePng(spec, bs[i], i, sheets.Length);
                if (png == null) continue;
                if (!File.Exists(png)) { WarnOnce(png, $"rig '{spec.DefId}': sheet file not found: {png}"); continue; }

                Texture2D tex = LoadTexture(png);
                Texture2D baseTex = bs[i].Texture;
                if (baseTex != null && (tex.Width != baseTex.Width || tex.Height != baseTex.Height))
                    WarnOnce(key + i, $"rig '{spec.DefId}': '{Path.GetFileName(png)}' is {tex.Width}x{tex.Height} but base sheet "
                        + $"'{baseTex.Name}' is {baseTex.Width}x{baseTex.Height} — frames will sample the wrong cells (visual garbage). "
                        + "Repaint the vanilla sheet 1:1 (same size, same grid).");

                // Keep the base's normal/metal maps, frame size AND offset: a 1:1 repaint stays lit + aligned.
                sheets[i] = new SpriteSheet(tex, bs[i].NormalMap, bs[i].MetalMap, bs[i].SpriteWidth, bs[i].SpriteHeight, bs[i].Offset);
                replaced++;
            }

            if (replaced == 0)
            {
                WarnOnce(key, $"rig '{spec.DefId}': no PNG matched any sheet of spac '{baseSpac.Name}' — keeping vanilla art. "
                    + "Key `sheets` entries by the base sheet basenames listed above.");
                _spacCache[key] = baseSpac;
                return baseSpac;
            }
            if (spec.SinglePng != null && spec.PngBySheet == null && sheets.Length > 1)
                WarnOnce(key + "_multi", $"rig '{spec.DefId}': base spac '{baseSpac.Name}' has {sheets.Length} sheets but only "
                    + "`sheet` (one PNG) was given — only the first sheet was replaced. Use `sheets` to repaint the rest.");

            Spac clone = baseSpac.GetCopyWithOtherSpriteSheets(sheets);   // shares SpritesAnimations: states keep working
            clone.Name = "cf_rig_" + spec.DefId + "__" + baseSpac.Name;
            SpacDataBase.SpacMap[clone.Name] = clone;
            _spacCache[key] = clone;
            Plugin.Log.LogInfo($"Content Forge: rig '{spec.DefId}' built ({replaced}/{sheets.Length} sheet(s) repainted) as spac '{clone.Name}'.");
            return clone;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"rig '{spec.DefId}' build from '{baseSpac.Name}' failed: {e.Message}");
            _spacCache[key] = baseSpac;   // cache the fallback so we don't retry every frame
            return baseSpac;
        }
    }

    // Which author PNG (if any) replaces base sheet i? `sheets` matches by the vanilla texture's basename
    // (e.g. "skeletonidle" for "media/sprites/creatures/hostiles/skeleton/skeletonidle"); `sheet` means
    // index 0 only (the single-sheet common case).
    private static string ResolvePng(RigSpec spec, SpriteSheet baseSheet, int index, int count)
    {
        if (spec.PngBySheet != null)
        {
            string name = baseSheet.Texture?.Name;
            if (name == null) return null;
            int slash = name.LastIndexOf('/');
            string basename = slash >= 0 ? name.Substring(slash + 1) : name;
            return spec.PngBySheet.TryGetValue(basename, out string png) ? png : null;
        }
        return index == 0 ? spec.SinglePng : null;
    }

    // Same PNG -> GPU texture pattern as the custom icons/skins/trails (Texture2D.FromStream). Cached so
    // several creatures (or rebuilt spacs after a DB reload) sharing one PNG load it once.
    private static Texture2D LoadTexture(string path)
    {
        if (_textureCache.TryGetValue(path, out Texture2D tex)) return tex;
        using var fs = File.OpenRead(path);
        tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
        _textureCache[path] = tex;
        return tex;
    }

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
