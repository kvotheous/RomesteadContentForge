#if !CF_SERVER
using Candide.Save;                     // CharacterSaveManager (client — characters save on their own client)
#endif
using CandideServer;                    // ServerGameState
using CandideServer.Entities;           // ServerEntityModel
using CandideServer.Models.Player;      // ServerPlayerCharacter
using CandideServer.ServerManagers;     // PlayerCharacterServerManager, FavourServerManager
using HarmonyLib;
using Microsoft.Xna.Framework;          // Point
using Shared.Data;                      // FavourDataBase, FavourData, FavourTreeData, SharedDataSetup, AuraDataBase, GodsDataBase
using Shared.Entity;                    // SharedEntityAuraManager
using Shared.Aura.Args;                 // StatsChangeAuraArgs
using Shared.Models.Auras;              // EntityAuraInfo
using Shared.Models.Player;             // PlayerCharacterModel, PlayerCharacterSaveModel, CharacterFlag
using Shared.Models.Stats;              // StatModificationData
using Shared.Text;                      // StringId
using System;
using System.Collections.Generic;
using System.Linq;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Custom favour-tree nodes — a pack adds a node to the gods' favour tree (the FavoursWindow grid), attached
// under any vanilla (or pack) node, carrying a stat aura / an existing aura / a recipe unlock / a flag.
//
// REGISTRATION (Register, postfix of SharedDataSetup.Setup — both DBs are rebuilt there each world load):
//   1. FavourDataBase.AddFavours (it rewrites Name/Description to the "<id>*favour:name|description" string
//      keys — the strings are registered by BuildPack through the normal _strings/_locStrings pipeline);
//   2. a FavourTreeData in SharedDataSetup.FavourGrid at the requested cell (the tree UI lays nodes out by
//      Position; vanilla occupies columns 0–14 of rows 0–9 — row 10+ is always free);
//   3. splice into the parent's NextUnlockIds (the client's reachability search — FavourManager.
//      IsFavourAvailable — walks FavourGridFirstRow + NextUnlockIds) AND NextUnlockPositions (the UI draws
//      the connecting lines from positions). Vanilla resolves NextUnlockIds FROM positions in
//      SetupFavourGrid, which already ran, so we append to both arrays ourselves, consistently.
//   The OLD per-god favours tab (SkillsAndFavourWindow) lays out by (TierRow, UiColumn) per god and THROWS
//   on two favours sharing a cell — we auto-stack our nodes below the god's existing column-0 rows.
//
// UNLOCKING is untouched vanilla: the server's FavourServerManager.TryUnlockFavour only checks points > 0 +
// not-learned + the id exists in FavourDataBase — our injected nodes pass on a modded host. Auras need no
// extra wiring: GetFavourAndGlobalAuras (both sides) reads Skills.Favours live and instantiates each
// favour's AuraId on every aura recalc.
//
// UNINSTALL SAFETY (strip-on-save): the server hard-rejects login when Skills.Favours contains an id that
// isn't in FavourDataBase (PlayerCharacterServerManager.SanityCheckCharacter) — so a learned modded favour
// would BRICK the character once the pack/mod is removed. Characters save CLIENT-side only, all paths
// through CharacterSaveManager.SaveCharacterData, which serializes synchronously before the async disk IO —
// so a prefix/postfix pair brackets exactly what hits the disk:
//   prefix   : every modded id is removed from Favours, the point refunded (CurrentFavourPoints++), and a
//              "forge:favour:<id>" marker added to Flags (IsStatic = false — the login flow strips
//              IsStatic flags, ours must survive) carrying the rank in Value1;
//   postfix  : exact inverse (re-add, un-refund, remove marker) — the live model never changes.
//   ON DISK  : the save is 100% vanilla-sane — no modded favour, the point refunded, one inert flag.
//   AT LOGIN : FavourRegrantPatch (postfix of SetupCharacter, server-side — same hook as SkillBackfillPatch,
//              runs for every joining player and the result is what the client receives) converts each
//              marker back: favour known → learn it, charge the point, drop the flag (+ ApplyFavourFlag);
//              favour unknown (pack removed) → the flag stays inert and the point stays refunded — a clean
//              automatic respec, and the node re-learns itself if the pack ever returns.
//   Invariant: a favour is EITHER learned in Favours (in memory / on a modded host) OR flagged+refunded (on
//   disk / after pack removal) — never both, so no path double-refunds or double-charges.
//   UnlearnAllFavours (canteen respec) still works: while the pack is present the favour is a normal known
//   entry (aura removed, +1 point); unknown ids never reach the server in the first place.
//
// CO-OP: like everything else, server-authoritative — the HOST must have the pack for the node to be
// unlockable; clients also need it or the tree won't show the node (and its name/icon).
// ---------------------------------------------------------------------------------------------------------

