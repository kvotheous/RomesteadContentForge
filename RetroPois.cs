using CandideServer;                      // ServerGameState, ServerRunState, ServerGlobals
using CandideServer.Database;             // PoiDataBase
using CandideServer.Entities;             // ServerEntityModel
using CandideServer.Models;               // PoiData, ServerWorldChunk, ServerChunkedWorld
using CandideServer.Models.Dungeons;      // DungeonLocationModel
using CandideServer.Saving;               // GameSaveManager
using CandideServer.ServerManagers;       // PoiServerManager, WorldFlagsServerManager
using HarmonyLib;
using Microsoft.Xna.Framework;            // Point, Rectangle, Vector2
using Shared.Data;                        // TileDataBase
using Shared.Models;                      // WorldTile, WorldFlag
using System;
using System.Collections.Generic;
using RectangleF = CandideCreator.Shared.Models.RectangleF;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Retrofit dungeon entrances into EXISTING saves ("retrofit": true on a MapDef that also lists biomes).
//
// WHY THIS WORKS: vanilla "entrances roll in NEW worlds only" is not an engine limit — it is just that
// PoiServerManager.GeneratePois only runs inside WorldGenerator. The placement primitive itself,
// PoiServerManager.SpawnPoi -> DungeonServerManager.AddHandcraftedDungeonExterior ->
// PointOfInterestInserter.LoadInWorldAt, is runtime-callable: the game itself ships a "spawn-poi-cheat"
// network message that calls SpawnPoi on a LIVE loaded world (WorldService.SpawnPoiCheat in the client).
// LoadInWorldAt mutates ServerGameState.WorldTiles + spawns the exterior's entities (incl. the
// DungeonEntrance carrying the locationId), and AddHandcraftedDungeonExterior creates + registers the
// DungeonLocationModel — exactly what world-gen does for a rolled POI. Everything lands in ServerGameState,
// so it persists with the save.
//
// SYNC IS FREE AT THIS HOOK: tiles/entities/crops stream to clients PER CHUNK on demand (server replies to
// chunk loads with a ChunkDataMessage built from live ServerGameState; the client rebuilds tiles, map and
// collision from it, and server collision is rebuilt per chunk in ChunkServerManager.ChunkLoaded). We run in
// a postfix of GameSaveManager.LoadGame — host world load, AFTER every "servergamestate-loaded" handler
// (the event is published inside LoadGame) and BEFORE any client (even the host's own) has joined or loaded
// a single chunk — so nobody can hold a stale copy of the mutated area. No mid-session terrain resync needed.
//
// THE ONE THING VANILLA SKIPS FOR US: LoadInWorldAt stamps with updateTileHeights:false; at world-gen the
// generator recomputes ALL heights afterwards (SetWorldTileHeights), but on a loaded world the heights were
// derived from WorldTiles BEFORE our stamp — so we re-derive them for the stamped rectangle ourselves
// (same formula the loader uses: TileDataBase.Structures[structure].Height).
//
// PLACEMENT: random candidate tiles, accepted when the stamp rectangle (+1 tile fringe) is entirely inside a
// requested biome (ServerGameState.BiomeIds — same "biome:forest" ids the POI roll matches), structure-free,
// not road/farmland ground, empty of entities & crops (chunk lists), and far from the world spawn, towns,
// buildings, saved player positions and other dungeon entrances. If no clear spot is found we log and simply
// try again on the next load (the world flag is only set after a location verifiably exists).
//
// ONCE PER SAVE: world flag "cf_retrofit_<mapId>" (WorldFlags persist in ServerGameState). A save whose
// world-gen already rolled the POI (or that was retrofitted before) is marked done without placing anything.
// ---------------------------------------------------------------------------------------------------------

internal static class RetroPoisFeature
{
    private const int Attempts = 2500;            // candidate tiles tried per map per load (cheap checks)
    private const float MinDistSpawn = 48f;       // tiles from the world spawn point
    private const float MinDistTown = 40f;        // tiles from any town center
    private const float MinDistBuilding = 24f;    // tiles from any building center
    private const float MinDistPlayer = 40f;      // tiles from any saved overworld player position
    private const float MinDistDungeon = 24f;     // tiles from any existing dungeon entrance

    // grounds we refuse to stamp over: player farmland and every road type (vanilla town/route work)
    private static readonly HashSet<WorldTile.GroundType> BlockedGround = new()
    {
        WorldTile.GroundType.Farmland, WorldTile.GroundType.FarmlandWet,
        WorldTile.GroundType.CobbleStoneRoad, WorldTile.GroundType.CobbleStoneRoadStraight,
        WorldTile.GroundType.DirtRoad, WorldTile.GroundType.BrickRoad, WorldTile.GroundType.BrickOrangeRoad,
        WorldTile.GroundType.ConcreteRoad, WorldTile.GroundType.ConcreteRoadStraight,
        WorldTile.GroundType.MarbleRoad, WorldTile.GroundType.MarbleBrickRoad,
        WorldTile.GroundType.MarbleRoadOvergrown, WorldTile.GroundType.TerracottaRoad,
        WorldTile.GroundType.BasaltRoad,
    };

