using CandideCreator.Shared.Helpers;
using CandideCreator.Shared.Models;
using CandideCreator.Shared.Serialization;
using CandideServer.Data.ServerDataSetups;
#if !CF_SERVER
using Google.Protobuf;
#endif
using HarmonyLib;
using Microsoft.Xna.Framework;
using Shared.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
#if !CF_SERVER
using Yarn.Compiler;   // YarnSpinner isn't on a dedicated server — dialogue compiles on each client
#endif

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Dialogue NPCs in custom maps. A pack ships a YarnSpinner .yarn script; this feature
//   1) compiles it (YarnSpinner.Compiler 2.5 — MUST match the game's shipped YarnSpinner.dll 2.5.0) and
//      writes Content/dialogue/cf/<safe-id>.xnb in the game's exact XNB layout (Candide.Readers.YarnReader:
//      int32 programByteCount + Yarn.Program protobuf + int32 stringCount + per-line (key, text, int32
//      metaCount + metadata) + int32 localizationCount + per-locale (localeCode, stringsFilePath, "")),
//      plus per-language string sidecars Content/dialogue/cf/<safe-id>_<locale> (int32 count + (lineID, text)
//      pairs — read by YarnProject.EnsureLocaleLoaded with the locale picked from Globals.LanguageCode);
//   2) places a WorldDoodad referencing the vanilla "neutral_satyr" base doodad (unkillable, client
//      controller "yarnneutralnpc") into the generated map World. The doodad's ClientControllerParameters
//      carry yarn_dialogue_path/yarn_starting_node/dialogue_distance, exactly like vanilla yarn-NPC doodads.
//      The doodad's link to its base travels as CandideSerializer "extra data": a per-doodad
//      (string, string dbKey, Guid, string, string, string name) tuple plus a per-world
//      List<(fileName, directoryPath)> of doodad databases — both client (DoodadDatabaseManager) and server
//      (ServerEntityDataManager) resolve them in their World AfterDeserialize / OnWorldRead. The SERVER spawn
//      path (ServerWorldDataManager.OnWorldRead -> ServerWorldData.EntitySpawnData -> SpawnWorldEntities)
//      resolves by Guid ONLY, and the client name-fallback only searches the database's ROOT folder — so the
//      satyr's real Guid is resolved from Content/db/doodads/doodads.cdb (a plain text index: 36-char guid +
//      space + doodad path per line) at generation time, with a hardcoded fallback.
//   3) registers actor display names: the <<Actor key ...>> dialogue command resolves the speaker's name via
//      StringDefinitions.GetString(key), which renders unknown keys as "<key>" — so packs can map keys to
//      names, re-applied after every SetLocalization (which clears StringDefinitions.Strings).
// ---------------------------------------------------------------------------------------------------------