internal static class FavoursFeature
{
    // (def, pack file name) parsed from packs by ContentLoader.BuildPack, in dependency order — so a favour
    // may parent another pack's node as long as that pack is a dependency (registered first).
    internal static readonly List<(FavourDef Def, string Pack)> Defs = new();

    // Favour ids registered this session. Survives world reloads (Setup reruns) — the strip patch keys off it.
    internal static readonly HashSet<string> RegisteredIds = new();

    // Nodes with screen:true — laid out by ForgeFavourScreen on its own grid, never in the vanilla tree.
    // Rebuilt on every Register (the DBs it mirrors are rebuilt each world load too).
    internal static readonly List<FavourDef> ScreenDefs = new();

    internal const string FlagPrefix = "forge:favour:";

    // Is this learned favour one the save must be protected from? Ours for sure; an id unknown to a LOADED
    // favour DB would brick the login sanity check anyway, so flag it too (covers a pack uninstalled while
    // the favour was still learned in an older save). The Count guard keeps a save racing a DB rebuild from
    // flagging vanilla favours.
    internal static bool IsModded(string favourId) =>
        RegisteredIds.Contains(favourId)
        || (FavourDataBase.DataMap.Count > 0 && !FavourDataBase.DataMap.ContainsKey(favourId));

    // Called from SetupPatch's postfix (after vanilla SetupFavours + SetupFavourGrid have rebuilt both maps).
    public static void Register()
    {
        ScreenDefs.Clear();
        foreach ((FavourDef f, string pack) in Defs)
        {
            try { RegisterOne(f, pack); }
            catch (Exception e) { Plugin.Log.LogWarning($"[{pack}] favour '{f?.Id}': registration failed: {e.Message}"); }
        }
    }

    private static void RegisterOne(FavourDef f, string pack)
    {
        var pos = new Point(f.Position[0], f.Position[1]);
        if (pos.X < 0 || pos.Y < 0)
        { Plugin.Log.LogWarning($"[{pack}] favour '{f.Id}': position must be non-negative — skipped."); return; }

        FavourTreeData parent = null;
        if (f.Screen)
        {
            // Custom-screen node: never touches the vanilla grid. Parent (optional) must be an earlier
            // screen node so the connection line has both endpoints on OUR grid.
            if (!string.IsNullOrWhiteSpace(f.Parent) && !ScreenDefs.Any(d => d.Id == f.Parent))
            { Plugin.Log.LogWarning($"[{pack}] favour '{f.Id}': screen parent '{f.Parent}' is not a screen node registered before it — the node becomes a root."); f.Parent = null; }
            foreach (FavourDef d in ScreenDefs)
                if (d.Position[0] == pos.X && d.Position[1] == pos.Y)
                { Plugin.Log.LogWarning($"[{pack}] favour '{f.Id}': screen cell [{pos.X},{pos.Y}] is taken by '{d.Id}' — skipped."); return; }
        }
        else
        {
            // The parent must already be in the grid: a vanilla node, or a pack node registered before this one.
            if (!SharedDataSetup.FavourGrid.TryGetValue(f.Parent, out parent))
            { Plugin.Log.LogWarning($"[{pack}] favour '{f.Id}': parent '{f.Parent}' is not in the favour grid — skipped."); return; }

            // Two nodes on one cell = overlapping icons in the tree (and a dead click target) — refuse early.
            if (!SharedDataSetup.FavourGrid.ContainsKey(f.Id))
                foreach (FavourTreeData td in SharedDataSetup.FavourGrid.Values)
                    if (td.Position == pos)
                    { Plugin.Log.LogWarning($"[{pack}] favour '{f.Id}': grid cell [{pos.X},{pos.Y}] is taken by '{td.FavourId}' (vanilla fills columns 0-14 of rows 0-9; row 10+ is free) — skipped."); return; }
        }

        // The effect: statBonuses builds a permanent stat aura (same shape as vanilla favour auras); aura
        // reuses an existing aura id; recipe/flag ride on FavourData directly.
        string auraId = null;
        if (f.StatBonuses != null && f.StatBonuses.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(f.Aura))
                Plugin.Log.LogWarning($"[{pack}] favour '{f.Id}': both statBonuses and aura set — using statBonuses.");
            auraId = "aura:favour:" + f.Id;
            if (!AuraDataBase.AuraDataMap.ContainsKey(auraId))
            {
                var stats = new Dictionary<string, StatModificationData>();
                foreach (var kv in f.StatBonuses)
                {
                    var b = kv.Value ?? new StatBonusDef();
                    stats[kv.Key] = new StatModificationData
                    {
                        Additive = b.Additive,
                        Multiplier = b.Multiplier,
                        AdditiveMultiplier = b.AdditiveMultiplier,
                        BaseMultiplier = b.BaseMultiplier,
                        BonusMultiplier = b.BonusMultiplier,
                    };
                }
                AuraDataBase.AddData(new List<EntityAuraInfo>
                {
                    new EntityAuraInfo
                    {
                        Id = auraId,
                        GetStateArgs = _ => new StatsChangeAuraArgs { StatsToAdd = stats },
                        InstanceTypeId = f.Id,
                        AuraTypeId = "auratype:stats_change",
                        AuraStatusTypeId = "status:other_aura_type",
                        IsBuff = true,
                    }
                });
            }
        }
        else if (!string.IsNullOrWhiteSpace(f.Aura))
        {
            auraId = f.Aura;
            if (!AuraDataBase.AuraDataMap.ContainsKey(auraId))
                Plugin.Log.LogWarning($"[{pack}] favour '{f.Id}': aura '{auraId}' not found — the node will be inert.");
        }

