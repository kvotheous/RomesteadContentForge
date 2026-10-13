using CandideServer;
using CandideServer.Data.DataModels;
using CandideServer.Database;
using CandideServer.Models.Dungeons;
using CandideServer.Models.Rewards;
using CandideServer.ServerManagers;
using HarmonyLib;
using Shared.Models.Quests;
using Shared.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// QuestRewardsPlus — two additions:
//
// A) QUEST ITEM REWARDS (`"rewardItems": { "mymod:relic": 2 }` on a quest).
//    The engine's own ItemRewardStrategy is a STUB (ApplyReward is empty, tooltip says "Implement this"),
//    but the strategy PIPELINE is fully wired: FinishQuest -> RewardsServerManager.UnlockQuestReward(questId,
//    playerCharacterId, ...) -> QuestData.RewardId -> RewardDataBase -> RewardData.RewardStrategies[i]
//    .ApplyReward(playerCharacterId). So we register a working IRewardStrategy of our own under a reward id
//    derived from the quest id ("cf:reward:<questId>") and point the quest's RewardId at it.
//    - WHO gets the items: our pack quests are WORLD quests, and world-quest completion passes
//      playerCharacterId = null (auto turn-in and the FinishQuestMessage path both do). Mirroring
//      RewardPlayerCraftingRecipeIds — which unlocks for the whole world — a null id grants the items to
//      EVERY logged-in player; a non-null id (personal-quest reuse) grants only that player.
//    - REPLAY GUARD: RewardsSController.OnWorldStatsLoaded RE-RUNS every unlocked reward's strategies on
//      each world load, distinguishable by sendToNetwork == false. Items must be granted once, at unlock
//      time (sendToNetwork == true) — we skip the replay or every load would duplicate the items.
//    - UNINSTALL-SAFE: QuestData/RewardData live in process-static DataBases rebuilt from code each boot;
//      the save stores only the unlocked reward ID string. With the mod removed the load path logs a
//      "Couldn't find Reward data" warning and carries on.
//
// B) REPLACE A VANILLA DUNGEON ROOM (`"replace": "<vanilla room id>"` on a map).
//    Dungeon interiors are TEMPORARY worlds rebuilt on every dungeon load: TryEnterDungeon ->
//    TryLoadInDungeonLayout -> SetUpRoom -> WorldSController.CreateInterior(dungeonRoomData
//    .InteriorWorldMapId, ..., temporary: true). The room id (DataId) is what persists in the save;
//    the world it resolves to is looked up in DungeonRoomDataBase at load time. So overwriting the vanilla
//    DungeonRoomDataModel.InteriorWorldMapId with our generated world ("maps/cf/<id>") swaps the interior
//    everywhere that room id is used — entrances/POIs/loot/display name all stay vanilla.
//    - Saved worlds: dungeon locations NOT yet entered generate fully fresh with the new interior. A
//      location already entered in an old save keeps its persisted layout state (Rooms/TransitionAreas/
//      ChestInventories/KilledEntities recorded against the OLD map) — it gets the new interior art on the
//      next world rebuild, but chest/kill/transition state can mismatch. Document packs as "new worlds /
//      first-visit dungeons" for full consistency.
//    - Exits: the persisting room model keeps the VANILLA ExitDirection, and the engine reserves direction
//      (exitDirection+2)%4 as "leave the dungeon" — our map's exit node must sit on the matching side, so
//      a mismatch between MapDef.ExitDirection and the vanilla room's is warned about here.
//    Call RoomReplaceFeature.ApplyReplacements() right after MapsFeature.RegisterRooms() (MapRoomPatch's
//    postfix on ServerDataSetup.Setup): vanilla rooms are in the DB by then and LoadMapFiles has already
//    loaded our generated worlds (SetupDataFileLoading runs first).
// ---------------------------------------------------------------------------------------------------------

// The working item-reward strategy the vanilla ItemRewardStrategy never got.
internal sealed class ForgeItemRewardStrategy : IRewardStrategy
{
    private readonly string _rewardId;                       // for log lines only
    private readonly List<(string ItemId, int Amount)> _items;

