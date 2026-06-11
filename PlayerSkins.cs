using Candide;
using Candide.Data;
using Candide.Database;
using Candide.GameModels.Managers;
using CandideCreator.Shared;
using CandideCreator.Shared.Graphics;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Player skins (pack.playerSkins) — three capabilities, all extensions of the existing custom-held-skin
// machinery (ContentLoader.ApplyCustomSkins / ApplyDisplayData):
//
//   1. RESKINS  — overwrite an existing player part's sprite sheet with a 1:1 repaint. The registry is
//      PlayerSkinManager.Parts (public Dictionary<int, Dictionary<string, PlayerSkinPart>>, keyed
//      [skinTag][partName], case-insensitive part names). A simple part holds one slice keyed "" whose
//      SpriteSheet dict maps direction-variation suffix ("" base, "-2") -> SpriteSheet. We swap entries
//      INSIDE that dict (never replace the dict itself): tunic parts hold struct COPIES of the sleeves
//      slice that share the same SpriteSheet dict instance, so in-dict mutation propagates to them too.
//      Palette-swapped renders bypass the SpriteSheet dict entirely — PlayerSkinBuilder.SetSkins calls
//      Content.GetTextureWithPaletteSwap(device, slice.SpriteSheetPath + variation, ...) which reads raw
//      pixels by path — so we also seed the game's private Content.RawImageCache under the part's
//      SpriteSheetPath (same reflection trick ApplyPalettes already uses).
//
//   2. NEW HAIR — append a PlayerSkinPart to Parts[0]. Character creation builds its spinner straight from
//      Parts[0].Values and stores (byte)Selected.Id in the appearance data; rendering matches that byte via
//      SetSpacSkin's `part.Id == index` scan. So Id MUST be Parts[0].Count at insert time (append-only,
//      byte-indexed) and stays stable only while the vanilla hair list and pack order don't change.
//      Hair color always renders through the palette path ("hair" palette + chosen row), so the raw-cache
//      seed is REQUIRED here: each new part gets a synthetic SpriteSheetPath ("cf:skin:<name>") whose ""
//      and "-2" raw images we register ourselves.
//
//   3. WORN LOOKS — optional new parts (any worn tag) + a CharacterDisplayData registered via
//      CharacterDisplayDataBase.AddCharacterDisplayData, mirroring vanilla SetupCharacterDisplayData
//      entries like:  Id = "cdd:pants:copper", Fragments = [ new CharacterDisplayFragment(5, "pants_metal",
//      "metal", 0) ]  and helmets with SpacTagsToHide = [6] (spac tag 6 = hair/headwear -> hideHair).
//      Items reference the look through the EXISTING equippable.displayId field — no new item wiring.
//
// Timing: Apply() is called from the PlayerSkinManager.Setup postfix right after ApplyCustomSkins. Setup
// re-creates Parts from scratch on every run, so GPU resources are built ONCE (cached here) and re-inserted
// into the live registry on every call. CharacterDisplayDataBase.DisplayDataMap persists, so looks are only
// added when their id isn't registered yet (avoids the duplicate-id warning on re-runs).
//
// Sheet contracts (warn + skip on mismatch): layered 48x48 body parts are 2640x240 base ("", 55x5 cells)
// and 2688x240 for the "-2" variation (56x5); sleeves are 576x240; shields 192x240. Reskins are instead
// validated against the LIVE vanilla texture they replace (exact pixel match), which also covers parts
// with non-48 cells. A missing "-2" never crashes: the renderer uses TryGetValue on the variation name
// and simply doesn't draw that facing.
//
// Multiplayer: appearance (hair) travels as a byte Id and display ids travel as strings. A client WITHOUT
// the pack renders an unknown hair byte as nothing (SetSpacSkin finds no matching Id — no crash) and an
// unknown displayId logs "Could not find equipment display data ..." and skips it. Cosmetic-only desync.
// ---------------------------------------------------------------------------------------------------------

