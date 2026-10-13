using CandideServer;                                // ServerGlobals, ServerGameState, ServerRunState
using CandideServer.Database;                       // DungeonRoomDataBase
using CandideServer.Generators.Dungeon;             // ServerDungeonLoader
using CandideServer.MessageModels;                  // PlayVfxOnEntityMessage, PlaySfxOnEntityMessage
using CandideServer.MessageModels.Dungeons;         // ActiveDungeonMessage, DungeonLocationMessage
using CandideServer.MessageModels.PlayerCharacters; // EnterRoomMessage
using CandideServer.Models.Dungeons;                // DungeonLocationModel, DungeonLocationRoomModel, ActiveDungeonModel, DungeonState, DungeonTransitionArea, DungeonRoomDataModel
using CandideServer.Models.Worlds;                  // WorldModel
using CandideServer.ServerControllers;              // DungeonServerController
using CandideServer.ServerManagers;                 // DungeonServerManager, PlayerServerManager
using CandideServer.ServerServices;                 // DungeonServerService, PlayerServerService, ServerSoundAndVfxService, ServerJoinedPlayerModel
using CandideServer.SyncStrategies;                 // SyncStrategy
using Microsoft.Xna.Framework;                      // Vector3
using Shared.Entity;                                // EntityWrapper
using Shared.Entity.Base;                           // AbstractController
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// "forge_portal" — interactive placeable (Tier 3, see Interactive.cs) that teleports the interacting player
// into a Content-Forge custom map (a dungeon room registered by Maps.cs) from ANYWHERE: overworld, a
// building, or another room.
//
// WHY THE DUNGEON PATH (and not a raw world switch): interior worlds in this game are moved into by a
// client/server round-trip, not by a server-side entity move —
//   server: DungeonServerManager.EnterRoom -> "PlayerServerService.SendEnterDungeonRoom(new EnterRoomMessage
//           { Position = ..., NewWorldId = model.Id }, ServerGlobals.LastEventBusArgs)" (to ONE peer);
//   client: OnReceive_EnterRoom -> WorldManager.EnterInterior -> SwitchWorld (loads the cmx via
//           "OldInteriorWorldHandler.SetMainWorldAndClearActive(OldInteriorWorldHandler.Load(world.MapName))"),
//           spawns the local player at Position and replies "player-switch-world";
//   server: PlayerServerManager.PlayerSwitchWorld moves the authoritative player entity into the new world
//           and broadcasts "player-switched-world" to everyone else.
// (EntityServerManager.MoveEntity DOES support cross-world moves for plain entities — remove+respawn
// messages — but it never makes the moved player's own client load the destination map, so it is not the
// player path.)
//
// Crucially, the client's per-frame transition check HARD-REQUIRES dungeon context inside a Dungeon-type
// interior: "if (GameState.ActiveDungeon == null) ConnectService.ErrorDisconnect(Desync, ...)" — so a
// "bare" world switch without an ActiveDungeonModel + DungeonLocationModel would desync-kick the player on
// the next frame. The portal therefore mirrors DungeonServerManager.TryEnterDungeon/EnterInterior exactly,
// reusing the vanilla lazy world loader:
//   1) find (or fabricate, hidden) the DungeonLocationModel for the target room;
//   2) if it has no active dungeon: ServerDungeonLoader.GenerateDungeonLayout (first time only) +
//      TryLoadInDungeonLayout — which creates the interior WorldModels on demand
//      ("WorldSController.CreateInterior(dungeonRoomData.InteriorWorldMapId, InteriorType.Dungeon, room.Id,
//      temporary: true, ...)") and already sends the world + location to clients;
//   3) mirror EnterInterior: bump LastActive, set State, UpdateActiveDungeon, Send_ActiveDungeon (this is
//      what delivers the WorldModel to the entering client: OnReceive_UpdateActiveDungeon does
//      "WorldController.AddWorld(msg.WorldModel)"), then SendEnterDungeonRoom with OUR arrival position.
// Exiting back: the room's exit transport node works like any dungeon — the client's transition check calls
// WorldManager.ExitDungeon(), which returns the player to the overworld at the location's EntrancePosition.
// For a portal-fabricated location we point EntrancePosition at the PORTAL, so the exit drops the player
// right back where they left.
//
// Controller parameters (construction `controllerParams`, all strings):
//   map       pack map id of the destination (Maps.cs registers rooms under this id; vanilla dungeon-room
//             ids work too). REQUIRED.
//   x, y      arrival position in px inside the map. Default: the vanilla arrival point just inside the
//             room's exit node (so the player immediately knows the way out); last resort (no exit node):
//             160,160.
//   vfx       spac vfx played on the portal for everyone on use, default "" (off)
//   sfx       FMOD event played on the portal for everyone on use, default "" (off)
//   message   floating text shown by the CLIENT controller on interact (inherited ForgeClientController)
// ---------------------------------------------------------------------------------------------------------

