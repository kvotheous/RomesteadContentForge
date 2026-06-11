using Candide;                              // Globals (client GraphicsDevice / Reflection)
using Candide.Database.Doodad;              // DoodadDatabaseManager (client entity registry)
using CandideCreator.Shared.Graphics;       // SpriteSheet
using CandideCreator.Shared.Serialization;  // CandideReflection
using CandideServer;                        // ServerGlobals
using CandideServer.Entities;               // ServerEntityDataManager (server entity registry)
using HarmonyLib;
using Microsoft.Xna.Framework;              // Color
using Microsoft.Xna.Framework.Graphics;     // Texture2D
using Shared.Entity;                        // EntitySystem, EntityCopyHelper
using Shared.Entity.Components;             // Component (mask flags)
using System;
using System.Collections.Generic;
using System.IO;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Per-NPC unique BODIES for dialogue NPCs. Today every dialogue NPC is the vanilla "neutral_satyr" doodad —
// ONE shared prototype Guid, so repainting/tinting it would change EVERY dialogue NPC (and the story satyr)
// at once. When an NpcDef carries `sheets` (1:1 repaint PNGs) and/or `tint` ([r,g,b]), we instead CLONE the
// neutral_satyr prototype EntitySystem under a deterministic per-NPC Guid and point that NPC's doodad at the
// clone, so each NPC owns its art.
//
// HOW THE NEUTRAL SATYR RENDERS (all verified against the decompiles — it is NOT spac-based, so RigSwap's
// RegisterEntitySpac hook never fires for it):
//   - Its client controller is YarnNeutralNpcController (rdll), which builds NO animation state machine and
//     never sets Entity.UsesSpac — it only reads yarn_dialogue_path/dialogue_distance/yarn_starting_node.
//   - Its art is the prototype's GeneralComponent.SpriteSheet, a single static sheet
//     "media/sprites/creatures/hostiles/goblin_bandit2/no_weapons" (48x48 px, one frame; from
//     Content/db/doodads/enemies/boss/satyrboss/neutral_satyr.cdx), textured onto the doodad's IndexedMesh:
//       rdll DrawComplexEntity: "effect.Parameters[\"AlbedoTexture\"]?.SetValue(entity.SpriteSheet.Texture);"
//       rdll DrawEntitiesBatched: "batchData.TintColor = current3.Color.ToVector4(); ...
//         batchData.NormalTex = spriteSheet2.NormalMap; batchData.MetalTex = spriteSheet2.MetalMap;"
//   So a repaint = swap General[0].SpriteSheet on the clone (texture only; same maps/frame size/offset —
//   the mesh UVs were authored for the 48x48 layout, hence the strict 1:1 contract), and a tint =
//   General[0].Color, exactly like creature `color`. Both fields propagate to every spawned instance:
//   spawn deep-copies the prototype, and CandideReflection clones a SpriteSheet BY REFERENCE
//   (cc SimpleTypeHandler.Clone: "return source;" — SpriteSheet registers through SpriteSheetSerializer).
//
// TIMING (the critical part): NpcsFeature.AddNpcDoodads runs in a PREFIX of
// ServerDataSetup.SetupDataFileLoading (map generation), BEFORE any prototype exists. So GetBodyGuid only
// COMPUTES the deterministic Guid (MD5 of the npc id — the same construction as Plugin.GuidFromId, so it is
// stable across machines/co-op). The actual cloning happens later in this file's LoadDoodadDatabase
// postfixes, which enumerate ContentLoader.NpcDefs themselves (no hand-off needed — works even on a co-op
// client that never generates maps). The server then resolves the map's doodads strictly AFTER our clone is
// registered, because SetupDataFileLoading is (cs2 ServerDataSetup):
//      "ServerEntityDataManager.LoadDoodadDatabase("db/doodads", "doodads");
//       ServerWorldDataManager.LoadMapFiles();"
// and LoadMapFiles -> OnWorldRead resolves each doodad's extra-data Guid against the registry we just wrote:
//      "if (guid != default(Guid) && ServerEntityDataManager.EntitySystems.TryGetValue(guid, out ...))"
//
// CONTROLLER: DeepCopyEntity with ignoreControllers:true reflection-clones ControllerComponent like any
// other component array ("object value2 = reflection.Clone(value); obj2.SetValue(value2, targetId);"), so
// the clone keeps the satyr's live controller object (client "yarnneutralnpc", server "ServerAi") and its
// .cdx default Parameters — identical to how RegisterCreatures/Interactive clones carry theirs. At spawn,
// DeepCopyEntity(prototype -> world) re-clones it properly: "Controller.Clone(wrapper); AttachEntity(wrapper,
// new Parameters(sourceSystem.ControllerComponent[sourceId].Controller.Parameters))" (sh EntityCopyHelper),
// and the doodad's ClientControllerParameters/ServerControllerParameters (the yarn path etc.) merge on top.
//
// AUTHOR CONTRACT (`sheets`): keyed by the vanilla sheet's BASENAME -> a pack PNG repainting it 1:1 (same
// pixel size, same layout). The neutral satyr has exactly ONE sheet: "no_weapons", 48x48. A wrong-size PNG
// never crashes (the mesh just samples wrong texels = visual garbage) — we warn loudly, like RigSwap.
// SERVER side: never renders; SpriteSheetSerializer.Read returns null without graphics ("if
// (deserializer.Graphics == null) return null;"), so the server clone gets no sheet work (tint is set on
// both sides for symmetry with RegisterCreatures; it is only read by the client renderer).
// ---------------------------------------------------------------------------------------------------------

