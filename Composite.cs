using Candide;                              // Globals (client GraphicsDevice / client entity registry reflection)
using Candide.Database.Doodad;              // DoodadDatabaseManager (client entity registry)
using Candide.Entities.Animations;          // EntitySpacAnimationStateMachine
using Candide.Graphics.VFX;                 // SpacRenderingArgs
using CandideCreator.Shared;                // Content (DefaultNormalMap/DefaultMetalMap)
using CandideCreator.Shared.Collision;      // CollisionRectangle
using CandideCreator.Shared.Graphics;       // SpriteSheet
using CandideCreator.Shared.Models.SpritesAnimationCollections; // Spac, SpacSprite, SpacFrame, SpacAnimation, SpacDirectionsAnimation
using CandideCreator.Shared.Serialization;  // CandideReflection
using CandideServer;                        // ServerGlobals, ServerGameState
using CandideServer.Entities;               // ServerEntityDataManager
using CandideServer.ServerControllers;      // SpawnEntityArgs
using CandideServer.ServerManagers;         // EntityServerManager
using HarmonyLib;
using Microsoft.Xna.Framework;              // GameTime, Vector2, Vector3, MathHelper
using Microsoft.Xna.Framework.Graphics;     // Texture2D
using Shared;                               // EntityIds
using Shared.Entity;                        // EntitySystem, EntityWrapper, EntityCopyHelper, EntityRemoveInfo/Type, EntityType, ServerEntityModel ns siblings
using Shared.Entity.Base;                   // AbstractController
using Shared.Entity.Components;             // Component, ControllerComponent, CollisionComponent
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// COMPOSITE placeables — a multi-part building as ONE buildable: several pack PNGs drawn at per-part offsets
// with a controlled draw order, plus per-part collision boxes, placed and demolished as a single unit.
//
// ROUTE: hybrid (a)+(b), both mirroring engine-native machinery —
//
//  (a) VISUALS = ONE entity, ONE in-memory Spac. The engine's own multi-sprite renderer is the spac path:
//      a SpacFrame holds SpacSprite[] and every sprite carries its own sheet + Offset + DepthOffset + Scale +
//      Opacity + Angle + Flip + Horizontal3D (cc SpacSprite struct, ~13716). DrawSpacFrame iterates them in
//      ARRAY ORDER with a +0.05 depth step per sprite (rdll DeferredRenderer.DrawSpacFrame: "num +=
//      depthOffsetStep; ... DrawSpacSprite(..., sprite, spriteSheet, in result, num, ...)") — later parts
//      draw in front, exactly how the player's body/armor sprite stack layers (PlayerSpacState.DrawPlayer
//      uses the same scheme). So the composite renderer is a hand-built single-frame Spac: Directions=1, one
//      looping "idle" animation, one SpacFrame, N SpacSprites — fed to the same EntitySpacAnimationStateMachine
//      vanilla creature controllers own ("Entity.UsesSpac = true; _animationStateMachine.Args =
//      SpacRenderingArgs.Default;" + BeforeRender -> Draw(Entity) -> DeferredRenderer.RegisterEntitySpac;
//      EntitySystem.BeforeRender calls every controller's BeforeRender — sh 13411). UsesSpac=true also makes
//      the plain-sheet entity pass skip the clone base's art (rdll DrawEntitiesImmediate gates on
//      "current3.UsesSpac"), so the workbench base never shows through.
//      Per-sprite mapping (verified in rdll DrawSpacSprite): Offset.X = screen px right; Offset.Y = screen px
//      DOWN ("translation.Y -= result.Y"), quads are CENTER-anchored ("_verticalCenteredPlaneBuffer") — we
//      convert the pack's bottom-anchored "offset: [x, y-up]" to Offset=(x, -(yUp + drawnHeight/2)).
//      DepthOffset adds to BOTH render height and depth ("translation.Y += sprite.DepthOffset + depthOffset;
//      translation.Z += ..."), i.e. a pure sort bias along the camera axis — exposed as parts[].depth.
//
//  (b) COLLISION = per-part child blocker entities (the hunter-lodge satellite pattern). An entity owns ONE
//      CollisionComponent.Shape (sh CheckEntityCollision: "item.Shape"), so true per-part boxes with walkable
//      gaps need one entity per box. Part 0's box lives on the PARENT itself (its prototype Shape is resized;
//      keeps the entity demolish-targetable the same way every tier-3 placeable already is); parts 1+ each get
//      an invisible, indestructible, controller-less prototype clone (Collision mask + CollisionRectangle only;
//      Visible=false skips the render gate "if (!current3.Visible ...) continue") that the parent's SERVER
//      controller spawns at place time via EntityServerManager.SpawnEntity (replicates, cs2 37749) and removes
//      on demolish via EntityServerManager.RemoveEntity (replicates, cs2 37760).
//
// CHILD LIFECYCLE (bulletproofing, all verified):
//   - SPAWN is deferred to the first server Update tick, NOT EntityInitialize — world load spawns entities
//     while iterating spawn/chunk lists (cs2 SpawnWorldEntities/CreateExteriorWorld), and spawning more
//     entities mid-iteration from EntityInitialize could mutate a list being enumerated.
//   - PERSISTENCE: child ids ride the parent's controller Parameters ("cf-children" = "i:guid,i:guid").
//     The model and controller share ONE dictionary after spawn (cs2 ServerEntitySystemManager.SpawnEntity:
//     "entityModel.Parameters = entityWrapper.Controller.Parameters.Dictionary;", re-asserted at room unload:
//     "if (RemoveFromSimulation) entityModel.Parameters = controller.Parameters.Dictionary") — so writes to
//     Parameters.Dictionary are save-persistent with zero extra plumbing (the proven cf-summon pattern).
//     The children themselves persist as ordinary saved entities (BaseId = our deterministic prototype Guid,
//     present in both registries every launch), so save/load needs no respawn — the parent's first tick just
//     RE-VALIDATES the list and respawns any child that went missing.
//   - DEMOLISH: the parent's OnRemove fires on every removal path with the remove info (sh EntitySystem
//     .RemoveEntity: "ControllerComponent[id].Controller.OnRemove(removeInfo)"); we cascade to the children
//     for REAL removals (demolish sends EntityRemoveType.Removed; damage-death sends Killed/OutOfHealth) and
//     pass through lifecycle removals (Unloaded / RemoveFromSimulation / WorldRemoved / ChangingWorld / ...)
//     where the children unload themselves as ordinary entities in the same room.
//   - Children are NOT player-removable on their own: not constructions (demolish tool walks construction
//     registries + hardcoded BaseGuid whitelists — rdll TryRemoveFortificationInTileBounds), no Health mask
//     (can't be damaged), no Interactable, no DropTable.
//
// LIMITS (honest): one composite = one controller slot, so a composite can't also be a shrine/dialogue/station
// (parts are ignored with a warning if those are set). Placement footprint is the single width x height tile
// rect (per-part placement areas not wired). The whole composite depth-sorts at the anchor like any tall
// single-sprite building; parts[].depth is a per-part bias, not true per-part world depth.
// ---------------------------------------------------------------------------------------------------------