internal static class NpcsFeature
{
    // Resolve <game>/Content from the BepInEx install (plugins live in <game>/BepInEx/plugins).
    private static string ContentDir => Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath, "Content"));

    // The base doodad every dialogue NPC reuses: "unkillable satyr meant ... satyr interactions" — client
    // controller "yarnneutralnpc" (YarnNeutralNpcController), shipped at db/doodads/enemies/boss/satyrboss.
    private const string BaseDoodadName = "neutral_satyr";
    private const string BaseDoodadPath = "enemies/boss/satyrboss";
    private static readonly Guid FallbackSatyrGuid = new("ace09b92-a240-4762-a528-4c685bbab5dc");

    // The doodad database both sides key by directoryPath ("db/doodads"); fileName is "doodads"
    // (ServerDataSetup.SetupDataFileLoading: LoadDoodadDatabase("db/doodads", "doodads")).
    private const string DbDirectory = "db/doodads";
    private const string DbFileName = "doodads";

    // The XNB reader string exactly as shipped (verified against Content/dialogue/altar/blessings/blessings.xnb).
    private const string XnbReader =
        "Candide.Readers.YarnReader, Romestead, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

    private static bool _filesDone;
    private static Guid _satyrGuid;
    // npc ids whose dialogue xnb was written this session — only these get a doodad (a missing/broken xnb
    // would make the game's ContentManager.Load throw on first interaction).
    private static readonly HashSet<string> _compiled = new();
    // locales each npc actually shipped a sidecar for (goes into the xnb's localization list)
    private static readonly Dictionary<string, List<string>> _locales = new();

    // CandideSerializer "extra data" is produced by a per-type getter on the reflection; we key the data by
    // doodad instance (reference equality) and hand the world's database list to any world that has doodads.
    private static readonly Dictionary<WorldDoodad, (string, string, Guid, string, string, string)> _doodadExtra = new();
    private static readonly List<(string, string)> _worldDbList = new() { (DbFileName, DbDirectory) };

    internal static string SafeId(string id)
    {
        var sb = new StringBuilder(id.Length);
        foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }

    // Wire the extra-data getters into the cmx serializer reflection. Maps.cs BuildReflection calls this
    // LAST: Configure<T>() replaces the type handler, so these supersede the bare SetExtraDataType calls
    // (same extra-data types — the round-trip deserialization in GenerateOne still reads them fine).
    public static void ConfigureReflection(CandideReflection r)
    {
        r.Configure<WorldDoodad>()
            .ExtraData<WorldDoodad, (string, string, Guid, string, string, string)>(
                d => _doodadExtra.TryGetValue(d, out var t) ? t : default);
        r.Configure<World>()
            .ExtraData<World, List<(string, string)>>(w => w.Doodads.Count > 0 ? _worldDbList : null);
    }

    // ------------------------------------------------------------------ 1) dialogue files (xnb + sidecars)

    // Compile every pack .yarn into Content/dialogue/cf/. Runs in the same hook window as map generation
    // (prefix of ServerDataSetup.SetupDataFileLoading) and lazily from AddNpcDoodads (prefix order between
    // two patch classes on one method is not guaranteed).
    public static void GenerateDialogueFiles()
    {
        if (_filesDone) return;
        _filesDone = true;

        var mapIds = new HashSet<string>(ContentLoader.Maps.Select(t => t.Def.Id));
        foreach ((NpcDef def, string dir) in ContentLoader.NpcDefs)
        {
            try
            {
                if (!mapIds.Contains(def.Map))
                    Plugin.Log.LogWarning($"npc '{def.Id}': map '{def.Map}' is not a map of any loaded pack — the NPC will never spawn.");
                GenerateOne(def, dir);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"npc '{def?.Id}': dialogue generation failed: {e.Message}"); }
        }

        // Talking furniture rides the same compile pipeline (no doodad — the construction places the entity).
        foreach ((NpcDef def, string dir) in ContentLoader.PlaceableDialogues)
        {
            try { GenerateOne(def, dir); }
            catch (Exception e) { Plugin.Log.LogWarning($"furniture '{def?.Id}': dialogue generation failed: {e.Message}"); }
        }
    }

#if CF_SERVER
    // Dedicated server: the dialogue runs (and compiles) on each player's client, never on the server — just
    // confirm the .yarn is there, which is what lets AddNpcDoodads place the NPC in its map.
    private static void GenerateOne(NpcDef def, string packDir)
    {
        if (!string.IsNullOrWhiteSpace(def.Yarn) && File.Exists(Path.Combine(packDir, def.Yarn))) _compiled.Add(def.Id);
        else Plugin.Log.LogWarning($"npc '{def.Id}': yarn '{def.Yarn}' not found next to the pack.");
    }