internal static class NpcBodies
{
    // Mirrors NpcsFeature's private satyr resolution (those members are private there; same source of truth:
    // Content/db/doodads/doodads.cdb — "ace09b92-a240-4762-a528-4c685bbab5dc enemies/boss/satyrboss/neutral_satyr").
    private const string BaseDoodadName = "neutral_satyr";
    private static readonly Guid FallbackSatyrGuid = new("ace09b92-a240-4762-a528-4c685bbab5dc");
    private static Guid _satyrGuid;

    // Author PNGs are GPU textures — load each once, reuse across DB reloads (same pattern as RigSwap).
    private static readonly Dictionary<string, Texture2D> _textureCache = new();

    private static string ContentDir => Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath, "Content"));

    // Does this NPC need its own body prototype? (No sheets and no tint = keep sharing the vanilla satyr.)
    internal static bool NeedsBody(NpcDef def)
        => def != null && ((def.Sheets != null && def.Sheets.Count > 0) || (def.Tint != null && def.Tint.Length >= 3));

    // The Guid the NPC's doodad should reference. Called by NpcsFeature.AddNpcDoodads at MAP-GENERATION time
    // (SetupDataFileLoading prefix) — prototypes don't exist yet, so this only COMPUTES the deterministic
    // per-NPC Guid; RegisterBodies (LoadDoodadDatabase postfix, later in the same call) creates the clone
    // under the same Guid before LoadMapFiles resolves it (ordering quoted in the header).
    internal static Guid GetBodyGuid(NpcDef def, Guid satyrGuid)
        => NeedsBody(def) ? GuidFromId(def.Id) : satyrGuid;

    // Clone the neutral_satyr prototype for every body-needing NPC into one side's entity registry. Called
    // once for the server registry and once for the client registry (a host runs both) — the same dual
    // registration as ContentLoader.RegisterCreatures, and idempotent the same way (skips present Guids).
    internal static void RegisterBodies(Dictionary<Guid, EntitySystem> protos, CandideReflection reflection, bool serverSide)
    {
        ContentLoader.EnsureParsed(Plugin.PacksDir);
        foreach ((NpcDef def, string dir) in ContentLoader.NpcDefs)
        {
            if (!NeedsBody(def)) continue;
            try
            {
                Guid guid = GuidFromId(def.Id);
                if (protos.ContainsKey(guid)) continue;   // already registered this side (DB reloads re-run this)

                Guid satyr = ResolveSatyrGuid();
                if (!protos.TryGetValue(satyr, out EntitySystem src))
                { Plugin.Log.LogWarning($"npc '{def.Id}': '{BaseDoodadName}' prototype not in the entity registry — body skipped."); continue; }

                var clone = new EntitySystem(1);
                int id = clone.NewEntity();   // slot 0 (keeps its own Wrapper/IncId, which DeepCopyEntity preserves)
                EntityCopyHelper.DeepCopyEntity(src, 0, clone, id, initializeControllers: false, ignoreControllers: true, reflection);

                // New identity. BaseGuid too: spawned instances inherit it, and anything matching by
                // BaseGuid (RigSwap-style hooks, kill-quest objectives) must see the clone, not the satyr.
                clone.General[0].Id = guid;
                clone.General[0].BaseGuid = guid;

                if (def.Tint != null && def.Tint.Length >= 3)
                    clone.General[0].Color = new Color(Clamp255(def.Tint[0]), Clamp255(def.Tint[1]), Clamp255(def.Tint[2]));

                if (!serverSide) ApplySheets(clone, def, dir);   // visuals are client-only (see header)

                protos[guid] = clone;
                Plugin.Log.LogInfo($"Registered npc body '{def.Id}' (clone of {BaseDoodadName}, "
                    + $"{(def.Sheets != null && def.Sheets.Count > 0 ? def.Sheets.Count + " sheet(s)" : "no sheets")}"
                    + $"{(def.Tint != null && def.Tint.Length >= 3 ? ", tinted" : "")}, {(serverSide ? "server" : "client")}).");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"npc '{def?.Id}' body failed: {e.Message}"); }
        }
    }

    // Swap the clone's General.SpriteSheet for the author's repaint. The satyr's mesh UVs map the 48x48
    // sheet 1:1, so we keep EVERYTHING but the texture (normal/metal maps, frame size, offset, the mesh).
    private static void ApplySheets(EntitySystem clone, NpcDef def, string packDir)
    {
        if (def.Sheets == null || def.Sheets.Count == 0) return;   // tint-only body

        SpriteSheet bs = clone.General[0].SpriteSheet;
        if (bs?.Texture == null)
        { Plugin.Log.LogWarning($"npc '{def.Id}': base satyr sheet not loaded on this side — keeping vanilla art."); return; }

        // Tell authors exactly what to repaint (same discovery log as RigSwap).
        string texName = bs.Texture.Name ?? "";
        int slash = texName.LastIndexOf('/');
        string basename = slash >= 0 ? texName.Substring(slash + 1) : texName;
        Plugin.Log.LogInfo($"Content Forge: npc '{def.Id}' body from sheet '{texName}' "
            + $"{bs.Texture.Width}x{bs.Texture.Height} (frame {bs.SpriteWidth}x{bs.SpriteHeight}) — key `sheets` by '{basename}'.");

        string png = null;
        foreach (var kv in def.Sheets)
            if (string.Equals(kv.Key, basename, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
            { png = Path.Combine(packDir, kv.Value); break; }
        if (png == null)
        {
            Plugin.Log.LogWarning($"npc '{def.Id}': no `sheets` entry matched base sheet '{basename}' "
                + $"(got: {string.Join(",", def.Sheets.Keys)}) — keeping vanilla art.");
            return;
        }
        if (!File.Exists(png)) { Plugin.Log.LogWarning($"npc '{def.Id}': sheet file not found: {png}"); return; }
        if (Globals.GraphicsDevice == null)
        { Plugin.Log.LogWarning($"npc '{def.Id}': graphics device not ready — keeping vanilla art."); return; }

        Texture2D tex = LoadTexture(png);
        if (tex.Width != bs.Texture.Width || tex.Height != bs.Texture.Height)
            Plugin.Log.LogWarning($"npc '{def.Id}': '{Path.GetFileName(png)}' is {tex.Width}x{tex.Height} but base sheet "
                + $"'{basename}' is {bs.Texture.Width}x{bs.Texture.Height} — the body mesh will sample the wrong texels "
                + "(visual garbage). Repaint the vanilla sheet 1:1 (same size, same layout).");

        // Keep the base's normal/metal maps, frame size AND offset: a 1:1 repaint stays lit + aligned.
        clone.General[0].SpriteSheet = new SpriteSheet(tex, bs.NormalMap, bs.MetalMap, bs.SpriteWidth, bs.SpriteHeight, bs.Offset);
    }

    private static Texture2D LoadTexture(string path)
    {
        if (_textureCache.TryGetValue(path, out Texture2D tex)) return tex;
        using var fs = File.OpenRead(path);
        tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
        tex.Name = path;   // SpriteSheetSerializer.Write writes Texture.Name — never leave it null
        _textureCache[path] = tex;
        return tex;
    }

    // Stable Guid from the npc id (MD5-based, deterministic across machines -> co-op-safe). The same
    // construction as Plugin.GuidFromId (private there). NOTE: shares its Guid space with custom creatures —
    // an NPC id reused as a creature id would collide; pack namespacing makes that an authoring error.
    internal static Guid GuidFromId(string id)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(id)));
    }

    // The satyr's Guid from the doodad database INDEX (mirrors NpcsFeature.ResolveSatyrGuid, private there):
    // doodads.cdb is "cdb10" header, then "<36-char guid><sep><path>" per line. Hardcoded fallback if moved.
    private static Guid ResolveSatyrGuid()
    {
        if (_satyrGuid != Guid.Empty) return _satyrGuid;
        try
        {
            string cdb = Path.Combine(ContentDir, "db", "doodads", "doodads.cdb");
            if (File.Exists(cdb))
                foreach (string line in File.ReadLines(cdb))
                {
                    if (line.Length < 38) continue;
                    string path = line[37..].Trim();
                    if ((path.EndsWith("/" + BaseDoodadName, StringComparison.Ordinal) || path == BaseDoodadName)
                        && Guid.TryParse(line.AsSpan(0, 36), out Guid g))
                    { _satyrGuid = g; return g; }
                }
            Plugin.Log.LogWarning($"'{BaseDoodadName}' not found in doodads.cdb — using the known shipped Guid.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"doodads.cdb scan failed ({e.Message}) — using the known shipped Guid."); }
        _satyrGuid = FallbackSatyrGuid;
        return _satyrGuid;
    }

    private static int Clamp255(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);
}

// Register NPC body clones into the SERVER entity registry, right after the vanilla doodad DB loads (same
// chokepoint + timing as CreatureServerPatch — the satyr prototype exists, LoadMapFiles hasn't resolved yet).
internal static class NpcBodyServerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerEntityDataManager), nameof(ServerEntityDataManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { NpcBodies.RegisterBodies(ServerEntityDataManager.EntitySystems, ServerGlobals.Reflection, serverSide: true); }
        catch (Exception e) { Plugin.Log.LogWarning($"NPC body server registration failed: {e.Message}"); }
    }
}