internal static class CompositeFeature
{
    // composite placeable id -> (its def, its pack dir). Filled at pack parse, read by prototypes/controllers.
    internal static readonly Dictionary<string, (PlaceableDef Def, string Dir)> Composites = new();

    // Built composite Spacs, one per placeable id (textures are GPU resources — build once, share across
    // every placed instance, exactly like SpriteSwap's sheet cache).
    private static readonly Dictionary<string, Spac> _spacs = new();
    private static readonly Dictionary<string, SpriteSheet> _sheets = new();

    // Registered from Plugin.Awake (before the controller maps are built/injected).
    public static void Init()
    {
        InteractiveFeature.RegisterController("forge_composite",
            typeof(ForgeCompositeClientController), typeof(ForgeCompositeServerController));
    }

    // ------------------------------------------------------------------ parse time (BuildPack)

    // Turn a `parts` placeable into a forge_composite tier-3 entity construction. Must run BEFORE the yarn /
    // station handling in BuildPack: composite owns the controller slot, so those combinations are rejected.
    public static void PreparePlaceable(PlaceableDef f, string pack, string dir)
    {
        if (f?.Parts == null || f.Parts.Count == 0) return;
        if (string.IsNullOrWhiteSpace(f.Id))
        { Plugin.Log.LogWarning($"[{pack}] composite placeable skipped (needs id)."); return; }
        if (!string.IsNullOrWhiteSpace(f.Yarn) || !string.IsNullOrWhiteSpace(f.Station) || !string.IsNullOrWhiteSpace(f.Controller))
        {
            Plugin.Log.LogWarning($"[{pack}] placeable '{f.Id}': `parts` can't be combined with yarn/station/controller " +
                "(one controller slot per entity) — parts ignored.");
            f.Parts = null;
            return;
        }
        bool anySprite = false;
        for (int i = 0; i < f.Parts.Count; i++)
            if (f.Parts[i] != null && !string.IsNullOrWhiteSpace(f.Parts[i].Sprite)) anySprite = true;
            else Plugin.Log.LogWarning($"[{pack}] placeable '{f.Id}': parts[{i}] has no sprite — that layer is skipped.");
        if (!anySprite)
        { Plugin.Log.LogWarning($"[{pack}] placeable '{f.Id}': no part has a sprite — parts ignored."); f.Parts = null; return; }

        f.Controller = "forge_composite";
        if (string.IsNullOrWhiteSpace(f.CloneEntity)) f.CloneEntity = "WorkbenchGuid";   // the established tier-3 base
        f.ControllerParams ??= new Dictionary<string, string>();
        f.ControllerParams["cf-composite"] = f.Id;   // both controllers find the def through this (co-op safe:
                                                     // the registry rebuilds from the local pack every launch)
        Composites[f.Id] = (f, dir);
    }

