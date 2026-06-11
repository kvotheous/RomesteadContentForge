using CandideServer.Database;               // WorkRecipeGroupDataBase (job level -> recipe ids; also loaded client-side)
using HarmonyLib;
using Shared.Data;                          // BuildingDataBase, ConstructionDataBase, JobDataBase, WorkRecipeDataBase
using Shared.Data.DataModels;               // WorkRecipeModel, WorkOutputModel, WorkOrderType, FuelSourceRequirements
using Shared.Models.Buildings;              // BuildingData, CitizenSlotData
using Shared.Models.Construction;           // ConstructionModel, ConstructionType, ConstructionUiCategory, ...
using Shared.Models.Items;                  // ItemAmount, ResourceAmount
using Shared.Text;                          // StringId
using System;
using System.Collections.Generic;
using System.Linq;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Custom TOWN JOB-BUILDINGS (the workshops citizens staff: bakery/forge tier) + custom WORK RECIPES
// (what those citizens produce).
//
// WHAT A TOWN BUILDING IS (all data, researched):
//   construction (ConstructionModel, Type=Building, SpawnedId -> building id)  — the build-menu entry: cost,
//     footprint, hammering effort. Same DB our furniture already uses; unlocked through the same login patch.
//   building (Shared.Models.Buildings.BuildingData, BuildingDataBase)          — ExteriorMap/InteriorMap (map
//     ASSETS — the exterior's tiles get stamped into the town when built), CitizenSlots (JobId + where the
//     worker stands), Input/Output logistics, appeal, dispensers, proximity functionality.
//   building TYPE (CandideServer BuildingTypeDataBase: "bakery", "blacksmith", ...) — picks the SERVER
//     controller class (BakeryController: furnace + storage/output inventories + work-order ticking). The
//     type name loc key is "<typeId>*building_type:name".
//   Citizen behaviour is job-agnostic: EVERY vanilla job maps to GenericServerCitizenController
//   (ServerDataSetup_CitizenControllers), so reusing a vanilla job + building type means ZERO new controllers.
//
// WHAT A JOB PRODUCES: WorkRecipeModel (WorkRecipeDataBase, shared) grouped per job LEVEL in
//   WorkRecipeGroupDataBase under ids "{jobId}:{level}" ("job:baker:1" = what a level-1+ baker can queue;
//   WorkRecipesHelper.GetRecipeGroupsForCitizen). Recipes are per-JOB, not per-building — adding to
//   "job:baker:1" teaches every bakery in the world. (Per-building one-offs exist too:
//   ServerRunState.BuildingExtraJobRecipes via JobsServerManager.AddRecipeForSpecificBuilding — not used here.)
//
// WHAT THIS FEATURE DOES (pure data — clone-from-base keeps every hard part consistent):
//   townBuildings: clone a vanilla BuildingData (`base`, e.g. "building:bakery:0") AND its construction
//     (found by SpawnedId) into a new buildable building. The clone keeps the base's maps, footprint,
//     citizen-slot positions, logistics and building TYPE — so the vanilla controller's hardcoded bits
//     (furnace pixel-areas per upgrade rank, desk positions) stay valid. Overridable: name/desc/icon,
//     materials, build effort, appeal, town defence, and the slot JOB (a bakery-shaped building staffed by
//     a blacksmith works: work-order ticking is generic for Item outputs).
//   workRecipes: new WorkRecipeModel entries appended into the "{job}:{level}" recipe group.
//
// LIFECYCLE: buildings/constructions/work-recipes live in SHARED DBs cleared+rebuilt on every
//   SharedDataSetup.Setup — re-registered from SetupPatch's postfix each time. Recipe GROUPS live in a
//   CandideServer DB filled ONCE per process (ServerDataSetup.SharedSetup, AFTER the shared Setup — so on
//   first boot our SetupPatch postfix runs too early to append): a postfix on AddRecipeGroups does the
//   authoritative first bind, SetupPatch re-binds (idempotent contains-check) on later reloads.
//
// CO-OP: building spawn + work-order production are server-authoritative (host-only). Clients WITHOUT the
//   pack see the building work (exterior tiles + interior replicate as map names — vanilla maps resolve
//   everywhere) but show raw loc keys for its name and "missing" work-recipe entries in the job UI
//   (the recipe ids replicate, the models resolve locally). Ship the pack to everyone for clean UI.
// UNINSTALL: a saved world that contains a pack building will fail to resolve its BuildingData/construction
//   (GetBuildingOrMissing) — same caveat as every shared-DB feature: remove pack content before uninstalling.
// ---------------------------------------------------------------------------------------------------------

