using Candide;                              // Globals (GraphicsDevice)
using Candide.Data;                         // ClientDataSetup, CropDisplayData, CropDisplayStage
using Candide.Database;                     // CropDisplayDataBase
using CandideCreator.Shared;                // Content (SpriteSheet loader, default normal/metal maps)
using CandideCreator.Shared.Graphics;       // SpriteSheet, FrameAnimation
using CandideServer.Data.DataModels;        // CropData, CropType
using CandideServer.Data.SharedDataSetups;  // SharedDataSetup (server-side crop DB fills inside SharedSetup)
using CandideServer.Database;               // CropDataBase
using HarmonyLib;
using Microsoft.Xna.Framework;              // Vector3 (CropDisplayData offsets)
using Microsoft.Xna.Framework.Graphics;     // Texture2D
using Shared.Combat.Spells.Parameters;      // PlaceCropSpellArgs
using Shared.Models.DropTables;             // DropTableSystem, DropTableModel, DropTableItemSpawnModel
using Shared.Models.Items;                  // ItemData, ItemFlag, UsableItem, TradableItem
using System;
using System.Collections.Generic;
using System.IO;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Custom crops: a pack defines a crop (growth times, stages, biomes, harvest drops, per-stage sprites) and
// a seed item that plants it. Three registries are involved:
//   1) SERVER — CropData into CropDataBase (growth simulation + harvest drops). Crops are added inside
//      SharedDataSetup.SharedSetup() (instance method), so CropServerPatch postfixes it and appends ours.
//   2) CLIENT — CropDisplayData into CropDisplayDataBase (per-stage sprite/animation). The display DB fills
//      in ClientDataSetup.SetupClientData() (static), so CropClientPatch postfixes it. The crop links to its
//      display via DisplayId = "crop_display:<cropId>".
//   3) SEED ITEM — a normal ItemData with Flags=Seed and Usable{SpellId="spell:place-crop",
//      SpellTypeArgs=PlaceCropSpellArgs{CropId}} (the vanilla planting spell reads the crop id from the
//      item). BuildSeedItem constructs it; the existing item pipeline (ContentLoader) injects it and
//      registers its "<id>*item:name"/"*item:description" strings like any other custom item.
// Crops themselves have no name UI string (no "*crop:name" key exists anywhere in the game) — players see
// the sprite while growing and the drop items' names on harvest.
// Both registries are cleared + rebuilt on world/client reload, so registration re-runs idempotently
// (skip ids already present in the public maps).
// ---------------------------------------------------------------------------------------------------------

internal static class CropsFeature
{
    // Custom PNG sheets are GPU textures — load each once and reuse across SetupClientData re-runs.
    // Key: "<absolute path>|<frameW>x<frameH>".
    private static readonly Dictionary<string, SpriteSheet> _customSheets = new();

    // ---------------------------------------------------- server ----------------------------------------------------