#else
    private static void GenerateOne(NpcDef def, string packDir)
    {
        string srcYarn = Path.Combine(packDir, def.Yarn);
        if (!File.Exists(srcYarn)) { Plugin.Log.LogWarning($"npc '{def.Id}': yarn '{def.Yarn}' not found next to the pack."); return; }

        string safe = SafeId(def.Id);
        var result = Compiler.Compile(CompilationJob.CreateFromString(safe, File.ReadAllText(srcYarn)));

        bool errors = false;
        foreach (Diagnostic d in result.Diagnostics)
        {
            if (d.Severity == Diagnostic.DiagnosticSeverity.Error)
            { errors = true; Plugin.Log.LogWarning($"npc '{def.Id}': yarn error: {d}"); }
            else
                Plugin.Log.LogInfo($"npc '{def.Id}': yarn: {d}");
        }
        if (errors || result.Program == null) { Plugin.Log.LogWarning($"npc '{def.Id}': skipped (yarn did not compile)."); return; }

        string outDir = Path.Combine(ContentDir, "dialogue", "cf");
        Directory.CreateDirectory(outDir);

        // Per-language sidecars first, so the xnb's localization list only names files that exist.
        // Sidecar format (YarnProject.EnsureLocaleLoaded): int32 count + count x (ReadString lineID, ReadString text).
        var locales = new List<string>();
        if (def.Translations != null)
            foreach (var kv in def.Translations)
            {
                var entries = ParseTranslationFile(def, packDir, kv.Key, kv.Value);
                if (entries == null || entries.Count == 0) continue;
                using var fs = File.Create(Path.Combine(outDir, safe + "_" + kv.Key));
                using var w = new BinaryWriter(fs, Encoding.UTF8);
                w.Write(entries.Count);
                foreach ((string id, string text) in entries) { w.Write(id); w.Write(text); }
                locales.Add(kv.Key);
            }
        _locales[def.Id] = locales;

        WriteXnb(Path.Combine(outDir, safe + ".xnb"), result, locales, safe);
        _compiled.Add(def.Id);
        Plugin.Log.LogInfo($"Generated dialogue '{def.Id}' -> dialogue/cf/{safe} "
            + $"({result.StringTable?.Count ?? 0} line(s){(locales.Count > 0 ? ", locales: " + string.Join(",", locales) : "")}).");
    }

    // locale file: one "lineID,translated text" (or tab-separated) per line; lineIDs are the yarn #line: tags.
    private static List<(string, string)> ParseTranslationFile(NpcDef def, string packDir, string locale, string file)
    {
        string path = Path.Combine(packDir, file);
        if (!File.Exists(path)) { Plugin.Log.LogWarning($"npc '{def.Id}': translation '{file}' ({locale}) not found."); return null; }
        var list = new List<(string, string)>();
        foreach (string raw in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            int sep = raw.IndexOf('\t');
            if (sep < 0) sep = raw.IndexOf(',');
            if (sep <= 0) continue;
            string id = raw[..sep].Trim();
            string text = raw[(sep + 1)..].Trim();
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                text = text[1..^1].Replace("\"\"", "\"");   // tolerate quoted csv values
            if (id.Length == 0 || id == "id") continue;      // header line
            list.Add((id, text));
        }
        return list;
    }

    // XNB container (verified byte-for-byte against the shipped blessings.xnb): "XNB" 'w' version=5 flags=0x01
    // int32 totalFileSize, 7-bit readerCount=1, reader string (BinaryWriter.Write(string) = the XNB 7-bit
    // length-prefixed format), int32 readerVersion=0, 7-bit sharedResourceCount=0, 7-bit typeId=1, payload.
    private static void WriteXnb(string dst, CompilationResult result, List<string> locales, string safe)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)'X'); w.Write((byte)'N'); w.Write((byte)'B'); w.Write((byte)'w');
            w.Write((byte)5);   // container version
            w.Write((byte)1);   // flags (HiDef, uncompressed) — matches the game's own files
            w.Write(0);         // total file size — patched below
            w.Write((byte)1);   // reader count (single-byte 7-bit int)
            w.Write(XnbReader);
            w.Write(0);         // reader version
            w.Write((byte)0);   // shared resource count
            w.Write((byte)1);   // typeId 1 = our (only) reader

            // --- YarnReader payload ---
            byte[] program = result.Program.ToByteArray();
            w.Write(program.Length);
            w.Write(program);

            var table = result.StringTable != null
                ? result.StringTable.ToList()
                : new List<KeyValuePair<string, Yarn.Compiler.StringInfo>>();
            w.Write(table.Count);
            foreach (var kv in table)
            {
                w.Write(kv.Key);
                w.Write(kv.Value.text ?? "");
                // metadata = the line's #hashtags; the line-id tag itself is not metadata (mirrors the
                // YarnSpinner Unity importer the game's tables were produced with)
                string[] meta = (kv.Value.metadata ?? Array.Empty<string>())
                    .Where(m => !string.IsNullOrEmpty(m) && !m.StartsWith("line:")).ToArray();
                w.Write(meta.Length);
                foreach (string m in meta) w.Write(m);
            }

            w.Write(locales.Count);
            foreach (string locale in locales)
            {
                w.Write(locale);                 // LocaleCode — must equal Globals.LanguageCode, e.g. "de_DE"
                w.Write(safe + "_" + locale);    // StringsFilePath, relative to Content/dialogue/cf/
                w.Write("");                     // AssetsFilePath (unused; "" like the shipped files)
            }
        }
        byte[] file = ms.ToArray();
        BitConverter.GetBytes(file.Length).CopyTo(file, 6);   // patch total size at offset 6
        File.WriteAllBytes(dst, file);
    }

