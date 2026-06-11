using Candide;                              // Globals (client GraphicsDevice)
using CandideCreator.Shared;                // Content (DefaultNormalMap/DefaultMetalMap)
using CandideCreator.Shared.Graphics;       // SpriteSheet, FrameAnimation
using Microsoft.Xna.Framework;              // Vector3, Color
using Microsoft.Xna.Framework.Graphics;     // Texture2D
using Shared.Entity;                        // EntitySystem
using Shared.Entity.Base;                   // AbstractController
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;      // ConditionalWeakTable

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Placeable Tier 2 — CUSTOM-SPRITE placeables: a pack ships its own PNG and the placed entity renders it.
//
// HOW A STATIC ENTITY RENDERS (all verified against the decompiles): the client's entity render loop draws
// every entity that passes the plain-sheet gates —
//   rdll DrawEntitiesImmediate: "if (!current3.Visible || !current3.Render3D || current3.UsesSpac ||
//     current3.Eid == SkipEntityId || (DrawingShadows && current3.HasNoShadow)) continue;
//     SpriteSheet spriteSheet2 = current3.SpriteSheet; if (spriteSheet2 == null) continue;"
// and then, when the entity has NO mesh, draws a simple standing quad from the sheet + Frame:
//   rdll: "if (current3.EntityMesh == null) { GenerateOldSimpleEntityMatrix(current3.Position, spriteSheet2,
//     current3.MeshOffset, current3.DepthOffset, out var world2); ... DrawSimpleEntity(effect,
//     spriteSheet2.Texture, spriteSheet2.GetFrame(current3.System.General[current3.Eid].GetRotatedFrame()),
//     flip: false, in world2); }"
// The effect inputs all come from GeneralComponent: TintColor=Color, FlashColor, Highlight, and the sheet's
// own maps ("MetallicMap" <- spriteSheet.MetalMap; normal map only used when
// spriteSheet.NormalMap.Name != "media/rendering/basic_normal_map" — our sheets use Content.DefaultNormalMap,
// which IS that texture, so lighting stays flat exactly like custom icons/crops/vfx).
//
// So a sprite swap on the registered prototype is: SpriteSheet=<author sheet>, UsesSpac=false,
// EntityMesh=null (forces the simple-quad path instead of the base doodad's mesh, whose UVs were authored
// for the OLD texture), Frame=<first frame>, optional MeshOffset. Everything else (collision Shape,
// Interactable mask, controller, health, footprint) is untouched — the interact prompt and collision come
// from those components, not from the visuals.
//
// WHY THIS PROPAGATES TO PLACED ENTITIES: every spawn deep-copies the prototype (rdll SpawnDoodad:
// "EntityCopyHelper.DeepCopyEntity(value, 0, targetSystem, num, ...)"), and CandideReflection clones a
// SpriteSheet by REFERENCE (cc SimpleTypeHandler: "public object Clone(...) { return source; }" — SpriteSheet
// is registered through SpriteSheetSerializer as a SimpleTypeHandler). One GPU texture, shared by all
// instances. Spawn also copies the prototype controller's Parameters (sh DeepCopyEntity:
// "Controller.AttachEntity(wrapper, new Parameters(sourceSystem.ControllerComponent[sourceId].Controller
// .Parameters))"), which is how the ambient-animation settings ride along (see ForgeSpriteAnimator).
//
// SPAC NOTE: "uses spac" is per-entity General.UsesSpac, set by controllers that own an
// EntitySpacAnimationStateMachine ("Entity.UsesSpac = true;" + "_animationStateMachine.Draw(Entity)" in
// BeforeRender). FurnitureController (the workbench's family) sets it NOWHERE — workbenches already render
// through the plain-sheet path. Our clones replace the controller anyway, so UsesSpac=false simply
// guarantees the plain path for ANY clone base (vanilla precedent: SatyrWatchtowerController.EntityInitialize
// does "Entity.UsesSpac = false;").
//
// ANIMATION: nothing auto-advances General.Frame for plain-sheet entities — vanilla controllers do it by
// hand (e.g. RangedAggroedState: "base.Entity.Frame = _runningAnimation.GetFrame(); _runningAnimation
// .Update(...)"). So ambient animation is one line in ForgeClientController.Update:
// ForgeSpriteAnimator.Tick(this) — parameter-driven ("sprite-frames"/"sprite-frame-duration"), zero cost
// for non-animated placeables.
//
// SHADOWS: General.ShadowSpriteSheet is read by NOTHING in the renderer (only an ImGui debug line) — shadow
// passes reuse SpriteSheet itself, gated by HasNoShadow. Leaving ShadowSpriteSheet null is safe.
// SERVER: the server never renders; entity replication sends ServerEntityModel (BaseId/Position/...), never
// General.SpriteSheet — so the SERVER prototype needs no visual fields at all (serverSide => no-op).
// ---------------------------------------------------------------------------------------------------------

