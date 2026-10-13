#if !CF_SERVER
using Candide;                              // Globals (client CandideReflection)
using Candide.Database.Doodad;              // DoodadDatabaseManager (client entity registry)
using Candide.Entities;                     // ClientEntityControllerLoader
#endif
using CandideCreator.Shared.Serialization;  // CandideReflection
using CandideServer;                        // ServerGlobals (server CandideReflection)
using CandideServer.Entities;               // ServerEntityControllerLoader, ServerEntityDataManager
using CandideServer.MessageModels;          // PlayVfxOnEntityMessage, PlaySfxOnEntityMessage
using CandideServer.ServerManagers;         // EntityServerManager, AuraServerManager, PlayerServerManager, SimpleInventoryServerManager
using CandideServer.ServerServices;         // ServerSoundAndVfxService, ServerJoinedPlayerModel
using CandideServer.SyncStrategies;         // SyncStrategy
using HarmonyLib;
using Microsoft.Xna.Framework;              // GameTime, Rectangle
using Shared;                               // EntityIds
using Shared.Aura.Args;                     // HealingOverTimeAuraArgs
using Shared.Entity;                        // EntitySystem, EntityWrapper, EntityCopyHelper
using Shared.Entity.Base;                   // AbstractController
using Shared.Entity.Components;             // Component, ControllerComponent
using Shared.Models.Construction;           // ConstructionModel, ConstructionType, ConstructionSpaceInfo, ...
using Shared.Models.Interaction;            // InteractionType
using Shared.Text;                          // StringId
#if !CF_SERVER
using Candide.CandideUI;                    // PlayerUi (talking furniture opens the dialogue window)
using Candide.GameModels.Managers;          // CandideDialogueContext
using Candide.Graphics;                     // FloatingTextSystem (client interact message)
#endif
using System;
using System.Collections.Generic;
using System.Text;                          // StringBuilder (GetToolTip)

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Tier 3 — INTERACTIVE furniture: a pack furniture entry that, instead of placing a static decoration,
// places a real ENTITY with a server-authoritative controller (press E -> the SERVER controller runs the
// effect; clients only see the replicated results). Three pieces, all mirroring established game flows:
//
//   1) ENTITY PROTOTYPE — like custom creatures (ContentLoader.RegisterCreatures), we deep-copy a vanilla
//      entity prototype (`cloneEntity`: a Shared.EntityIds field name or a raw Guid) into BOTH entity
//      registries under a deterministic Guid, then give the clone our controller + the Interactable mask.
//      Vanilla wires controllers to prototypes exactly this way at doodad-DB load (an instance is created
//      on the prototype's wrapper; every spawn re-clones it):
//        cs2 (BaseDoodad AfterDeserialize): "AbstractController abstractController =
//          ServerEntityControllerLoader.CreateController(controllerFormValue.Name, entityWrapper); ...
//          entityWrapper.Controller = abstractController;"
//        sh EntityCopyHelper.DeepCopyEntity: "targetSystem.ControllerComponent[targetId].Controller =
//          sourceSystem.ControllerComponent[sourceId].Controller.Clone(wrapper);" (+ AttachEntity w/ params,
//          + EntityInitialize when initializeControllers).
//
//   2) CONTROLLER TYPES — both sides resolve controllers through a public name->Type map
//      (ClientEntityControllerLoader.ControllerMap / ServerEntityControllerLoader.ControllerMap). The
//      vanilla Initialize() only scans the game's own assemblies, so our types are inserted by Initialize
//      postfixes (and defensively again before registration). CLIENT and SERVER get DIFFERENT types under
//      the SAME key — exactly like vanilla's GateController vs ServerGateController split across
//      assemblies — so a controller never needs an "am I the server?" runtime check: server types carry
//      the effects, client types are cosmetic no-ops. Registered types need a parameterless ctor
//      (ReflectionHelper.CreateInstance<AbstractController>(value)).
//
//   3) CONSTRUCTION — the build-menu entry is a ConstructionModel with Type=ConstructionType.Entity and
//      SpawnedId = our prototype Guid as a string; the game's construction-complete code does
//      "BaseId = Guid.Parse(construction.SpawnedId)" and spawns it as a normal entity (cs2 SpawnConstruction).
//      Per-furniture controller parameters ride ConstructionModel.Parameters: SpawnConstruction merges them
//      into the spawn parameters, and the server spawn does
//      "entityWrapper.Controller.Parameters.AddOrOverwrite(entityModel.Parameters)" before EntityInitialize
//      (the client spawn path is symmetric), so the controller reads them like any vanilla doodad parameter.
//
// Interact round-trip (all verified): client interaction scan needs Mask Component.Interactable
// (+ Controller) and the controller's GetInteraction (base default = new Interaction(InteractionType.Entity,
// Entity)); pressing interact sends "entity-interact" AND calls the CLIENT controller's OnInteract locally
// (cosmetic only — keep it a no-op); the server handler EntityServerManager.EntityInteract requires
// Component.Controller and calls the SERVER controller's OnInteract(playerEntityWrapper).
//
// Built-in presets (usable from pack JSON with no author C#):
//   "forge_shrine" — on interact (per-entity cooldown): heal the player (vanilla healing-over-time aura,
//                    server-authoritative + replicates), optionally give an item, play vfx/sfx on the shrine.
//   "forge_lever"  — on interact: toggle controller State 0/1 via EntityServerManager.UpdateEntityState
//                    (replicates to every client's controller OnServerSetState), optional vfx.
// ---------------------------------------------------------------------------------------------------------

