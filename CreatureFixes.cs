using CandideServer;                    // ServerGameState
using CandideServer.Entities;           // ServerEntityModel
using CandideServer.Models.Spells;      // ServerSummonCreatureSpell, ServerSpellCastingContext
using CandideServer.ServerControllers;  // SpawnEntityArgs
using CandideServer.ServerManagers;     // EntityServerManager, PlayerServerManager, ServerEntityDataManager
using CandideServer.ServerServices;     // ServerJoinedPlayerModel
using HarmonyLib;
using Shared;                           // EntityIds
using Shared.Data;                      // Parameters.GetString
using Shared.Entity;                    // EntitySystem, EntityWrapper, EntityIds
using Shared.Entity.Components;         // Component
using Shared.Helpers;                   // HasFlags
using Shared.Models.DropTables;         // DropTableSystem, ItemDrop, DropTableModel, DropTableItemSpawnModel
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Creature gap-closers — three independent fixes:
//
// 1) SUMMON KILL ATTRIBUTION (RESEARCH.md §9): a player's summon landing the killing blow read "not a
//    player" in PvpExactKillPatch (the summon entity is the DamageResult source; vanilla carries no owner).
//    ServerSummonCreatureSpell.DoCast has the caster in hand (context.CastingEntity) and synchronously calls
//    EntityServerManager.SpawnEntity — we stash the caster across that call and stamp a "cf-owner" parameter
//    (caster entity Guid) into the spawn args, exactly like the cf-summon marker the mod already stamps at
//    spell-build time. SpawnEntityArgs.Parameters round-trips into Controller.Parameters and the save (the
//    same mechanism SummonRestorePatch relies on), so TryResolveSummonOwner can resolve the owner at kill
//    time — Pvp.cs's exact-kill prefix calls it when the direct player resolve fails.
//    (EventsPlus hunt-contest kill credit rides the vanilla quest funnel and is deliberately untouched.)
//
// 2) WILDLIFE DROPS (known limitation): wildlife loot is NOT a shared drop table — on death,
//    LootServerManager.DropGoodsAndInventory reads the entity's DropTableComponent (a DropTableSystem baked
//    into the entity prototype from its .cdb), so ServerDropTablesDataBase never sees it. drops[].creature
//    appends an ItemDrop to that creature PROTOTYPE's drop component (prototypes live at eid 0 in
//    ServerEntityDataManager.EntitySystems) — every later spawn carries it. Registered at the same
//    LoadDoodadDatabase chokepoint as RegisterCreatures, AFTER it (Priority.Low + later PatchAll), so pack
//    creatures resolve too. Server-side only: loot rolls are server-authoritative.
//
// 3) COMPANION FACING (known limitation): after CompanionPatch blinks a companion to the player it kept its
//    stale direction until its AI next moved it. Forcing Direction every tick fought the AI (the old anim
//    thrash); setting it ONCE at the teleport is safe — the server's per-tick EntityInfoMessage2 sync
//    carries Direction to clients, and the teleport only happens when no enemy is near (idle AI, so nothing
//    immediately overwrites it).
// ---------------------------------------------------------------------------------------------------------
internal static class CreatureFixes
{
    // ---- FIX 1: summon owner resolution (consumed by PvpExactKillPatch in Pvp.cs) ----

    // Resolve the player who summoned `src` via its cf-owner parameter. False when src isn't a stamped
    // summon, the owner entity is gone (offline), or the owner isn't a player.
    internal static bool TryResolveSummonOwner(EntityWrapper src, out ServerJoinedPlayerModel owner)
    {
        owner = null;
        try
        {
            string s = src?.Controller?.Parameters?.GetString("cf-owner", null);
            if (string.IsNullOrEmpty(s) || !Guid.TryParse(s, out Guid ownerId)) return false;
            if (!ServerGameState.Entities.TryGetValue(ownerId, out ServerEntityModel sem) || sem?.EntityWrapper == null)
                return false;
            return PlayerServerManager.TryGetPlayerFromEntity(sem.EntityWrapper, out owner) && owner != null;
        }
        catch { return false; }   // kill-attribution path — never throw into a death resolution
    }

