using Candide;                      // Globals (LanguageCode)
#if !CF_SERVER
using Candide.CandideUI;            // PlayerUi (ShowSecondaryCraftingWindow)
using Candide.GameModels.Managers;  // LocalizationManager
#endif
using HarmonyLib;
using Shared.Data;                  // SharedDataSetup, CraftingStationDataBase
using Shared.Entity;                // EntityWrapper
using Shared.Entity.Base;           // AbstractController
using Shared.Models.Crafting;       // CraftingStationData
using Shared.Text;                  // StringDefinitions, StringDefinition
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// CUSTOM CRAFTING STATIONS — a pack placeable that opens its own crafting window listing only that pack's
// recipes. Stations in this game are plain STRINGS end-to-end, so no enum hacking is needed:
//
//   * ItemRecipe.RequiredCraftingStation is `string?` (sh ItemRecipe; the 4 vanilla values are the consts
//     "campfire" / "cauldron" / "clay-barrel" / "war-table"). TryBuildRecipe already passes RecipeDef.Station
//     through verbatim, so pack recipes can carry any station id today.
//   * Vanilla station entities (campfire/cauldron/war table) are Furniture whose controllers' OnInteract all
//     do the same one call: PlayerUi.ShowSecondaryCraftingWindow(Entity.CraftingStationFlags, Entity, dist)
//     (rdll CampfireController/CauldronController/WarTableController). The window filters by simple string
//     compare: ItemRecipeManager.GetLocalPlayerRecipesForStation does stationIds.Contains(
//     recipe.RequiredCraftingStation) over the player's KNOWN recipes — with a non-null station array,
//     station-less recipes are excluded, so the window shows ONLY matching recipes.
//   * The window header resolves the station's name/icon through CraftingStationDataBase
//     .GetCraftingStationOrMissing (falls back to "Unknown"/sam) and the game's recipe validation warns once
//     at load for station ids missing from that database — both fixed by registering a CraftingStationData
//     for each pack station (public AddCraftingStations; the DataMap is cleared on world reload and refilled
//     by SharedDataSetup.Setup, so we re-add from a Setup postfix like everything else).
//   * The SERVER does not validate stations at all: the craft handler (cs2 OnReceive_CraftItem) only resolves
//     the recipe id and crafts — there is no station-proximity check anywhere server-side. The station gate
//     is purely "which recipes the client's window lists", so a custom id passes trivially.
//
// Wiring (the Tier-3 Interactive machinery does the heavy lifting):
//   pack JSON:  "placeables": [{ "id": "my:runeforge", "cloneEntity": "WorkbenchGuid",
//                                "station": "my:runeforge", ... }]
//               "recipes":    [{ "id": "my:r1", "crafted": "my:rune", "station": "my:runeforge", ... }]
//   A non-empty `station` defaults the placeable's controller to the "forge_station" preset and rides the
//   station id in as the "station" controller parameter (the same ConstructionModel.Parameters channel
//   forge_shrine uses: merged into the spawned entity's controller on BOTH sides before EntityInitialize,
//   and persisted in the save). The client controller mirrors the vanilla one-liner with our id; the server
//   controller is a no-op (interaction round-trip requires a server type to exist, nothing more).
//   `station` may be a comma-separated list ("my:runeforge,campfire") — the window then also lists the
//   vanilla station's recipes, for "acts as a campfire too" furniture.
// ---------------------------------------------------------------------------------------------------------

internal static class StationsFeature
{
    // pack station id -> (window-tab icon, display name, optional langcode->name). Collected at pack parse;
    // (re)applied to the game's station database after every SharedDataSetup.Setup.
    private static readonly Dictionary<string, (string Icon, string Name, Dictionary<string, string> NameLocalized)>
        _stations = new(StringComparer.Ordinal);

    // The four ids the base game uses (NB: vanilla only registers station DATA for the first three —
    // "clay-barrel" recipes exist but the id has no CraftingStationData; harmless, it just renders as the
    // missing-station header if a pack reuses it without art).
    internal static readonly HashSet<string> VanillaStationIds = new(StringComparer.OrdinalIgnoreCase)
        { "campfire", "cauldron", "clay-barrel", "war-table" };

    // Register the forge_station preset into the Interactive controller machinery (client opens the crafting
    // window; server is a no-op). Called from Plugin.Load; also re-called defensively from the Setup postfix
    // (idempotent — RegisterController is an indexer assignment).
    internal static void Init()
        => InteractiveFeature.RegisterController("forge_station",
            InteractiveFeature.ClientType("ForgeStationClientController"), typeof(ForgeStationServerController));

    // Called from BuildPack for every placeable (early-outs unless `station` is set), BEFORE TryBuildPlaceable:
    // turn the placeable into a crafting station. Order note: this runs after the yarn block, so a placeable
    // with BOTH yarn and station keeps the dialogue controller — set controller:"forge_station" explicitly to
    // override.
    internal static void PreparePlaceable(PlaceableDef f, string pack)
    {
        if (f == null || string.IsNullOrWhiteSpace(f.Station)) return;
        if (string.IsNullOrWhiteSpace(f.CloneEntity))
        {
            Plugin.Log.LogWarning($"[{pack}] placeable '{f.Id}': `station` requires `cloneEntity` " +
                "(a station must be a real entity, not a decoration) — station ignored.");
            return;
        }
        if (string.IsNullOrWhiteSpace(f.Controller)) f.Controller = "forge_station";
        f.ControllerParams ??= new Dictionary<string, string>();
        f.ControllerParams.TryAdd("station", f.Station);

        // Remember every NEW id for database + name registration (vanilla ids already have data/strings —
        // never overwrite those, or we'd rename the real campfire).
        foreach (string id in SplitStations(f.Station))
        {
            if (VanillaStationIds.Contains(id)) continue;
            _stations.TryAdd(id, (string.IsNullOrWhiteSpace(f.Icon) ? "sam" : f.Icon,
                string.IsNullOrWhiteSpace(f.Name) ? id : f.Name, f.NameLocalized));
        }
    }