internal static class PlayerSkinsFeature
{
    // Filled by ContentLoader.BuildPack at parse time (defs + the folder their PNGs resolve against + the
    // pack file name for log context). DTO types live in ContentPack.cs with the other pack DTOs.
    internal static readonly List<(SkinReskinDef Def, string PackDir, string Pack)> ReskinDefs = new();
    internal static readonly List<(SkinHairDef Def, string PackDir, string Pack)> HairDefs = new();
    internal static readonly List<(SkinPartDef Def, string PackDir, string Pack)> PartDefs = new();
    internal static readonly List<(SkinLookDef Def, string PackDir, string Pack)> LookDefs = new();

    // Author-facing tag names -> SkinTags ints (case-insensitive, so "headwearback" works too).
    private static readonly Dictionary<string, int> TagIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hair"] = SkinTags.Hair,                       // 0
        ["eyes"] = SkinTags.Eyes,                       // 1
        ["tunic"] = SkinTags.Shirt,                     // 4
        ["legwear"] = SkinTags.Pants,                   // 5
        ["footwear"] = SkinTags.Shoes,                  // 6
        ["back"] = SkinTags.Back,                       // 7
        ["shield"] = SkinTags.Shield,                   // 9
        ["skin"] = SkinTags.Body,                       // 10 (reskin-only: body parts are multi-slice)
        ["armour"] = SkinTags.Armour,                   // 12
        ["shoulders"] = SkinTags.Shoulder,              // 13
        ["decoration"] = SkinTags.TopDecoration,        // 14
        ["sleeves"] = SkinTags.Sleeves,                 // 15
        ["headwearBack"] = SkinTags.HeadwearBack,       // 16
        ["headwearMiddle"] = SkinTags.HeadwearMiddle,   // 17
        ["headwearFront"] = SkinTags.HeadwearFront,     // 18
    };

    // Per-tag slice construction data for NEW parts — mirrors PlayerSkinManager.Setup's AddSimple calls
    // (spacTag, hideBaseSkin, layer, depthOffset). Tag 10 (body) is deliberately absent: vanilla builds it
    // from multi-slice subfolders, which a single PNG can't express.
    private static readonly Dictionary<int, (int SpacTag, bool HideBase, float Layer, float Depth)> NewPartSlice = new()
    {
        [SkinTags.Hair] = (6, true, 0f, 0f),            // AddSimple(0, "hair", 6, hideBaseSkin: true)
        [SkinTags.Eyes] = (1, false, 0f, 0f),           // AddSimple(1, "eyes", 1, hideBaseSkin: false)
        [SkinTags.Shirt] = (2, true, 0f, 0f),           // AddSimple(4, "top/tunic", 2, true)
        [SkinTags.Pants] = (3, true, 0f, 0f),           // AddSimple(5, "bottom/legwear", 3, true)
        [SkinTags.Shoes] = (3, true, 0f, 0f),           // AddSimple(6, "bottom/footwear", 3, true)
        [SkinTags.Back] = (12, true, 0f, 0f),           // AddSimple(7, "back", 12, true)
        [SkinTags.Shield] = (13, true, 0f, 0f),         // AddSimple(9, "tools/shield", 13, true)
        [SkinTags.Armour] = (2, true, 1f, 0f),          // AddSimple(12, "top/armour", 2, true, 1f)
        [SkinTags.Shoulder] = (2, true, 2f, 0.5f),      // AddSimple(13, "top/shoulders", 2, true, 2f, …, 0.5f)
        [SkinTags.TopDecoration] = (2, true, 3f, 0f),   // AddSimple(14, "top/decoration", 2, true, 3f)
        [SkinTags.Sleeves] = (11, true, 0f, 0f),        // AddSimple(15, "top/sleeves", 11, true)
        [SkinTags.HeadwearBack] = (6, true, -1f, 0f),   // AddSimple(16, "headwear/back", 6, true, -1f)
        [SkinTags.HeadwearMiddle] = (6, true, 1f, 0f),  // AddSimple(17, "headwear/middle", 6, true, 1f)
        [SkinTags.HeadwearFront] = (6, true, 2f, 0f),   // AddSimple(18, "headwear/front", 6, true, 2f)
    };

    private const int Cell = 48;
    private const int HairSpacTag = 6;   // spac tag hidden by hideHair (vanilla helmets: SpacTagsToHide = [6])

    // Expected author-PNG dimensions for NEW parts per tag: base ("") plus the "-2" variation when known.
    // null = unknown contract -> only require dimensions divisible by the 48px cell.
    private static (int W, int H, int? W2, int? H2) ExpectedDims(int tag) => tag switch
    {
        SkinTags.Sleeves => (576, 240, null, null),
        SkinTags.Shield => (192, 240, null, null),
        _ => (2640, 240, 2688, 240),                    // layered 48x48 body parts (55x5 / 56x5 cells)
    };

    // ---- built-once GPU caches (Setup re-runs; textures must not be recreated every time) ----
    private static bool _built;
    private static readonly List<(int Tag, string Part, string SliceKey, string Variant, SpriteSheet Sheet)> _reskins = new();
    private static readonly List<(int Tag, string Name, PlayerSkinPart Part)> _newParts = new();   // hair included (tag 0)
    private static readonly List<CharacterDisplayData> _looks = new();
    private static bool _loggedIds;

    // The game's private raw-pixel cache (Content.RawImageCache), keyed by content path. Seeding it is how
    // palette-swapped renders (hair color, "metal"/"leather" dye rows on worn looks) see OUR pixels —
    // GetTextureWithPaletteSwap reads by SpriteSheetPath, not from the slice's SpriteSheet dict. Same
    // reflection access ApplyPalettes already relies on.
    private static System.Collections.IDictionary _rawCache;
    private static bool _rawCacheLooked;
    private static System.Collections.IDictionary RawCache
    {
        get
        {
            if (!_rawCacheLooked)
            {
                _rawCacheLooked = true;
                _rawCache = AccessTools.Field(typeof(Content), "RawImageCache")?.GetValue(null)
                    as System.Collections.IDictionary;
                if (_rawCache == null)
                    Plugin.Log.LogWarning("Content Forge: RawImageCache not found — custom skin parts won't recolor through palettes.");
            }
            return _rawCache;
        }
    }

    // Called from the PlayerSkinManager.Setup postfix, right after ContentLoader.ApplyCustomSkins() —
    // same hook window, so Parts is freshly rebuilt and ApplyPalettes has already registered custom dyes.
    public static void Apply()
    {
        ContentLoader.EnsureParsed(Plugin.PacksDir);
        if (ReskinDefs.Count == 0 && HairDefs.Count == 0 && PartDefs.Count == 0 && LookDefs.Count == 0) return;
        if (Globals.GraphicsDevice == null) return;   // device not up yet; a later Setup retries

        BuildOnce();

        // 1) Reskins: re-overwrite the freshly rebuilt vanilla sheets (in-dict swap; see header comment).
        foreach (var (tag, partName, sliceKey, variant, sheet) in _reskins)
        {
            if (!PlayerSkinManager.Parts.TryGetValue(tag, out var dict) || !dict.TryGetValue(partName, out var part)) continue;
            if (!part.Slices.TryGetValue(sliceKey, out var slice)) continue;
            slice.SpriteSheet[variant] = sheet;
        }

        // 2) New parts (incl. hair): re-append with Id = current count — the byte index char creation
        //    stores and SetSpacSkin matches. Names colliding with vanilla were dropped at build time.
        foreach (var (tag, name, part) in _newParts)
        {
            if (!PlayerSkinManager.Parts.TryGetValue(tag, out var dict) || dict.ContainsKey(name)) continue;
            if (dict.Count > byte.MaxValue) { Plugin.Log.LogWarning($"skin part '{name}' skipped: tag {tag} already has {dict.Count} parts (byte index full)."); continue; }
            part.Id = dict.Count;
            dict[name] = part;
        }
        if (!_loggedIds && _newParts.Count > 0)
        {
            _loggedIds = true;
            foreach (var (tag, name, part) in _newParts)
                Plugin.Log.LogInfo($"Content Forge: skin part '{name}' (tag {tag}) -> id {part.Id}.");
        }

        // 3) Worn looks: DisplayDataMap persists across Setups — only add ids not registered yet (re-adding
        //    would log the game's duplicate-id warning every reload).
        var fresh = _looks.Where(l => !CharacterDisplayDataBase.DisplayDataMap.ContainsKey(l.Id)).ToList();
        if (fresh.Count > 0) CharacterDisplayDataBase.AddCharacterDisplayData(fresh);
    }

    // ---------------------------------------------------------------------------------------------------
    // One-time build: load PNGs, validate dimensions, create SpriteSheets, seed raw pixels. Needs the live
    // Parts registry (reskins validate against the texture they replace), so it runs inside the first
    // successful Apply rather than at parse time.
    // ---------------------------------------------------------------------------------------------------
    private static void BuildOnce()
    {
        if (_built) return;
        _built = true;

        int hair = 0, parts = 0;
        foreach (var (def, packDir, pack) in ReskinDefs) BuildReskin(def, packDir, pack);
        foreach (var (def, packDir, pack) in HairDefs)
            if (BuildNewPart(SkinTags.Hair, def?.Name, def?.Sheet, def?.Sheet2, packDir, pack)) hair++;
        foreach (var (def, packDir, pack) in PartDefs)
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Tag)) { Plugin.Log.LogWarning($"[{pack}] playerSkins.part skipped (needs tag)."); continue; }
            if (!TryTag(def.Tag, out int tag) || !NewPartSlice.ContainsKey(tag))
            { Plugin.Log.LogWarning($"[{pack}] playerSkins.part '{def.Name}': unknown/unsupported tag '{def.Tag}'."); continue; }
            if (BuildNewPart(tag, def.Name, def.Sheet, def.Sheet2, packDir, pack)) parts++;
        }
        foreach (var (def, _, pack) in LookDefs) BuildLook(def, pack);

        Plugin.Log.LogInfo($"Content Forge: player skins — {_reskins.Count} reskin sheet(s), {hair} hair style(s), " +
            $"{parts} new part(s), {_looks.Count} worn look(s).");
    }

    private static void BuildReskin(SkinReskinDef def, string packDir, string pack)
    {
        try
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Tag) || string.IsNullOrWhiteSpace(def.Part) || string.IsNullOrWhiteSpace(def.Sheet))
            { Plugin.Log.LogWarning($"[{pack}] playerSkins.reskin skipped (needs tag + part + sheet)."); return; }
            if (!TryTag(def.Tag, out int tag))
            { Plugin.Log.LogWarning($"[{pack}] reskin '{def.Part}': unknown tag '{def.Tag}'."); return; }
            if (!PlayerSkinManager.Parts.TryGetValue(tag, out var dict) || !dict.TryGetValue(def.Part, out var part))
            { Plugin.Log.LogWarning($"[{pack}] reskin: no vanilla part '{def.Part}' under tag '{def.Tag}' (names = the game's file names, e.g. 'Braid', 'Pants_metal')."); return; }

            // Pick the slice to repaint: simple parts have exactly one keyed "". Multi-slice parts (body
            // "skin", tunics carrying merged "sleeves_" copies) repaint their own "" slice; pure multi-slice
            // parts without a "" slice (body types) can't map to a single PNG.
            string sliceKey;
            if (part.Slices.ContainsKey("")) sliceKey = "";
            else if (part.Slices.Count == 1) sliceKey = part.Slices.Keys.First();
            else
            { Plugin.Log.LogWarning($"[{pack}] reskin '{def.Part}': part has multiple slices ({string.Join(", ", part.Slices.Keys)}) — not supported."); return; }
            var slice = part.Slices[sliceKey];

            foreach (var (variant, file) in new[] { ("", def.Sheet), ("-2", def.Sheet2) })
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                if (!slice.SpriteSheet.TryGetValue(variant, out var old))
                { Plugin.Log.LogWarning($"[{pack}] reskin '{def.Part}': vanilla part has no '{variant}' variation — sheet skipped."); continue; }
                var tex = LoadTexture(Path.Combine(packDir, file), pack);
                if (tex == null) continue;
                if (tex.Width != old.Texture.Width || tex.Height != old.Texture.Height)
                {
                    Plugin.Log.LogWarning($"[{pack}] reskin '{def.Part}'{variant}: {tex.Width}x{tex.Height} doesn't match the " +
                        $"vanilla sheet ({old.Texture.Width}x{old.Texture.Height}) — must be a 1:1 repaint. Skipped.");
                    continue;
                }
                // Keep the vanilla normal/metal maps and cell size — it's the same shapes, just repainted.
                _reskins.Add((tag, part.Name ?? def.Part, sliceKey, variant,
                    new SpriteSheet(tex, old.NormalMap, old.MetalMap, old.SpriteWidth, old.SpriteHeight)));
                // Palette-swapped renders read pixels by path — overwrite the raw image under the vanilla key.
                if (!string.IsNullOrEmpty(slice.SpriteSheetPath)) SeedRawImage(slice.SpriteSheetPath + variant, tex);
            }
            if (string.IsNullOrWhiteSpace(def.Sheet2) && slice.SpriteSheet.ContainsKey("-2"))
                Plugin.Log.LogWarning($"[{pack}] reskin '{def.Part}': no sheet2 given but the part has a '-2' variation — some facings keep vanilla art.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[{pack}] reskin '{def?.Part}' failed: {e.Message}"); }
    }

    // Builds one appendable part (hair or worn). Returns true when at least the base sheet loaded.
    private static bool BuildNewPart(int tag, string name, string sheetFile, string sheet2File, string packDir, string pack)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sheetFile))
            { Plugin.Log.LogWarning($"[{pack}] playerSkins part skipped (needs name + sheet)."); return false; }
            if ((PlayerSkinManager.Parts.TryGetValue(tag, out var dict) && dict.ContainsKey(name))
                || _newParts.Any(p => p.Tag == tag && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            { Plugin.Log.LogWarning($"[{pack}] skin part '{name}' (tag {tag}) already exists — skipped."); return false; }

            var (w, h, w2, h2) = ExpectedDims(tag);
            var tex = LoadTexture(Path.Combine(packDir, sheetFile), pack);
            if (tex == null) return false;
            if (tex.Width != w || tex.Height != h)
            { Plugin.Log.LogWarning($"[{pack}] skin part '{name}': sheet is {tex.Width}x{tex.Height}, expected {w}x{h} ({Cell}x{Cell} cells) — skipped."); return false; }

            // Synthetic content path: hair color / dye palettes re-read the sheet's raw pixels by this key.
            string rawKey = "cf:skin:" + name.ToLowerInvariant();
            var sheets = new Dictionary<string, SpriteSheet>
            { { "", new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap, Cell, Cell) } };
            SeedRawImage(rawKey, tex);

            if (!string.IsNullOrWhiteSpace(sheet2File))
            {
                var tex2 = LoadTexture(Path.Combine(packDir, sheet2File), pack);
                if (tex2 != null)
                {
                    bool ok = w2.HasValue ? (tex2.Width == w2 && tex2.Height == h2)
                                          : (tex2.Width % Cell == 0 && tex2.Height % Cell == 0);
                    if (!ok)
                        Plugin.Log.LogWarning($"[{pack}] skin part '{name}': sheet2 is {tex2.Width}x{tex2.Height}, expected " +
                            (w2.HasValue ? $"{w2}x{h2}" : $"multiples of {Cell}") + " — '-2' variation skipped.");
                    else
                    {
                        sheets["-2"] = new SpriteSheet(tex2, Content.DefaultNormalMap, Content.DefaultMetalMap, Cell, Cell);
                        SeedRawImage(rawKey + "-2", tex2);
                    }
                }
            }
            else Plugin.Log.LogWarning($"[{pack}] skin part '{name}': no sheet2 — the part won't draw for facings that use the '-2' variation.");

            var info = NewPartSlice[tag];
            _newParts.Add((tag, name, new PlayerSkinPart
            {
                Id = 0,   // assigned on every Apply (= Parts[tag].Count of the fresh registry)
                Name = name,
                Slices = new Dictionary<string, PlayerSkinSlice>
                {
                    [""] = new PlayerSkinSlice
                    {
                        SpacTag = info.SpacTag, HideBase = info.HideBase, Layer = info.Layer, DepthOffset = info.Depth,
                        SpriteSheet = sheets,
                        SpriteSheetPath = rawKey,
                    },
                },
            }));
            return true;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[{pack}] skin part '{name}' failed: {e.Message}"); return false; }
    }

    private static void BuildLook(SkinLookDef def, string pack)
    {
        try
        {
            if (def == null || string.IsNullOrWhiteSpace(def.Id) || def.Fragments == null || def.Fragments.Count == 0)
            { Plugin.Log.LogWarning($"[{pack}] playerSkins.look skipped (needs id + fragments)."); return; }

            var frags = new List<CharacterDisplayFragment>();
            foreach (SkinFragmentDef f in def.Fragments)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Tag) || string.IsNullOrWhiteSpace(f.Part))
                { Plugin.Log.LogWarning($"[{pack}] look '{def.Id}': fragment skipped (needs tag + part)."); continue; }
                if (!TryTag(f.Tag, out int tag))
                { Plugin.Log.LogWarning($"[{pack}] look '{def.Id}': unknown tag '{f.Tag}' — fragment skipped."); continue; }
                if (!PartExists(tag, f.Part))
                    Plugin.Log.LogWarning($"[{pack}] look '{def.Id}': part '{f.Part}' (tag '{f.Tag}') not found — that fragment won't draw.");
                if (!string.IsNullOrWhiteSpace(f.Palette))
                {
                    if (!PlayerSkinManager.PalettesMap.ContainsKey(f.Palette))
                    { Plugin.Log.LogWarning($"[{pack}] look '{def.Id}': unknown palette '{f.Palette}' — look skipped."); return; }
                    frags.Add(new CharacterDisplayFragment(tag, f.Part, f.Palette, f.Row));
                }
                else frags.Add(new CharacterDisplayFragment(tag, f.Part));   // no palette = drawn as authored
            }
            if (frags.Count == 0) { Plugin.Log.LogWarning($"[{pack}] look '{def.Id}': no valid fragments — skipped."); return; }

            _looks.Add(new CharacterDisplayData
            {
                Id = def.Id,
                Fragments = frags.ToArray(),
                SpacTagsToHide = def.HideHair ? new[] { HairSpacTag } : null,
            });
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[{pack}] look '{def?.Id}' failed: {e.Message}"); }
    }

    // ---- helpers ----

    private static bool TryTag(string tagName, out int tag) =>
        TagIds.TryGetValue(tagName ?? "", out tag) || int.TryParse(tagName, out tag);

    // A look fragment may reference a vanilla part (live registry) or one of OUR parts (built but not yet
    // inserted while BuildOnce is running).
    private static bool PartExists(int tag, string name) =>
        (PlayerSkinManager.Parts.TryGetValue(tag, out var d) && d.ContainsKey(name))
        || _newParts.Any(p => p.Tag == tag && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static Texture2D LoadTexture(string path, string pack)
    {
        if (!File.Exists(path)) { Plugin.Log.LogWarning($"[{pack}] skin sheet not found: {path}"); return null; }
        using var fs = File.OpenRead(path);
        return Texture2D.FromStream(Globals.GraphicsDevice, fs);
    }

    private static void SeedRawImage(string key, Texture2D tex)
    {
        if (string.IsNullOrEmpty(key) || RawCache == null) return;
        var colors = new uint[tex.Width * tex.Height];
        tex.GetData(colors);
        RawCache[key] = new Content.CachedRawImage { Colors = colors, Width = tex.Width };
    }
}
