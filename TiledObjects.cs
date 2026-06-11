using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// VISUAL authoring via Tiled object layers: authors already build their maps in Tiled, so let them CLICK
// props and NPC spawn points into place there instead of typing pixel coordinates into the pack json.
//   - An object layer named `doodads`: every object becomes a DoodadDef appended to MapDef.Doodads (exactly
//     as if it had been written in the json — DoodadsFeature places/registers it with zero changes). The
//     object's NAME (or an `entity` custom property) is the entity id; OR a `sprite` custom property names a
//     pack PNG. Custom properties mirror the json fields: id, spriteFrames (csv ints), spriteFrameDuration
//     (float), spriteFrameWidth/spriteFrameHeight (ints), offset3D (csv floats).
//   - An object layer named `npcs`: every object is a spawn point whose NAME is an `npcs` entry's id in the
//     pack json (the json still carries yarn/actors/translations); the object's position OVERRIDES — or
//     supplies, when the json omits it — that NpcDef's `position`.
//
// COORDINATES (16px tiles, px from the map's top-left — Tiled and our maps agree, floats, no scaling):
//   - A POINT object marks the exact spot: position = (x, y).
//   - A RECTANGLE object anchors at its TOP-LEFT in the tmx; we place at its CENTER (x+w/2, y+h/2), and for
//     SPRITE props the rectangle's w×h becomes the `collision` box — which CollisionRectangle centers on the
//     entity position, so THE BOX YOU DRAW IN TILED IS THE BLOCKING BOX IN GAME. A zero-size rectangle (or a
//     point) means walk-through. Entity doodads never take a collision from the rectangle (the vanilla
//     entity keeps its own; the rectangle only fixes the center). Ellipses are treated like rectangles.
//   - Objects (or whole layers) hidden in Tiled (visible="0") are skipped — toggle props off without
//     deleting them.
//
// THE GAME'S OWN TMX LOADER IGNORES THESE LAYERS (verified in the decompile): CandideCreator.Shared.Tiled
// .TiledMapParser.ParseXml/ParseXMLDataOnly turn EVERY <objectgroup> into an inert ObjectGroup layer (its
// Draw overrides are empty, GetTile returns 0) that is only ever reached by NAME lookups (the audio layer
// pattern) — nothing queries "doodads"/"npcs". No vanilla maps/*.tmx even ships an objectgroup. One caveat:
// ObjectGroup's TiledObject ctor reads each property's `value` ATTRIBUTE — a MULTILINE string property
// (Tiled stores those as element text, no attribute) would NRE the client map render. Keep property values
// single-line (every property in our scheme is).
//
// IDEMPOTENT: MapDef/NpcDef instances live for the whole session while the cmx can regenerate — harvesting
// a map twice would double-append its doodads, so harvested map ids are tracked and re-calls are no-ops
// (returning the cached npc placements).
//
// TIMING: MapsFeature.GenerateOne calls Harvest+ApplyNpcPositions FIRST (before AddNpcDoodads/AddMapDoodads
// consume MapDef.Doodads / NpcDef.Position). Sprite-prop doodads additionally need their prototype clones in
// BOTH entity registries (DoodadsFeature.RegisterSpriteProtos, postfix on each side's LoadDoodadDatabase) —
// on a co-op client no map ever generates, so the PREFIX patches below harvest before those postfixes run
// (prefixes always precede postfixes on the same method). Harvested entries append AFTER the json ones in
// tmx document order on every machine, so the index-derived sprite-prop Guids match across host and clients.
// ---------------------------------------------------------------------------------------------------------

internal static class TiledObjectsFeature
{
    // mapId -> npc placements harvested from its tmx. Doubles as the idempotency marker (see header).
    private static readonly Dictionary<string, Dictionary<string, float[]>> _harvested = new(StringComparer.Ordinal);