internal static class InteractiveFeature
{
    // Registered controller behaviors: pre-normalized name -> (client type, server type). Both types must
    // derive from AbstractController and have a parameterless ctor. Authors of future C# add-ons can call
    // RegisterController before the world loads; packs reference the name in furniture.controller.
    internal static readonly Dictionary<string, (Type Client, Type Server)> ForgeControllers = new()
    {
        ["forge_shrine"] = (ClientType("ForgeShrineClientController"), typeof(ForgeShrineServerController)),
        ["forge_lever"] = (ClientType("ForgeLeverClientController"), typeof(ForgeLeverServerController)),
        ["forge_dialogue"] = (ClientType("ForgeDialogueClientController"), typeof(ForgeDialogueServerController)),
        ["forge_prop"] = (ClientType("ForgePropClientController"), typeof(ForgeDialogueServerController)),   // inert prop (custom-sprite decor); server side reuses the no-op
        ["forge_portal"] = (ClientType("ForgePortalClientController"), typeof(ForgePortalServerController)),  // teleports into a pack map (Portals.cs)
    };

    // A client controller type by name. Client controllers derive from client-assembly types, so the
    // dedicated-server build (CF_SERVER) doesn't contain them: there this returns null, which is fine —
    // a server only ever injects/creates the Server half.
    internal static Type ClientType(string name) => typeof(InteractiveFeature).Assembly.GetType("RomesteadContentForge." + name);

    public static void RegisterController(string name, Type clientType, Type serverType)
    {
        ForgeControllers[NormalizeControllerName(name)] = (clientType, serverType);
    }

    // Interactive furniture defs collected at pack-parse time (BuildPack -> TryBuildEntityConstruction);
    // consumed when the doodad databases load (RegisterEntities, once per side per game load).
    private static readonly List<(PlaceableDef Def, string Dir)> _interactiveDefs = new();
    // base guids of placeables marked indestructible — IndestructiblePatch blocks their player-removal
    internal static readonly HashSet<Guid> IndestructibleBases = new();

    // ------------------------------------------- construction -------------------------------------------

