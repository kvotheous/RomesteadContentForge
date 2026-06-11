using CandideServer;                        // ServerGlobals
using CandideServer.Data.DataModels;        // CitizenData
using CandideServer.Database;               // CitizenDataBase
using CandideServer.Models.Citizen;         // CitizenModel, JobProgress
using CandideServer.ServerControllers;      // CitizensSController
using CandideServer.ServerManagers;         // CitizenNameServerManager
using HarmonyLib;
using Shared;                               // EntityIds
using Shared.Data;                          // JobDataBase
using Shared.Models.Citizens;               // CitizenAppearance
using Shared.Text;                          // StringId
using System;
using System.Collections.Generic;
using System.Linq;
// The client controller lives in a namespace that ends with its own name — alias to stay readable.
using ClientCitizenController = Candide.Entities.Controllers.CitizenController.CitizenController;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Custom CITIZENS (the town job-workers) + edits to the vanilla citizen types.
//
// WHAT A CITIZEN TYPE IS (CandideServer.Data.DataModels.CitizenData — deliberately shallow):
//   Id, BaseEntityGuid (always EntityIds.GenericCitizenBaseId in vanilla), IconId (the wee portrait shown
//   in citizen slots / tooltips — IconDataBase, so pack icons work), DefaultStats (the SIX citizen stats:
//   Citizen_Efficiency / Expertise / Happiness / FoodCost / ExperienceGain / LoyaltyGain). That's ALL.
//   No name, no rarity, no appearance hook — vanilla types: citizen:blacksmith, citizen:lumberjack,
//   citizen:leatherworker, citizen:merchant, citizen:generic (ServerDataSetup.SetupCitizens).
//
// REGISTRATION: CitizenDataBase.CitizenDataMap is filled ONCE per process (SharedDataSetup.SharedSetup →
//   CitizenDataBase.AddCitizens) and — unlike the shared DBs — is NEVER cleared on world reload. We postfix
//   AddCitizens (vanilla entries are then guaranteed present, so overrides hit) and also run from
//   SetupPatch's postfix as a belt-and-braces re-apply; both paths are plain map writes → idempotent.
//
// HOW CITIZENS ARRIVE (researched; there is NO weighting in vanilla):
//   1. organic: a WILD citizen wanders the world (ServerWildCitizenEntityController, tier rolled from map
//      difficulty: Bad/Normal/Good/Legendary*), the player talks to it → "citizens-send-wild-citizen-to-
//      closest-town" → CitizenServerManager.ConvertWildCitizenToTownCitizen → CitizensSController.
//      CreateCitizenForTownFromWildCitizen — which HARDCODES CitizenBaseId = "citizen:generic".
//   2. cheat: "citizens-add-generic-citizen" → CitizenServerManager.AddNewCitizenToTown → CitizensSController
//      .CreateCitizenForTown → CitizenDataBase.TryCreateCitizenModel("citizen:generic", ...).
//   So a new type would NEVER appear naturally — we add the weighting ourselves:
//      - prefix TryCreateCitizenModel: "citizen:generic" is re-rolled to a weighted pick among
//        {generic: weight 100 (override via citizenOverrides) + every pack citizen's weight};
//      - postfix CreateCitizenForTownFromWildCitizen: same roll for recruited wild citizens — but only for
//        no-job recruits (Bad/Normal tiers); Good/Legendary recruits keep their rolled craft + stats.
//   Both end in CitizenServerManager.AddCitizenToTown → the "Town_Chat_CitizenArriveInTown" chat line with
//   the ui_chat_citizen_added jingle — pack citizens arrive through the normal flow, name in the chat.
//
// APPEARANCE (the part vanilla never exposes): citizens use the PLAYER skeleton ("player_new_animations"
//   spac) skinned via PlayerSkinManager.ApplyCitizenSkinAndDisplayData from a per-INSTANCE CitizenAppearance
//   (record struct: IsMale, Hair, HairColor, EyeColor, Tunic, TunicColor, ShoeColor, BodyColor — bytes
//   indexing the same parts/palette rows as character creation — plus DisplayIds: an array of "cdd:" looks,
//   the same CharacterDisplayData machinery our playerSkins.looks already extends; vanilla uses it for Gaius
//   Solinus' flamines robes). Appearance is rolled ONCE per citizen, SEEDED from the citizen's Guid
//   (RandomCitizenGenerator.GenerateRandomCitizenAppearance) — that's why the walking entity looks right on
//   every client without sync: each client re-derives the same "random" face from the Guid.
//   FIXED LOOKS therefore need both sides:
//     server: model.Appearance overwritten at creation (replicates through CitizenTownOverviewData /
//             GetCitizenResponse — town-overview portraits and dialogue use it);
//     client: the ENTITY falls back to the Guid-seeded roll unless the citizen is actively synced, so the
//             server stamps "cf_citizen" = type id into the spawn Parameters (round-trips through the save)
//             and a client postfix on CitizenController.EntityInitialize re-skins from the pack look.
//
// NAMES: a citizen's Name is a StringId (localization KEY — vanilla draws from the Citizens_Name_* pools
//   via CitizenNameServerManager). CitizenData itself has NO name and NO loc-key suffix exists for citizen
//   types (verified: "*citizenaura:name" and "*building_type:name" exist, "*citizen:name" does not) — so
//   "<id>*citizen:name" is OUR convention, registered through the normal _strings/_locStrings pipeline and
//   set as the model's Name when a pack citizen with a fixed `name` is created. No fixed name → vanilla
//   random names (re-rolled for the right gender if the look pins one).
//
// CO-OP: creation/arrival is fully server-authoritative — only the HOST needs the mod+pack for pack
//   citizens to exist and work. Clients need the pack too or they see: fallback portrait icon ("sam" via
//   GetCitizenOrMissing), the raw "<id>*citizen:name" key as the name, and the Guid-seeded random look
//   instead of the fixed one. UNINSTALL: saved citizens keep working (CitizenModel round-trips through the
//   save with stats; only the icon/name/look degrade as above) — nothing bricks.
// ---------------------------------------------------------------------------------------------------------

