#if !CF_SERVER
using Candide;                              // Globals (client reflection)
using Candide.Database.Doodad;              // DoodadDatabaseManager (client entity registry)
using Candide.Entities;                     // ClientEntityControllerLoader
#endif
using CandideCreator.Shared.Collision;      // CollisionRectangle
using CandideCreator.Shared.Models;         // World, WorldDoodad
using CandideCreator.Shared.Serialization;  // CandideReflection
using CandideServer;                        // ServerGlobals
using CandideServer.Entities;               // ServerEntityControllerLoader, ServerEntityDataManager
using HarmonyLib;
using Microsoft.Xna.Framework;              // Matrix, Vector3
using Shared;                               // EntityIds
using Shared.Entity;                        // EntitySystem, EntityWrapper, EntityCopyHelper
using Shared.Entity.Base;                   // AbstractController
using Shared.Entity.Components;             // Component, ControllerComponent, CollisionComponent
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;      // RuntimeHelpers

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Pre-placed DOODADS (props) in pack maps: a map's `doodads` list drops entities into the generated cmx —
// either a vanilla/pack ENTITY by name ("CampFire", a raw Guid, a pack creature id, a pack cloneEntity
// placeable id) or a pack PNG (`sprite`) that becomes its own inert prop, optionally with a collision box
// and an ambient frame animation (the Sprites.cs conventions).
//
// HOW A MAP DOODAD BECOMES AN ENTITY (all verified against the decompiles):
//   - At generation time we append raw WorldDoodad instances exactly like NpcsFeature.AddNpcDoodads
//     (ModelToWorld = Identity BEFORE Position — the Position setter writes the translation into the
//     matrix; field initializers don't run on GetUninitializedObject). The link to the base entity travels
//     as CandideSerializer extra data — the SAME per-doodad 6-tuple NpcsFeature registers a getter for in
//     ConfigureReflection (Maps.cs BuildReflection wires it), so our doodads simply ride that getter: we
//     insert our tuples into NpcsFeature's private `_doodadExtra` dictionary (AccessTools — same assembly,
//     same shape, one handler for every forge doodad). The per-world database list
//     ("doodads"/"db/doodads") getter already fires for ANY world with doodads
//     (NpcsFeature.ConfigureReflection: "w.Doodads.Count > 0 ? _worldDbList : null").
//   - The SERVER resolves strictly by Guid against the entity registry (cs2 ServerWorldDataManager
//     .OnWorldRead, reached via LoadMapFiles with the BARE ServerDataSystem.Reflection — no AfterDeserialize
//     culling): "if (guid != default(Guid) && ServerEntityDataManager.EntitySystems.TryGetValue(guid, out
//     EntitySystem value5))" -> a ServerEntitySpawnData per doodad, classified by the prototype's mask:
//     Controller -> Complex, else Collision -> StaticCollision (its prototype Shape is ALSO added to the
//     room's EntityTerrainGroup), else None.
//   - When a player enters the room, EVERY spawn-data entry becomes a real replicated entity (cs2
//     WorldSController.NewWorld -> ServerWorldDataManager.SpawnWorldEntities: "EntitySController.SpawnEntity
//     (spawnEntityArgs)" for each, no SpawnType filter), and the client rebuilds it from its OWN prototype
//     registry by BaseId (rdll EntityController.SpawnEntity -> SpawnEntityBase ->
//     "DoodadDatabaseManager.SpawnDoodad(baseId, GameState.EntitySystem, ...)"). So entity doodads need no
//     registration at all (vanilla prototypes are in both registries), and sprite doodads need a prototype
//     CLONE in both registries — registered below at LoadDoodadDatabase time, the NpcBodies chokepoint
//     (before LoadMapFiles resolves, present on co-op clients that never generate maps).
//
// SPRITE PROPS reuse the placeable Tier-2/Tier-3 machinery wholesale: clone the WorkbenchGuid entity (the
// established simple base every Tier-3 example uses), attach the "forge_prop" controller pair (client:
// ForgePropClientController — its Update ticks ForgeSpriteAnimator for the ambient animation; server:
// the no-op ForgeDialogueServerController), strip Interactable (no "press E" prompt on scenery), DropTable
// (no workbench item on death) and Health (a map author's prop can't be punched away), then
// SpriteSwap.Apply paints the author PNG (UsesSpac=false, EntityMesh=null -> the simple-quad render path).
//
// COLLISION is a component, not geometry: an entity blocks movement when its Mask has Component.Collision
// and its CollisionComponent.Shape overlaps the mover (sh CollisionSpecialGroup.CheckEntityCollision skips
// only on "entity.NoEntityCollision || (entity.NoCollisionGroupMask & mask) != 0 || (entity.
// CollisionGroupMask & noCollisionMask) != 0" — all zero/false on our clone -> blocks everyone; the shape's
// default Height3D=160000 blocks at every height). So per-doodad collision = a per-prop prototype clone with
// CollisionComponent[0].Shape = new CollisionRectangle(w, h) (its ctor centers the offset: "Offset = new
// Vector2((0f - Width) / 2f, ...)") and the Collision mask flag; `collision` omitted = the flag stripped =
// pure walk-through decor. Each sprite doodad already owns its own clone (deterministic MD5 Guid of
// "forge-doodad:" + its id or mapId#index), so per-doodad sizing is free. Entity doodads keep their
// vanilla collision (the `collision` field is ignored there, with a warning).
//
// NOT PLAYER-REMOVABLE: spawned doodads are EntityType.PlacedInEditor — not constructions, so the demolish
// tool can't see them (rdll: demolition goes through building/decoration/construction registries plus
// hardcoded BaseGuid whitelists — TryRemoveConstructionAtTile walks town BuildingIds,
// TryRemoveFortificationInTileBounds checks "current.BaseGuid != EntityIds.CoalTrap && ... Catapult ..."
// etc.). Sprite props additionally lose Component.Health, so they can't be destroyed by damage either.
// Entity doodads keep their vanilla nature: a Chest is lootable, a DestructibleBush is destructible.
//
// CO-OP / SAVES: same rules as the dialogue-NPC doodads — every player needs the pack installed (the
// prototypes and PNGs are local); dungeon interiors are temporary worlds regenerated from the cmx each
// visit, and the cmx regenerates from the pack each launch, so edits apply on restart and nothing persists
// into saves.
// ---------------------------------------------------------------------------------------------------------