    // ------------------------------------------------------------------ guids

    // Parent prototype Guid — InteractiveFeature.EntityGuid's exact construction (MD5 "forge-tier3:<id>");
    // DoodadsFeature.TryResolveEntity already relies on this equivalence via NpcBodies.GuidFromId.
    internal static Guid ParentGuid(string placeableId) => NpcBodies.GuidFromId("forge-tier3:" + placeableId);

    // Child blocker prototype Guid — deterministic per (placeable, part), disjoint prefix from every other
    // forge Guid space, stable across machines (co-op clients resolve replicated children by this BaseId).
    internal static Guid PartGuid(string placeableId, int partIndex)
        => NpcBodies.GuidFromId("forge-composite-part:" + placeableId + "#" + partIndex);

    internal static bool HasCollision(PartDef p)
        => p?.Collision != null && p.Collision.Length >= 2 && p.Collision[0] > 0f && p.Collision[1] > 0f;

    // A part's collision-box world offset from the anchor: explicit collisionOffset, else [offset.x, 0]
    // (the visual y is screen-UP/height, not world depth — it makes no sense as a ground-plane offset).
    internal static Vector2 CollisionOffset(PartDef p)
    {
        if (p.CollisionOffset != null && p.CollisionOffset.Length >= 2)
            return new Vector2(p.CollisionOffset[0], p.CollisionOffset[1]);
        return new Vector2(p.Offset != null && p.Offset.Length >= 1 ? p.Offset[0] : 0f, 0f);
    }

    // ------------------------------------------------------------------ prototypes (db-load time)

