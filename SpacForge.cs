using Candide;                                                  // Globals (GraphicsDevice)
using Candide.Data;                                             // VfxSpacAnimationData
using Candide.Database;                                         // SpacDataBase, VfxSpacAnimationDataBase
using Candide.GameModels.Managers;                              // PlayerSkinManager
using Candide.Graphics.VFX;                                     // SpacRenderingArgs
using CandideCreator.Shared;                                    // Content (DefaultNormalMap/DefaultMetalMap)
using CandideCreator.Shared.Graphics;                           // SpriteSheet
using CandideCreator.Shared.Models.SpritesAnimationCollections; // Spac, SpacDirectionsAnimation, SpacAnimation, SpacFrame, SpacSprite
using HarmonyLib;
using Microsoft.Xna.Framework;                                  // Vector2
using Microsoft.Xna.Framework.Graphics;                         // Texture2D
using System;
using System.Collections.Generic;
using System.IO;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// FROM-SCRATCH SPACS — the last animation gap: brand-new sprite animations with NO vanilla base.
// A pack ships PNG sheet(s) + frame definitions and gets a fully NEW Spac, usable
//   (a) anywhere a spac/vfx id is accepted (weapon hitVfx, spell spacVfx, ... — via the vfx mirror), and
//   (b) as a custom creature's ENTIRE rig through `forge_creature_anim`'s `spac` param — zero vanilla art.
//
// Extends the existing `spacs` section: entries WITHOUT `base` route here (entries WITH `base` keep going
// to SpacForgeFeature in AttackTuningPlus.cs — clone/reskin/retime of a vanilla spac).
//
// THE SPAC OBJECT GRAPH (cc = CandideCreator.Shared.decompiled.cs, all field-exact):
//   Spac (cc:13565)                    { Name, SpriteSheets[], SpacDirectionsAnimation[] SpritesAnimations,
//                                        int Directions }
//   SpacDirectionsAnimation (cc:13696) { AnimationName, SpacAnimation[] SpritesAnimations }   // 1 per DIRECTION
//   SpacAnimation (cc:13702)           { SpacFrame[] Frames }
//   SpacFrame (cc:13706)               { SpacSprite[] Sprites, SpacVfxTrigger[]? Triggers, float TickLength }
//   SpacSprite (cc:13714)              { int SpriteSheetIndex, int Frame (cell index into the sheet grid),
//                                        Vector2 Offset, float Opacity, float Angle(rad), bool Flip,
//                                        float Scale, int Tag, bool Horizontal3D, bool HorizontalAndVertical,
//                                        float DepthOffset, string VariationName, bool Hidden }
//
// WHAT THE RENDERERS ACTUALLY REQUIRE (both draw paths verified):
//   * Entity path — DeferredRenderer.RegisterEntitySpac -> DrawEntitySpacs -> DrawSpacFrame (rdll:119797):
//     args.Is3D must be true; for each visible sprite it does
//     "SpriteSheet spriteSheet = spac.SpriteSheets[sprite.SpriteSheetIndex]" then DrawSpacSprite, which
//     culls when (Opacity * tints).W <= 0 — so Opacity MUST be 1 — scales the quad by SpriteWidth/Height *
//     args.Scale * sprite.Scale — so Scale MUST be 1 — and samples UVs via
//     "spriteSheet.GetVectorFrame(sprite.Frame)" (pure grid math, cc:19850: frame % ColumnCount columns,
//     frame / ColumnCount rows). It ALSO dereferences "spriteSheet.NormalMap.Name" unconditionally
//     (rdll:119895) — a SpriteSheet without a normal map CRASHES the renderer; Content.DefaultNormalMap
//     ("media/rendering/basic_normal_map") is the engine's own flat map and disables normal lighting.
//     The skins block only triggers for sprites with Tag != 0, so Tag = 0 sprites never read VariationName
//     on this path; we still set "" (vanilla ParseSpacData always assigns it, cc:13641).
//   * Vfx path — VfxPlayer.PlaySpacAnimation(id) -> VfxSpacAnimation { SimpleSpacAnimationPlayer } whose
//     base ctor is "new SpacAnimationState(spac.GetDirectionsAnimation(animationName), spac.Directions,
//     looping)" (rdll:196438). GetDirectionsAnimation matches AnimationName or THROWS (cc:13580) — the
//     mirrored VfxSpacAnimationData.AnimationName must be an animation that exists. Id playback is always
//     looping:false (rdll:127327) — one-shot, finishes when the last frame's TickLength elapses.
//   * Timing — SpacAnimationState.Update (cc:4861): frame shows for "TickLength * (1/60)" seconds, advanced
//     by "dt * TimeScale * _baseSpeed"; so TickLength is in 60ths of a second ("ticks").
//   * Directions — SpacAnimationState.CurrentDirection mods by ITS animation's own per-direction array
//     length (cc:4781 "value.Mod(Animation.SpritesAnimations.Length)"), so every consumer (creature machine
//     8/4-int directions, projectile vfx 4-int, rdll:127724) is safe for ANY direction count >= 1.
//     Spac.Directions itself is only passed through to ctors (availableDirections is unused there) and read
//     by forge_creature_anim to pick the 8-int vs 4-int direction source (CreatureAnim.cs:309-311).
//
// forge_creature_anim COMPATIBILITY (the (b) goal — verified against CreatureAnim.cs):
//   its machine ctor enumerates spac.SpritesAnimations matching State.AnimationName (cc:4500-4525) — our
//   named SpacDirectionsAnimations are exactly that; looping is decided by the CONSUMER (anim-map
//   :loop/:once flags + the idle/walk/... name heuristic), not stored in the spac; direction selection uses
//   spac.Directions as above. So a from-scratch spac with animations named idle/walk/attack/death plugs in
//   with zero extra config.
//
// SHEET LAYOUT CONTRACT (documented to authors):
//   frameWidth/frameHeight cut the PNG into a grid (SpriteSheet.CalculateRowCount, cc:19838: ColumnCount =
//   texWidth/frameW, RowCount = texHeight/frameH). Art should be centered in each cell (the quad is centered
//   on the entity position + half-bbox-height up via SetDefaultEntityOffset).
//   directions == 1: animation `frames` are ABSOLUTE cell indices (row-major) anywhere in the grid.
//   directions  > 1: ROW r of the sheet = direction r; `frames` are COLUMN indices (0-based) and direction d
//   draws cell `frame + d * ColumnCount`. Row order follows the game's int-direction convention
//   (DirectionToAngle = dir * pi/2 with screen-down Y, cc:18734): 4-dir rows = E, S, W, N; 8-dir rows =
//   E, SE, S, SW, W, NW, N, NE. One row per direction (frames spanning multiple rows are a directions==1
//   feature only).
//
// REGISTRATION runs on the same hook as SpacForgeFeature (PlayerSkinManager.Setup postfix — right after
// SpacDataBase.LoadSpacs, GraphicsDevice + Content defaults alive, rdll:10912-10913) into the same two maps:
// SpacDataBase.SpacMap (spac consumers) + VfxSpacAnimationDataBase.AnimationDataMap (vfx-id consumers).
// This file is self-contained (Register takes plain values + the SpacAnimDef DTO below, the CreatureAnim.cs
// pattern) so it compiles before SpacDef grows the new fields.
// ---------------------------------------------------------------------------------------------------------