    // Build the construction for a furniture def with a controller. Mirrors ContentLoader.TryBuildPlaceable
    // (Tier 1) except Type=Entity: SpawnedId is our prototype Guid, and the controller params ride
    // ConstructionModel.Parameters (merged into the spawned entity's controller by the vanilla spawn path).
    internal static bool TryBuildEntityConstruction(PlaceableDef f, string pack, string dir, out ConstructionModel cm)
    {
        cm = null;
        if (f == null || string.IsNullOrWhiteSpace(f.Id) || string.IsNullOrWhiteSpace(f.CloneEntity))
        { Plugin.Log.LogWarning($"[{pack}] interactive furniture skipped (needs id + cloneEntity)."); return false; }
        string controller = NormalizeControllerName(f.Controller);
        if (controller.Length == 0)
        { Plugin.Log.LogWarning($"[{pack}] furniture '{f.Id}' skipped (empty controller name)."); return false; }
        if (!ForgeControllers.ContainsKey(controller))
            // Not one of ours: it may still be a VANILLA controller name (both vanilla maps already hold
            // those, e.g. "gate"/"bed"). We build optimistically; registration warns+skips if unresolved.
            Plugin.Log.LogInfo($"[{pack}] furniture '{f.Id}': controller '{controller}' is not a Forge preset — " +
                "assuming it is a vanilla controller name.");

        int w = f.Width < 1 ? 1 : f.Width;
        int h = f.Height < 1 ? 1 : f.Height;
        cm = new ConstructionModel
        {
            Id = f.Id,
            Name = string.IsNullOrWhiteSpace(f.Name) ? f.Id : f.Name,                  // rewritten to loc key by AddConstructions
            Description = string.IsNullOrWhiteSpace(f.Description) ? (StringId?)null : f.Description,
            IconId = string.IsNullOrWhiteSpace(f.Icon) ? "sam" : f.Icon,
            UpgradeLevel = 0,
            RequiredConstructionProgress = 0f,
            ConstructionMaterialsRequirement = new ConstructionMaterialsAggregate
            {
                MaterialAmounts = f.Materials ?? new Dictionary<string, int>()
            },
            ConstructionSpaceInfo = new ConstructionSpaceInfo
            {
                Areas = new[]
                {
                    new ConstructionTileArea
                    {
                        TileBounds = new Rectangle(0, 0, w, h),
                        Requirements = ConstructionSpaceInfo.PlacementRequirements.FreeTilesAndNoFurniture
                    }
                },
                PlacingStyle = ConstructionSpaceInfo.PlacementStyle.Single
            },
            Type = ConstructionType.Entity,
            SpawnedId = EntityGuid(f.Id).ToString(),     // construction spawn does Guid.Parse(SpawnedId)
            Parameters = (f.ControllerParams != null && f.ControllerParams.Count > 0) ? f.ControllerParams : null,
            ConstructionUiCategory = ConstructionUiCategory.Decorations
        };

        if (!_interactiveDefs.Exists(d => d.Def.Id == f.Id)) _interactiveDefs.Add((f, dir));
        return true;
    }

    // ------------------------------------------- prototypes -------------------------------------------