internal static class CitizensFeature
{
    // Parsed from packs by ContentLoader.BuildPack (dependency order). (def, pack file name) for log blame.
    internal static readonly List<(CitizenDef Def, string Pack)> Defs = new();
    internal static readonly List<(string Id, CitizenOverrideDef Def, string Pack)> Overrides = new();

    // Registered pack citizens by id — the roll table, the fixed-look/name source, the client skin lookup.
    private static readonly Dictionary<string, CitizenDef> _byId = new();
    // Arrival weight of the vanilla "citizen:generic" (every natural arrival in vanilla). A pack can lower
    // it via citizenOverrides["citizen:generic"].weight — 0 = only pack citizens arrive.
    private static int _genericWeight = 100;
    private static bool _announced;
    private static readonly HashSet<string> _warned = new();   // one warning per problem, not per arrival

    private const string ParamKey = "cf_citizen";        // entity spawn parameter carrying the type id
    private const string NameSuffix = "*citizen:name";   // our loc-key convention (none exists in vanilla)
    private const string GenericId = "citizen:generic";

    // Called from SetupPatch's postfix AND from the AddCitizens postfix below. Plain map writes — running
    // it any number of times converges to the same state.
    public static void Register()
    {
        foreach ((CitizenDef def, string pack) in Defs)
        {
            try { RegisterOne(def, pack); }
            catch (Exception e) { Plugin.Log.LogWarning($"[{pack}] citizen '{def?.Id}': registration failed: {e.Message}"); }
        }
        ApplyOverrides();
        if (!_announced && (_byId.Count > 0 || Overrides.Count > 0))
        {
            _announced = true;
            Plugin.Log.LogInfo($"Content Forge: {_byId.Count} citizen type(s) registered, {Overrides.Count} citizen override(s).");
        }
    }