    // Runs right AFTER InteractiveFeature.RegisterEntities on the same chokepoint (our postfix is patched
    // later, so Harmony executes it later): adjusts the freshly registered parent prototype (composite =
    // scenery: no "press E"; collision = part 0's box or none) and registers the invisible child blocker
    // prototypes for parts 1+ with collision. Idempotent: skips Guids already present, re-applies the
    // (idempotent) parent edits.
    internal static void RegisterPrototypes(Dictionary<Guid, EntitySystem> protos, CandideReflection reflection, bool serverSide)
    {
        ContentLoader.EnsureParsed(Plugin.PacksDir);
        if (Composites.Count == 0) return;

        foreach ((PlaceableDef f, string dir) in Composites.Values)
        {
            try
            {
                // ---- parent (registered moments ago by InteractiveFeature.RegisterEntities) ----
                if (protos.TryGetValue(ParentGuid(f.Id), out EntitySystem parent))
                {
                    parent.Mask[0] &= ~Component.Interactable;   // scenery: no interact prompt
                    PartDef p0 = f.Parts.Count > 0 ? f.Parts[0] : null;
                    if (HasCollision(p0))
                    {
                        Vector2 off = CollisionOffset(p0);
                        parent.Mask[0] |= Component.Collision;
                        ref CollisionComponent cc = ref parent.CollisionComponent[0];
                        cc.Shape = new CollisionRectangle(p0.Collision[0], p0.Collision[1],
                            off.X - p0.Collision[0] / 2f, off.Y - p0.Collision[1] / 2f);
                    }
                    else
                        parent.Mask[0] &= ~Component.Collision;  // fully walk-through anchor
                    parent.General[0].Immovable = true;
                }
                else
                    Plugin.Log.LogWarning($"composite '{f.Id}': parent prototype missing — was the placeable rejected earlier?");

                // ---- child blockers for parts 1+ with collision ----
                for (int i = 1; i < f.Parts.Count; i++)
                {
                    PartDef p = f.Parts[i];
                    if (!HasCollision(p)) continue;
                    Guid guid = PartGuid(f.Id, i);
                    if (protos.ContainsKey(guid)) continue;   // DB reloads re-enter
                    if (!protos.TryGetValue(EntityIds.WorkbenchGuid, out EntitySystem src))
                    { Plugin.Log.LogWarning($"composite '{f.Id}': workbench base prototype missing — part {i} blocker skipped."); continue; }

                    var clone = new EntitySystem(1);
                    int id = clone.NewEntity();   // slot 0 (keeps its own Wrapper/IncId, preserved by DeepCopyEntity)
                    EntityCopyHelper.DeepCopyEntity(src, 0, clone, id, initializeControllers: false, ignoreControllers: true, reflection);

                    clone.General[0].Id = guid;
                    clone.General[0].BaseGuid = guid;
                    clone.General[0].Name = "cf:composite:" + f.Id + "#" + i;
                    clone.General[0].Immovable = true;
                    clone.General[0].Visible = false;        // pure blocker — render passes gate on Visible
                    clone.General[0].UsesSpac = false;
                    // No controller, no prompt, no drops, no health (can't be interacted with, damaged or looted;
                    // removal is exclusively the parent's cascade).
                    clone.Mask[0] &= ~(Component.Controller | Component.Interactable | Component.DropTable | Component.Health);
                    clone.Mask[0] |= Component.Collision;
                    ref CollisionComponent ccc = ref clone.CollisionComponent[0];
                    ccc.Shape = new CollisionRectangle(p.Collision[0], p.Collision[1]);   // centered; placement = spawn position
                    clone.General[0].Wrapper.IsInteractionArea = false;

                    protos[guid] = clone;
                    Plugin.Log.LogInfo($"Registered composite blocker '{f.Id}' part {i} " +
                        $"({p.Collision[0]}x{p.Collision[1]}px, {(serverSide ? "server" : "client")}).");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"composite '{f?.Id}' prototype pass failed: {e.Message}"); }
        }
    }

    // ------------------------------------------------------------------ the composite Spac (client only)