#endif

    // ------------------------------------------------------------------ 2) NPC doodads in the generated cmx

    // Called by MapsFeature.GenerateOne right before the world is serialized. Appends one neutral_satyr
    // doodad per NPC of this map, with the dialogue parameters in ClientControllerParameters (the client
    // merges them into the spawned controller in DoodadDatabaseManager.ApplyCustomPropertiesAndScript).
    public static void AddNpcDoodads(World world, string mapId)
    {
        GenerateDialogueFiles();   // idempotent; guarantees _compiled regardless of Harmony prefix order

        ushort uid = 1;
        foreach ((NpcDef def, _) in ContentLoader.NpcDefs)
        {
            if (def.Map != mapId) continue;
            try
            {
                if (!_compiled.Contains(def.Id))
                { Plugin.Log.LogWarning($"npc '{def.Id}': not placed (its dialogue did not compile)."); continue; }
                if (def.Position == null || def.Position.Length < 2)
                { Plugin.Log.LogWarning($"npc '{def.Id}': not placed (needs position [x,y] px)."); continue; }

                Guid baseGuid = NpcBodies.GetBodyGuid(def, ResolveSatyrGuid());

                // WorldDoodad's only ctor wants a live BaseDoodad (we have none until the db loads), but its
                // Base field is [Unmanaged] (never serialized) — so build the instance raw and set the same
                // fields the ctor/initializers would. Position MUST come after ModelToWorld = Identity: the
                // setter writes the translation into the matrix, and field initializers don't run here.
                var d = (WorldDoodad)RuntimeHelpers.GetUninitializedObject(typeof(WorldDoodad));
                d.ModelToWorld = Matrix.Identity;
                d.EditorColor = Vector3.One;
                d.UniqueId = uid++;
                // cmx stores editor coordinates (X, height, Y); both loaders swap with Position.ToXzy()
                d.Position = new Vector3(def.Position[0], 0f, def.Position[1]);
                var yarnParams = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["yarn_dialogue_path"] = "cf/" + SafeId(def.Id),
                    ["yarn_starting_node"] = string.IsNullOrWhiteSpace(def.StartNode) ? "Start" : def.StartNode,
                    ["dialogue_distance"] = (def.DialogueDistance > 0f ? def.DialogueDistance : 32f)
                        .ToString(CultureInfo.InvariantCulture),
                };
                // BOTH sides: in dungeon rooms the client entity spawns FROM THE SERVER's spawn data, which
                // forwards only ServerControllerParameters (OnWorldRead: Parameters = ServerControllerParameters
                // .Dictionary → Send_SpawnEntity → client merges into the controller before EntityInitialize).
                // ClientControllerParameters covers the client-local doodad-load path. Without the server copy
                // the satyr runs its .cdx default dialogue (the vanilla satyr-boss lines).
                d.ClientControllerParameters = new Parameters(yarnParams);
                d.ServerControllerParameters = new Parameters(new Dictionary<string, string>(yarnParams, StringComparer.Ordinal));

                // (Item1 != null marks "has a base reference"; Item2 = database key; Item3 = the guid BOTH
                // loaders actually resolve by; Item4/Item5/Item6 = directory/path/name, name doubles as the
                // client's root-folder-only fallback and the server's warning text.)
                _doodadExtra[d] = (DbFileName, DbDirectory, baseGuid, DbDirectory, BaseDoodadPath, BaseDoodadName);
                world.Doodads.Add(d);
                Plugin.Log.LogInfo($"Placed npc '{def.Id}' in '{mapId}' at ({def.Position[0]}, {def.Position[1]}).");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"npc '{def.Id}': placement failed: {e.Message}"); }
        }
        if (uid > 1) world.NextDoodadId = uid;
    }

    // The satyr's Guid, read from the doodad database INDEX (Content/db/doodads/doodads.cdb — a text file:
    // "cdb10" header line, then per line a 36-char guid, one separator char, the doodad's path). The binary
    // .cdx files never need parsing. Falls back to the known shipped Guid if the index moved.
    private static Guid ResolveSatyrGuid()
    {
        if (_satyrGuid != Guid.Empty) return _satyrGuid;
        try
        {
            string cdb = Path.Combine(ContentDir, "db", "doodads", DbFileName + ".cdb");
            if (File.Exists(cdb))
                foreach (string line in File.ReadLines(cdb))
                {
                    if (line.Length < 38) continue;
                    string path = line[37..].Trim();
                    if ((path.EndsWith("/" + BaseDoodadName, StringComparison.Ordinal) || path == BaseDoodadName)
                        && Guid.TryParse(line.AsSpan(0, 36), out Guid g))
                    { _satyrGuid = g; return g; }
                }
            Plugin.Log.LogWarning($"'{BaseDoodadName}' not found in {DbFileName}.cdb — using the known shipped Guid.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"doodads.cdb scan failed ({e.Message}) — using the known shipped Guid."); }
        _satyrGuid = FallbackSatyrGuid;
        return _satyrGuid;
    }

    // ------------------------------------------------------------------ 3) actor display names

    // <<Actor key sheet frame title>> shows StringDefinitions.GetString(key) as the speaker name; unknown keys
    // render as "<key>". Packs map key -> display name; never overwrites a key the game already defines.
    public static void ApplyActorStrings()
    {
        // Re-applied on every SetLocalization, so resolving against the CURRENT language live-switches names.
#if CF_SERVER
        const string lang = "en";   // no UI language on a dedicated server
#else
        string lang = Candide.Globals.LanguageCode;
#endif
        foreach ((NpcDef def, _) in ContentLoader.NpcDefs.Concat(ContentLoader.PlaceableDialogues))
        {
            if (def.Actors == null) continue;
            Dictionary<string, string> loc = null;
            if (lang != null) def.ActorsLocalized?.TryGetValue(lang, out loc);
            foreach (var kv in def.Actors)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || StringDefinitions.Strings.ContainsKey(kv.Key)) continue;
                string name = kv.Value ?? kv.Key;
                if (loc != null && loc.TryGetValue(kv.Key, out string ln) && !string.IsNullOrWhiteSpace(ln)) name = ln;
                StringDefinitions.Strings[kv.Key] = new StringDefinition(name);
            }
        }
    }
}