// JSON DTO for spacs[].animations.<name> (referenced by SpacDef.Animations once that field is added).
internal sealed class SpacAnimDef
{
    public int[] Frames;          // cell indices, in play order (directions>1: column indices; see contract)
    public float TickLength = 8f; // ticks each frame shows (60 ticks = 1 second); 8 ≈ 7.5 fps
    public bool Loop;             // advisory only: looping is chosen where the spac is USED (vfx id playback
                                  // is always one-shot; forge_creature_anim uses anim-map :loop/:once flags
                                  // + the idle/walk/run/... name heuristic) — kept so authors can document intent
    public string Sheet;          // multi-sheet spacs: which `sheets` key this animation draws from
                                  // (default: the first sheet) — the vanilla one-sheet-per-animation pattern
}

// Builds brand-new Spacs from pack PNGs + frame definitions and registers them in BOTH databases.
// ContentLoader routes `spacs` entries WITHOUT `base` here at parse time.
internal static class ScratchSpacFeature
{
    private sealed class Def
    {
        public string Id;
        public int Directions;
        public Dictionary<string, string> Sheets;   // sheet name -> PNG (insertion order = sheet index order)
        public int FrameWidth, FrameHeight;
        public Dictionary<string, SpacAnimDef> Animations;
        public string Dir, Pack;
    }

    private static readonly List<Def> Defs = new();
    private static readonly HashSet<string> Built = new();   // ids we finished (or permanently failed)

