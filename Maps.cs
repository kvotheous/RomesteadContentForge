using CandideCreator.Shared.Models;
using CandideCreator.Shared.Serialization;
using CandideServer.Data.ServerDataSetups;
using CandideServer.Data.SharedDataSetups;
using CandideServer.Database;
using CandideServer.Models;
using CandideServer.Models.Dungeons;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Custom maps (dungeon rooms): a pack ships a standard Tiled .tmx; this feature
//   1) generates the binary .cmx companion (a near-empty CandideCreator World — the floor mesh and collision
//      auto-generate from the tmx's `ground`/`structure` tile layers) and copies both into Content/maps/cf/
//      right before the server scans that folder (prefix on SetupDataFileLoading),
//   2) registers the room (DungeonRoomDataBase, postfix on ServerDataSetup.Setup — after maps have loaded),
//   3) registers an overworld POI entrance (PoiDataBase, postfix on SharedDataSetup.SharedSetup), reusing a
//      vanilla exterior map that already contains the DungeonEntrance entity.
// The player enters via the POI's dungeon entrance; one TransportNode in the cmx (facing opposite the room's
// exitDirection) is the way out — stepping in it returns to the overworld next to the entrance.
// ---------------------------------------------------------------------------------------------------------

internal static class MapsFeature
{
    private static bool _filesDone;