    public static void RetrofitAll()
    {
        foreach ((MapDef m, _) in ContentLoader.Maps)
        {
            if (!m.Retrofit || m.Biomes == null || m.Biomes.Count == 0) continue;
            try { RetrofitOne(m); }
            catch (Exception e) { Plugin.Log.LogWarning($"retrofit '{m?.Id}' failed: {e.Message}"); }
        }
    }

    private static void RetrofitOne(MapDef m)
    {
        string flag = "cf_retrofit_" + m.Id;
        if (ServerGameState.WorldFlags.TryGetValue(flag, out WorldFlag done) && done.IntValue1 == 1) return;

        // A visible location for this room already exists in this save (rolled at world-gen, or an earlier
        // retrofit before the flag was introduced): mark done, never place a second entrance. Portal-fabricated
        // locations are IsHidden and have no overworld stamp — those do NOT count.
        if (FindVisibleLocation(m.Id) != null)
        {
            WorldFlagsServerManager.SetFlagValue(flag, 1, null);
            Plugin.Log.LogInfo($"retrofit '{m.Id}': entrance already exists in this save — marked done.");
            return;
        }

        string poiId = "cf:tiny:dungeon:" + m.Id;   // same id Maps.cs registers in RegisterPois
        if (!PoiDataBase.TryGetPoiData(poiId, out PoiData poi))
        { Plugin.Log.LogWarning($"retrofit '{m.Id}': POI '{poiId}' is not registered (biomes set?)."); return; }

        Point size = PoiServerManager.GetWorldSizeForPoi(poi.Map);
        if (size.X <= 0 || size.Y <= 0)
        { Plugin.Log.LogWarning($"retrofit '{m.Id}': exterior map '{poi.Map}' has no tile data."); return; }

        if (!TryPickCenter(m, size, out Point center))
        {
            Plugin.Log.LogInfo($"retrofit '{m.Id}': no clear {size.X}x{size.Y} spot found this load — will retry next load.");
            return;
        }

        // The vanilla runtime placement primitive (same call the spawn-poi-cheat makes). For a
        // HandcraftedDungeon POI only bounds.Center is used: stamp tiles + spawn the exterior's entities
        // (the DungeonEntrance gets the locationId) + create/register the DungeonLocationModel.
        Vector2 centerVec = center.ToVector2();
        float difficulty = ServerGameState.Difficulties[center.X, center.Y];
        PoiServerManager.SpawnPoi(poi, new RectangleF(centerVec), difficulty, ServerGlobals.Random);

        // Re-derive TileHeights for the stamped rectangle (see header: vanilla only recomputes at world-gen).
        Point tl = PoiServerManager.GetSpawnPointForWorldInSlot(poi.Map, centerVec);
        for (int y = tl.Y; y < tl.Y + size.Y; y++)
            for (int x = tl.X; x < tl.X + size.X; x++)
                ServerRunState.TileHeights[x, y] =
                    TileDataBase.Structures[(uint)ServerGameState.WorldTiles[x, y].Structure].Height;

        // Only mark the save done once the location verifiably exists (SpawnPoi logs-and-returns on failure).
        DungeonLocationModel placed = FindVisibleLocation(m.Id);
        if (placed != null)
        {
            WorldFlagsServerManager.SetFlagValue(flag, 1, null);
            Plugin.Log.LogInfo($"retrofit '{m.Id}': entrance placed at tile {center.X},{center.Y} "
                + $"({ServerGameState.BiomeIds[center.X, center.Y]}), entrance px {placed.EntrancePosition.X:F0},{placed.EntrancePosition.Y:F0}.");
        }
        else
            Plugin.Log.LogWarning($"retrofit '{m.Id}': SpawnPoi did not create a dungeon location (see server warnings).");
    }

    private static DungeonLocationModel FindVisibleLocation(string mapId)
    {
        foreach (DungeonLocationModel l in ServerGameState.DungeonLocations.Values)
            if (l.DataId == mapId && !l.IsChallengeDungeon && !l.IsHidden)
                return l;
        return null;
    }