    // One single-frame Spac per composite: Directions=1, one looping "idle" animation, one SpacFrame whose
    // SpacSprite[] is the parts in order. Never throws; a part with a missing PNG is skipped with a warning.
    internal static Spac BuildSpac(string placeableId)
    {
        if (_spacs.TryGetValue(placeableId, out Spac cached)) return cached;
        if (!Composites.TryGetValue(placeableId, out (PlaceableDef Def, string Dir) c)) return null;
        if (Globals.GraphicsDevice == null)
        { Plugin.Log.LogWarning($"composite '{placeableId}': graphics device not ready."); return null; }

        var sheets = new List<SpriteSheet>();
        var sprites = new List<SpacSprite>();
        for (int i = 0; i < c.Def.Parts.Count; i++)
        {
            PartDef p = c.Def.Parts[i];
            if (p == null || string.IsNullOrWhiteSpace(p.Sprite)) continue;
            try
            {
                SpriteSheet sheet = ResolveSheet(p, c.Dir);
                if (sheet == null) { Plugin.Log.LogWarning($"composite '{placeableId}': part {i} sprite '{p.Sprite}' not loadable — layer skipped."); continue; }

                float scale = p.Scale > 0f ? p.Scale : 1f;
                float offX = p.Offset != null && p.Offset.Length >= 1 ? p.Offset[0] : 0f;
                float offYUp = p.Offset != null && p.Offset.Length >= 2 ? p.Offset[1] : 0f;
                int cells = sheet.RowCount * sheet.ColumnCount;
                int frame = p.Frame >= 0 && p.Frame < cells ? p.Frame : 0;

                sheets.Add(sheet);
                sprites.Add(new SpacSprite
                {
                    SpriteSheetIndex = sheets.Count - 1,
                    Frame = frame,
                    // pack offsets are bottom-anchored [x right, y up]; spac quads are CENTER-anchored with
                    // Offset.Y growing DOWN ("translation.Y -= result.Y") — convert (flat parts lie on the
                    // ground; their offset.y maps straight onto the ground-plane axis instead).
                    Offset = p.Flat
                        ? new Vector2(offX, offYUp)
                        : new Vector2(offX, -(offYUp + sheet.SpriteHeight * scale / 2f)),
                    Opacity = p.Opacity > 0f && p.Opacity <= 1f ? p.Opacity : 1f,
                    Scale = scale,
                    Angle = MathHelper.ToRadians(p.Angle),
                    Flip = p.Flip,
                    Horizontal3D = p.Flat,
                    DepthOffset = p.Depth,
                    Tag = 0,
                    VariationName = string.Empty,
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"composite '{placeableId}': part {i} failed ({e.Message}) — layer skipped."); }
        }
        if (sprites.Count == 0) { Plugin.Log.LogWarning($"composite '{placeableId}': no drawable parts."); return null; }

        var spac = new Spac
        {
            Name = "cf-composite:" + placeableId,
            Directions = 1,
            SpriteSheets = sheets.ToArray(),
            SpritesAnimations = new[]
            {
                new SpacDirectionsAnimation
                {
                    AnimationName = "idle",
                    SpritesAnimations = new[]
                    {
                        new SpacAnimation
                        {
                            Frames = new[]
                            {
                                new SpacFrame { Sprites = sprites.ToArray(), TickLength = 60f }
                            }
                        }
                    }
                }
            }
        };
        _spacs[placeableId] = spac;
        Plugin.Log.LogInfo($"composite '{placeableId}': built spac with {sprites.Count} layer(s).");
        return spac;
    }

    // The standard custom-PNG sheet load (Texture2D.FromStream + flat default maps), cached per path+grid.
    private static SpriteSheet ResolveSheet(PartDef p, string dir)
    {
        string path = Path.Combine(dir ?? "", p.Sprite);
        string key = path + "|" + p.FrameWidth + "x" + p.FrameHeight;
        if (_sheets.TryGetValue(key, out SpriteSheet cached)) return cached;
        if (!File.Exists(path)) { Plugin.Log.LogWarning($"composite part sprite not found: {path}"); return null; }

        using FileStream fs = File.OpenRead(path);
        var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
        var sheet = new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap,
            p.FrameWidth > 0 ? p.FrameWidth : tex.Width,
            p.FrameHeight > 0 ? p.FrameHeight : tex.Height);   // 0/omitted = whole PNG is one frame
        _sheets[key] = sheet;
        return sheet;
    }