    private static void RegisterOne(CitizenDef def, string pack)
    {
        CitizenDataBase.CitizenDataMap[def.Id] = new CitizenData
        {
            Id = def.Id,
            BaseEntityGuid = EntityIds.GenericCitizenBaseId,   // the only citizen body vanilla has
            IconId = string.IsNullOrWhiteSpace(def.Icon) ? "ui:citizen" : def.Icon,
            DefaultStats = BuildStats(def.Stats),
        };
        _byId[def.Id] = def;
    }

    // Edits to EXISTING CitizenData entries (vanilla or another pack's — deps load first). Skipped silently
    // until the target id exists: on first boot SetupPatch fires BEFORE vanilla AddCitizens, the AddCitizens
    // postfix then re-runs us with everything present.
    private static void ApplyOverrides()
    {
        foreach ((string id, CitizenOverrideDef ov, string pack) in Overrides)
        {
            CitizenData data = CitizenDataBase.GetCitizenOrNull(id);
            if (data == null)
            {
                if (CitizenDataBase.CitizenDataMap.ContainsKey(GenericId) && _warned.Add("ov:" + id))
                    Plugin.Log.LogWarning($"[{pack}] citizenOverrides: '{id}' is not a known citizen type — ignored.");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(ov.Icon)) data.IconId = ov.Icon;
            if (ov.Stats != null)
                foreach (KeyValuePair<string, float> kv in ov.Stats)
                    data.DefaultStats[NormalizeStatKey(kv.Key)] = kv.Value;
            if (ov.Weight.HasValue)
            {
                if (id == GenericId) _genericWeight = Math.Max(0, ov.Weight.Value);
                else if (_warned.Add("w:" + id))
                    Plugin.Log.LogWarning($"[{pack}] citizenOverrides: weight on '{id}' has no effect — " +
                        "only citizen:generic ever arrives in vanilla (other types are wild-recruit stat templates).");
            }
        }
    }

    // The six citizen stats, at citizen:generic's baseline; pack values overlay. Accepts the raw ids
    // ("Citizen_Efficiency") or the shorthand ("efficiency", "foodCost", ...).
    private static Dictionary<string, float> BuildStats(Dictionary<string, float> stats)
    {
        var d = new Dictionary<string, float>
        {
            { "Citizen_Efficiency", 8f }, { "Citizen_Expertise", 8f }, { "Citizen_Happiness", 0f },
            { "Citizen_FoodCost", 10f }, { "Citizen_ExperienceGain", 1f }, { "Citizen_LoyaltyGain", 1f },
        };
        if (stats != null)
            foreach (KeyValuePair<string, float> kv in stats)
                if (!string.IsNullOrWhiteSpace(kv.Key)) d[NormalizeStatKey(kv.Key)] = kv.Value;
        return d;
    }

    private static string NormalizeStatKey(string k) =>
        k.StartsWith("Citizen_") ? k : "Citizen_" + char.ToUpperInvariant(k[0]) + k.Substring(1);

    // Weighted arrival roll: vanilla generic vs every pack citizen with weight > 0.
    internal static string RollArrival()
    {
        int total = Math.Max(0, _genericWeight);
        foreach (CitizenDef d in _byId.Values) total += Math.Max(0, d.Weight);
        if (total <= 0) return GenericId;     // everything zeroed out — keep the game functional
        int roll = ServerGlobals.Random.Next(total);
        roll -= Math.Max(0, _genericWeight);
        if (roll < 0) return GenericId;
        foreach (CitizenDef d in _byId.Values)
        {
            roll -= Math.Max(0, d.Weight);
            if (roll < 0) return d.Id;
        }
        return GenericId;
    }

    internal static bool TryGetDef(string citizenBaseId, out CitizenDef def)
        => _byId.TryGetValue(citizenBaseId ?? "", out def);