    internal static string[] SplitStations(string station)
        => (station ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // (Re)register our stations into CraftingStationDataBase — cleared by ClearSharedDataBases on world
    // reload and refilled by Setup, so this runs from the Setup postfix each time. AddCraftingStations
    // rewrites Name to the "<id>*crafting_station:name" loc key (we register the text in ApplyStrings) and
    // warns on duplicates, hence the ContainsKey guard.
    internal static void Apply()
    {
        if (_stations.Count == 0) return;
        var add = new List<CraftingStationData>();
        foreach (var kv in _stations)
            if (!CraftingStationDataBase.DataMap.ContainsKey(kv.Key))
                add.Add(new CraftingStationData { Id = kv.Key, Name = kv.Key, IconId = kv.Value.Icon });
        if (add.Count > 0) CraftingStationDataBase.AddCraftingStations(add);
        ApplyStrings();
    }

    // The window header shows CraftingStationData.Name.GetTranslation() — register the actual text under the
    // loc key. Re-applied after every SetLocalization (which clears StringDefinitions.Strings); existing keys
    // are never overwritten (vanilla wins; same pattern as the NPC actor names in Npcs.cs).
    internal static void ApplyStrings()
    {
        if (_stations.Count == 0) return;
#if CF_SERVER
        const string lang = "en";   // no UI language on a dedicated server
#else
        string lang = Globals.LanguageCode ?? "en";
#endif
        foreach (var kv in _stations)
        {
            string key = kv.Key + CraftingStationDataBase.SuffixName;
            if (StringDefinitions.Strings.ContainsKey(key)) continue;
            string text = kv.Value.Name;
            if (kv.Value.NameLocalized != null && kv.Value.NameLocalized.TryGetValue(lang, out string t)
                && !string.IsNullOrEmpty(t))
                text = t;
            StringDefinitions.Strings[key] = new StringDefinition(text);
        }
    }
}

// ------------------------------------------------ patches ------------------------------------------------

// Re-add our station data after every Setup (the station database is cleared and rebuilt on world reload,
// same lifecycle as the item/recipe databases SetupPatch handles). EnsureParsed makes this hook order-proof:
// whichever Setup postfix runs first triggers the one-time pack parse that collects the stations.
[HarmonyPatch(typeof(SharedDataSetup), nameof(SharedDataSetup.Setup))]
internal static class StationSetupPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            StationsFeature.Init();   // defensive re-register (no-op when Plugin.Load already did it)
            ContentLoader.EnsureParsed(Plugin.PacksDir);
            StationsFeature.Apply();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Station registration failed: {e.Message}"); }
    }
}

#if !CF_SERVER
// Station display names survive language changes: SetLocalization clears StringDefinitions.Strings and
// refills it from the language file, dropping our "<id>*crafting_station:name" keys — add them back.
[HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.SetLocalization))]
internal static class StationStringsPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try { StationsFeature.ApplyStrings(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Station string injection failed: {e.Message}"); }
    }
}
#endif

// ----------------------------------------------- controllers -----------------------------------------------

#if !CF_SERVER
// CLIENT station: interact -> open the vanilla secondary crafting window scoped to this station's id(s) —
// the exact call the vanilla WarTableController makes, with our ids instead of the entity's Furniture
// CraftingStationFlags (our clones don't need the Furniture component at all). The window itself does the
// rest: header name/icon from CraftingStationDataBase, recipe list = the player's known recipes whose
// RequiredCraftingStation string-matches one of the ids. Controller parameters (from controllerParams;
// "station" is injected automatically from the placeable's `station` field):
//   station    comma-separated station id(s) whose recipes the window lists (required)
//   distance   how far the player can wander before the window auto-closes, default 64
// Inherits the preset plumbing from ForgeClientController: "tooltip"/"tooltip-color" hover text,
// "message"/"message-color" floating text on interact, and the Tier-2 ambient sprite animation.
public sealed class ForgeStationClientController : ForgeClientController
{
    public override void OnInteract(EntityWrapper otherEntity, bool interactionHeld)
    {
        if (interactionHeld) return;   // hold = vanilla pickup/demolish, not our tap action
        base.OnInteract(otherEntity, interactionHeld);   // optional floating "message" param
        try
        {
            if (Entity == null || PlayerUi.DisallowMoreInteractions) return;   // same guard as vanilla stations
            string[] ids = StationsFeature.SplitStations(Parameters?.GetString("station", null));
            if (ids.Length == 0) return;
            float dist = 64f;
            string d = Parameters.GetString("distance", null);
            if (d != null) float.TryParse(d, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out dist);
            PlayerUi.ShowSecondaryCraftingWindow(ids, Entity, dist <= 0f ? 64f : dist);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"forge_station interact failed: {e.Message}"); }
    }
}
#endif

// SERVER station: nothing to simulate — crafting itself is an ordinary inventory craft message the server
// already handles (and the server performs no station checks; see header). A server controller type must
// exist for the interact round-trip and entity registration, that's all.
public sealed class ForgeStationServerController : AbstractController
{
    public override void EntityInitialize() { }
}