    private static bool TryPickCenter(MapDef m, Point size, out Point center)
    {
        ServerChunkedWorld world = ServerRunState.ServerChunkedWorld;
        Point ws = world.WorldSize;
        Random rnd = ServerGlobals.Random;
        int halfW = size.X / 2, halfH = size.Y / 2;
        // candidate center range keeping stamp + 2-tile fringe inside the world
        int minX = halfW + 2, maxX = ws.X - (size.X - halfW) - 2;
        int minY = halfH + 2, maxY = ws.Y - (size.Y - halfH) - 2;
        if (maxX <= minX || maxY <= minY) { center = default; return false; }

        for (int i = 0; i < Attempts; i++)
        {
            int cx = rnd.Next(minX, maxX), cy = rnd.Next(minY, maxY);
            if (!m.Biomes.Contains(ServerGameState.BiomeIds[cx, cy] ?? "")) continue;   // cheap reject first
            var rect = new Rectangle(cx - halfW - 1, cy - halfH - 1, size.X + 2, size.Y + 2); // stamp + fringe
            if (!AreaTilesClear(rect, m.Biomes)) continue;
            if (AreaHasEntitiesOrCrops(rect)) continue;
            if (!FarFromEveryone(new Vector2(cx, cy))) continue;
            center = new Point(cx, cy);
            return true;
        }
        center = default;
        return false;
    }

    // every tile in the rect: requested biome, no structure (water/cliffs/walls/crops), allowed ground
    private static bool AreaTilesClear(Rectangle rect, List<string> biomes)
    {
        for (int y = rect.Top; y < rect.Bottom; y++)
            for (int x = rect.Left; x < rect.Right; x++)
            {
                if (!biomes.Contains(ServerGameState.BiomeIds[x, y] ?? "")) return false;
                WorldTile t = ServerGameState.WorldTiles[x, y];
                if (t.Structure != WorldTile.StructureType.None) return false;
                if (BlockedGround.Contains(t.Ground)) return false;
            }
        return true;
    }

    // trees/rocks/props/chests are all entities — require the rect to be a natural clearing
    private static bool AreaHasEntitiesOrCrops(Rectangle tileRect)
    {
        ServerChunkedWorld world = ServerRunState.ServerChunkedWorld;
        Point c0 = world.TileToChunkPosition(new Point(tileRect.Left, tileRect.Top));
        Point c1 = world.TileToChunkPosition(new Point(tileRect.Right - 1, tileRect.Bottom - 1));
        float l = tileRect.Left * 16f, r = tileRect.Right * 16f, t = tileRect.Top * 16f, b = tileRect.Bottom * 16f;
        for (int cy = c0.Y; cy <= c1.Y; cy++)
            for (int cx = c0.X; cx <= c1.X; cx++)
            {
                ref ServerWorldChunk chunk = ref world.GetWorldChunk(cx, cy);
                if (chunk.Entities != null)
                    foreach (ServerEntityModel e in chunk.Entities)
                        if (e.Position.X >= l && e.Position.X < r && e.Position.Y >= t && e.Position.Y < b)
                            return true;
                if (chunk.Crops != null)
                    foreach (Point p in chunk.Crops.Keys)
                        if (tileRect.Contains(p))
                            return true;
            }
        return false;
    }

    // keep clear of the spawn, towns, buildings, saved player positions and other entrances (all in TILES)
    private static bool FarFromEveryone(Vector2 centerTile)
    {
        if (Vector2.Distance(centerTile, ServerGameState.SpawnTile.ToVector2()) < MinDistSpawn) return false;

        foreach (var town in ServerGameState.Towns.Values)
            if (Vector2.Distance(centerTile, town.Model.TileBounds.Center.ToVector2()) < MinDistTown) return false;

        foreach (var building in ServerGameState.Buildings.Values)
            if (Vector2.Distance(centerTile, building.InstanceModel.TileBounds.Center.ToVector2()) < MinDistBuilding) return false;

        Guid outsideId = ServerRunState.OutsideWorld.Id;
        foreach (var pc in ServerGameState.PlayerCharacters.Values)
            if (pc.Character != null && pc.Character.WorldId == outsideId
                && Vector2.Distance(centerTile, new Vector2(pc.Character.Position.X, pc.Character.Position.Y) / 16f) < MinDistPlayer)
                return false;

        foreach (DungeonLocationModel l in ServerGameState.DungeonLocations.Values)
            if (l.ParentWorldId == outsideId && !l.IsHidden
                && Vector2.Distance(centerTile, new Vector2(l.EntrancePosition.X, l.EntrancePosition.Y) / 16f) < MinDistDungeon)
                return false;

        return true;
    }
}

// Host-only by construction: GameSaveManager.LoadGame runs when the hosting process loads a save (it
// publishes "servergamestate-loaded" itself, so every vanilla load handler has finished before this postfix),
// and no client — not even the host's local one — has joined or loaded chunks yet.
internal static class RetroPoiPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameSaveManager), nameof(GameSaveManager.LoadGame))]
    private static void Postfix()
    {
        try { ContentLoader.EnsureParsed(Plugin.PacksDir); RetroPoisFeature.RetrofitAll(); }
        catch (Exception e) { Plugin.Log.LogWarning($"POI retrofit failed: {e.Message}"); }
    }
}