    // Stamp a freshly created pack citizen with its fixed name / fixed look / starting job levels.
    internal static void ApplyToModel(CitizenModel m)
    {
        if (m == null || !_byId.TryGetValue(m.CitizenBaseId, out CitizenDef def)) return;

        if (def.Look != null) m.Appearance = BuildAppearance(def.Look, m.Appearance);

        if (!string.IsNullOrWhiteSpace(def.Name))
            m.Name = (StringId)(def.Id + NameSuffix);
        else if (def.Look?.Male != null)
            // no fixed name but a pinned gender — the random name vanilla rolled may not match it
            m.Name = CitizenNameServerManager.GetRandomName(def.Look.Male.Value ? "male" : "female");

        if (def.JobLevels != null)
        {
            m.JobExperience ??= new Dictionary<string, JobProgress>();
            foreach (KeyValuePair<string, int> kv in def.JobLevels)
            {
                if (JobDataBase.GetJobOrNull(kv.Key) == null)
                {
                    if (_warned.Add("job:" + def.Id + ":" + kv.Key))
                        Plugin.Log.LogWarning($"citizen '{def.Id}': unknown job '{kv.Key}' in jobLevels — skipped.");
                    continue;
                }
                m.JobExperience[kv.Key] = new JobProgress { Level = Math.Clamp(kv.Value, 0, 4) };
            }
        }
    }

    // Overlay the look's pinned fields on a (Guid-seeded random) base appearance. Unset fields keep the
    // random roll, so a look can pin just the wardrobe and leave the face to chance. Both sides call this
    // on the same seeded base → identical result everywhere.
    internal static CitizenAppearance BuildAppearance(CitizenLookDef l, CitizenAppearance baseApp)
    {
        CitizenAppearance a = baseApp;   // record struct — value copy
        if (l.Male.HasValue) a.IsMale = l.Male.Value;
        if (l.Hair.HasValue) a.Hair = (byte)l.Hair.Value;
        if (l.HairColor.HasValue) a.HairColor = (byte)l.HairColor.Value;
        if (l.EyeColor.HasValue) a.EyeColor = (byte)l.EyeColor.Value;
        if (l.Body.HasValue) a.BodyColor = (byte)l.Body.Value;
        if (l.Tunic.HasValue) a.Tunic = (byte)l.Tunic.Value;
        if (l.TunicColor.HasValue) a.TunicColor = (byte)l.TunicColor.Value;
        if (l.ShoeColor.HasValue) a.ShoeColor = (byte)l.ShoeColor.Value;
        if (l.DisplayIds != null && l.DisplayIds.Count > 0) a.DisplayIds = l.DisplayIds.ToArray();
        return a;
    }
}

// Vanilla fills CitizenDataBase exactly once per process (SharedSetup, AFTER SharedDataSetup.Setup — so
// SetupPatch's first Register ran too early for overrides; this one is the authoritative re-apply).
[HarmonyPatch(typeof(CitizenDataBase), nameof(CitizenDataBase.AddCitizens))]
internal static class CitizenDataBasePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try { CitizensFeature.Register(); }
        catch (Exception e) { Plugin.Log.LogError($"Citizen registration failed: {e}"); }
    }
}