    // Parse the map tmx's `doodads`/`npcs` object layers: append synthesized DoodadDefs to m.Doodads and
    // return the npc placements (npc id -> [x,y] px). Re-calls for an already-harvested map are no-ops.
    public static Dictionary<string, float[]> Harvest(MapDef m, string packDir)
    {
        if (m == null || string.IsNullOrWhiteSpace(m.Id)) return new(StringComparer.Ordinal);
        if (_harvested.TryGetValue(m.Id, out var cached)) return cached;
        var npcs = new Dictionary<string, float[]>(StringComparer.Ordinal);
        _harvested[m.Id] = npcs;
        try
        {
            string srcTmx = Path.Combine(packDir, m.Tmx ?? "");
            if (string.IsNullOrWhiteSpace(m.Tmx) || !File.Exists(srcTmx)) return npcs;   // GenerateOne warns about the tmx itself

            // Same XML API + file resolution as MapsFeature.GenerateOne (plain XmlDocument over the pack tmx).
            var doc = new XmlDocument();
            doc.Load(srcTmx);
            int doodads = 0;
            foreach (XmlNode layerNode in doc.DocumentElement.ChildNodes)
            {
                if (layerNode is not XmlElement g || g.Name != "objectgroup") continue;
                if (g.GetAttribute("visible") == "0") continue;   // layer hidden in Tiled = disabled
                bool isDoodads = string.Equals(g.GetAttribute("name"), "doodads", StringComparison.OrdinalIgnoreCase);
                bool isNpcs = string.Equals(g.GetAttribute("name"), "npcs", StringComparison.OrdinalIgnoreCase);
                if (!isDoodads && !isNpcs) continue;

                foreach (XmlNode objNode in g.ChildNodes)
                {
                    if (objNode is not XmlElement o || o.Name != "object") continue;
                    if (o.GetAttribute("visible") == "0") continue;   // object hidden in Tiled = disabled
                    try
                    {
                        if (isDoodads) { if (HarvestDoodad(m, o)) doodads++; }
                        else HarvestNpc(m, o, npcs);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"map '{m.Id}': tmx object #{o.GetAttribute("id")} "
                            + $"('{o.GetAttribute("name")}', layer '{g.GetAttribute("name")}') skipped: {e.Message}");
                    }
                }
            }
            if (doodads > 0 || npcs.Count > 0)
                Plugin.Log.LogInfo($"map '{m.Id}': Tiled object layers -> {doodads} doodad(s), {npcs.Count} npc placement(s).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"map '{m.Id}': Tiled object-layer harvest failed: {e.Message}"); }
        return npcs;
    }

    // Merge `npcs`-layer placements into the parsed NpcDefs: the Tiled position overrides (or supplies) the
    // json `position` of the npc with that id ON THIS MAP. Safe to re-run (same values). Must run before
    // NpcsFeature.AddNpcDoodads reads NpcDef.Position.
    public static void ApplyNpcPositions(MapDef m, Dictionary<string, float[]> placements)
    {
        if (m == null || placements == null || placements.Count == 0) return;
        foreach (KeyValuePair<string, float[]> kv in placements)
        {
            bool matched = false, otherMap = false;
            foreach ((NpcDef def, _) in ContentLoader.NpcDefs)
            {
                if (def.Id != kv.Key) continue;
                if (def.Map != m.Id) { otherMap = true; continue; }
                def.Position = kv.Value;
                matched = true;
                Plugin.Log.LogInfo($"npc '{def.Id}': position ({kv.Value[0]}, {kv.Value[1]}) set from the Tiled `npcs` layer.");
            }
            if (!matched)
                Plugin.Log.LogWarning($"map '{m.Id}': Tiled `npcs` object '{kv.Key}' matches no `npcs` entry of this pack"
                    + (otherMap ? " FOR THIS MAP (an npc with that id points at a different map)" : "")
                    + " — a Tiled object is only a spawn point; the npc itself (yarn/actors/...) must exist in the json.");
        }
    }

    // Harvest every pack map (idempotent). Called by the LoadDoodadDatabase prefixes so the sprite-prop
    // prototypes register on machines that never generate maps (co-op clients) — see the header.
    internal static void HarvestAll()
    {
        ContentLoader.EnsureParsed(Plugin.PacksDir);
        foreach ((MapDef m, string dir) in ContentLoader.Maps) Harvest(m, dir);
    }

    // ------------------------------------------------------------------ per-object synthesis

    private static bool HarvestDoodad(MapDef m, XmlElement o)
    {
        Dictionary<string, string> props = ReadProperties(o);
        (float[] pos, float w, float h) = Anchor(o);
        string name = o.GetAttribute("name");
        props.TryGetValue("entity", out string entity);
        props.TryGetValue("sprite", out string sprite);
        bool isSprite = !string.IsNullOrWhiteSpace(sprite);
        if (!isSprite && string.IsNullOrWhiteSpace(entity)) entity = name;   // the plain case: name = entity id

        if (isSprite && !string.IsNullOrWhiteSpace(entity))
        {
            Plugin.Log.LogWarning($"map '{m.Id}': tmx object #{o.GetAttribute("id")} has BOTH an entity "
                + "(name/`entity` property) and a `sprite` property — pick one; not placed.");
            return false;
        }
        if (!isSprite && string.IsNullOrWhiteSpace(entity))
        {
            Plugin.Log.LogWarning($"map '{m.Id}': tmx object #{o.GetAttribute("id")} on the `doodads` layer has "
                + "no name and no `entity`/`sprite` property — not placed (name it after an entity id, "
                + "or add a `sprite` property).");
            return false;
        }

        var dd = new DoodadDef { Position = pos };
        if (isSprite)
        {
            dd.Sprite = sprite.Trim();
            if (w > 0f && h > 0f) dd.Collision = new[] { w, h };   // the drawn rectangle = the blocking box
        }
        else
            dd.Entity = entity.Trim();   // entity doodads keep their vanilla collision — the rectangle only centers

        // Stable id: the `id` property, else (sprite props) the object's free name — keeps the prototype Guid
        // stable when objects are added/removed above it. Unnamed objects fall back to the list index.
        if (props.TryGetValue("id", out string id) && !string.IsNullOrWhiteSpace(id)) dd.Id = id.Trim();
        else if (isSprite && !string.IsNullOrWhiteSpace(name)) dd.Id = name.Trim();

        if (props.TryGetValue("spriteFrames", out string frames) && !string.IsNullOrWhiteSpace(frames))
            dd.SpriteFrames = Csv(frames).Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
        if (props.TryGetValue("spriteFrameDuration", out string dur) && F(dur) > 0f) dd.SpriteFrameDuration = F(dur);
        if (props.TryGetValue("spriteFrameWidth", out string fw)) dd.SpriteFrameWidth = (int)F(fw);
        if (props.TryGetValue("spriteFrameHeight", out string fh)) dd.SpriteFrameHeight = (int)F(fh);
        if (props.TryGetValue("offset3D", out string off) && !string.IsNullOrWhiteSpace(off))
            dd.SpriteOffset3D = Csv(off).Select(F).ToArray();

        (m.Doodads ??= new List<DoodadDef>()).Add(dd);
        return true;
    }

    private static void HarvestNpc(MapDef m, XmlElement o, Dictionary<string, float[]> npcs)
    {
        string id = o.GetAttribute("name");
        if (string.IsNullOrWhiteSpace(id))
        {
            Plugin.Log.LogWarning($"map '{m.Id}': tmx object #{o.GetAttribute("id")} on the `npcs` layer has no "
                + "name — name it after an `npcs` entry's id (e.g. 'mymod:hermit'); skipped.");
            return;
        }
        id = id.Trim();
        if (npcs.ContainsKey(id))
            Plugin.Log.LogWarning($"map '{m.Id}': duplicate `npcs` object '{id}' — the later one wins.");
        npcs[id] = Anchor(o).Pos;
    }

    // ------------------------------------------------------------------ tmx primitives

    // Tiled anchoring: a rectangle's x/y is its TOP-LEFT corner; a point object is the exact spot. Our
    // convention: point (or zero-size rectangle) = (x, y) as-is, walk-through; rectangle = its CENTER
    // (x + w/2, y + h/2) with w×h as the (sprite-prop) collision size — so what you draw is what blocks.
    private static (float[] Pos, float W, float H) Anchor(XmlElement o)
    {
        float x = F(o.GetAttribute("x")), y = F(o.GetAttribute("y"));
        float w = F(o.GetAttribute("width")), h = F(o.GetAttribute("height"));
        bool point = w <= 0f || h <= 0f;   // a tmx point object simply has no width/height attributes
        foreach (XmlNode c in o.ChildNodes)
            if (c is XmlElement { Name: "point" }) { point = true; break; }
        return point ? (new[] { x, y }, 0f, 0f) : (new[] { x + w / 2f, y + h / 2f }, w, h);
    }

    // <properties><property name="..." value="..."/>... — value may live in the element text instead of the
    // attribute (Tiled's multiline-string form); we accept both. Property names are case-insensitive.
    private static Dictionary<string, string> ReadProperties(XmlElement o)
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XmlNode c in o.ChildNodes)
        {
            if (c is not XmlElement { Name: "properties" } block) continue;
            foreach (XmlNode p in block.ChildNodes)
            {
                if (p is not XmlElement { Name: "property" } prop) continue;
                string key = prop.GetAttribute("name");
                if (string.IsNullOrEmpty(key)) continue;
                props[key] = prop.HasAttribute("value") ? prop.GetAttribute("value") : prop.InnerText;
            }
        }
        return props;
    }

    // Tiled writes floats with '.' decimals; missing attributes come back as "".
    private static float F(string s)
        => string.IsNullOrWhiteSpace(s) ? 0f : float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static IEnumerable<string> Csv(string s)
        => s.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0);
}

// Tiled-authored SPRITE props must already sit in MapDef.Doodads when DoodadsFeature.RegisterSpriteProtos
// (a POSTFIX on this same method) enumerates it to clone their prototypes — prefixes always run first, and
// this also covers co-op clients, which never run map generation. Idempotent (harvested map ids tracked).
internal static class TiledObjectsServerDbPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CandideServer.Entities.ServerEntityDataManager),
        nameof(CandideServer.Entities.ServerEntityDataManager.LoadDoodadDatabase))]
    private static void Prefix()
    {
        try { TiledObjectsFeature.HarvestAll(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Tiled object harvest (server) failed: {e.Message}"); }
    }
}

// Same chokepoint on the CLIENT entity registry (DoodadClientPatch's postfix needs the harvested defs too).
internal static class TiledObjectsClientDbPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Candide.Database.Doodad.DoodadDatabaseManager),
        nameof(Candide.Database.Doodad.DoodadDatabaseManager.LoadDoodadDatabase))]
    private static void Prefix()
    {
        try { TiledObjectsFeature.HarvestAll(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Tiled object harvest (client) failed: {e.Message}"); }
    }
}