// A new town job-building cloned from a vanilla one. The base fixes maps/footprint/slots/controller; you
// re-skin the identity and economy around it.
internal sealed class TownBuildingDef
{
    public string Id;                              // required, namespaced ("mymod:grand_bakery") — the construction id;
                                                   // the building data id becomes "building:<id>"
    public string Base;                            // required: vanilla BuildingData id to clone ("building:bakery:0",
                                                   // "building:blacksmith:0", "building:farmstead:1", ...)
    public string Name;                            // build-menu name (loc key "<id>*construction:name")
    public Dictionary<string, string> NameLocalized;
    public string Description;                     // build-menu description
    public Dictionary<string, string> DescriptionLocalized;
    public string Icon;                            // build-menu/building icon (vanilla id or pack icon); default: base's
    public Dictionary<string, int> Materials;      // construction cost, e.g. { "resource:lumber": 5 }; default: base's
    public float BuildProgress = -1f;              // hammering effort; default: base construction's
    public float Appeal = -1f;                     // MaxBuildingAppeal (decoration cap); default: base's
    public string Job;                             // optional: replace the slot job ("job:blacksmith") — the worker
                                                   // type employed inside; default: base's (bakery -> job:baker)
    public int? TownDefence;                       // optional town defence contribution; default: base construction's
}

// A new work recipe (what a citizen produces at its job-building), unlocked at a job level.
internal sealed class WorkRecipeDef
{
    public string Id;                              // required ("recipe:mymod:garum_bread" — vanilla convention is "recipe:")
    public string Job;                             // required: the job that learns it ("job:baker", "job:blacksmith", ...)
    public int Level = 1;                          // job level that unlocks it (appended to group "<job>:<level>")
    public string Output;                          // required: crafted item id (or construction resource id, see below)
    public int Amount = 1;                         // produced per completion
    public bool ResourceOutput;                    // true = Output is a construction RESOURCE ("resource:lumber" tier)
    public Dictionary<string, int> Ingredients;    // item id -> count, pulled from the building's storage
    public Dictionary<string, int> Resources;      // construction resource -> count (internal resource storage)
    public float Progress = 30f;                   // work effort (bread 30, armor ~60-150)
    public float Experience = 3f;                  // job XP per completion
    public bool Fuel;                              // true = needs the building's furnace lit (bakery/blacksmith have one)
    public string Name;                            // optional display name; default: the output item's name
    public Dictionary<string, string> NameLocalized;
    public string Description;                     // optional tooltip text
    public Dictionary<string, string> DescriptionLocalized;
    public string Icon;                            // optional; default: the output item's icon
    public bool Unrepeatable;                      // true = can't be set to repeat in the work queue
}

internal static class TownBuildingsFeature
{
    // Parsed from packs by ContentLoader.BuildPack (dependency order). (def, pack file name) for log blame.
    internal static readonly List<(TownBuildingDef Def, string Pack)> Defs = new();
    internal static readonly List<(WorkRecipeDef Def, string Pack)> RecipeDefs = new();

    // Vanilla's recipe groups are in (ServerDataSetup.SharedSetup ran) — creating NEW "{job}:{level}" groups
    // is safe now (creating one EARLIER would block vanilla's TryAdd and eat the job's own recipes).
    internal static bool GroupsReady;
    private static bool _binding;                  // our own AddRecipeGroups call re-enters the postfix — gate it
    private static bool _announced;
    private static readonly HashSet<string> _warned = new();   // one warning per problem, not per Setup

    // Called from SetupPatch's postfix (shared DBs just rebuilt — buildings/constructions/recipes must be
    // re-added) and indirectly from the AddRecipeGroups postfix (group binding). Idempotent.
    public static void Register()
    {
        foreach ((TownBuildingDef def, string pack) in Defs)
        {
            try { RegisterBuilding(def, pack); }
            catch (Exception e) { Plugin.Log.LogWarning($"[{pack}] townBuilding '{def?.Id}': registration failed: {e.Message}"); }
        }
        RegisterRecipes();
        BindRecipeGroups();   // no-op for groups that don't exist yet on first boot (postfix below finishes it)
        if (!_announced && (Defs.Count > 0 || RecipeDefs.Count > 0))
        {
            _announced = true;
            Plugin.Log.LogInfo($"Content Forge: {Defs.Count} town building(s), {RecipeDefs.Count} work recipe(s) registered.");
        }
    }