    internal static void Register(string id, int directions, Dictionary<string, string> sheets,
        int frameWidth, int frameHeight, Dictionary<string, SpacAnimDef> animations, string packDir, string packName)
    {
        Defs.Add(new Def
        {
            Id = id,
            Directions = directions,
            Sheets = sheets,
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            Animations = animations,
            Dir = packDir,
            Pack = packName,
        });
    }

    // Same timing as SpacForgeFeature.Apply (PlayerSkinManager.Setup postfix below).
    internal static void Apply()
    {
        if (Defs.Count == 0 || Globals.GraphicsDevice == null) return;
        foreach (Def def in Defs)
        {
            if (Built.Contains(def.Id)) continue;
            try { BuildOne(def); }
            catch (Exception e) { Plugin.Log.LogError($"[{def.Pack}] spac '{def.Id}' build failed: {e.Message}"); }
            Built.Add(def.Id);   // never retry a throwing def every Setup
        }
    }

    private static void BuildOne(Def def)
    {
        string pack = def.Pack;
        if (SpacDataBase.SpacMap.ContainsKey(def.Id))
        {
            Plugin.Log.LogWarning($"[{pack}] spac id '{def.Id}' already exists (vanilla or another pack) — skipped.");
            return;
        }
        if (def.Sheets == null || def.Sheets.Count == 0 || def.Animations == null || def.Animations.Count == 0)
        {
            Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': from-scratch spacs need `sheets` + `animations` — skipped.");
            return;
        }
        // DrawSpacSprite dereferences SpriteSheet.NormalMap unconditionally (rdll:119895) — the flat engine
        // default keeps lighting neutral; if it isn't loaded yet we must not register a crashing spac.
        if (Content.DefaultNormalMap == null || Content.DefaultMetalMap == null)
        {
            Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': engine default normal/metal maps not loaded yet — skipped.");
            return;
        }
        int directions = def.Directions > 0 ? def.Directions : 1;

        // 1) Sheets: PNG -> GPU texture -> grid SpriteSheet (the Sprites.cs/Particles.cs pattern). Insertion
        //    order of the `sheets` map defines SpriteSheetIndex order; animations reference sheets by name.
        var sheetList = new List<SpriteSheet>(def.Sheets.Count);
        var sheetIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> kv in def.Sheets)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
            string png = Path.Combine(def.Dir, kv.Value);
            if (!File.Exists(png))
            {
                Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': sheet file not found: {png} — skipped.");
                return;   // a missing sheet would shift every SpriteSheetIndex; abort the whole spac
            }
            Texture2D tex;
            using (FileStream fs = File.OpenRead(png)) tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
            tex.Name = "cf:spac:" + def.Id + ":" + kv.Key;
            int fw = def.FrameWidth > 0 ? def.FrameWidth : tex.Width;     // 0 = whole PNG is one frame
            int fh = def.FrameHeight > 0 ? def.FrameHeight : tex.Height;
            sheetIndexByName[kv.Key] = sheetList.Count;
            sheetList.Add(new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap, fw, fh));
        }
        if (sheetList.Count == 0)
        {
            Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': no usable sheets — skipped.");
            return;
        }

        // 2) Animations: name -> SpacDirectionsAnimation with one SpacAnimation per direction.
        var anims = new List<SpacDirectionsAnimation>(def.Animations.Count);
        foreach (KeyValuePair<string, SpacAnimDef> kv in def.Animations)
        {
            string animName = kv.Key;
            SpacAnimDef a = kv.Value;
            if (string.IsNullOrWhiteSpace(animName) || a == null) continue;

            int sheetIndex = 0;
            if (!string.IsNullOrWhiteSpace(a.Sheet) && !sheetIndexByName.TryGetValue(a.Sheet, out sheetIndex))
            {
                Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': animation '{animName}' names unknown sheet " +
                    $"'{a.Sheet}' (has: {string.Join(", ", sheetIndexByName.Keys)}) — using the first sheet.");
                sheetIndex = 0;
            }
            SpriteSheet sheet = sheetList[sheetIndex];
            int cellCount = sheet.RowCount * sheet.ColumnCount;
            if (directions > 1 && sheet.RowCount < directions)
                Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': sheet for '{animName}' has {sheet.RowCount} " +
                    $"row(s) but directions={directions} — missing direction rows will sample wrapped cells.");

            int[] frames = a.Frames is { Length: > 0 } ? a.Frames : new[] { 0 };
            float tick = a.TickLength > 0f ? a.TickLength : 8f;

            var perDirection = new SpacAnimation[directions];
            for (int d = 0; d < directions; d++)
            {
                var list = new List<SpacFrame>(frames.Length);
                foreach (int f in frames)
                {
                    // directions==1: absolute cell index. directions>1: column in direction-row d.
                    int cell = directions == 1 ? f : f + d * sheet.ColumnCount;
                    if (f < 0 || cell >= cellCount || (directions > 1 && f >= sheet.ColumnCount))
                    {
                        Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': animation '{animName}' frame {f} is outside " +
                            $"the sheet grid ({sheet.ColumnCount}x{sheet.RowCount} cells" +
                            (directions > 1 ? $", {directions} direction rows" : "") + ") — frame skipped.");
                        continue;
                    }
                    list.Add(new SpacFrame
                    {
                        TickLength = tick,
                        Triggers = null,
                        Sprites = new[]
                        {
                            new SpacSprite
                            {
                                SpriteSheetIndex = sheetIndex,
                                Frame = cell,
                                Offset = Vector2.Zero,
                                Opacity = 1f,            // (opacity*tints).W <= 0 culls the sprite (rdll:119852)
                                Angle = 0f,
                                Flip = false,
                                Scale = 1f,              // quad size = SpriteWidth/Height * Scale (rdll:119857)
                                Tag = 0,                 // Tag 0 = never enters the skins path
                                Horizontal3D = false,
                                HorizontalAndVertical = false,
                                DepthOffset = 0f,
                                VariationName = string.Empty,   // vanilla ParseSpacData always assigns it (cc:13641)
                                Hidden = false,
                            },
                        },
                    });
                }
                if (list.Count == 0)
                {
                    Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': animation '{animName}' has no valid frames — animation dropped.");
                    perDirection = null;
                    break;
                }
                perDirection[d] = new SpacAnimation { Frames = list.ToArray() };
            }
            if (perDirection != null)
                anims.Add(new SpacDirectionsAnimation { AnimationName = animName, SpritesAnimations = perDirection });
        }
        if (anims.Count == 0)
        {
            Plugin.Log.LogWarning($"[{pack}] spac '{def.Id}': no valid animations — skipped.");
            return;
        }

        // 3) Register: the spac itself + the vfx-id mirror (same dual registration as SpacForgeFeature —
        //    weapon hitVfx / spell spacVfx fields are vfx-ANIMATION ids resolved via AnimationDataMap).
        var spac = new Spac
        {
            Name = def.Id,
            Directions = directions,
            SpriteSheets = sheetList.ToArray(),
            SpritesAnimations = anims.ToArray(),
        };
        SpacDataBase.SpacMap[def.Id] = spac;
        VfxSpacAnimationDataBase.AnimationDataMap[def.Id] = new VfxSpacAnimationData
        {
            Id = def.Id,
            SpacId = def.Id,
            AnimationName = anims[0].AnimationName,   // first listed animation = what the vfx id plays (one-shot)
            SpacRenderingArgs = SpacRenderingArgs.Default,
            TimeScale = null,
        };

        var names = new List<string>(anims.Count);
        foreach (SpacDirectionsAnimation a in anims) names.Add(a.AnimationName);
        SpriteSheet s0 = sheetList[0];
        Plugin.Log.LogInfo($"[{pack}] registered from-scratch spac '{def.Id}' ({directions} direction(s), " +
            $"{sheetList.Count} sheet(s), grid {s0.ColumnCount}x{s0.RowCount} of {s0.SpriteWidth}x{s0.SpriteHeight}px) — " +
            $"animations: {string.Join(", ", names)} (vfx id plays '{anims[0].AnimationName}').");
    }
}

// Same hook as SpacForgePatch (AttackTuningPlus.cs): SpacDataBase.LoadSpacs runs immediately before
// PlayerSkinManager.Setup on the loading thread (rdll:10912-10913) — SpacMap populated (collision check
// sees vanilla ids), GraphicsDevice + Content.Default*Map alive. Idempotent via the Built set.
// Add `h.PatchAll(typeof(ScratchSpacPatch));` next to SpacForgePatch in Plugin.Load.
internal static class ScratchSpacPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerSkinManager), nameof(PlayerSkinManager.Setup))]
    private static void Postfix()
    {
        try { ScratchSpacFeature.Apply(); }
        catch (Exception e) { Plugin.Log.LogError($"from-scratch spac inject failed: {e}"); }
    }
}
