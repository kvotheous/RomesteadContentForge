using RomesteadContentForge;          // the shared pack DTOs (linked ContentPack.cs)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

// Content Forge pack validator. Checks structure, enum values, intra-pack references, and string keys —
// everything that doesn't need the game. Run on a pack file or a folder of packs.
//   cfvalidate <pack.json | folder>
// Exit code 1 if any errors. Warnings don't fail the build but are printed.

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Valid enum values (mirror the game). Unknown stat keys only warn (the list may be incomplete).
    private static readonly HashSet<string> EquipTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Helmet", "Armor", "Boots", "Trinket", "Weapon", "Offhand", "LightSource", "LumberAxe", "Pickaxe", "FishingRod", "Ammunition", "Back" };
    private static readonly HashSet<string> Materials = new(StringComparer.OrdinalIgnoreCase)
        { "Flint", "Copper", "Bronze", "Iron", "Silver", "Gold", "Steel", "Cheat", "Rusted" };
    private static readonly HashSet<string> WeaponClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Sword", "Spear", "Dagger", "Sledgehammer", "Bow", "Crossbow", "Javelin", "Fists", "Shield", "SpellTome" };
    private static readonly HashSet<string> UsableTypes = new(StringComparer.OrdinalIgnoreCase) { "Special", "Healing" };
    private static readonly HashSet<string> AttackTypes = new(StringComparer.OrdinalIgnoreCase) { "Magic", "Melee", "Ranged" };
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
        { "NoFlags", "Food", "Material", "Ammunition", "Money", "Millable", "Quest", "Seed", "Offering" };
    private static readonly HashSet<string> DamageKeys = new(StringComparer.OrdinalIgnoreCase)
        { "true", "slashing", "slash", "piercing", "pierce", "bludgeoning", "bludgeon", "pyro", "fire", "chloro", "nature", "aqua", "water", "cosmo", "necro" };
    private static readonly HashSet<string> Stations = new(StringComparer.OrdinalIgnoreCase)
        { "campfire", "cauldron", "clay-barrel", "war-table" };
    private static readonly HashSet<string> StatKeys = new(StringComparer.OrdinalIgnoreCase)
        { "Health", "Energy", "EnergyRegeneration", "Armor", "CritChance", "AttackSpeed", "MovementSpeed",
          "MeleeDamage", "RangedDamage", "MagicDamage", "ThrowingDamage", "PickaxePower", "AxePower", "LumberPower",
          "HealingDoneModifier", "Knockback", "KnockbackResistance", "MagicDamageModifier", "RangedDamageModifier",
          "ThrowingDamageModifier", "BludgeoningDamageModifier", "PiercingDamageModifier", "NecroDamageModifier",
          "ChloroDamageModifier", "PyroDamageModifier", "CosmoDamageModifier", "PyroResistance", "AquaResistance",
          "ChloroResistance", "CosmoResistance", "NecroResistance", "PiercingResistance", "BludgeoningResistance",
          "MagicResistance", "LightSource" };

    private static int _errors, _warnings;

    private static int Main(string[] args)
    {
        string target = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var files = new List<string>();
        if (File.Exists(target)) files.Add(target);
        else if (Directory.Exists(target)) files.AddRange(Directory.GetFiles(target, "*.json"));
        else { Console.Error.WriteLine($"Not found: {target}"); return 2; }

        if (files.Count == 0) { Console.WriteLine("No .json packs found."); return 0; }

        foreach (string f in files) ValidateFile(f);

        Console.WriteLine();
        Console.WriteLine($"Done: {files.Count} pack(s), {_errors} error(s), {_warnings} warning(s).");
        return _errors > 0 ? 1 : 0;
    }

    private static void ValidateFile(string file)
    {
        string name = Path.GetFileName(file);
        Console.WriteLine($"== {name} ==");
        ContentPack pack;
        try { pack = JsonSerializer.Deserialize<ContentPack>(File.ReadAllText(file), Json); }
        catch (Exception e) { Err(name, $"JSON parse error: {e.Message}"); return; }
        if (pack == null) { Err(name, "empty pack."); return; }

        string dir = Path.GetDirectoryName(Path.GetFullPath(file));
        var strings = pack.Strings ?? new Dictionary<string, string>();
        var iconIds = new HashSet<string>(pack.Icons?.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        var spellIds = new HashSet<string>((pack.Spells ?? new()).Where(s => s?.Id != null).Select(s => s.Id), StringComparer.Ordinal);
        var itemIds = new HashSet<string>((pack.Items ?? new()).Where(i => i?.Id != null).Select(i => i.Id), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // stations declared by this pack's own placeables (comma-separated) — same rule as editor/validate.py
        var packStations = new HashSet<string>(
            new[] { pack.Placeables, pack.Furniture, pack.Structures }
                .SelectMany(l => l ?? new())
                .Where(p => !string.IsNullOrEmpty(p?.Station))
                .SelectMany(p => p.Station.Split(',').Select(s => s.Trim())),
            StringComparer.Ordinal);

        // Custom icon files exist?
        if (pack.Icons != null)
            foreach (var kv in pack.Icons)
                if (!File.Exists(Path.Combine(dir, kv.Value)))
                    Warn(name, $"icon '{kv.Key}': file '{kv.Value}' not found next to the pack.");

        // (Item names/descriptions and spell displayNames are LITERAL display text in this format — registered
        // automatically under the game's "<id>*item:name" keys — so they are never validated as string keys.
        // They may freely contain ':' etc.)

        foreach (SpellDef s in pack.Spells ?? new())
        {
            if (string.IsNullOrWhiteSpace(s?.Id)) { Err(name, "spell with empty id."); continue; }
            if (!seen.Add("spell:" + s.Id)) Err(name, $"duplicate spell id '{s.Id}'.");
            if (s.Attack != null)
            {
                CheckDamage(name, s.Attack.MinDamage, $"spell '{s.Id}' minDamage");
                CheckDamage(name, s.Attack.MaxDamage, $"spell '{s.Id}' maxDamage");
                if (s.Attack.AttackType != null && !AttackTypes.Contains(s.Attack.AttackType))
                    Warn(name, $"spell '{s.Id}': unknown attackType '{s.Attack.AttackType}'.");
            }
        }

        foreach (ItemDef it in pack.Items ?? new())
        {
            if (string.IsNullOrWhiteSpace(it?.Id)) { Err(name, "item with empty id."); continue; }
            if (!seen.Add("item:" + it.Id)) Err(name, $"duplicate item id '{it.Id}'.");
            if (!it.Id.Contains(':')) Warn(name, $"item '{it.Id}': id has no namespace (use 'mymod:{it.Id}').");
            if (!string.IsNullOrEmpty(it.Flags) && !Flags.Contains(it.Flags.Replace(" ", "").Split(',')[0]))
                Warn(name, $"item '{it.Id}': unknown flag '{it.Flags}'.");

            var eq = it.Equippable;
            if (eq != null)
            {
                if (string.IsNullOrEmpty(eq.EquipmentType) || !EquipTypes.Contains(eq.EquipmentType))
                    Err(name, $"item '{it.Id}': invalid equipmentType '{eq.EquipmentType}'.");
                if (!string.IsNullOrEmpty(eq.Material) && !Materials.Contains(eq.Material))
                    Warn(name, $"item '{it.Id}': unknown material '{eq.Material}'.");
                if (eq.StatBonuses != null)
                    foreach (var key in eq.StatBonuses.Keys)
                        if (!StatKeys.Contains(key)) Warn(name, $"item '{it.Id}': unknown stat key '{key}'.");
                if (eq.Weapon != null && !WeaponClasses.Contains(eq.Weapon.Class ?? ""))
                    Warn(name, $"item '{it.Id}': unknown weapon class '{eq.Weapon.Class}'.");
                if (eq.Weapon != null) { CheckDamage(name, eq.Weapon.MinDamage, $"item '{it.Id}' weapon minDamage"); CheckDamage(name, eq.Weapon.MaxDamage, $"item '{it.Id}' weapon maxDamage"); }
                if (eq.SpellTome != null && !string.IsNullOrEmpty(eq.SpellTome.SpellId) && !spellIds.Contains(eq.SpellTome.SpellId))
                    Warn(name, $"item '{it.Id}': spellTome casts '{eq.SpellTome.SpellId}' which isn't a spell in this pack (ok if it's a vanilla/other-pack spell).");
            }

            var us = it.Usable;
            if (us != null)
            {
                if (us.Type != null && !UsableTypes.Contains(us.Type))
                    Err(name, $"item '{it.Id}': invalid usable type '{us.Type}'.");
                if (string.Equals(us.Type, "Special", StringComparison.OrdinalIgnoreCase))
                    Warn(name, $"item '{it.Id}': a 'Special' usable has no built-in use trigger — for a castable scroll use an Offhand spellTome instead.");
            }
        }

        foreach (RecipeDef r in pack.Recipes ?? new())
        {
            if (string.IsNullOrWhiteSpace(r?.Id)) { Err(name, "recipe with empty id."); continue; }
            if (!seen.Add("recipe:" + r.Id)) Err(name, $"duplicate recipe id '{r.Id}'.");
            if (string.IsNullOrWhiteSpace(r.Crafted)) Err(name, $"recipe '{r.Id}': missing 'crafted' item id.");
            else if (!itemIds.Contains(r.Crafted)) Warn(name, $"recipe '{r.Id}': crafts '{r.Crafted}' which isn't an item in this pack (ok if vanilla/other-pack).");
            if (!string.IsNullOrEmpty(r.Station) && !Stations.Contains(r.Station) && !packStations.Contains(r.Station))
                Warn(name, $"recipe '{r.Id}': unknown station '{r.Station}' (not vanilla and no placeable declares it — never craftable).");
            if (r.Ingredients != null)
                foreach (var kv in r.Ingredients)
                {
                    if (kv.Value <= 0) Warn(name, $"recipe '{r.Id}': ingredient '{kv.Key}' has count {kv.Value}.");
                    if (kv.Key != null && kv.Key.StartsWith("resource:", StringComparison.Ordinal))
                        Warn(name, $"recipe '{r.Id}': ingredient '{kv.Key}' is a construction resource, not an item — crafting can't use it (use an item id such as 'material:...').");
                }
        }

        foreach (NpcDef n in pack.Npcs ?? new())
        {
            if (n?.Sheets != null)
                foreach (var kv in n.Sheets)
                {
                    if (!kv.Key.Equals("no_weapons", StringComparison.OrdinalIgnoreCase))
                        Warn(name, $"npc '{n.Id}': sheets key '{kv.Key}' — the satyr body's only sheet is 'no_weapons' (48x48).");
                    if (!File.Exists(Path.Combine(dir, kv.Value)))
                        Warn(name, $"npc '{n.Id}': sheet '{kv.Value}' not found next to the pack.");
                }
            if (n?.Tint != null && (n.Tint.Length < 3 || n.Tint.Any(v => v < 0 || v > 255)))
                Warn(name, $"npc '{n.Id}': tint must be [r,g,b] 0-255.");
        }
    }

    private static void CheckDamage(string pack, Dictionary<string, float> dmg, string where)
    {
        if (dmg == null) return;
        foreach (var key in dmg.Keys)
            if (!DamageKeys.Contains(key)) Warn(pack, $"{where}: unknown damage type '{key}'.");
    }

    private static void Err(string pack, string msg) { _errors++; Console.WriteLine($"  ERROR: {msg}"); }
    private static void Warn(string pack, string msg) { _warnings++; Console.WriteLine($"  warn:  {msg}"); }
}