    // Register the interactive-furniture entity prototypes into one side's entity registry. Called from a
    // LoadDoodadDatabase postfix on each side (same chokepoint as ContentLoader.RegisterCreatures), so the
    // vanilla base prototypes exist. Idempotent: skips Guids already present.
    internal static void RegisterEntities(Dictionary<Guid, EntitySystem> protos, CandideReflection reflection, bool serverSide)
    {
        ContentLoader.EnsureParsed(Plugin.PacksDir);
        if (_interactiveDefs.Count == 0) return;

        // Make sure this side's controller map exists and contains our types. The Initialize postfixes
        // normally already did this; calling again is harmless (Initialize() early-outs when filled, and
        // injection uses indexer assignment).
        if (serverSide) { ServerEntityControllerLoader.Initialize(); InjectControllerTypes(serverSide: true); }
#if !CF_SERVER
        else { ClientEntityControllerLoader.Initialize(); InjectControllerTypes(serverSide: false); }
#endif

        foreach ((PlaceableDef f, string dir) in _interactiveDefs)
        {
            try
            {
                Guid guid = EntityGuid(f.Id);
                if (protos.ContainsKey(guid)) continue;
                if (!TryResolveEntityBase(f.CloneEntity, out Guid baseGuid) || !protos.TryGetValue(baseGuid, out EntitySystem src))
                { Plugin.Log.LogWarning($"furniture '{f.Id}': cloneEntity '{f.CloneEntity}' not found in entity registry."); continue; }

                var clone = new EntitySystem(1);
                int id = clone.NewEntity();   // slot 0 (keeps its own Wrapper/IncId, which DeepCopyEntity preserves)
                EntityCopyHelper.DeepCopyEntity(src, 0, clone, id, initializeControllers: false, ignoreControllers: true, reflection);

                clone.General[0].Id = guid;
                clone.General[0].BaseGuid = guid;
                if (!string.IsNullOrWhiteSpace(f.Name)) clone.General[0].Name = f.Name;

                // Attach THIS side's controller to the prototype wrapper (the vanilla doodad-load pattern).
                // CreateController lowercases/strips the name, news the type up and calls AttachEntity.
                EntityWrapper wrapper = clone.General[0].Wrapper;
                string name = NormalizeControllerName(f.Controller);
#if CF_SERVER
                AbstractController controller = ServerEntityControllerLoader.CreateController(name, wrapper);
#else
                AbstractController controller = serverSide
                    ? ServerEntityControllerLoader.CreateController(name, wrapper)
                    : ClientEntityControllerLoader.CreateController(name, wrapper);
#endif
                if (controller == null)
                { Plugin.Log.LogWarning($"furniture '{f.Id}': controller '{name}' unknown on the {(serverSide ? "server" : "client")} side — not registered."); continue; }

                // Replace whatever the base prototype carried (DeepCopyEntity with ignoreControllers still
                // shallow-copies the component; also drops any base Ai, which belonged to the base's controller).
                clone.ControllerComponent[0] = new ControllerComponent { Controller = controller };
                clone.Mask[0] |= Component.Controller | Component.Interactable;
                // Strip the inherited drop table: the base entity (e.g. a workbench) drops ITSELF as an item
                // when demolished — so demolishing a shrine handed back a vanilla workbench, and re-placing
                // that item built a vanilla workbench:0. Interactive placeables demolish cleanly instead
                // (their build recipe is permanently unlocked, so the real one can always be rebuilt).
                clone.Mask[0] &= ~Component.DropTable;
                if (f.Indestructible)
                {
                    // no Health = can't be damaged to death; IndestructiblePatch blocks demolish/removal
                    clone.Mask[0] &= ~Component.Health;
                    IndestructibleBases.Add(guid);
                }
                wrapper.InteractionType = InteractionType.Entity;   // default scan path ("press E" prompt)
                wrapper.IsInteractionArea = false;

#if !CF_SERVER
                SpriteSwap.Apply(clone, f, dir, serverSide);        // Tier 2 custom sprite (no-op without f.Sprite; client-only)
#endif

                protos[guid] = clone;
                Plugin.Log.LogInfo($"Registered interactive furniture '{f.Id}' (clone of {f.CloneEntity}, controller {name}, {(serverSide ? "server" : "client")}).");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"interactive furniture '{f?.Id}' failed: {e.Message}"); }
        }
    }

    // Insert our controller types into one side's name->Type map. Idempotent (indexer assignment); keys are
    // pre-normalized (lowercase, no "controller" suffix) to match what CreateController looks up.
    internal static void InjectControllerTypes(bool serverSide)
    {
#if CF_SERVER
        if (!serverSide) return;   // no client controller map on a dedicated server
        Dictionary<string, Type> map = ServerEntityControllerLoader.ControllerMap;
#else
        Dictionary<string, Type> map = serverSide
            ? ServerEntityControllerLoader.ControllerMap
            : ClientEntityControllerLoader.ControllerMap;
#endif
        if (map == null) return;   // client map is null until ClientEntityControllerLoader.Initialize ran
        foreach (var kv in ForgeControllers)
            map[kv.Key] = serverSide ? kv.Value.Server : kv.Value.Client;
    }

    // Same normalization CreateController applies to a looked-up name, so pack values like "ForgeShrineController"
    // and "forge_shrine" land on the same key.
    private static string NormalizeControllerName(string name)
    {
        name = (name ?? "").Trim().ToLowerInvariant();
        if (name.EndsWith("controller")) name = name.Remove(name.Length - 10);
        return name;
    }

    // A furniture's clone source: a Shared.EntityIds field name (e.g. "WorkbenchGuid") or a raw Guid string.
    private static bool TryResolveEntityBase(string nameOrGuid, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(nameOrGuid)) return false;
        if (Guid.TryParse(nameOrGuid, out id)) return true;
        var field = AccessTools.Field(typeof(EntityIds), nameOrGuid);
        if (field == null || field.FieldType != typeof(Guid)) return false;
        id = (Guid)field.GetValue(null);
        return true;
    }

    // Stable Guid from the furniture id (MD5-based, deterministic across machines -> co-op-safe). The
    // "forge-tier3:" prefix keeps it disjoint from ContentLoader's creature Guids even for an identical id.
    private static Guid EntityGuid(string id)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("forge-tier3:" + id)));
    }
}