    internal ForgeItemRewardStrategy(string rewardId, Dictionary<string, int> items)
    {
        _rewardId = rewardId;
        _items = new List<(string, int)>();
        foreach (var kv in items)
            if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0) _items.Add((kv.Key, kv.Value));
    }

    public void ApplyReward(Guid? playerCharacterId = null, bool sendToNetwork = true)
    {
        try
        {
            // sendToNetwork == false is the on-load REPLAY (RewardsSController.OnWorldStatsLoaded re-applies
            // every already-unlocked reward each world load). Grant items only on the live unlock.
            if (!sendToNetwork) return;

            // null = world quest -> everyone online; a Guid = that player only (vanilla personal-quest path).
            var inventories = new List<Guid>();
            if (playerCharacterId.HasValue)
            {
                if (ServerRunState.LoggedInPlayerCharacters.TryGetValue(playerCharacterId.Value, out var one)
                    && one?.Character != null)
                    inventories.Add(one.Character.CharacterInventoryModel.InventoryId);
                else
                    Plugin.Log.LogWarning($"Reward '{_rewardId}': player {playerCharacterId} not logged in — items not granted.");
            }
            else
                foreach (var p in ServerRunState.LoggedInPlayerCharacters.Values)
                    if (p?.Character != null) inventories.Add(p.Character.CharacterInventoryModel.InventoryId);

            foreach (Guid inv in inventories)
                foreach ((string itemId, int amount) in _items)
                    if (!SimpleInventoryServerManager.TryAddItemAmounts(inv, itemId, amount, null, allowPartialStack: true, null))
                        Plugin.Log.LogWarning($"Reward '{_rewardId}': couldn't add {amount}x {itemId} (inventory full?).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Reward '{_rewardId}' item grant failed: {e.Message}"); }
    }

    // Shown in the quest/reward tooltip — one "• N x Name" line per item.
    public void GetTextDescription(StringBuilder sb)
    {
        bool first = true;
        foreach ((string itemId, int amount) in _items)
        {
            if (!first) sb.Append('\n');
            first = false;
            sb.Append(" • ").Append(amount).Append(" x ").Append(ItemName(itemId));
        }
    }

    public void GetName(StringBuilder sb)
    {
        bool first = true;
        foreach ((string itemId, int amount) in _items)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(amount).Append(" x ").Append(ItemName(itemId));
        }
    }

    private static string ItemName(string itemId)
    {
        try { return Shared.Data.ItemDataBase.GetItemDataOrMissing(itemId).Name.GetTranslation(); }
        catch { return itemId; }
    }
}

// FEATURE A wiring: quest defs record their item rewards at parse time; the RewardData entries register
// once per process, right after vanilla SetupRewards.
internal static class QuestRewardsPlusFeature
{
    // rewardId -> itemId -> amount, captured while packs parse (TryBuildQuest).
    private static readonly Dictionary<string, Dictionary<string, int>> _pending = new();

    // Called from ContentLoader.TryBuildQuest right after the QuestData is built: points the quest at our
    // reward entry and remembers the items. (QuestData.RewardId is exactly how vanilla quests pay out.)
    internal static void RegisterQuestItems(QuestDef q, QuestData qd)
    {
        if (q?.RewardItems == null || q.RewardItems.Count == 0 || qd == null) return;
        string rewardId = "cf:reward:" + qd.Id;
        qd.RewardId = rewardId;
        _pending[rewardId] = q.RewardItems;
    }