// CLIENT portal: cosmetic only — the inherited base already shows the optional "tooltip" and floating
// "message" params; the actual move arrives as the vanilla "update-active-dungeon" + "enter-room" messages.
#if !CF_SERVER
public sealed class ForgePortalClientController : ForgeClientController
{
}
#endif

// SERVER portal: the authoritative "teleport player into map M at P" — a faithful mirror of
// DungeonServerManager.TryEnterDungeon + EnterInterior with a custom arrival position and no intro cinematic.
public sealed class ForgePortalServerController : AbstractController
{
    private string _map;
    private float _x, _y;          // NaN = not set
    private string _vfx;
    private string _sfx;

    public override void EntityInitialize()
    {
        // construction Parameters were merged into the controller before EntityInitialize (vanilla spawn path)
        _map = Parameters.GetString("map", "");
        _x = Parameters.GetFloat("x", float.NaN);
        _y = Parameters.GetFloat("y", float.NaN);
        _vfx = Parameters.GetString("vfx", "");
        _sfx = Parameters.GetString("sfx", "");
        if (string.IsNullOrWhiteSpace(_map))
            Plugin.Log.LogWarning("forge_portal: no 'map' parameter set — the portal will do nothing.");
    }

    public override void OnInteract(EntityWrapper otherEntity, bool interactionHeld)
    {
        if (interactionHeld) return;   // hold = vanilla pickup/demolish, not our tap action
        try
        {
            if (string.IsNullOrWhiteSpace(_map)) return;
            // only players can travel (the round-trip targets the interacting peer's client)
            if (!PlayerServerManager.TryGetPlayerFromEntity(otherEntity, out ServerJoinedPlayerModel _)) return;
            if (!DungeonRoomDataBase.TryGetDungeonRoomData(_map, out DungeonRoomDataModel data))
            { Plugin.Log.LogWarning($"forge_portal: map '{_map}' is not a registered dungeon room."); return; }

            // 1) the destination's DungeonLocationModel: reuse the world's own (rolled POI entrance or an
            //    earlier portal's), else fabricate a hidden one (no overworld stamp, no map icon).
            DungeonLocationModel location = FindOrCreateLocation(data);
            if (location == null) return;

            // While the portal stands in the overworld, keep the dungeon's return point on the portal:
            // client ExitDungeon = "MovePlayerToWorldAndPosition(GameState.OutsideWorldId,
            // value.EntrancePosition + value.ExitDirection.DirectionToVector3() * 16f)".
            if (Entity.System.WorldId == ServerRunState.ServerChunkedWorld.WorldId)
                location.EntrancePosition = Entity.Position;

            // vanilla TryEnterDungeon refuses entry while a boss fight has the world locked
            foreach (var bossFight in ServerRunState.ActiveBossFights.Values)
                if (bossFight.WorldId == location.Id && bossFight.WorldLocked) return;

            // 2) load the interior worlds on demand (exact TryEnterDungeon logic — vanilla also only creates
            //    the WorldModels on first enter).
            WorldModel entranceWorld = null;
            if (!ServerRunState.ActiveDungeons.TryGetValue(location.Id, out ActiveDungeonModel activeDungeon))
            {
                if (location.Rooms == null || location.Rooms.Count == 0)
                {
                    if (!ServerDungeonLoader.GenerateDungeonLayout(data, location))
                    { Plugin.Log.LogWarning($"forge_portal: failed to generate layout for '{_map}'."); return; }
                }
                // creates the WorldModels (runtime Guids = room ids; entrance room id == location.Id) and
                // sends "update-dungeon-location" to everyone + the entrance world to the current peer
                if (!ServerDungeonLoader.TryLoadInDungeonLayout(location, out entranceWorld, out activeDungeon))
                { Plugin.Log.LogWarning($"forge_portal: failed to load worlds for '{_map}'."); return; }
            }
            if (entranceWorld == null && !ServerGameState.Worlds.TryGetValue(activeDungeon.Id, out entranceWorld))
            { Plugin.Log.LogWarning($"forge_portal: active dungeon world for '{_map}' not in ServerGameState.Worlds."); return; }

            // keep every client's copy of the location fresh (the return point may have just moved)
            DungeonServerService.Send_DungeonLocationToEveryone(new DungeonLocationMessage { Location = location });

            // 3) mirror DungeonServerManager.EnterInterior (without the intro cinematic of the
            //    EnterDungeonMessage variant): this delivers the WorldModel to the entering client...
            activeDungeon.LastActive = ServerGameState.Time;
            bool firstTimeEntering = false;
            if (location.State != DungeonState.Cleared && location.State != DungeonState.Entered)
            {
                location.State = DungeonState.Entered;
                firstTimeEntering = true;
            }
            DungeonServerController.UpdateActiveDungeon(activeDungeon);
            DungeonServerService.Send_ActiveDungeon(new ActiveDungeonMessage
            {
                ActiveDungeon = activeDungeon,
                WorldModel = entranceWorld,
                FirstTimeEntering = firstTimeEntering
            });

            // ...and this makes that client switch world + reply "player-switch-world" (the server-side move).
            // OnInteract runs inside the "entity-interact" dispatch, so LastEventBusArgs is the interactor's
            // peer — the same way vanilla EnterRoom targets the entering player.
            PlayerServerService.SendEnterDungeonRoom(new EnterRoomMessage
            {
                NewWorldId = entranceWorld.Id,
                Position = ArrivalPosition(location)
            }, ServerGlobals.LastEventBusArgs);

            if (!string.IsNullOrEmpty(_vfx))
                ServerSoundAndVfxService.Send_PlayVfxOnEntity(new PlayVfxOnEntityMessage
                {
                    EntityId = Entity.Id,
                    SpacVfxId = _vfx
                });
            if (!string.IsNullOrEmpty(_sfx))
            {
                // 3D at the portal for bystanders…
                ServerSoundAndVfxService.Send_PlaySfxOnEntity(new PlaySfxOnEntityMessage
                {
                    FmodEventName = _sfx,
                    EntityId = Entity.Id
                }, SyncStrategy.SendToAll);
                // …and 2D to the TRAVELER: the 3D shot plays at the portal they just LEFT — by the time it
                // starts they're in the destination world, far outside positional falloff and hear nothing.
                if (ServerGlobals.LastEventBusArgs?.Peer != null)
                    ServerSoundAndVfxService.Send_PlaySfxToPeer(new PlaySfxMessage { FmodEventName = _sfx },
                        ServerGlobals.LastEventBusArgs.Peer);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"forge_portal interact failed: {e.Message}"); }
    }