// ------------------------------------------------ patches ------------------------------------------------

// Insert our controller types right after the game builds the SERVER controller map (Initialize scans only
// the game's own assembly, so mod types must be added afterwards; Initialize early-outs once filled, so this
// postfix is the reliable "just after it filled" hook).
internal static class InteractiveControllerMapServerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerEntityControllerLoader), nameof(ServerEntityControllerLoader.Initialize))]
    private static void Postfix()
    {
        try { InteractiveFeature.InjectControllerTypes(serverSide: true); }
        catch (Exception e) { Plugin.Log.LogWarning($"Interactive-furniture server controller-map inject failed: {e.Message}"); }
    }
}

#if !CF_SERVER
// Same for the CLIENT controller map (built in RomesteadGame.Initialize).
internal static class InteractiveControllerMapClientPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ClientEntityControllerLoader), nameof(ClientEntityControllerLoader.Initialize))]
    private static void Postfix()
    {
        try { InteractiveFeature.InjectControllerTypes(serverSide: false); }
        catch (Exception e) { Plugin.Log.LogWarning($"Interactive-furniture client controller-map inject failed: {e.Message}"); }
    }
}
#endif

// Register the furniture entity prototypes into the SERVER registry right after the vanilla doodad DB loads
// (same chokepoint as CreatureServerPatch — the base prototypes we clone are present from here on).
internal static class InteractiveServerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerEntityDataManager), nameof(ServerEntityDataManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { InteractiveFeature.RegisterEntities(ServerEntityDataManager.EntitySystems, ServerGlobals.Reflection, serverSide: true); }
        catch (Exception e) { Plugin.Log.LogWarning($"Interactive-furniture server entity registration failed: {e.Message}"); }
    }
}

#if !CF_SERVER
// And into the CLIENT registry (renders the entity + provides the cosmetic controller). Same Guid both sides.
internal static class InteractiveClientPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DoodadDatabaseManager), nameof(DoodadDatabaseManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { InteractiveFeature.RegisterEntities(DoodadDatabaseManager.EntitySystems, Globals.Reflection, serverSide: false); }
        catch (Exception e) { Plugin.Log.LogWarning($"Interactive-furniture client entity registration failed: {e.Message}"); }
    }
}
#endif

// Indestructible placeables: block player-initiated removal (demolish, kill) of marked entities. Unloading
// (world save/exit) must always pass or the entity would leak across world reloads.
internal static class IndestructiblePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityServerManager), nameof(EntityServerManager.RemoveEntity), typeof(Guid), typeof(EntityRemoveInfo))]
    private static bool Prefix(Guid guid, EntityRemoveInfo info)
    {
        try
        {
            if (InteractiveFeature.IndestructibleBases.Count == 0) return true;
            if (info.RemoveType == EntityRemoveType.Unloaded) return true;   // world unload/save must pass
            if (ServerGameState.Entities.TryGetValue(guid, out var model)
                && model.EntityWrapper != null
                && InteractiveFeature.IndestructibleBases.Contains(model.EntityWrapper.BaseGuid))
                return false;   // swallow the removal — the structure is permanent
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Indestructible check failed: {e.Message}"); }
        return true;
    }
}

// ----------------------------------------------- controllers -----------------------------------------------

#if !CF_SERVER
// CLIENT base: gameplay no-op — every real effect happens on the server and arrives as replicated
// state/vfx/aura/inventory messages. What IS client-side here is pure presentation: a hover tooltip
// (params "tooltip", optional "tooltip-color" #rrggbb) and a floating message on interact (params
// "message", optional "message-color"). The client's interaction scan only needs the type to exist
// (the inherited GetInteraction default makes the "press E" prompt appear).
public abstract class ForgeClientController : AbstractController
{
    public override void EntityInitialize() { }

    // drives the optional Tier-2 ambient sprite animation ("sprite-frames"/"sprite-frame-duration" params)
    public override void Update(GameTime gameTime) => ForgeSpriteAnimator.Tick(this);