    // The one-state machine every composite client controller owns (per-instance: machines hold mutable
    // animation state). No transitions; "idle" loops its single frame forever.
    internal static EntitySpacAnimationStateMachine CreateStateMachine(Spac spac)
    {
        var sm = new EntitySpacAnimationStateMachine(spac,
            new[] { new CandideCreator.Shared.SpacSystem.State("idle", looping: true) },
            Array.Empty<CandideCreator.Shared.SpacSystem.Transition>());
        sm.SetState("idle");
        SpacRenderingArgs args = SpacRenderingArgs.Default;
        args.DrawOffScreen = true;       // parent may have no collision shape — the on-screen test would be
                                         // a point check and big composites would pop at screen edges
        args.UseEntityColors = false;    // always full-bright white tint (entity Color is not guaranteed white)
        sm.Args = args;
        return sm;
    }
}

// ------------------------------------------------ patches ------------------------------------------------

// Adjust the parent + register child blocker prototypes in the SERVER registry — same chokepoint as
// InteractiveServerPatch, patched AFTER it in Plugin.Awake so this postfix runs once the parent clone exists.
internal static class CompositeServerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerEntityDataManager), nameof(ServerEntityDataManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { CompositeFeature.RegisterPrototypes(ServerEntityDataManager.EntitySystems, ServerGlobals.Reflection, serverSide: true); }
        catch (Exception e) { Plugin.Log.LogWarning($"Composite server prototype pass failed: {e.Message}"); }
    }
}

// And in the CLIENT registry (co-op clients resolve replicated children by BaseId from here). Same Guids.
internal static class CompositeClientPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DoodadDatabaseManager), nameof(DoodadDatabaseManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { CompositeFeature.RegisterPrototypes(DoodadDatabaseManager.EntitySystems, Globals.Reflection, serverSide: false); }
        catch (Exception e) { Plugin.Log.LogWarning($"Composite client prototype pass failed: {e.Message}"); }
    }
}

// ----------------------------------------------- controllers -----------------------------------------------

// CLIENT: renders the composite. Builds the (cached) Spac, owns a per-instance state machine, registers it
// with the deferred renderer every frame — the exact static-spac-entity pattern vanilla controllers use.
public sealed class ForgeCompositeClientController : ForgeClientController
{
    private EntitySpacAnimationStateMachine _sm;

    public override void EntityInitialize()
    {
        try
        {
            string id = Parameters?.GetString("cf-composite", null);
            if (string.IsNullOrEmpty(id)) { Plugin.Log.LogWarning("forge_composite: no cf-composite parameter on spawned entity."); return; }
            Spac spac = CompositeFeature.BuildSpac(id);
            if (spac == null) return;   // warned inside; the clone base's art stays visible as feedback
            _sm = CompositeFeature.CreateStateMachine(spac);
            Entity.UsesSpac = true;     // spac path draws us; plain-sheet pass skips us (no workbench ghost)
        }
        catch (Exception e) { Plugin.Log.LogWarning($"forge_composite client init failed: {e.Message}"); }
    }

    public override void BeforeRender()
    {
        if (_sm != null && Entity != null) _sm.Draw(Entity);   // -> DeferredRenderer.RegisterEntitySpac
    }
}

// SERVER: owns the child blockers. First Update tick after every (re)spawn validates/spawns them (deferred
// out of EntityInitialize — see header); OnRemove cascades real removals to the children.
public sealed class ForgeCompositeServerController : AbstractController
{
    private bool _childrenChecked;