internal static class DoodadsFeature
{
    // The doodad database key both loaders resolve through (same constants as NpcsFeature, private there).
    private const string DbDirectory = "db/doodads";
    private const string DbFileName = "doodads";

    // NpcsFeature's per-doodad extra-data dictionary — its ConfigureReflection getter (wired LAST in Maps.cs
    // BuildReflection) serializes every entry, so writing ours into it gives all forge doodads ONE handler.
    // Private there; same assembly, so the field is ours to read via AccessTools (logged loudly if renamed).
    private static Dictionary<WorldDoodad, (string, string, Guid, string, string, string)> _extra;
    private static Dictionary<WorldDoodad, (string, string, Guid, string, string, string)> NpcExtra()
        => _extra ??= (Dictionary<WorldDoodad, (string, string, Guid, string, string, string)>)
            AccessTools.Field(typeof(NpcsFeature), "_doodadExtra")?.GetValue(null);

    // ------------------------------------------------------------------ placement (map-generation time)

    // Called by MapsFeature.GenerateOne right next to AddNpcDoodads, before the world serializes. Appends
    // one WorldDoodad per `doodads` entry of this map. UniqueIds continue after the NPC doodads' (AddNpcDoodads
    // runs first and leaves world.NextDoodadId at the next free id).
    public static void AddMapDoodads(World world, string mapId)
    {
        foreach ((MapDef m, _) in ContentLoader.Maps)
        {
            if (m.Id != mapId || m.Doodads == null || m.Doodads.Count == 0) continue;

            var extra = NpcExtra();
            if (extra == null)
            { Plugin.Log.LogWarning($"map '{mapId}': NpcsFeature._doodadExtra not found — doodads skipped."); return; }

            ushort uid = (ushort)Math.Max(1, world.NextDoodadId);
            int placed = 0;
            for (int i = 0; i < m.Doodads.Count; i++)
            {
                DoodadDef dd = m.Doodads[i];
                string label = Label(m, dd, i);
                try
                {
                    if (dd == null) continue;
                    if (dd.Position == null || dd.Position.Length < 2)
                    { Plugin.Log.LogWarning($"doodad '{label}': not placed (needs position [x,y] px)."); continue; }

                    bool isSprite = !string.IsNullOrWhiteSpace(dd.Sprite);
                    bool isEntity = !string.IsNullOrWhiteSpace(dd.Entity);
                    if (isSprite == isEntity)
                    { Plugin.Log.LogWarning($"doodad '{label}': needs exactly ONE of `entity` or `sprite`."); continue; }

                    Guid guid;
                    string name;
                    if (isSprite)
                    {
                        guid = SpriteDoodadGuid(m, dd, i);   // the prototype clone registered below
                        name = label;
                    }
                    else
                    {
                        if (!TryResolveEntity(dd.Entity, out guid))
                        {
                            Plugin.Log.LogWarning($"doodad '{label}': entity '{dd.Entity}' is not an EntityIds name, "
                                + "a Guid, a pack creature id or a pack cloneEntity-placeable id — not placed.");
                            continue;
                        }
                        name = dd.Entity;
                        if (dd.Collision != null)
                            Plugin.Log.LogWarning($"doodad '{label}': `collision` is ignored for entity doodads "
                                + "(the vanilla entity keeps its own collision) — only sprite doodads take it.");
                    }

                    // Raw construction, mirroring AddNpcDoodads: WorldDoodad's only ctor wants a live
                    // BaseDoodad (none exists until the db loads) but Base is [Unmanaged] (never serialized).
                    // ModelToWorld = Identity MUST precede Position (the setter writes the translation into
                    // the matrix; field initializers don't run on an uninitialized object).
                    var d = (WorldDoodad)RuntimeHelpers.GetUninitializedObject(typeof(WorldDoodad));
                    d.ModelToWorld = Matrix.Identity;
                    d.EditorColor = Vector3.One;
                    d.UniqueId = uid++;
                    // cmx stores editor coordinates (X, height, Y); both loaders swap with Position.ToXzy()
                    d.Position = new Vector3(dd.Position[0], 0f, dd.Position[1]);

                    // (Item1 != null marks "has a base reference"; Item2 = database key; Item3 = the Guid
                    // BOTH loaders resolve by; Item4/Item5/Item6 = directory/path/name — warning text plus
                    // the client's root-folder-only name fallback, which never matches our names: resolution
                    // is Guid-driven, exactly like the NPC body clones.)
                    extra[d] = (DbFileName, DbDirectory, guid, DbDirectory, "cf", name);
                    world.Doodads.Add(d);
                    placed++;
                    Plugin.Log.LogInfo($"Placed doodad '{label}' ({(isSprite ? "sprite " + dd.Sprite : dd.Entity)}) "
                        + $"in '{mapId}' at ({dd.Position[0]}, {dd.Position[1]}).");
                }
                catch (Exception e) { Plugin.Log.LogWarning($"doodad '{label}': placement failed: {e.Message}"); }
            }
            if (placed > 0) world.NextDoodadId = uid;
        }
    }