    public override (string?, Color?) GetToolTip(StringBuilder tooltipBuilder)
    {
        string tip = Parameters?.GetString("tooltip", null);
        return tip == null ? (null, null) : (tip, ParseColor(Parameters.GetString("tooltip-color", null)) ?? Color.White);
    }

    public override void OnInteract(EntityWrapper otherEntity, bool interactionHeld)
    {
        if (interactionHeld) return;   // hold = vanilla pickup/demolish, not our tap action
        string msg = Parameters?.GetString("message", null);
        if (string.IsNullOrEmpty(msg) || Entity == null) return;
        FloatingTextSystem.AddNewFloatText(msg, Entity.Position2, ParseColor(Parameters.GetString("message-color", null)) ?? Color.LightGreen);
    }

    protected static Color? ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int v)) return null;
        return new Color((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }
}

public sealed class ForgeShrineClientController : ForgeClientController
{
}

public sealed class ForgeLeverClientController : ForgeClientController
{
    // State arrives via "update-entity-state" -> OnServerSetState (base just stores it). Kept minimal on
    // purpose: presets don't assume anything about the cloned entity's sprites.
}

// TALKING furniture: interact opens a Yarn dialogue window — the same call the satyr NPC controller makes.
// The framework compiles the furniture's `yarn` file through the NPC dialogue pipeline and injects the
// "yarn-path"/"yarn-node" parameters; Forge dialogue commands (<<ForgeGiveItem>>, <<ForgeHeal>>, ...) work
// inside, so a statue can bless, a tombstone can trade. Dialogue is client-side (same rule as NPCs).
public sealed class ForgeDialogueClientController : ForgeClientController
{
    public override void OnInteract(EntityWrapper otherEntity, bool interactionHeld)
    {
        if (interactionHeld) return;   // hold = vanilla pickup/demolish, not our tap action
        base.OnInteract(otherEntity, interactionHeld);   // optional floating "message" param
        try
        {
            string path = Parameters?.GetString("yarn-path", null);
            if (string.IsNullOrWhiteSpace(path) || Entity == null) return;
            string node = Parameters.GetString("yarn-node", "Start");
            float dist = 32f;
            string d = Parameters.GetString("dialogue-distance", null);
            if (d != null) float.TryParse(d, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out dist);
            if (dist <= 0f) dist = 32f;   // unparseable value: TryParse zeroes `dist`, which closes the window at once
            PlayerUi.ShowDialogueWindow(new CandideDialogueContext
            {
                CurrentCitizenId = null,
                EntityId = Entity.Id,
                OnDialogueClosing = null,
            }, path, Entity, dist <= 0f ? 32f : dist, node);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"forge_dialogue interact failed: {e.Message}"); }
    }
}

// Sprite-only decor: inherits the tooltip/message/animation plumbing from the client base; nothing else.
public sealed class ForgePropClientController : ForgeClientController
{
}
#endif

// Server side of the talking furniture: nothing to simulate — the dialogue runs on each client, and any
// gameplay effects go through the Forge dialogue commands' own server calls.
public sealed class ForgeDialogueServerController : AbstractController
{
    public override void EntityInitialize() { }
}

// SERVER shrine: on interact — heal the interacting player and/or hand them an item, with a per-shrine
// cooldown, plus vfx/sfx on the shrine entity. Controller parameters (from furniture.controllerParams):
//   cooldown    seconds between uses (shared per placed shrine), default 30
//   heal        total health restored, default 0 (off). Applied as the vanilla healing-over-time aura
//               ("aura:slow_heal") spread over 5 seconds — server-authoritative, replicated, and clamped to
//               MaxHealth by the game's own healing pipeline (the same one heal scrolls/potions use).
//   give-item   item id to add to the player's inventory, default "" (off)
//   give-amount how many of give-item, default 1
//   vfx         spac vfx played on the shrine for everyone, default "dust_poof_big" ("" = off)
//   sfx         FMOD event name played on the shrine for everyone, default "" (off)
public sealed class ForgeShrineServerController : AbstractController
{
    private float _cooldownSeconds;
    private float _cooldownRemaining;
    private float _heal;
    private string _giveItem;
    private int _giveAmount;
    private string _vfx;
    private string _sfx;