        string god = string.IsNullOrWhiteSpace(f.God) ? "minerva" : f.God;
        if (!GodsDataBase.DataMap.ContainsKey(god))
        { Plugin.Log.LogWarning($"[{pack}] favour '{f.Id}': unknown god '{god}' — using 'minerva'."); god = "minerva"; }

        if (!FavourDataBase.DataMap.ContainsKey(f.Id))
        {
            var fd = new FavourData
            {
                Id = f.Id,
                Name = (StringId)(string.IsNullOrWhiteSpace(f.Name) ? f.Id : f.Name),   // rewritten to "<id>*favour:name" by AddFavours
                GodId = god,
                Icon = string.IsNullOrWhiteSpace(f.Icon) ? "sam" : f.Icon,
                Description = string.IsNullOrWhiteSpace(f.Description) ? (StringId?)null : f.Description,
                AuraId = auraId,
                PlayerCraftingRecipeId = string.IsNullOrWhiteSpace(f.Recipe) ? null : f.Recipe,
                FlagId = string.IsNullOrWhiteSpace(f.FlagId) ? null : f.FlagId,
                FlagValue1 = f.FlagValue1,
                FlagValue2 = f.FlagValue2,
            };
            // The OLD per-god favours tab (skills window) lays out by (TierRow, UiColumn) and throws a
            // SamuelDevelopmentException on duplicates — stack our nodes below the god's column-0 rows.
            int row = 0;
            foreach (FavourData v in FavourDataBase.DataMap.Values)
                if (v.GodId == god && v.UiColumn == 0) row = Math.Max(row, v.TierRow + 1);
            fd.TierRow = row;
            fd.UiColumn = 0;
            FavourDataBase.AddFavours(new List<FavourData> { fd });
        }

        if (f.Screen)
        {
            ScreenDefs.Add(f);
        }
        else if (!SharedDataSetup.FavourGrid.ContainsKey(f.Id))
        {
            // NextUnlockIds must be a non-null array — the client's availability search iterates it once
            // this node is learned (vanilla only fills it in the position-resolution pass, which has run).
            SharedDataSetup.FavourGrid[f.Id] = new FavourTreeData
            {
                FavourId = f.Id,
                Position = pos,
                NextUnlockPositions = Array.Empty<Point>(),
                NextUnlockIds = Array.Empty<string>(),
            };
            parent.NextUnlockIds = (parent.NextUnlockIds ?? Array.Empty<string>()).Append(f.Id).ToArray();
            parent.NextUnlockPositions = (parent.NextUnlockPositions ?? Array.Empty<Point>()).Append(pos).ToArray();
        }

        RegisteredIds.Add(f.Id);
    }
}