    // Build + register the server-side CropData for every pack crop. Called from a SharedSetup postfix —
    // the vanilla crops were just added, so AddCrops appends ours into the same (possibly rebuilt) map.
    public static void RegisterServer()
    {
        var crops = new List<CropData>();
        foreach ((CropDef def, _) in ContentLoader.CropDefs)
        {
            try
            {
                if (!IsValid(def, out string why)) { Plugin.Log.LogWarning($"crop skipped: {why}"); continue; }
                if (CropDataBase.CropDataMap.ContainsKey(def.Id)) continue;   // already registered (re-run)
                crops.Add(BuildCrop(def));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"crop '{def?.Id}': server build failed: {e.Message}"); }
        }
        if (crops.Count > 0)
        {
            CropDataBase.AddCrops(crops);
            Plugin.Log.LogInfo($"Content Forge: {crops.Count} custom crop(s) registered (server).");
        }
    }

    private static CropData BuildCrop(CropDef def)
    {
        // CropType drives which farming mechanics apply (Plant/Mushroom/Tree/FruitTree/Flower).
        if (!Enum.TryParse(string.IsNullOrWhiteSpace(def.CropType) ? "Plant" : def.CropType,
                ignoreCase: true, out CropType cropType))
        {
            Plugin.Log.LogWarning($"crop '{def.Id}': unknown cropType '{def.CropType}' — using Plant.");
            cropType = CropType.Plant;
        }

        var crop = new CropData
        {
            Id = def.Id,
            DisplayId = "crop_display:" + def.Id,                 // ties to the client display we register below
            DryGrowthTime = TimeSpan.FromMinutes(def.DryGrowthMinutes <= 0f ? 40f : def.DryGrowthMinutes),
            WetGrowthTime = TimeSpan.FromMinutes(def.WetGrowthMinutes <= 0f ? 20f : def.WetGrowthMinutes),
            StageCount = def.StageCount,
            CanPlantOnAnyKindOfDirt = def.AnyDirt,
            // null = plantable in any biome (the planting check only filters when the list is non-null)
            AllowedBiomeCategories = (def.Biomes != null && def.Biomes.Count > 0) ? def.Biomes : null,
            AllowRepeatableHarvest = def.Repeatable,
            CropType = cropType,                                  // required member — must be set here
            DropTable = BuildDropTable(def.Drops),
            WildDropTable = BuildDropTable(def.WildDrops),
        };
        if (crop.DropTable == null)
            Plugin.Log.LogWarning($"crop '{def.Id}': no drops defined — harvesting will yield nothing.");
        return crop;
    }

    // Plain (unslotted) drop table, exactly like the vanilla crop tables: each entry rolls independently
    // at SpawnChance% for Min..Max of the item.
    private static DropTableSystem BuildDropTable(List<CropDropDef> drops)
    {
        if (drops == null || drops.Count == 0) return null;
        var spawns = new List<DropTableItemSpawnModel>();
        foreach (CropDropDef d in drops)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.Item)) continue;
            int min = d.Min < 1 ? 1 : d.Min;
            spawns.Add(new DropTableItemSpawnModel
            {
                ItemId = d.Item,
                MinAmount = min,
                MaxAmount = d.Max < min ? min : d.Max,
                SpawnChance = d.Chance <= 0f ? 100f : d.Chance,
            });
        }
        if (spawns.Count == 0) return null;
        return DropTableSystem.FromDropTableModel(new DropTableModel { DropTableItemSpawns = spawns });
    }

    // ---------------------------------------------------- client ----------------------------------------------------

    // Build + register the client-side CropDisplayData. Called from a SetupClientData postfix — the display
    // DB was just (re)built, so we append ours each time (ClearAllClientDataBases wipes it on reload).
    public static void RegisterClient()
    {
        var built = new List<CropDisplayData>();
        foreach ((CropDef def, string packDir) in ContentLoader.CropDefs)
        {
            try
            {
                if (!IsValid(def, out string why)) { Plugin.Log.LogWarning($"crop display skipped: {why}"); continue; }
                string displayId = "crop_display:" + def.Id;
                if (CropDisplayDataBase.DisplayDataMap.ContainsKey(displayId)) continue;   // already there (re-run)

                SpriteSheet sheet = ResolveSheet(def.Display, packDir, def.Id);
                if (sheet == null) continue;                       // warned inside ResolveSheet

                var stages = new CropDisplayStage[def.StageCount];
                for (int i = 0; i < def.StageCount; i++)
                {
                    CropStageDef s = def.Display.Stages[i];
                    int[] frames = (s?.Frames != null && s.Frames.Length > 0) ? s.Frames : new int[1];
                    // FrameAnimation(float animationDuration, int[] frames, bool loop = true) — first arg is the
                    // TOTAL duration in seconds, split evenly across the frames (vanilla wheat: 5f over 4 frames).
                    // duration <= 0 = static frame (vanilla uses 1f with a single frame for those).
                    float duration = (s != null && s.Duration > 0f) ? s.Duration : 1f;
                    stages[i] = new CropDisplayStage
                    {
                        Sprite = sheet,
                        Animation = new FrameAnimation(duration, frames),
                        Horizontal = s != null && s.Horizontal,    // draw flat on the ground (vanilla stage 0 = seeds)
                        Offset3D = Vec3(s?.Offset3D),
                    };
                }

                built.Add(new CropDisplayData
                {
                    Id = displayId,
                    Stages = stages,
                    Offset3D = Vec3(def.Display.Offset3D),
                    DisableRandomSpriteFlip = def.Display.DisableRandomFlip,
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning($"crop '{def?.Id}': display build failed: {e.Message}"); }
        }
        if (built.Count > 0)
        {
            CropDisplayDataBase.Add(built);
            Plugin.Log.LogInfo($"Content Forge: {built.Count} custom crop display(s) registered (client).");
        }
    }

    // A crop's sprite sheet: either a vanilla content path (display.sheet, e.g. "media/sprites/flora/wheat_animated")
    // or a PNG shipped in the pack folder (display.file). The PNG path mirrors the custom-icon pattern.
    private static SpriteSheet ResolveSheet(CropDisplayDef d, string packDir, string cropId)
    {
        int w = d.FrameWidth <= 0 ? 16 : d.FrameWidth;
        int h = d.FrameHeight <= 0 ? 32 : d.FrameHeight;

        if (!string.IsNullOrWhiteSpace(d.Sheet))
            return Content.SpriteSheet(d.Sheet, w, h);            // game caches the texture by path

        string path = Path.Combine(packDir, d.File);
        string key = path + "|" + w + "x" + h;
        if (_customSheets.TryGetValue(key, out SpriteSheet cached)) return cached;

        if (Globals.GraphicsDevice == null)
        { Plugin.Log.LogWarning($"crop '{cropId}': graphics device not ready for '{d.File}'."); return null; }
        if (!File.Exists(path))
        { Plugin.Log.LogWarning($"crop '{cropId}': sheet file not found: {path}"); return null; }

        using var fs = File.OpenRead(path);
        var tex = Texture2D.FromStream(Globals.GraphicsDevice, fs);
        var sheet = new SpriteSheet(tex, Content.DefaultNormalMap, Content.DefaultMetalMap, w, h);
        _customSheets[key] = sheet;
        return sheet;
    }

    // ---------------------------------------------------- seed item ----------------------------------------------------

    // The seed ItemData that plants this crop — an exact mirror of the vanilla seeds (SetupSeedItems):
    // Flags=Seed, infinite uses (UsesMax=0), no cooldown, the shared "spell:place-crop" spell with our crop id.
    // Injected through the existing item pipeline (which also registers the "<id>*item:name"/"*item:description"
    // strings; ItemDataBase.AddItems rewrites Name/Description to those keys). Returns null if no seed is defined.
    public static ItemData BuildSeedItem(CropDef def)
    {
        SeedDef seed = def?.Seed;
        if (seed == null || string.IsNullOrWhiteSpace(seed.Id))
        {
            Plugin.Log.LogWarning($"crop '{def?.Id}': no seed item defined — the crop can't be planted by players.");
            return null;
        }
        return new ItemData
        {
            Id = seed.Id,
            Name = string.IsNullOrWhiteSpace(seed.Name) ? seed.Id : seed.Name,
            Description = string.IsNullOrWhiteSpace(seed.Description) ? (Shared.Text.StringId?)null : seed.Description,
            Icon = string.IsNullOrWhiteSpace(seed.Icon) ? "sam" : seed.Icon,
            MaxStackSize = seed.MaxStack < 1 ? 100 : seed.MaxStack,
            Tier = seed.Tier < 1 ? 1 : seed.Tier,
            Flags = ItemFlag.Seed,
            Tradable = new TradableItem { Price = (ulong)(seed.Price < 0 ? 0 : seed.Price) },
            Usable = new UsableItem
            {
                UsesMax = 0,        // 0 = infinite uses (the seed STACK is consumed per planting, not "uses")
                Cooldown = 0f,
                SpellId = "spell:place-crop",
                SpellTypeArgs = new PlaceCropSpellArgs { CropId = def.Id },
            },
        };
    }

    // ---------------------------------------------------- shared ----------------------------------------------------

    // One validation for BOTH sides, so a bad def is skipped consistently (a server crop whose display is
    // missing — or whose stage count doesn't match its display stages — breaks the client renderer when the
    // crop is planted: the stage index derived from growth progress indexes straight into Stages[]).
    private static bool IsValid(CropDef def, out string why)
    {
        why = null;
        if (def == null || string.IsNullOrWhiteSpace(def.Id)) { why = "crop needs an id."; return false; }
        if (def.StageCount < 1) { why = $"'{def.Id}': stageCount must be >= 1."; return false; }
        if (def.Display == null) { why = $"'{def.Id}': needs a display block."; return false; }
        if (string.IsNullOrWhiteSpace(def.Display.Sheet) && string.IsNullOrWhiteSpace(def.Display.File))
        { why = $"'{def.Id}': display needs a sheet (vanilla path) or file (pack PNG)."; return false; }
        int stages = def.Display.Stages?.Count ?? 0;
        if (stages != def.StageCount)
        { why = $"'{def.Id}': display has {stages} stage(s) but stageCount is {def.StageCount} — they must match."; return false; }
        return true;
    }

    private static Vector3 Vec3(float[] a)
        => (a != null && a.Length >= 3) ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;
}

// Server crops register right after the vanilla ones: SharedSetup() runs SetupCrops + CropDataBase.AddCrops
// inside it, so a postfix lands after the crop DB is (re)filled on every world load/reload.
internal static class CropServerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SharedDataSetup), nameof(SharedDataSetup.SharedSetup))]
    private static void Postfix()
    {
        try { ContentLoader.EnsureParsed(Plugin.PacksDir); CropsFeature.RegisterServer(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Crop server registration failed: {e.Message}"); }
    }
}

// Client crop displays register right after the vanilla ones: SetupClientData() runs SetupCropDisplays +
// CropDisplayDataBase.Add inside it (and ClearAllClientDataBases wipes the map on reload, so we re-add).
internal static class CropClientPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ClientDataSetup), nameof(ClientDataSetup.SetupClientData))]
    private static void Postfix()
    {
        try { ContentLoader.EnsureParsed(Plugin.PacksDir); CropsFeature.RegisterClient(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Crop display registration failed: {e.Message}"); }
    }
}