    // RewardDataBase.RewardsDataMap is process-static and filled ONCE (client LoadContent -> the server
    // assembly's SharedDataSetup.SharedSetup) — same lifecycle as vanilla rewards, so register there too.
    internal static void InjectRewards()
    {
        if (_pending.Count == 0) return;
        var list = new List<RewardData>();
        foreach (var kv in _pending)
        {
            if (RewardDataBase.RewardsDataMap.ContainsKey(kv.Key)) continue;   // already registered (idempotent)
            foreach (string itemId in kv.Value.Keys)                            // items injected earlier this Setup
                if (!Shared.Data.ItemDataBase.DataMap.ContainsKey(itemId))
                    Plugin.Log.LogWarning($"Quest reward '{kv.Key}': unknown item id '{itemId}'.");
            list.Add(new RewardData
            {
                Id = kv.Key,
                Icon = "scroll:brown",
                RewardStrategies = new IRewardStrategy[] { new ForgeItemRewardStrategy(kv.Key, kv.Value) },
            });
        }
        if (list.Count > 0)
        {
            RewardDataBase.AddRewards(list);
            Plugin.Log.LogInfo($"Content Forge: {list.Count} quest item reward(s) registered.");
        }
    }
}

// Register our quest rewards right after the vanilla reward DB is built (SharedSetup ends with
// RewardDataBase.AddRewards). EnsureParsed already ran inside this very call (SetupPatch postfixes
// Shared.Data.SharedDataSetup.Setup, SharedSetup's first statement) — re-calling it is a no-op guard.
[HarmonyPatch(typeof(CandideServer.Data.SharedDataSetups.SharedDataSetup),
    nameof(CandideServer.Data.SharedDataSetups.SharedDataSetup.SharedSetup))]
internal static class QuestRewardInjectPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            ContentLoader.EnsureParsed(Plugin.PacksDir);
            QuestRewardsPlusFeature.InjectRewards();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Quest item reward registration failed: {e.Message}"); }
    }
}

// FEATURE B: swap a vanilla room's interior for a pack map. Integrator calls this right after
// MapsFeature.RegisterRooms() in MapRoomPatch (postfix on ServerDataSetup.Setup).
internal static class RoomReplaceFeature
{
    internal static void ApplyReplacements()
    {
        foreach ((MapDef m, _) in ContentLoader.Maps)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.Replace)) continue;
            if (!DungeonRoomDataBase.DataMap.TryGetValue(m.Replace, out DungeonRoomDataModel room))
            {
                Plugin.Log.LogWarning($"map '{m.Id}': replace target '{m.Replace}' is not a known dungeon room — skipped. "
                    + "(Vanilla ids look like 'animal_den:bear', 'tree_room:chest', 'forest_tiny_visible_cave-graves', ...)");
                continue;
            }
            string world = MapsFeature.WorldName(m);
            if (CandideServer.World.ServerWorldDataManager.GetWorldDataOrNull(world) == null)
            {
                Plugin.Log.LogWarning($"map '{m.Id}': generated world '{world}' isn't loaded — replacement of '{m.Replace}' skipped.");
                continue;
            }
            // The room model (with the VANILLA ExitDirection) is what the save and the exit logic resolve
            // through — our map's exit box must sit on the same side or the way out won't line up.
            if ((int)room.ExitDirection != m.ExitDirection)
                Plugin.Log.LogWarning($"map '{m.Id}': exitDirection {m.ExitDirection} doesn't match vanilla room "
                    + $"'{m.Replace}' ({(int)room.ExitDirection}) — set \"exitDirection\": {(int)room.ExitDirection} or the dungeon exit won't line up.");
            // Oriented challenge-dungeon rooms encode their open sides in Orientation (recomputed from the
            // ORIGINAL map's transport nodes) — replacing one only works if our openings match exactly.
            if (room.Orientation != DungeonRoomDataModel.RoomOrientation.HandCrafted
                && room.Orientation != DungeonRoomDataModel.RoomOrientation.Entrance)
                Plugin.Log.LogWarning($"map '{m.Id}': '{m.Replace}' is an oriented challenge-dungeon room "
                    + $"({room.Orientation}) — its door openings must match or generated dungeons break. Prefer handcrafted (tiny) rooms.");

            room.InteriorWorldMapId = world;   // every future dungeon (re)build of this room now loads our map
            Plugin.Log.LogInfo($"Room replace: '{m.Replace}' interior -> '{world}' (map '{m.Id}').");
        }
    }
}