#if !CF_SERVER
// Strip every modded favour out of the character the instant it is saved, and put it back the instant the
// serializer is done — the on-disk save never contains a modded favour id (which would fail the server's
// login sanity check once the pack is gone). SaveCharacterData serializes to a MemoryStream synchronously
// (only the disk write is Task.Run), so prefix→serialize→postfix is airtight; this is also the single choke
// point: autosave, save-and-quit and character creation all funnel through it, and characters only ever
// save on their own client (the server assembly has no character save path).
[HarmonyPatch(typeof(CharacterSaveManager), nameof(CharacterSaveManager.SaveCharacterData))]
internal static class FavourStripOnSavePatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerCharacterSaveModel saveModel, out List<(string Id, int Rank)> __state)
    {
        __state = null;
        try
        {
            PlayerCharacterModel ch = saveModel?.CharacterModel;
            if (ch?.Flags == null || ch.Skills.Favours == null) return;

            List<(string, int)> strip = null;
            foreach (var kv in ch.Skills.Favours)
                if (FavoursFeature.IsModded(kv.Key)) (strip ??= new List<(string, int)>()).Add((kv.Key, kv.Value));
            if (strip == null) return;

            foreach ((string id, int rank) in strip)
            {
                ch.Skills.Favours.Remove(id);
                ch.Skills.CurrentFavourPoints++;                       // the refund the player keeps if the pack vanishes
                ch.Flags[FavoursFeature.FlagPrefix + id] = new CharacterFlag { Value1 = rank };  // IsStatic=false: survives the login flag strip
            }
            __state = strip;
            Plugin.Log.LogDebug($"favour strip-on-save: {strip.Count} modded favour(s) flagged for '{ch.Name}'.");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"favour strip-on-save failed: {e.Message}"); }
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerCharacterSaveModel saveModel, List<(string Id, int Rank)> __state)
    {
        if (__state == null) return;
        try
        {
            PlayerCharacterModel ch = saveModel.CharacterModel;
            foreach ((string id, int rank) in __state)     // exact inverse of the prefix — nothing else is touched
            {
                ch.Flags.Remove(FavoursFeature.FlagPrefix + id);
                ch.Skills.Favours[id] = rank;
                ch.Skills.CurrentFavourPoints = Math.Max(0, ch.Skills.CurrentFavourPoints - 1);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"favour restore-after-save failed: {e.Message}"); }
    }
}
#endif

// Re-grant flagged favours at login, server-side (mirrors SkillBackfillPatch: SetupCharacter runs for every
// joining player — host and co-op clients — BEFORE the login response, so the converted model is what the
// client receives). Flag + favour known → learn it, charge the point back, drop the flag; favour unknown
// (pack removed) → the flag stays inert and the refund stands — login keeps working either way.
internal static class FavourRegrantPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerCharacterServerManager), nameof(PlayerCharacterServerManager.SetupCharacter))]
    private static void Postfix(PlayerCharacterModel playerCharacter, ServerPlayerCharacter __result)
    {
        try
        {
            Dictionary<string, CharacterFlag> flags = playerCharacter?.Flags;
            if (flags == null || flags.Count == 0) return;

            List<string> markers = null;
            foreach (string k in flags.Keys)
                if (k.StartsWith(FavoursFeature.FlagPrefix, StringComparison.Ordinal)) (markers ??= new List<string>()).Add(k);
            if (markers == null) return;

            int granted = 0;
            foreach (string key in markers)
            {
                string id = key.Substring(FavoursFeature.FlagPrefix.Length);
                if (!FavourDataBase.DataMap.TryGetValue(id, out FavourData favour)) continue;   // pack gone — leave it respec'd

                if (!playerCharacter.Skills.Favours.ContainsKey(id))
                {
                    playerCharacter.Skills.Favours[id] = Math.Max(1, (int)flags[key].Value1);
                    playerCharacter.Skills.CurrentFavourPoints = Math.Max(0, playerCharacter.Skills.CurrentFavourPoints - 1);
                }
                flags.Remove(key);
                if (__result != null) FavourServerManager.ApplyFavourFlag(__result, favour);   // favours with a FlagId need their game flag back
                granted++;
            }
            if (granted == 0) return;
            Plugin.Log.LogInfo($"favour regrant: restored {granted} modded favour(s) for '{playerCharacter.Name}'.");

            // Auras normally need no trigger — the player entity spawns AFTER SetupCharacter and its first
            // CalculateState pulls favour auras live from Skills.Favours. Recalc defensively if the entity
            // already exists (a relog reusing a cached server character).
            if (ServerGameState.TryGetServerEntity(playerCharacter.EntityId, out ServerEntityModel ent) && ent?.EntityWrapper != null)
                SharedEntityAuraManager.CalculateState(ent.EntityWrapper);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"favour regrant failed: {e.Message}"); }
    }
}