// The "fresh citizen" creation chokepoint (the citizens-add-generic-citizen cheat, and any future vanilla
// path that mints a citizen from a type id). The prefix swaps the hardcoded "citizen:generic" for a
// weighted roll; the postfix stamps fixed name/look/job levels when a pack type was created.
[HarmonyPatch(typeof(CitizenDataBase), nameof(CitizenDataBase.TryCreateCitizenModel))]
internal static class CitizenCreatePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref string citizenId)
    {
        try { if (citizenId == "citizen:generic") citizenId = CitizensFeature.RollArrival(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Citizen arrival roll failed: {e.Message}"); }
    }

    [HarmonyPostfix]
    private static void Postfix(bool __result, CitizenModel citizenModel)
    {
        try { if (__result) CitizensFeature.ApplyToModel(citizenModel); }
        catch (Exception e) { Plugin.Log.LogWarning($"Citizen fixups failed: {e.Message}"); }
    }
}

// The ORGANIC arrival path: a recruited wild citizen becomes a town citizen (hardcoded citizen:generic).
// Re-roll the type for jobless recruits only — Good/Legendary wild citizens carry rolled job levels the
// player recruited them FOR; those keep their craft, stats and identity untouched.
[HarmonyPatch(typeof(CitizensSController), nameof(CitizensSController.CreateCitizenForTownFromWildCitizen))]
internal static class CitizenWildRecruitPatch
{
    [HarmonyPostfix]
    private static void Postfix(CitizenModel __result)
    {
        try
        {
            if (__result == null) return;
            if (__result.JobExperience != null && __result.JobExperience.Count > 0) return;   // skilled recruit
            string id = CitizensFeature.RollArrival();
            if (id == "citizen:generic" || !CitizensFeature.TryGetDef(id, out _)) return;
            CitizenData data = CitizenDataBase.GetCitizenOrNull(id);
            if (data == null) return;
            __result.CitizenBaseId = id;
            // the type's stats replace the wild roll (a typed citizen IS its stat template — vanilla's own
            // legendary tiers work the same way); CitizenStats is a struct FIELD, so this mutates in place
            __result.CitizenStats.SetBaseStats(data.DefaultStats, Array.Empty<(string, float)>());
            CitizensFeature.ApplyToModel(__result);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Citizen recruit conversion failed: {e.Message}"); }
    }
}

// Server → client look bridge: the walking citizen entity derives its appearance from a Guid-seeded random
// roll on EVERY client (no sync) — a fixed look must travel some other way. The spawn Parameters round-trip
// through the network AND the save, so stamp the type id there for CitizenClientLookPatch to pick up.
[HarmonyPatch(typeof(CitizensSController), nameof(CitizensSController.SpawnCitizen))]
internal static class CitizenSpawnParamPatch
{
    [HarmonyPrefix]
    private static void Prefix(CitizenModel citizen, ref Dictionary<string, string> parameters)
    {
        try
        {
            if (citizen == null || !CitizensFeature.TryGetDef(citizen.CitizenBaseId, out CitizenDef def) || def.Look == null)
                return;
            // vanilla only builds the default parameter set when null — pre-build it so our key survives
            parameters ??= new Dictionary<string, string>
            {
                { "citizen_id", citizen.Id.ToString() },
                { "citizen_name", citizen.Name.Id },
                { "active_request_status", citizen.ActiveRequestState.Status.ToString() },
            };
            parameters["cf_citizen"] = citizen.CitizenBaseId;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Citizen spawn param failed: {e.Message}"); }
    }
}

// CLIENT: re-skin a fixed-look citizen entity after vanilla applied the Guid-seeded random appearance.
// Runs on every machine with the mod+pack (the look fields/displayIds resolve through PlayerSkinManager
// .Parts + CharacterDisplayDataBase — pack "cdd:" looks registered by ApplyDisplayData/playerSkins work).
[HarmonyPatch(typeof(ClientCitizenController), nameof(ClientCitizenController.EntityInitialize))]
internal static class CitizenClientLookPatch
{
    [HarmonyPostfix]
    private static void Postfix(ClientCitizenController __instance)
    {
        try
        {
            string id = __instance.Parameters.GetString("cf_citizen", null);
            if (string.IsNullOrEmpty(id) || !CitizensFeature.TryGetDef(id, out CitizenDef def) || def.Look == null)
                return;
            __instance.Appearance = CitizensFeature.BuildAppearance(def.Look, __instance.Appearance);
            __instance.AnimationStateMachine.Skins = __instance.GetSkin(__instance.Appearance);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Citizen client look failed: {e.Message}"); }
    }
}