internal static class SpriteSwap
{
    // Controller-parameter keys for the optional looping ambient animation. Written onto the prototype
    // controller by Apply (from PlaceableDef.SpriteFrames/SpriteFrameDuration); pack authors can also set
    // them directly in `controllerParams` (construction params merge into the spawned controller).
    internal const string FramesParam = "sprite-frames";                 // csv cell indices, e.g. "0,1,2,3"
    internal const string FrameDurationParam = "sprite-frame-duration"; // seconds per frame

    // Author PNGs are GPU textures — load each once and reuse (registration runs once per side per game
    // load, but reloads re-enter; key mirrors CropsFeature: "<absolute path>|<frameW>x<frameH>").
    private static readonly Dictionary<string, SpriteSheet> _sheets = new();

    // Re-skin a freshly cloned placeable prototype (slot 0 of `clone`, the same slot RegisterEntities
    // writes) with the pack's own PNG. No `sprite` in the def = keep the clone base's vanilla look (Tier 3
    // behavior, unchanged). Never throws — a bad sprite leaves the vanilla art and logs a warning.
    public static void Apply(EntitySystem clone, PlaceableDef def, string packDir, bool serverSide)
    {
        if (clone == null || def == null || string.IsNullOrWhiteSpace(def.Sprite)) return;
        if (serverSide) return;   // visuals are client-only; the server prototype never renders (see header)
        try
        {
            SpriteSheet sheet = ResolveSheet(def, packDir);
            if (sheet == null) return;                                   // warned inside ResolveSheet

            int[] frames = SaneFrames(def, sheet);
            clone.General[0].SpriteSheet = sheet;
            clone.General[0].Frame = frames != null ? frames[0] : 0;
            clone.General[0].UsesSpac = false;     // plain-sheet render path (see header quotes)
            clone.General[0].EntityMesh = null;    // simple-quad path; the base mesh's UVs fit the OLD art
            clone.General[0].HasFull3DMesh = false;
            clone.General[0].Visible = true;
            clone.General[0].Render3D = true;
            // GetRotatedFrame shifts the drawn frame by sheet ROWS when Direction != 0 (cc SpriteSheet:
            // "return frame + (int)(direction + 0.5f) % 4 * ColumnCount;") — zero the prototype's direction
            // so multi-row author sheets index exactly the cells they name.
            clone.General[0].Direction = 0f;
            // TintColor multiplies the texture — a default(Color) (all zero) base would render invisible.
            if (clone.General[0].Color.PackedValue == 0) clone.General[0].Color = Color.White;
            if (def.SpriteOffset3D != null && def.SpriteOffset3D.Length >= 3)
                clone.General[0].MeshOffset = new Vector3(def.SpriteOffset3D[0], def.SpriteOffset3D[1], def.SpriteOffset3D[2]);

            // Ambient animation rides the controller's Parameters: spawn copies the prototype controller's
            // Parameters into every placed instance (sh DeepCopyEntity — quoted in the header), where
            // ForgeSpriteAnimator.Tick picks them up. Requires the controller to already be attached, so
            // Apply must run AFTER RegisterEntities assigns clone.ControllerComponent[0].
            if (frames != null && frames.Length > 1)
            {
                AbstractController controller = clone.ControllerComponent[0].Controller;
                if (controller != null)
                {
                    controller.Parameters.Set(FramesParam, string.Join(",", frames));
                    controller.Parameters.Set(FrameDurationParam,
                        def.SpriteFrameDuration > 0f ? def.SpriteFrameDuration : 0.15f);
                }
                else
                    Plugin.Log.LogWarning($"placeable '{def.Id}': spriteFrames need a controller to animate " +
                        "(call SpriteSwap.Apply after the controller is attached) — sprite stays static.");
            }
            Plugin.Log.LogInfo($"placeable '{def.Id}': custom sprite '{def.Sprite}' applied " +
                $"({sheet.SpriteWidth}x{sheet.SpriteHeight}px frames" +
                (frames != null && frames.Length > 1 ? $", {frames.Length}-frame animation)." : ")."));
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"placeable '{def?.Id}': sprite swap failed ({e.Message}) — keeping the clone base's art.");
        }
    }

    // The author's PNG as a SpriteSheet — the exact custom-icon pattern (Plugin.ApplyIcons / Crops
    // ResolveSheet): Texture2D.FromStream + Content.DefaultNormalMap/DefaultMetalMap, cached per path+grid.
    private static SpriteSheet ResolveSheet(PlaceableDef def, string packDir)
    {
        string path = Path.Combine(packDir ?? "", def.Sprite);
        int w = def.SpriteFrameWidth;
        int h = def.SpriteFrameHeight;
        string key = path + "|" + w + "x" + h;
        if (_sheets.TryGetValue(key, out SpriteSheet cached)) return cached;

        if (Globals.GraphicsDevice == null)
        { Plugin.Log.LogWarning($"placeable '{def.Id}': graphics device not ready for '{def.Sprite}'."); return null; }
        if (!File.Exists(path))
        { Plugin.Log.LogWarning($"placeable '{def.Id}': sprite file not found: {path}"); return null; }

        using var fs = File.OpenRead(path);
        var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
        var sheet = new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap,
            w > 0 ? w : tex.Width, h > 0 ? h : tex.Height);   // 0/omitted = the whole PNG is one frame
        _sheets[key] = sheet;
        return sheet;
    }

    // The def's animation frames, clamped to cells that exist in the sheet (an out-of-range index would
    // make GetFrame produce a source rectangle outside the texture). null = static frame 0.
    private static int[] SaneFrames(PlaceableDef def, SpriteSheet sheet)
    {
        if (def.SpriteFrames == null || def.SpriteFrames.Length == 0) return null;
        int cellCount = sheet.RowCount * sheet.ColumnCount;
        var frames = new int[def.SpriteFrames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            int f = def.SpriteFrames[i];
            if (f < 0 || f >= cellCount)
            {
                Plugin.Log.LogWarning($"placeable '{def.Id}': spriteFrames[{i}]={f} is outside the sheet " +
                    $"({sheet.ColumnCount}x{sheet.RowCount} cells) — using 0.");
                f = 0;
            }
            frames[i] = f;
        }
        return frames;
    }
}