    public override void EntityInitialize()
    {
        // Spawn merges the construction's controllerParams into Parameters BEFORE EntityInitialize runs.
        _cooldownSeconds = Parameters.GetFloat("cooldown", 30f);
        _heal = Parameters.GetFloat("heal", 0f);
        _giveItem = Parameters.GetString("give-item", "");
        _giveAmount = Parameters.GetInt("give-amount", 1);
        _vfx = Parameters.GetString("vfx", "dust_poof_big");
        _sfx = Parameters.GetString("sfx", "");
    }

    public override void Update(GameTime gameTime)
    {
        if (_cooldownRemaining > 0f) _cooldownRemaining -= Entity.Fdt;   // vanilla furniture timers use Fdt
    }

    public override void OnInteract(EntityWrapper otherEntity, bool interactionHeld)
    {
        if (interactionHeld) return;   // hold = vanilla pickup/demolish, not our tap action
        try
        {
            if (_cooldownRemaining > 0f) return;
            if (!PlayerServerManager.TryGetPlayerFromEntity(otherEntity, out ServerJoinedPlayerModel player)) return;
            _cooldownRemaining = _cooldownSeconds < 0f ? 0f : _cooldownSeconds;

            if (_heal > 0f)
                AuraServerManager.TryApplyAuraToEntity("aura:slow_heal", otherEntity.Id, Entity.Id,
                    new HealingOverTimeAuraArgs
                    {
                        HealingPerTick = _heal / 5f,
                        TimeBetweenTicks = 1f,
                        Duration = 5f,
                    }, SyncStrategy.SendToAll);

            if (!string.IsNullOrEmpty(_giveItem))
            {
                Guid inventoryId = player.CharacterModel.Character.CharacterInventoryModel.InventoryId;
                if (!SimpleInventoryServerManager.TryAddItemAmounts(inventoryId, _giveItem,
                        _giveAmount < 1 ? 1 : _giveAmount, null, allowPartialStack: true, targetSlotIndex: null))
                    Plugin.Log.LogInfo($"forge_shrine: could not add '{_giveItem}' (inventory full or unknown item).");
            }

            if (!string.IsNullOrEmpty(_vfx))
                ServerSoundAndVfxService.Send_PlayVfxOnEntity(new PlayVfxOnEntityMessage
                {
                    EntityId = Entity.Id,
                    SpacVfxId = _vfx
                });
            if (!string.IsNullOrEmpty(_sfx))
                ServerSoundAndVfxService.Send_PlaySfxOnEntity(new PlaySfxOnEntityMessage
                {
                    FmodEventName = _sfx,
                    EntityId = Entity.Id
                }, SyncStrategy.SendToAll);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"forge_shrine interact failed: {e.Message}"); }
    }
}

// SERVER lever: on interact — toggle State 0/1, replicated to every client (and persisted: the entity model's
// ControllerState is what UpdateEntityState writes). Other mod code can poll the state via
// ServerGameState.Entities[id].ControllerState or react in a future controller. Parameters:
//   vfx   spac vfx played on the lever on every toggle, default "" (off)
public sealed class ForgeLeverServerController : AbstractController
{
    private string _vfx;

    public override void EntityInitialize()
    {
        _vfx = Parameters.GetString("vfx", "");
        if (State == -1) OnServerSetState(0);   // fresh spawn: start "off" (mirrors ServerGateController)
    }

    public override void OnInteract(EntityWrapper otherEntity, bool interactionHeld)
    {
        if (interactionHeld) return;   // hold = vanilla pickup/demolish, not our tap action
        try
        {
            int next = State == 1 ? 0 : 1;
            // updateSimulationState:true routes back into this controller's OnServerSetState (sets State),
            // updates the persisted ServerEntityModel.ControllerState, and broadcasts UpdateEntityStateMessage
            // to clients (their controller's OnServerSetState runs).
            EntityServerManager.UpdateEntityState(Entity.Id, next, updateSimulationState: true, SyncStrategy.SendToAll);

            if (!string.IsNullOrEmpty(_vfx))
                ServerSoundAndVfxService.Send_PlayVfxOnEntity(new PlayVfxOnEntityMessage
                {
                    EntityId = Entity.Id,
                    SpacVfxId = _vfx
                });
        }
        catch (Exception e) { Plugin.Log.LogWarning($"forge_lever interact failed: {e.Message}"); }
    }
}