    // Resolve <game>/Content from the BepInEx install (plugins live in <game>/BepInEx/plugins).
    private static string ContentDir => Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath, "Content"));

    internal static string WorldName(MapDef m) => "maps/cf/" + m.Id.Replace(':', '_');

    // 1) Generate Content/maps/cf/<id>.tmx + .cmx for every pack map. Runs in a PREFIX of
    // ServerDataSetup.SetupDataFileLoading so the files exist when LoadMapFiles scans Content/maps.
    public static void GenerateFiles()
    {
        if (_filesDone) return;
        _filesDone = true;
        foreach ((MapDef m, string dir) in ContentLoader.Maps)
        {
            try { GenerateOne(m, dir); }
            catch (Exception e) { Plugin.Log.LogWarning($"map '{m?.Id}': generation failed: {e.Message}"); }
        }
    }

    private static void GenerateOne(MapDef m, string packDir)
    {
        string srcTmx = Path.Combine(packDir, m.Tmx);
        if (!File.Exists(srcTmx)) { Plugin.Log.LogWarning($"map '{m.Id}': tmx '{m.Tmx}' not found next to the pack."); return; }
        TiledObjectsFeature.ApplyNpcPositions(m, TiledObjectsFeature.Harvest(m, packDir));   // Tiled `doodads`/`npcs` object layers — must precede AddNpcDoodads/AddMapDoodads

        string name = WorldName(m);                                  // e.g. maps/cf/mymod_crypt
        string outDir = Path.Combine(ContentDir, "maps", "cf");
        Directory.CreateDirectory(outDir);
        string file = Path.GetFileName(name);
        string dstTmx = Path.Combine(outDir, file + ".tmx");
        string dstCmx = Path.Combine(outDir, file + ".cmx");

        File.Copy(srcTmx, dstTmx, overwrite: true);

        // Map pixel size from the tmx (for default exit-node placement + player start).
        var doc = new XmlDocument();
        doc.Load(srcTmx);
        var mapEl = doc.DocumentElement;
        int tw = int.Parse(mapEl.GetAttribute("tilewidth"));
        int th = int.Parse(mapEl.GetAttribute("tileheight"));
        int pw = int.Parse(mapEl.GetAttribute("width")) * tw;
        int ph = int.Parse(mapEl.GetAttribute("height")) * th;

        // `ground`/`structure` are data-only (floor mesh + collision) — a tmx with no other tile layer renders
        // pitch black. Synthesize a plain cave floor in that case (overwrites the plain copy above; the
        // author's source tmx is never touched). Only the OUTPUT changes, and only when no art layer exists.
        try { SynthesizeFloorIfMissing(m, doc, mapEl, dstTmx); }
        catch (Exception e) { Plugin.Log.LogWarning($"map '{m.Id}': auto-floor synthesis failed ({e.Message}) — using the tmx as-is."); }

        var world = (World)Activator.CreateInstance(typeof(World), nonPublic: true);
        world.Name = name;
        world.TiledMapFileName = name;
        world.PlayerStartPosition = new Vector3(pw / 2f, ph / 2f, 0f);

        // Atmosphere: the World model carries its own music/ambience tracks and lighting — expose them so a
        // map can feel like a different realm (its own soundtrack, its own light).
        if (!string.IsNullOrWhiteSpace(m.Music))
            world.MusicAudioTrack = new WorldAudioTrack(m.Music,
                new WorldAudioTrackArgs(m.MusicVolume <= 0f ? 1f : m.MusicVolume, 1f, "", 0f));
        if (!string.IsNullOrWhiteSpace(m.Ambience))
            world.AmbienceAudioTrack = new WorldAudioTrack(m.Ambience, new WorldAudioTrackArgs(1f, 1f, "", 0f));
        if (m.AmbientLight != null && m.AmbientLight.Length >= 3)
            world.LightingProperties.Ambient = new Color(m.AmbientLight[0], m.AmbientLight[1], m.AmbientLight[2]);

        // A node box on an edge, facing INTO the room: edge 0=E 1=S 2=W 3=N -> Direction (edge+2)%4.
        // (The cmx stores editor coordinates (X, height, Y); the loader swaps to runtime (X, Y, Z).)
        void AddNode(string nodeName, int edge, float[] box, string targetMap)
        {
            float[] n = (box != null && box.Length >= 4) ? box : edge switch
            {
                0 => new float[] { pw - 32f, ph / 2f - 24f, 24f, 48f },   // east edge, centered
                2 => new float[] { 8f, ph / 2f - 24f, 24f, 48f },          // west
                3 => new float[] { pw / 2f - 24f, 8f, 48f, 24f },          // north
                _ => new float[] { pw / 2f - 24f, ph - 32f, 48f, 24f },    // south (default)
            };
            world.TransportNodes.Add(new TransportNode
            {
                Box = new AxisAlignedBox
                {
                    MinCorner = new Vector3(n[0], 0f, n[1]),
                    MaxCorner = new Vector3(n[0] + n[2], 64f, n[1] + n[3]),
                },
                Data = new TransportNodeData
                {
                    Name = nodeName,
                    TargetMapName = targetMap,   // "" = leave the dungeon; a room id = door into that room
                    TargetNodeName = "",
                    Direction = (edge + 2) % 4,
                    Type = TransitionStyle.Door,
                },
            });
        }

        if (m.Exit) AddNode("exit", m.ExitDirection, m.ExitNode, "");
        if (m.Doors != null)
            foreach (DoorDef door in m.Doors)
                if (door != null && !string.IsNullOrWhiteSpace(door.Target))
                {
                    // The game hardwires direction (exitDirection+2)%4 as "leave the dungeon" — a door on that
                    // edge would dump the player to the overworld instead of the target room.
                    if (door.Edge == m.ExitDirection)
                        Plugin.Log.LogWarning($"map '{m.Id}': door on edge {door.Edge} clashes with exitDirection — "
                            + "the game will treat it as the overworld exit. Move the door or change exitDirection.");
                    else
                        AddNode("door", door.Edge, door.Node, door.Target);
                }

        NpcsFeature.AddNpcDoodads(world, m.Id);
        DoodadsFeature.AddMapDoodads(world, m.Id); // pre-placed props (Doodads.cs) — must run after AddNpcDoodads (continues its UniqueId sequence)   // dialogue NPCs (Npcs.cs) — doodads + their extra data

        var reflection = BuildReflection();
        using (var fs = File.Create(dstCmx))
        {
            // header writer is disposed BEFORE Serialize: the serializer closes the stream itself when done
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
                bw.Write(name);          // leading length-prefixed tmx content name (the client reads this)
            new CandideSerializer().Serialize(reflection, fs, world);
        }

        // Round-trip sanity check with a fresh reflection — a malformed cmx would otherwise only fail at load.
        using (var fs = File.OpenRead(dstCmx))
        {
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            string tmxName = br.ReadString();
            World back = new CandideDeserializer(null, null).Deserialize<World>(BuildReflection(), fs, leaveOpen: true);
            if (back == null || back.Name != name || tmxName != name || back.TransportNodes.Count != world.TransportNodes.Count)
                throw new Exception("round-trip verification failed");
        }
        Plugin.Log.LogInfo($"Generated map '{m.Id}' -> {name} ({pw}x{ph}px).");
    }

    private const int CaveFloorTileId = 112;   // dungeon_full.tsx local tile 112 — the standard vanilla cave floor

    // If the tmx has no visible tile layer (same heuristic as the validator: every <layer> is named
    // ground/structure), write a modified copy to dstTmx with a `cf_autofloor` layer — the cave floor tile under
    // every ground tile. When a visible layer exists this returns without touching dstTmx (the verbatim copy).
    private static void SynthesizeFloorIfMissing(MapDef m, XmlDocument doc, XmlElement mapEl, string dstTmx)
    {
        var layers = mapEl.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "layer").ToList();
        if (layers.Any(l => l.GetAttribute("name") != "ground" && l.GetAttribute("name") != "structure"))
            return;   // has art — keep the byte-identical copy

        var ground = layers.FirstOrDefault(l => l.GetAttribute("name") == "ground");
        if (ground?.SelectSingleNode("data") is not XmlElement data) return;   // no ground layer — nothing to floor
        if (data.GetAttribute("encoding") != "csv")
        {
            Plugin.Log.LogWarning($"map '{m.Id}': no visible art layer and the ground layer isn't csv-encoded — "
                + "can't auto-generate a floor; the room will render black.");
            return;
        }

        int w = int.Parse(ground.GetAttribute("width"));
        int h = int.Parse(ground.GetAttribute("height"));
        uint[] gids = data.InnerText.Split(',').Select(s => uint.Parse(s.Trim())).ToArray();
        // Flip bits (top 3) are not tile ids; a cell counts as floored when the masked gid is non-zero.
        if (!gids.Any(g => (g & 0x1FFFFFFF) != 0)) return;   // intentionally floorless map — skip

        int floorGid = ResolveCaveFloorGid(doc, mapEl);
        int cells = 0;
        var csv = new StringBuilder("\n");
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                bool floored = i < gids.Length && (gids[i] & 0x1FFFFFFF) != 0;
                if (floored) cells++;
                csv.Append(floored ? floorGid : 0);
                if (i < w * h - 1) csv.Append(',');   // no trailing comma — the game's csv parser chokes on it
            }
            csv.Append('\n');
        }

        var layer = doc.CreateElement("layer");
        layer.SetAttribute("name", "cf_autofloor");
        layer.SetAttribute("width", w.ToString());
        layer.SetAttribute("height", h.ToString());
        var dataEl = doc.CreateElement("data");
        dataEl.SetAttribute("encoding", "csv");
        dataEl.AppendChild(doc.CreateTextNode(csv.ToString()));
        layer.AppendChild(dataEl);
        mapEl.InsertBefore(layer, layers[0]);   // first tile layer = bottom of the render stack (like the examples' Base)

        doc.Save(dstTmx);
        Plugin.Log.LogInfo($"map '{m.Id}': no visible art layer — auto-generated 'cf_autofloor' "
            + $"(plain cave floor over {cells} ground tiles). Add your own layer for real art.");
    }

    // Gid of the cave floor tile: reuse the tmx's dungeon_full tileset if referenced, else append a reference.
    private static int ResolveCaveFloorGid(XmlDocument doc, XmlElement mapEl)
    {
        var tilesets = mapEl.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "tileset").ToList();
        foreach (var ts in tilesets)
            if (ts.GetAttribute("source").Replace('\\', '/').EndsWith("/dungeon_full.tsx", StringComparison.OrdinalIgnoreCase))
                return int.Parse(ts.GetAttribute("firstgid")) + CaveFloorTileId;

        // The new firstgid must start past every existing tileset's range — the game's TileSetCollection.Add
        // throws "tilesets are overlapping" otherwise. Tile counts come from the tsx files under Content
        // (the same files the game resolves `source` to: "../"s stripped, relative to Content).
        int firstgid = 1;
        foreach (var ts in tilesets)
            firstgid = Math.Max(firstgid, int.Parse(ts.GetAttribute("firstgid")) + TileCount(ts));

        var el = doc.CreateElement("tileset");
        el.SetAttribute("firstgid", firstgid.ToString());
        el.SetAttribute("source", "../../tilesets/dungeon_full.tsx");   // how the shipped examples reference it
        if (tilesets.Count > 0) mapEl.InsertAfter(el, tilesets[tilesets.Count - 1]);
        else mapEl.PrependChild(el);
        return firstgid + CaveFloorTileId;
    }

    private static int TileCount(XmlElement tileset)
    {
        string inline = tileset.GetAttribute("tilecount");   // embedded tileset — count is right in the tmx
        if (inline.Length > 0) return int.Parse(inline);
        try
        {
            var tsx = new XmlDocument();
            tsx.Load(Path.Combine(ContentDir, tileset.GetAttribute("source").Replace("../", "").Replace('\\', '/')));
            return int.Parse(tsx.DocumentElement.GetAttribute("tilecount"));
        }
        catch { return 4096; }   // unknown — overshooting the gid range is harmless, overlapping is fatal
    }

    // Mirror the game's reflection config for World files (ServerDataSystem.Setup / client boot do the same).
    private static CandideReflection BuildReflection()
    {
        var r = new CandideReflection();
        r.Configure<WorldDoodad>().SetExtraDataType<(string, string, Guid, string, string, string)>();
        r.Configure<World>().SetExtraDataType<List<(string, string)>>();
        NpcsFeature.ConfigureReflection(r);   // supersedes the two handlers above with ones that also WRITE extra data
        return r;
    }

    // 2) Register the dungeon rooms. Setup() runs AFTER LoadMapFiles in both server start paths, so the
    // world-name validation inside AddDungeonRoomData passes.
    public static void RegisterRooms()
    {
        var rooms = new List<DungeonRoomDataModel>();
        foreach ((MapDef m, _) in ContentLoader.Maps)
        {
            if (DungeonRoomDataBase.DataMap.ContainsKey(m.Id)) continue;
            rooms.Add(new DungeonRoomDataModel
            {
                Id = m.Id,
                DisplayName = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name,
                InteriorWorldMapId = WorldName(m),
                Orientation = DungeonRoomDataModel.RoomOrientation.HandCrafted,
                IconId = string.IsNullOrWhiteSpace(m.Icon) ? "dungeon_skull" : m.Icon,
                ExitDirection = m.ExitDirection,
                LootTableId = string.IsNullOrWhiteSpace(m.LootTable) ? null : m.LootTable,
            });
        }
        if (rooms.Count > 0) DungeonRoomDataBase.AddDungeonRoomData(rooms);
    }

    // 3) Register the overworld entrances (a POI per map that lists biomes). New world generations roll these
    // like any vanilla dungeon; existing saves won't grow new POIs (vanilla behavior).
    public static void RegisterPois()
    {
        var pois = new List<PoiData>();
        foreach ((MapDef m, _) in ContentLoader.Maps)
        {
            if (m.Biomes == null || m.Biomes.Count == 0) continue;   // no auto-entrance requested
            string poiId = "cf:tiny:dungeon:" + m.Id;
            pois.Add(new PoiData
            {
                Id = poiId,
                BiomeIds = m.Biomes.ToArray(),
                Weight = m.Weight <= 0f ? 100f : m.Weight,
                Size = PoiSize.Tiny,
                PoiType = PoiType.HandcraftedDungeon,
                PoiLocationType = PoiLocationType.Clearing | PoiLocationType.Dungeon,
                Map = string.IsNullOrWhiteSpace(m.ExteriorMap) ? "maps/poi/forest/td_animal_den" : m.ExteriorMap,
                TypeDataId = m.Id,
                Tag = poiId,
                DropTables = new DropTables { Minor = "forest:minor", Major = "forest:major", Supplies = "forest:supplies", Poi = "forest:poi" },
            });
        }
        if (pois.Count > 0) PoiDataBase.AddPoiData(pois);
    }
}