    // Existing location with this room as its entrance, else a fabricated hidden one. Field set mirrors
    // DungeonServerManager.AddDungeonExterior minus PointOfInterestInserter.LoadInWorldAt (no exterior stamp).
    // The fabricated model lands in ServerGameState.DungeonLocations, so it persists with the save and is
    // found again next session.
    private DungeonLocationModel FindOrCreateLocation(DungeonRoomDataModel data)
    {
        try
        {
            foreach (DungeonLocationModel l in ServerGameState.DungeonLocations.Values)
                if (l.DataId == data.Id && !l.IsChallengeDungeon)
                    return l;

            var location = new DungeonLocationModel
            {
                Id = Guid.NewGuid(),
                NamePrefixKey = "Dungeons_Simple_DisplayName",
                Name = data.DisplayName,
                DataId = data.Id,
                IsChallengeDungeon = false,
                IsHidden = true,                                       // no overworld map icon
                State = DungeonState.Undiscovered,
                Rooms = new Dictionary<Guid, DungeonLocationRoomModel>(),
                ParentWorldId = ServerRunState.ServerChunkedWorld.WorldId,
                EntrancePosition = Entity.Position,                    // exit node returns the player here
                ExitDirection = data.ExitDirection,
                Difficulty = 0f,
                IconId = data.IconId ?? "dungeon_skull",
                BossDungeon = data.BossDungeon
            };
            DungeonServerController.UpdateDungeonLocation(location);   // ServerGameState.DungeonLocations[id] = location
            return location;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"forge_portal: location lookup for '{_map}' failed: {e.Message}");
            return null;
        }
    }

    // Explicit x/y, else the vanilla arrival point just inside the room's exit transition area (the same
    // GetTransitionAreaPosition(area, 6f) the dungeon-entrance flow uses), else a fixed fallback for rooms
    // built with "exit": false.
    private Vector3 ArrivalPosition(DungeonLocationModel location)
    {
        if (!float.IsNaN(_x) && !float.IsNaN(_y)) return new Vector3(_x, _y, 0f);
        int exitDirection = ((int)location.ExitDirection + 2) % 4;
        if (location.Rooms.TryGetValue(location.Id, out DungeonLocationRoomModel room)
            && room.TransitionAreas.TryGetValue(exitDirection, out DungeonTransitionArea area))
            return DungeonServerManager.GetTransitionAreaPosition(area, 6f);
        return new Vector3(160f, 160f, 0f);
    }
}