// Dialogue files must exist before a player can interact (same window as map generation; a second patch
// class on the same method is fine in Harmony, and AddNpcDoodads re-runs the generation if it fires first).
internal static class NpcFilesPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ServerDataSetup), nameof(ServerDataSetup.SetupDataFileLoading))]
    private static void Prefix()
    {
        try { ContentLoader.EnsureParsed(Plugin.PacksDir); NpcsFeature.GenerateDialogueFiles(); }
        catch (Exception e) { Plugin.Log.LogWarning($"NPC dialogue generation failed: {e.Message}"); }
    }
}

// Actor names re-apply after every language (re)load — SetLocalization clears StringDefinitions.Strings.
internal static class NpcStringsPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.SetLocalization))]
    private static void Postfix()
    {
        try { ContentLoader.EnsureParsed(Plugin.PacksDir); NpcsFeature.ApplyActorStrings(); }
        catch (Exception e) { Plugin.Log.LogWarning($"NPC actor string injection failed: {e.Message}"); }
    }
}

#if !CF_SERVER
// Forge dialogue commands — fill the one gap in the game's Yarn API: vanilla dialogue can CHECK and TAKE
// (HandInItem) but never GIVE. Unknown <<commands>> fall through to a library-function DynamicInvoke with
// STRING args, so these register as string-typed delegates on the same library every dialogue runner builds:
//   <<ForgeGiveItem "mymod:sword" 1>>   — put items in the player's bag
//   <<ForgeTakeItem "material:coal" 5>> — remove items (no quest needed; check the amount with
//                                         GetInventoryItemAmount first or the command just fails silently)
//   <<ForgeHeal 30>>                    — heal over ~5s (the same aura the shrine preset uses)
// Effects run through the server managers — authoritative in single-player and for the HOST in co-op
// (a non-host client's dialogue can't reach the server state; document packs accordingly).
internal static class ForgeYarnCommandsPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Candide.Dialogue.CustomYarnCommands), "RegisterCustomCommands")]
    private static void Postfix(Yarn.Library yarnLibrary)
    {
        try
        {
            yarnLibrary.RegisterFunction("ForgeGiveItem", (Delegate)new Action<string, string>(GiveItem));
            yarnLibrary.RegisterFunction("ForgeTakeItem", (Delegate)new Action<string, string>(TakeItem));
            yarnLibrary.RegisterFunction("ForgeHeal", (Delegate)new Action<string>(Heal));
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Forge yarn commands failed to register: {e.Message}"); }
    }

    // The dialogue runs on the local client; effects need the in-process server (single-player / host).
    private static bool TryGetLocalServerPlayer(out CandideServer.ServerServices.ServerJoinedPlayerModel player, out Guid entityId)
    {
        player = null;
        entityId = Candide.GameModels.GameState.LocalPlayer?.EntityId ?? Guid.Empty;
        if (entityId == Guid.Empty) return false;
        if (!CandideServer.ServerGameState.Entities.TryGetValue(entityId, out var model)) return false;
        return CandideServer.ServerManagers.PlayerServerManager.TryGetPlayerFromEntity(model.EntityWrapper, out player);
    }

    private static void GiveItem(string itemId, string amount)
    {
        try
        {
            if (!int.TryParse(amount, out int n) || n < 1 || string.IsNullOrWhiteSpace(itemId)) return;
            if (!TryGetLocalServerPlayer(out var player, out _))
            { Plugin.Log.LogWarning("<<ForgeGiveItem>>: no in-process server player (co-op client?)."); return; }
            Guid inv = player.CharacterModel.Character.CharacterInventoryModel.InventoryId;
            CandideServer.ServerManagers.SimpleInventoryServerManager.TryAddItemAmounts(inv, itemId, n, null, allowPartialStack: true, null);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"<<ForgeGiveItem>> failed: {e.Message}"); }
    }

    private static void TakeItem(string itemId, string amount)
    {
        try
        {
            if (!int.TryParse(amount, out int n) || n < 1 || string.IsNullOrWhiteSpace(itemId)) return;
            if (!TryGetLocalServerPlayer(out var player, out _)) return;
            Guid inv = player.CharacterModel.Character.CharacterInventoryModel.InventoryId;
            CandideServer.ServerManagers.SimpleInventoryServerManager.TryRemoveItems(
                new CandideServer.MessageModels.Inventories.RemoveItemsFromInventoryMessage
                {
                    InventoryId = inv,
                    RemoveItemsArgs = new[] { new Shared.Models.Items.ItemAmount(itemId, n) },
                });
        }
        catch (Exception e) { Plugin.Log.LogWarning($"<<ForgeTakeItem>> failed: {e.Message}"); }
    }

    private static void Heal(string amount)
    {
        try
        {
            if (!float.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out float h) || h <= 0f) return;
            if (!TryGetLocalServerPlayer(out _, out Guid entityId)) return;
            CandideServer.ServerManagers.AuraServerManager.TryApplyAuraToEntity("aura:slow_heal", entityId, entityId,
                new Shared.Aura.Args.HealingOverTimeAuraArgs
                {
                    HealingPerTick = h / 5f,
                    TimeBetweenTicks = 1f,
                    Duration = 5f,
                }, CandideServer.SyncStrategies.SyncStrategy.SendToAll);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"<<ForgeHeal>> failed: {e.Message}"); }
    }
}
#endif