    public override void EntityInitialize()
    {
        _childrenChecked = false;
    }

    public override void Update(GameTime gameTime)
    {
        if (_childrenChecked) return;
        _childrenChecked = true;
        try { EnsureChildren(); }
        catch (Exception e) { Plugin.Log.LogWarning($"forge_composite child spawn failed: {e.Message}"); }
    }

    private void EnsureChildren()
    {
        string id = Parameters.GetString("cf-composite", null);
        if (string.IsNullOrEmpty(id)) return;
        if (!CompositeFeature.Composites.TryGetValue(id, out (PlaceableDef Def, string Dir) c))
        { Plugin.Log.LogWarning($"forge_composite: '{id}' not in any installed pack — children unchanged."); return; }

        Dictionary<int, Guid> have = ParseChildren();
        bool changed = false;
        for (int i = 1; i < c.Def.Parts.Count; i++)
        {
            PartDef p = c.Def.Parts[i];
            if (!CompositeFeature.HasCollision(p)) continue;
            if (have.TryGetValue(i, out Guid g) && ServerGameState.Entities.ContainsKey(g)) continue;   // alive — saved children reload on their own

            Vector2 off = CompositeFeature.CollisionOffset(p);
            ServerEntityModel sem = EntityServerManager.SpawnEntity(new SpawnEntityArgs
            {
                Id = Guid.NewGuid(),
                BaseId = CompositeFeature.PartGuid(id, i),
                WorldId = Entity.WorldId,
                Position = Entity.Position + new Vector3(off.X, off.Y, 0f),
                EntityType = EntityType.Doodad,
            });
            if (sem == null)
            { Plugin.Log.LogWarning($"forge_composite '{id}': part {i} blocker failed to spawn."); continue; }
            have[i] = sem.Id;
            changed = true;
        }
        if (changed)
            // Parameters.Dictionary IS the persisted model dictionary after spawn (see header) — this line
            // alone makes the child ids survive room unloads and saves.
            Parameters.Dictionary["cf-children"] = string.Join(",", have.Select(kv => kv.Key + ":" + kv.Value));
    }

    public override void OnRemove(EntityRemoveInfo entityRemoveInfo)
    {
        switch (entityRemoveInfo.RemoveType)
        {
            // lifecycle removals: the parent comes back (or the whole world goes) — children handle themselves
            case EntityRemoveType.Unloaded:
            case EntityRemoveType.RemoveFromSimulation:
            case EntityRemoveType.ChangingWorld:
            case EntityRemoveType.WorldRemoved:
            case EntityRemoveType.IncorrectPrediction:
            case EntityRemoveType.CorrectPrediction:
            case EntityRemoveType.ErrorDuringInitialization:
            case EntityRemoveType.Stored:
            case EntityRemoveType.Disconnected:
                return;
        }
        try
        {
            // real removal (demolish = Removed, damage death = Killed/OutOfHealth, area clears = None/Unknown):
            // take the children down with us, replicated to every client.
            foreach (Guid g in ParseChildren().Values)
                if (ServerGameState.Entities.ContainsKey(g))
                    EntityServerManager.RemoveEntity(g, new EntityRemoveInfo { RemoveType = entityRemoveInfo.RemoveType });
        }
        catch (Exception e) { Plugin.Log.LogWarning($"forge_composite child cascade failed: {e.Message}"); }
    }

    // "cf-children" = "partIndex:guid,partIndex:guid"
    private Dictionary<int, Guid> ParseChildren()
    {
        var have = new Dictionary<int, Guid>();
        string csv = Parameters?.GetString("cf-children", null);
        if (string.IsNullOrEmpty(csv)) return have;
        foreach (string tok in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int k = tok.IndexOf(':');
            if (k > 0 && int.TryParse(tok.Substring(0, k), out int pi) && Guid.TryParse(tok.Substring(k + 1), out Guid g))
                have[pi] = g;
        }
        return have;
    }
}