    // ------------------------------------------------------------------ sprite-prop prototypes (db-load time)

    // Register one prototype clone per sprite doodad into one side's entity registry. Called from the
    // LoadDoodadDatabase postfixes below — the same chokepoint and timing as NpcBodies.RegisterBodies (the
    // vanilla bases exist, LoadMapFiles hasn't resolved yet, and it runs on co-op clients that never
    // generate maps because it enumerates ContentLoader.Maps itself). Idempotent: skips present Guids.
    internal static void RegisterSpriteProtos(Dictionary<Guid, EntitySystem> protos, CandideReflection reflection, bool serverSide)
    {
        ContentLoader.EnsureParsed(Plugin.PacksDir);
        bool injected = false;
        foreach ((MapDef m, string dir) in ContentLoader.Maps)
        {
            if (m.Doodads == null) continue;
            for (int i = 0; i < m.Doodads.Count; i++)
            {
                DoodadDef dd = m.Doodads[i];
                if (dd == null || string.IsNullOrWhiteSpace(dd.Sprite)) continue;
                string label = Label(m, dd, i);
                try
                {
                    Guid guid = SpriteDoodadGuid(m, dd, i);
                    if (protos.ContainsKey(guid)) continue;   // already registered this side (DB reloads re-run)
                    if (!protos.TryGetValue(EntityIds.WorkbenchGuid, out EntitySystem src))
                    { Plugin.Log.LogWarning($"doodad '{label}': workbench base prototype missing — prop skipped."); continue; }

                    // Make sure this side's controller map exists and holds the forge types (the Interactive
                    // Initialize postfixes normally already did; calling again is harmless).
                    if (!injected)
                    {
                        injected = true;
                        if (serverSide) { ServerEntityControllerLoader.Initialize(); InteractiveFeature.InjectControllerTypes(serverSide: true); }
#if !CF_SERVER
                        else { ClientEntityControllerLoader.Initialize(); InteractiveFeature.InjectControllerTypes(serverSide: false); }
#endif
                    }

                    var clone = new EntitySystem(1);
                    int id = clone.NewEntity();   // slot 0 (keeps its own Wrapper/IncId, preserved by DeepCopyEntity)
                    EntityCopyHelper.DeepCopyEntity(src, 0, clone, id, initializeControllers: false, ignoreControllers: true, reflection);

                    clone.General[0].Id = guid;
                    clone.General[0].BaseGuid = guid;
                    clone.General[0].Name = label;
                    clone.General[0].Immovable = true;   // scenery never gets nudged/knocked around

                    // The inert prop controller pair: client ticks the ambient sprite animation + optional
                    // tooltip params, server is a no-op. Same vanilla attach pattern as InteractiveFeature.
                    EntityWrapper wrapper = clone.General[0].Wrapper;
#if CF_SERVER
                    AbstractController controller = ServerEntityControllerLoader.CreateController("forge_prop", wrapper);
#else
                    AbstractController controller = serverSide
                        ? ServerEntityControllerLoader.CreateController("forge_prop", wrapper)
                        : ClientEntityControllerLoader.CreateController("forge_prop", wrapper);
#endif
                    if (controller == null)
                    { Plugin.Log.LogWarning($"doodad '{label}': forge_prop controller unknown on the {(serverSide ? "server" : "client")} side — prop skipped."); continue; }
                    clone.ControllerComponent[0] = new ControllerComponent { Controller = controller };
                    clone.Mask[0] |= Component.Controller;
                    // Scenery: no "press E" prompt, no workbench item on removal, no health to punch away.
                    clone.Mask[0] &= ~(Component.Interactable | Component.DropTable | Component.Health);
                    wrapper.IsInteractionArea = false;

                    if (dd.Collision != null && dd.Collision.Length >= 2 && dd.Collision[0] > 0f && dd.Collision[1] > 0f)
                    {
                        // Blocks everyone: zero masks pass every CheckEntityCollision skip test (header),
                        // and CollisionRectangle centers itself on the entity position. Base fields
                        // (masks/step heights) stay as the workbench had them.
                        clone.Mask[0] |= Component.Collision;
                        ref CollisionComponent cc = ref clone.CollisionComponent[0];
                        cc.Shape = new CollisionRectangle(dd.Collision[0], dd.Collision[1]);
                    }
                    else
                        clone.Mask[0] &= ~Component.Collision;   // walk-through decor

                    // The Tier-2 sprite swap (UsesSpac=false, EntityMesh=null, animation params onto the
                    // controller — must run AFTER the controller is attached; serverSide => visual no-op).
#if !CF_SERVER
                    SpriteSwap.Apply(clone, Synthetic(dd, label), dir, serverSide);
#endif

                    protos[guid] = clone;
                    Plugin.Log.LogInfo($"Registered doodad prop '{label}' ('{dd.Sprite}'"
                        + $"{(dd.Collision != null && dd.Collision.Length >= 2 ? $", collision {dd.Collision[0]}x{dd.Collision[1]}" : ", walk-through")}"
                        + $", {(serverSide ? "server" : "client")}).");
                }
                catch (Exception e) { Plugin.Log.LogWarning($"doodad '{label}' prop failed: {e.Message}"); }
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string Label(MapDef m, DoodadDef d, int i)
        => d != null && !string.IsNullOrWhiteSpace(d.Id) ? d.Id : m.Id + ":doodad#" + i;

    // Deterministic per-prop Guid (MD5 like Plugin.GuidFromId — stable across machines, co-op-safe). The
    // "forge-doodad:" prefix keeps it disjoint from creature and Tier-3 placeable Guid spaces. Explicit
    // `id`s survive list reordering; index-keyed ones change when entries are inserted above them (harmless:
    // maps regenerate each launch and nothing persists).
    private static Guid SpriteDoodadGuid(MapDef m, DoodadDef d, int i)
        => NpcBodies.GuidFromId("forge-doodad:" + (!string.IsNullOrWhiteSpace(d.Id) ? d.Id : m.Id + "#" + i));

    // A doodad's entity reference: raw Guid, pack creature id, vanilla Shared.EntityIds field name (mirrors
    // Plugin.TryResolveCreature / InteractiveFeature.TryResolveEntityBase), or a pack PLACEABLE id — Tier-3
    // cloneEntity placeables register a prototype under MD5("forge-tier3:" + id) (InteractiveFeature
    // .EntityGuid's construction). A Tier-1 (decoration) placeable id has no prototype: the map loader then
    // warns "Could not find a base doodad ... when loading map" and skips that doodad.
    private static bool TryResolveEntity(string name, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (Guid.TryParse(name, out id)) return true;
        if (ContentLoader.CustomCreatureGuids.TryGetValue(name, out id)) return true;
        var f = AccessTools.Field(typeof(EntityIds), name);
        if (f != null && f.FieldType == typeof(Guid)) { id = (Guid)f.GetValue(null); return true; }
        if (ContentLoader.PlaceableRecipeIds.Contains(name))
        {
            Plugin.Log.LogInfo($"doodad entity '{name}': treating as a pack placeable (must be a cloneEntity "
                + "placeable — a plain decoration has no entity prototype and the map loader will skip it).");
            id = NpcBodies.GuidFromId("forge-tier3:" + name);
            return true;
        }
        return false;
    }

    // SpriteSwap.Apply speaks PlaceableDef — hand it the doodad's sprite fields under our label.
    private static PlaceableDef Synthetic(DoodadDef d, string label) => new()
    {
        Id = label,
        Sprite = d.Sprite,
        SpriteFrameWidth = d.SpriteFrameWidth,
        SpriteFrameHeight = d.SpriteFrameHeight,
        SpriteFrames = d.SpriteFrames,
        SpriteFrameDuration = d.SpriteFrameDuration,
        SpriteOffset3D = d.SpriteOffset3D,
    };
}

// Register sprite-prop prototypes into the SERVER entity registry right after the vanilla doodad DB loads
// (same chokepoint + timing as NpcBodyServerPatch — bases exist, LoadMapFiles hasn't resolved yet).
internal static class DoodadServerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerEntityDataManager), nameof(ServerEntityDataManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { DoodadsFeature.RegisterSpriteProtos(ServerEntityDataManager.EntitySystems, ServerGlobals.Reflection, serverSide: true); }
        catch (Exception e) { Plugin.Log.LogWarning($"Doodad prop server registration failed: {e.Message}"); }
    }
}

#if !CF_SERVER
// And into the CLIENT entity registry (renders the PNG + ticks the ambient animation). Same Guid both sides.
internal static class DoodadClientPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DoodadDatabaseManager), nameof(DoodadDatabaseManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { DoodadsFeature.RegisterSpriteProtos(DoodadDatabaseManager.EntitySystems, Globals.Reflection, serverSide: false); }
        catch (Exception e) { Plugin.Log.LogWarning($"Doodad prop client registration failed: {e.Message}"); }
    }
}
#endif