    private static void RegisterBuilding(TownBuildingDef def, string pack)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.Id) || string.IsNullOrWhiteSpace(def.Base))
        { Plugin.Log.LogWarning($"[{pack}] townBuilding skipped (needs id + base)."); return; }
        if (ConstructionDataBase.DataMap.ContainsKey(def.Id)) return;   // already added this Setup cycle

        BuildingData baseB = BuildingDataBase.GetBuildingOrNull(def.Base);
        if (baseB == null)
        { Plugin.Log.LogWarning($"[{pack}] townBuilding '{def.Id}': base building '{def.Base}' not found (vanilla ids look like \"building:bakery:0\")."); return; }
        // The construction that builds the base (its footprint MUST match the exterior map we're inheriting).
        ConstructionModel baseC = ConstructionDataBase.DataMap.Values
            .FirstOrDefault(c => c.Type == ConstructionType.Building && c.SpawnedId == def.Base);
        if (baseC == null)
        { Plugin.Log.LogWarning($"[{pack}] townBuilding '{def.Id}': no vanilla construction spawns '{def.Base}' — can't inherit a footprint."); return; }

        // Optional job swap on the cloned citizen slots (validated — JobDataBase was just rebuilt).
        string job = def.Job;
        if (!string.IsNullOrWhiteSpace(job) && JobDataBase.GetJobOrNull(job) == null)
        {
            if (_warned.Add("job:" + def.Id))
                Plugin.Log.LogWarning($"[{pack}] townBuilding '{def.Id}': unknown job '{job}' — keeping the base's job.");
            job = null;
        }
        var slots = new List<CitizenSlotData>();
        foreach (CitizenSlotData s in baseB.CitizenSlots)
            slots.Add(string.IsNullOrWhiteSpace(job) || string.IsNullOrEmpty(s.JobId)
                ? s : new CitizenSlotData(job, s.SpawnPosition, s.SpawnLocation));

        string buildingId = "building:" + def.Id;
        BuildingDataBase.AddBuildings(new List<BuildingData>
        {
            new BuildingData
            {
                Id = buildingId,
                BuildingTypeId = baseB.BuildingTypeId,    // keeps the base's server controller (furnace, inventories)
                IconId = string.IsNullOrWhiteSpace(def.Icon) ? baseB.IconId : def.Icon,
                ExteriorMap = baseB.ExteriorMap,
                BiomeCategoryExteriorMaps = baseB.BiomeCategoryExteriorMaps,
                InteriorMap = baseB.InteriorMap,
                CitizenSlots = slots,
                FoodDispenser = baseB.FoodDispenser,
                WaterDispenser = baseB.WaterDispenser,
                EntityProximityFunctionality = baseB.EntityProximityFunctionality,
                BuildingProximityFunctionality = baseB.BuildingProximityFunctionality,
                TileProximityFunctionality = baseB.TileProximityFunctionality,
                Input = baseB.Input,
                Output = baseB.Output,
                Parameters = new Dictionary<string, string>(baseB.Parameters),   // beehive-style controllers read these
                CanStartTown = baseB.CanStartTown,
                MaxBuildingAppeal = def.Appeal >= 0f ? def.Appeal : baseB.MaxBuildingAppeal,
                IsPlayerDecoratable = baseB.IsPlayerDecoratable,
            }
        });
        ConstructionDataBase.AddConstructions(new List<ConstructionModel>
        {
            new ConstructionModel
            {
                Id = def.Id,
                Name = string.IsNullOrWhiteSpace(def.Name) ? def.Id : def.Name,   // rewritten to "<id>*construction:name"
                Description = string.IsNullOrWhiteSpace(def.Description) ? (StringId?)null : def.Description,
                IconId = string.IsNullOrWhiteSpace(def.Icon) ? baseC.IconId : def.Icon,
                RequiredConstructionProgress = def.BuildProgress >= 0f ? def.BuildProgress : baseC.RequiredConstructionProgress,
                ConstructionMaterialsRequirement = new ConstructionMaterialsAggregate
                {
                    MaterialAmounts = def.Materials ?? new Dictionary<string, int>(baseC.ConstructionMaterialsRequirement.MaterialAmounts)
                },
                ConstructionSpaceInfo = baseC.ConstructionSpaceInfo,   // footprint must match the inherited exterior map
                Type = ConstructionType.Building,
                SpawnedId = buildingId,
                ConstructionUiCategory = ConstructionUiCategory.Buildings,
                // furnace pixel-areas and job level caps key off the construction's UpgradeLevel — inherit it
                // (NOT UpgradeForId: the clone is a standalone buildable, not an upgrade of the vanilla chain)
                UpgradeLevel = baseC.UpgradeLevel,
                TownDefenceValue = def.TownDefence ?? baseC.TownDefenceValue,
                OnlyOutside = baseC.OnlyOutside,
                BuildNearbyConstructions = baseC.BuildNearbyConstructions,
                PlaceSoundId = baseC.PlaceSoundId,
                ConstructSoundId = baseC.ConstructSoundId,
                RequiresTown = baseC.RequiresTown,
                Parameters = baseC.Parameters,
                SpawnInventory = baseC.SpawnInventory,
                SpawnInventoryParameters = baseC.SpawnInventoryParameters,
                SpawnOffset = baseC.SpawnOffset,
            }
        });
    }

    // The recipe MODELS (shared DB, cleared each Setup). Group membership is bound separately below.
    private static void RegisterRecipes()
    {
        var list = new List<WorkRecipeModel>();
        foreach ((WorkRecipeDef r, string pack) in RecipeDefs)
        {
            try
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Job) || string.IsNullOrWhiteSpace(r.Output))
                { Plugin.Log.LogWarning($"[{pack}] workRecipe skipped (needs id + job + output)."); continue; }
                if (WorkRecipeDataBase.DataMap.ContainsKey(r.Id)) continue;   // already added this Setup cycle
                list.Add(new WorkRecipeModel
                {
                    Id = r.Id,
                    ItemIngredients = (r.Ingredients ?? new Dictionary<string, int>())
                        .Select(kv => new ItemAmount { ItemId = kv.Key, Amount = kv.Value }).ToArray(),
                    ResourceAmounts = (r.Resources ?? new Dictionary<string, int>())
                        .Select(kv => new ResourceAmount(kv.Key, kv.Value)).ToArray(),
                    FuelSourceRequirements = r.Fuel ? new FuelSourceRequirements { Active = true } : (FuelSourceRequirements?)null,
                    RequiredProgress = r.Progress <= 0f ? 30f : r.Progress,
                    Experience = r.Experience,
                    Output = new WorkOutputModel
                    {
                        Type = r.ResourceOutput ? WorkOrderType.Resource : WorkOrderType.Item,
                        Amount = r.Amount < 1 ? 1 : r.Amount,
                        CraftedId = r.Output,
                    },
                    // null Name/Icon fall back to the output item's (AddRecipes only re-keys set ones)
                    Name = string.IsNullOrWhiteSpace(r.Name) ? (StringId?)null : r.Name,
                    Description = string.IsNullOrWhiteSpace(r.Description) ? (StringId?)null : r.Description,
                    Icon = string.IsNullOrWhiteSpace(r.Icon) ? null : r.Icon,
                    LevelRequirement = r.Level < 1 ? 1 : r.Level,
                    Unrepeatable = r.Unrepeatable,
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[{pack}] workRecipe '{r?.Id}': {e.Message}"); }
        }
        if (list.Count > 0) WorkRecipeDataBase.AddRecipes(list);
    }

    // Put each recipe id into its "{job}:{level}" group. The group DB is filled once per process and never
    // cleared — append is an idempotent contains-check; missing groups are only CREATED once vanilla's own
    // are in (GroupsReady), otherwise vanilla's TryAdd would lose the job's real recipes to ours.
    internal static void BindRecipeGroups()
    {
        if (_binding) return;
        _binding = true;
        try
        {
            foreach ((WorkRecipeDef r, string pack) in RecipeDefs)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Job)) continue;
                string gid = $"{r.Job}:{(r.Level < 1 ? 1 : r.Level)}";
                WorkRecipeGroupData group = WorkRecipeGroupDataBase.GetRecipesOrNull(gid);
                if (group != null)
                {
                    if (Array.IndexOf(group.RecipeIds, r.Id) < 0)
                        group.RecipeIds = group.RecipeIds.Append(r.Id).ToArray();
                }
                else if (GroupsReady)
                {
                    if (JobDataBase.GetJobOrNull(r.Job) == null)
                    {
                        if (_warned.Add("rjob:" + r.Id))
                            Plugin.Log.LogWarning($"[{pack}] workRecipe '{r.Id}': unknown job '{r.Job}' (vanilla ids look like \"job:baker\").");
                        continue;
                    }
                    WorkRecipeGroupDataBase.AddRecipeGroups(new List<WorkRecipeGroupData>
                    {
                        new WorkRecipeGroupData { Id = gid, RecipeIds = new[] { r.Id } }
                    });
                }
                // else: first boot, vanilla groups aren't in yet — WorkRecipeGroupsPatch re-runs us right after
            }
        }
        finally { _binding = false; }
    }
}

// Vanilla fills WorkRecipeGroupDataBase exactly once per process (ServerDataSetup.SharedSetup, AFTER the
// shared Setup our SetupPatch postfixes) — this postfix is the authoritative first bind of our recipe ids
// into the "{job}:{level}" groups.
[HarmonyPatch(typeof(WorkRecipeGroupDataBase), nameof(WorkRecipeGroupDataBase.AddRecipeGroups))]
internal static class WorkRecipeGroupsPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            TownBuildingsFeature.GroupsReady = true;
            TownBuildingsFeature.BindRecipeGroups();
        }
        catch (Exception e) { Plugin.Log.LogError($"Work recipe group binding failed: {e}"); }
    }
}