// Looping ambient animation for custom-sprite placeables: advances General.Frame from the client
// controller's Update — exactly how vanilla plain-sheet entities animate (RangedAggroedState:
// "base.Entity.Frame = _runningAnimation.GetFrame(); _runningAnimation.Update(...)"). Parameter-driven
// ("sprite-frames" csv + "sprite-frame-duration" seconds/frame), so ONE line in ForgeClientController.Update
// covers every preset; controllers without the parameters pay a single dictionary lookup per frame.
public static class ForgeSpriteAnimator
{
    private sealed class AnimState
    {
        public bool Active;
        public FrameAnimation Animation;   // struct field on a class — mutates in place across Ticks
    }

    // Per-controller-INSTANCE state (every placed entity gets its own controller clone at spawn);
    // weak-keyed so despawned entities' states are collected with their controllers.
    private static readonly ConditionalWeakTable<AbstractController, AnimState> _states = new();

    public static void Tick(AbstractController controller)
    {
        if (controller?.Entity == null) return;
        AnimState st = _states.GetValue(controller, CreateState);
        if (!st.Active) return;
        st.Animation.Update(controller.Entity.Fdt);   // vanilla furniture timers use Fdt (FurnitureController)
        controller.Entity.Frame = st.Animation.CurrentFrame;
    }

    private static AnimState CreateState(AbstractController controller)
    {
        var st = new AnimState();
        try
        {
            string csv = controller.Parameters?.GetString(SpriteSwap.FramesParam, null);
            if (string.IsNullOrWhiteSpace(csv)) return st;               // not animated — stays inactive

            string[] parts = csv.Split(',');
            var frames = new List<int>(parts.Length);
            foreach (string p in parts)
                if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int f) && f >= 0)
                    frames.Add(f);
            if (frames.Count < 2) return st;                              // one frame = static, nothing to do

            float perFrame = controller.Parameters.GetFloat(SpriteSwap.FrameDurationParam, 0.15f);
            if (perFrame <= 0f) perFrame = 0.15f;
            // FrameAnimation(int[] frames, float frameDuration, bool loop = true) — per-frame seconds
            // (cc: "FrameDuration = frameDuration"), looping.
            st.Animation = new FrameAnimation(frames.ToArray(), perFrame);
            st.Active = true;
        }
        catch (Exception e)
        { Plugin.Log.LogWarning($"sprite animation init failed for '{controller.Entity?.Name}': {e.Message}"); }
        return st;
    }
}