    // ---- FIX 3: face the player once at the companion teleport (consumed by CompanionPatch in Plugin.cs) ----

    internal static void FaceToward(EntityWrapper summon, EntityWrapper target)
    {
        try { if (summon != null && target != null) summon.TurnTo(target); }   // sets Direction; replicated per tick
        catch { }
    }

    // ---- FIX 2: drops[].creature — append loot to a creature prototype's baked-in drop component ----

    // Parsed once from packs/*.json: drops entries with a `creature` (and no `table` — table is the existing
    // ServerDropTablesDataBase path in ContentLoader.ApplyDrops; creature is the alternative for wildlife).
    private static bool _dropsParsed;
    private static readonly List<(string creature, string item, float chance, int min, int max, string aura)> _creatureDrops = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Minimal re-read of the packs (ContentLoader keeps its parsed drops private, and DropDef has no
    // Creature field — System.Text.Json ignores the extra key there, so the two parsers never collide:
    // ContentLoader skips entries without `table`, we skip entries without `creature`).
    private sealed class PackDropsOnly { public List<DropEntry> Drops; }
    private sealed class DropEntry
    {
        public string Creature; public string Table; public string Item;
        public float Chance = 5f; public int Min = 1; public int Max = 1; public string Aura;
    }

    private static void EnsureDropsParsed()
    {
        if (_dropsParsed) return;
        _dropsParsed = true;
        ContentLoader.EnsureParsed(Plugin.PacksDir);   // populates LoadedPacks (dependency-filtered)
        var loadedFiles = new HashSet<string>(ContentLoader.LoadedPacks.Select(p => p.File), StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(Plugin.PacksDir, "*.json"))
        {
            if (!loadedFiles.Contains(Path.GetFileName(file))) continue;   // pack was dropped (deps/parse error)
            try
            {
                var pack = JsonSerializer.Deserialize<PackDropsOnly>(File.ReadAllText(file), JsonOpts);
                if (pack?.Drops == null) continue;
                foreach (DropEntry d in pack.Drops)
                {
                    if (d == null || string.IsNullOrWhiteSpace(d.Creature) || string.IsNullOrWhiteSpace(d.Item)) continue;
                    if (!string.IsNullOrWhiteSpace(d.Table)) continue;   // table path — ContentLoader.ApplyDrops owns it
                    int min = d.Min < 1 ? 1 : d.Min;
                    _creatureDrops.Add((d.Creature, d.Item, d.Chance <= 0f ? 5f : d.Chance, min, d.Max < min ? min : d.Max, d.Aura));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Creature drops: pack '{Path.GetFileName(file)}' re-read failed: {e.Message}"); }
        }
    }

    // Same resolve order as ContentLoader.TryResolveCreature (private there): pack creature id first, then a
    // vanilla Shared.EntityIds field name (e.g. "Bear").
    private static bool TryResolveCreature(string name, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (ContentLoader.CustomCreatureGuids.TryGetValue(name, out id)) return true;
        var f = AccessTools.Field(typeof(EntityIds), name);
        if (f == null || f.FieldType != typeof(Guid)) return false;
        id = (Guid)f.GetValue(null);
        return true;
    }

    // Append each creature drop to its prototype's DropTableComponent. Idempotent the same way as
    // ContentLoader.ApplyDrops (skips when the item is already in the component); _itemDrops is readonly,
    // so the array is replaced via reflection — the exact ApplyDrops pattern.
    internal static void ApplyCreatureDrops()
    {
        EnsureDropsParsed();
        if (_creatureDrops.Count == 0) return;
        var protos = ServerEntityDataManager.EntitySystems;
        var itemDropsField = AccessTools.Field(typeof(DropTableSystem), "_itemDrops");
        int added = 0, missing = 0;
        foreach (var d in _creatureDrops)
        {
            if (!TryResolveCreature(d.creature, out Guid guid) || !protos.TryGetValue(guid, out EntitySystem proto))
            { missing++; Plugin.Log.LogWarning($"Drop: creature '{d.creature}' not found; '{d.item}' not added."); continue; }

            if (proto.Mask[0].HasFlags(Component.DropTable) && proto.DropTableComponent[0].DropTable != null)
            {
                DropTableSystem sys = proto.DropTableComponent[0].DropTable;
                var arr = sys._itemDrops ?? Array.Empty<ItemDrop>();
                if (arr.Any(x => x.ItemId == d.item)) continue;   // already injected (DB reloads re-run this)
                var list = new List<ItemDrop>(arr)
                { new ItemDrop(d.chance, d.max, d.min, d.item, null, null, string.IsNullOrEmpty(d.aura) ? null : d.aura) };
                itemDropsField.SetValue(sys, list.ToArray());
            }
            else
            {
                // No baked-in loot at all (rare) — give the prototype a fresh single-entry component.
                var model = new DropTableModel();
                model.DropTableItemSpawns.Add(new DropTableItemSpawnModel
                {
                    SpawnChance = d.chance, MaxAmount = d.max, MinAmount = d.min,
                    ItemId = d.item, AuraId = string.IsNullOrEmpty(d.aura) ? null : d.aura,
                });
                proto.DropTableComponent[0].DropTable = DropTableSystem.FromDropTableModel(model);
                proto.Mask[0] |= Component.DropTable;
            }
            added++;
        }
        if (added > 0 || missing > 0)
            Plugin.Log.LogInfo($"Content Forge: applied {added} creature drop(s)" + (missing > 0 ? $", {missing} missing creature(s)" : "") + ".");
    }
}

// (server) FIX 1 stamp: while ServerSummonCreatureSpell.DoCast runs for a PLAYER caster, every entity it
// spawns gets "cf-owner" = the caster's entity Guid. DoCast builds a fresh per-cast Parameters dictionary
// and calls SpawnEntity synchronously, so a thread-static handoff is exact (no shared spell-args mutation);
// SummonFactionPatch's extra count>1 spawns reuse the same dictionary, so they inherit the stamp too.
internal static class SummonOwnerStampPatch
{
    [System.ThreadStatic] private static string _pendingOwner;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ServerSummonCreatureSpell), "DoCast")]
    private static void DoCastPrefix(ServerSpellCastingContext context)
    {
        _pendingOwner = null;
        try
        {
            ServerEntityModel ce = context.CastingEntity;
            if (ce?.EntityWrapper != null && PlayerServerManager.TryGetPlayerFromEntity(ce.EntityWrapper, out _))
                _pendingOwner = ce.Id.ToString();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Summon owner stamp failed: {e.Message}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerSummonCreatureSpell), "DoCast")]
    private static void DoCastPostfix() => _pendingOwner = null;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityServerManager), nameof(EntityServerManager.SpawnEntity))]
    private static void SpawnPrefix(SpawnEntityArgs args)
    {
        if (_pendingOwner != null && args?.Parameters != null) args.Parameters["cf-owner"] = _pendingOwner;
    }
}

// (server) FIX 2 registration: same chokepoint as CreatureServerPatch (the prototypes exist here), forced to
// run AFTER it (Priority.Low; it's also PatchAll'd later) so pack creature ids resolve.
internal static class CreatureDropsPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Low)]
    [HarmonyPatch(typeof(ServerEntityDataManager), nameof(ServerEntityDataManager.LoadDoodadDatabase))]
    private static void Postfix()
    {
        try { CreatureFixes.ApplyCreatureDrops(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Creature drop inject failed: {e.Message}"); }
    }
}