// Files must exist before LoadMapFiles scans Content/maps (called inside SetupDataFileLoading).
internal static class MapFilesPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ServerDataSetup), nameof(ServerDataSetup.SetupDataFileLoading))]
    private static void Prefix()
    {
        try { ContentLoader.EnsureParsed(Plugin.PacksDir); MapsFeature.GenerateFiles(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Map file generation failed: {e.Message}"); }
    }
}

// Rooms register after the maps have loaded (Setup always follows SetupDataFileLoading).
internal static class MapRoomPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerDataSetup), nameof(ServerDataSetup.Setup))]
    private static void Postfix()
    {
        try { MapsFeature.RegisterRooms(); RoomReplaceFeature.ApplyReplacements(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Map room registration failed: {e.Message}"); }
    }
}

// POIs register with the other POI data at boot (no world validation there).
internal static class MapPoiPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SharedDataSetup), nameof(SharedDataSetup.SharedSetup))]
    private static void Postfix()
    {
        try { ContentLoader.EnsureParsed(Plugin.PacksDir); MapsFeature.RegisterPois(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Map POI registration failed: {e.Message}"); }
    }
}

// Map atmosphere ids are MusicTracksDataBase / AmbienceTracksDataBase TRACK ids ("dungeon", "crypt", ...) —
// NOT raw FMOD events; an unknown id plays the game's "music not available" placeholder loop. So authors can
// also write raw "event:/..." paths, each such id is registered as its own track at client data setup.
internal static class AtmosphereTracksPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Candide.Data.ClientDataSetup), nameof(Candide.Data.ClientDataSetup.SetupClientData))]
    private static void Postfix()
    {
        try
        {
            ContentLoader.EnsureParsed(Plugin.PacksDir);
            var music = new List<Candide.Data.MusicTrackData>();
            var amb = new List<Candide.Data.AmbienceTrackData>();
            foreach ((MapDef m, _) in ContentLoader.Maps)
            {
                if (m.Music != null && m.Music.StartsWith("event:/"))
                    music.Add(new Candide.Data.MusicTrackData { Id = m.Music, EventName = m.Music });
                if (m.Ambience != null && m.Ambience.StartsWith("event:/"))
                    amb.Add(new Candide.Data.AmbienceTrackData { Id = m.Ambience, EventName = m.Ambience });
            }
            if (music.Count > 0) Candide.Database.MusicTracksDataBase.AddMusicTracks(music);
            if (amb.Count > 0) Candide.Database.AmbienceTracksDataBase.AddAmbienceTracks(amb);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Atmosphere track registration failed: {e.Message}"); }
    }
}