// And into the CLIENT entity registry (renders the repaint/tint). Same Guid both sides.
internal static class NpcBodyClientPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(DoodadDatabaseManager), nameof(DoodadDatabaseManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { NpcBodies.RegisterBodies(DoodadDatabaseManager.EntitySystems, Globals.Reflection, serverSide: false); }
        catch (Exception e) { Plugin.Log.LogWarning($"NPC body client registration failed: {e.Message}"); }
    }
}

// Saved worlds persist their NPC ENTITIES — and the client NEVER sees a server-side color change: the spawn
// message (cs2 ServerEntityModel) carries NO Color field, so the client rebuilds each NPC every session from
// its OWN prototype registry keyed by the model's BaseId (rdll EntityController.SpawnEntity ->
// SpawnEntityBase(model.BaseId, ...)). That means the ONLY thing that makes a saved NPC render tinted is its
// saved BaseId pointing at our (client-registered, tinted) clone. Worlds saved BEFORE an NPC had its own
// body still reference the PLAIN satyr Guid — the old BaseGuid-only retint matched nothing and, even when it
// did match, only recolored the server wrapper, which never reaches the client renderer. Fix at world load:
//   1) find our NPC entities by BaseGuid (new worlds) OR by their yarn path ("cf/<safe-id>" in the saved
//      controller Parameters — the one marker every forge NPC entity carries since spawn);
//   2) REPOINT legacy entities' BaseId/BaseGuid at the per-NPC clone (persists into the save; next client
//      spawn uses the tinted/repainted prototype);
//   3) re-apply the def's tint to the server wrapper (server-side symmetry, same as RegisterBodies).
internal static class NpcBodyRetintPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CandideServer.ServerSystems.ServerDirectorSystem), nameof(CandideServer.ServerSystems.ServerDirectorSystem.AfterWorldGameStateLoaded))]
    private static void Postfix()
    {
        try
        {
            ContentLoader.EnsureParsed(Plugin.PacksDir);
            // yarn path -> (clone guid, tint) for every NPC that owns a body (tint may be absent: sheets-only
            // bodies still need the repoint so the client picks the repainted prototype).
            var byPath = new Dictionary<string, (Guid Guid, Color? Tint)>(StringComparer.Ordinal);
            var byGuid = new Dictionary<Guid, Color?>();
            foreach ((NpcDef def, _) in ContentLoader.NpcDefs)
            {
                if (!NpcBodies.NeedsBody(def)) continue;
                Color? tint = def.Tint != null && def.Tint.Length >= 3
                    ? new Color(Math.Clamp(def.Tint[0], 0, 255), Math.Clamp(def.Tint[1], 0, 255), Math.Clamp(def.Tint[2], 0, 255))
                    : null;
                Guid guid = NpcBodies.GuidFromId(def.Id);
                byPath["cf/" + NpcsFeature.SafeId(def.Id)] = (guid, tint);
                byGuid[guid] = tint;
            }
            if (byPath.Count == 0) return;

            int retinted = 0, repointed = 0;
            foreach (var kv in ServerGameState.Entities)
            {
                var model = kv.Value;
                var w = model?.EntityWrapper;
                if (w == null) continue;

                if (!byGuid.TryGetValue(w.BaseGuid, out Color? tint))
                {
                    // Legacy entity from a save made before this NPC had its own body? Its BaseId is the
                    // plain satyr, but its controller parameters still carry our yarn path.
                    string yarnPath = null;
                    if (model.Parameters == null || !model.Parameters.TryGetValue("yarn_dialogue_path", out yarnPath))
                        if (w.Mask.HasFlags(Component.Controller))
                            yarnPath = w.Controller?.Parameters?.GetString("yarn_dialogue_path");
                    if (yarnPath == null || !byPath.TryGetValue(yarnPath, out var body)) continue;

                    w.BaseGuid = body.Guid;     // in-memory identity (and what this loop matches next time)
                    model.BaseId = body.Guid;   // what spawn messages + the save carry -> client uses the clone
                    repointed++;
                    tint = body.Tint;
                }

                if (tint.HasValue && w.Color != tint.Value) { w.Color = tint.Value; retinted++; }
            }
            if (repointed > 0 || retinted > 0)
                Plugin.Log.LogInfo($"NPC bodies: {repointed} saved entit(ies) repointed at their body clone, {retinted} re-tinted.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"NPC body re-tint failed: {e.Message}"); }
    }
}
