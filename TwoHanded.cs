using Candide.GameModels;
using CandideServer;
using CandideServer.Helpers;
using CandideServer.MessageModels.Inventories;
using CandideServer.Models.Player;
using CandideServer.ServerControllers;
using CandideServer.ServerManagers;
using CandideServer.ServerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Shared.Entity;
using Shared.Inventory.DeltaModels;
using Shared.Models.Items;
using Shared.Models.Player;
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// weapon.twoHanded: while a two-handed weapon sits in an equipment set's WEAPON slot, that set's OFFHAND
// slot is forcibly kept empty. ONE consistent rule, both directions: THE TWO-HANDED WEAPON WINS — the
// offhand is ejected to the bag (bag full -> dropped at the player's feet; never refused, never destroyed).
//
// DESIGN — post-commit fixup on the HOST (server inventories are authoritative; clients see the result via
// the normal inventory-listener sync):
//   * There is no dedicated "equip" message to prefix-reject: equips/drag-drops/weapon-set-swaps all arrive
//     as generic DeltaAction batches (InventoryApplyEngine.TryApplyAction via Player_InventoryApplyManyDelta),
//     and the client has already PREDICTED the move in its UI. Vetoing a batch server-side desyncs the client
//     (the engine only resyncs on hard errors). So instead we let the change commit and immediately fix it.
//   * Every committed server-side inventory mutation funnels through SimpleInventoryServerService.
//     Send_NEWInventoryChanged (the proven UnequipDismissPatch hook). We postfix it: when an Equipment or
//     SecondaryEquipment inventory changes and "2H in weapon slot + offhand occupied" holds, eject the offhand.
//   * The eject is a TRUE INSTANCE MOVE — TryDetachItemFromInventory + AddItemInstance relocate the same
//     ItemInstanceModel object, so durability/uses/auras ride along (a remove-by-id + re-add would lose them).
//     Bag-full fallback: SimpleInventoryServerManager.TryDropItem (also instance-preserving) at the player's feet.
//   * Enforced on BOTH equipment sets independently: a 2H in either set keeps THAT set's offhand empty.
//     Weapon-set swaps need no special handling — the swap moves items between the two inventories, both
//     notify through the same funnel, and each set is re-checked.
//   * Re-entrancy: our own eject re-triggers Send_NEWInventoryChanged; a [ThreadStatic] guard stops the
//     nested call, and the fix itself is idempotent (after the eject the offhand slot is empty -> no-op).
//     We never touch ServerInventoryApplyBridge state, so running inside the delta engine's notify loop is safe.
//   * Known cosmetic quirk: equipping an offhand while holding a 2H briefly shows it equipped on the client
//     (client prediction) before it bounces to the bag one round-trip later.
//   * Saves that already violate the rule (an author newly marks a weapon twoHanded) are swept once at login
//     (SetupCharacter fills the equipment inventories SILENTLY — no change event — so the funnel alone misses them).
internal static class TwoHandedFeature
{
    // Last seen (weapon, offhand) per equipment inventory — lets a conflict know WHICH side just changed,
    // so the newest equip wins: equipping the offhand while a 2H is held stows the 2H (user intent),
    // equipping the 2H stows the offhand. Host-side, in-memory (rebuilt on the first event after load).
    private static readonly Dictionary<Guid, (string Weapon, string Off)> _lastSeen = new();

    // Item ids (BaseDataId) of main-hand weapons flagged twoHanded in a pack. Filled by ContentLoader.
    public static readonly HashSet<string> TwoHandedItemIds = new();

    // Re-check one equipment inventory and eject the offhand if a two-handed weapon holds the weapon slot.
    // Idempotent; only called from inventory-change events (no per-frame work).
    internal static void Enforce(SharedInventoryModel inv)
    {
        int wIdx, oIdx;
        if (inv.InventoryType == InventoryType.Equipment)
        {
            wIdx = EquipmentSimpleInventoryHelper.GetInventorySlotIndexForType(EquipmentType.Weapon);     // 3
            oIdx = EquipmentSimpleInventoryHelper.GetInventorySlotIndexForType(EquipmentType.Offhand);    // 4
        }
        else if (inv.InventoryType == InventoryType.SecondaryEquipment)
        {
            wIdx = SecondaryEquipmentSimpleInventoryHelper.GetInventorySlotIndexForType(EquipmentType.Weapon);  // 0
            oIdx = SecondaryEquipmentSimpleInventoryHelper.GetInventorySlotIndexForType(EquipmentType.Offhand); // 1
        }
        else return;
        if (wIdx < 0 || oIdx < 0 || oIdx >= inv.InventorySlots.Length) return;

        ItemInstanceModel weapon = inv.InventorySlots[wIdx];
        ItemInstanceModel off = inv.InventorySlots[oIdx];
        bool conflict = weapon != null && off != null && TwoHandedItemIds.Contains(weapon.BaseDataId);
        _lastSeen.TryGetValue(inv.Id, out (string Weapon, string Off) prev);
        if (!conflict)
        {
            _lastSeen[inv.Id] = (weapon?.BaseDataId, off?.BaseDataId);
            return;
        }
        // Newest equip wins: if the 2H was already held (same weapon as last time) the OFFHAND is the new
        // arrival -> the user wants it -> stow the WEAPON instead. Otherwise (2H just equipped / unknown
        // history) stow the offhand, as before.
        bool stowWeapon = prev.Weapon == weapon.BaseDataId && prev.Off != off.BaseDataId;
        int victimIdx = stowWeapon ? wIdx : oIdx;
        ItemInstanceModel victim = stowWeapon ? weapon : off;

        // Owner: both equipment inventories are registered in InventoryIdToPlayerCharacterIdMap at SetupCharacter.
        if (!ServerRunState.InventoryIdToPlayerCharacterIdMap.TryGetValue(inv.Id, out Guid charId)) return;
        if (!ServerGameState.TryGetPlayerCharacter(charId, out ServerPlayerCharacter pc) || pc?.Character == null) return;
        Guid bagId = pc.Character.CharacterInventoryModel.InventoryId;

        bool toBag = ServerGameState.TryGetInventory(bagId, out SharedInventoryModel bag)
                     && InventoryHelper.CanAddItemInstance(bag, victim, allowPartialStack: false, out _);
        if (toBag)
        {
            if (!SimpleInventorySController.TryDetachItemFromInventory(inv, victimIdx)) return;
            var changed = new HashSet<int>();
            if (SimpleInventorySController.AddItemInstance(victim, bagId, allowPartialStack: false, changed, out _))
            {
                SimpleInventoryServerManager.OnInventoryChanged(inv.Id, new HashSet<int> { victimIdx });
                SimpleInventoryServerManager.OnInventoryChanged(bagId, changed);
            }
            else
            {
                // Pre-checked, so this shouldn't happen — put the item back so nothing is ever lost.
                victim.InventoryId = inv.Id;
                inv.InventorySlots[victimIdx] = victim;
                Plugin.Log.LogWarning($"Two-handed: bag add failed for '{victim.BaseDataId}', item restored.");
                return;
            }
        }
        else
        {
            // Bag full: drop at the player's feet (instance-preserving; sends its own slot update).
            if (!SimpleInventoryServerManager.TryDropItem(inv.Id, victim.Id, null,
                    pc.Character.Position + new Vector3(0f, 0f, 10f), Vector3.Zero,
                    pc.Character.WorldId, null, pc.PlayerId))
                return;
        }
        _lastSeen[inv.Id] = (inv.InventorySlots[wIdx]?.BaseDataId, inv.InventorySlots[oIdx]?.BaseDataId);
        Plugin.Log.LogInfo($"Two-handed: " + (stowWeapon
            ? $"offhand equipped — '{victim.BaseDataId}' (the 2H weapon) stowed "
            : $"'{weapon.BaseDataId}' ejected offhand '{victim.BaseDataId}' ")
            + (toBag ? "to the bag." : "to the ground (bag full)."));
        NotifyLocal(charId, toBag, stowWeapon);
    }

    // Banner (top-center) or toast per [Notifications] Style — host-side only (the server runs in the
    // host's process; remote players see the item visibly jump to their bag via the normal inventory sync).
    private static void NotifyLocal(Guid charEntityId, bool toBag, bool stowedWeapon)
    {
        try
        {
            if (GameState.LocalPlayer?.EntityId != charEntityId) return;
            bool tr = Candide.Globals.LanguageCode == "tr_TR";
            string what = stowedWeapon ? (tr ? "Çift elli silah" : "Two-handed weapon")
                                       : (tr ? "Yan el eşyası" : "Offhand item");
            string msg = toBag ? (tr ? $"{what} çantana kaldırıldı." : $"{what} moved to your bag.")
                               : (tr ? $"{what} yere bırakıldı (çanta dolu)." : $"{what} dropped (bag full).");
            BannerSystem.Announce(tr ? "Çift Elli" : "Two-Handed", msg);
        }
        catch { }
    }
}

// The change-event hook: every committed server-side inventory mutation (equip, drag-drop, weapon-set swap,
// loot-into-equipment) notifies through here. Equipment/SecondaryEquipment changes re-run the enforcement.
internal static class TwoHandedEnforcePatch
{
    [ThreadStatic] private static bool _busy;   // our own eject re-enters this method — don't recurse

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SimpleInventoryServerService), nameof(SimpleInventoryServerService.Send_NEWInventoryChanged))]
    private static void Postfix(InventoryListenerUpdate update)
    {
        if (_busy) return;
        try
        {
            if (TwoHandedFeature.TwoHandedItemIds.Count == 0) return;
            if (!ServerGameState.TryGetInventory(update.InventoryId, out SharedInventoryModel inv)) return;
            if (inv.InventoryType != InventoryType.Equipment && inv.InventoryType != InventoryType.SecondaryEquipment) return;
            _busy = true;
            try { TwoHandedFeature.Enforce(inv); }
            finally { _busy = false; }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Two-handed enforcement failed: {e.Message}"); }
    }
}

// Login sweep: SetupCharacter creates the equipment inventories and fills them from the save SILENTLY
// (FillInventoryWithSeparateSystemInstancesSilent — no change event), so a save violating the rule
// (e.g. the weapon was newly marked twoHanded) must be fixed once here. Runs before the login snapshot
// is sent, so the joining client never even sees the stale state.
internal static class TwoHandedLoginPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerCharacterServerManager), nameof(PlayerCharacterServerManager.SetupCharacter))]
    private static void Postfix(ServerPlayerCharacter __result)
    {
        try
        {
            if (TwoHandedFeature.TwoHandedItemIds.Count == 0 || __result?.Character == null) return;
            CharacterInventoryModel cim = __result.Character.CharacterInventoryModel;
            if (ServerGameState.TryGetInventory(cim.EquipmentInventoryId, out SharedInventoryModel eq))
                TwoHandedFeature.Enforce(eq);
            if (ServerGameState.TryGetInventory(cim.SecondaryEquipmentInventoryId, out SharedInventoryModel sec))
                TwoHandedFeature.Enforce(sec);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Two-handed login sweep failed: {e.Message}"); }
    }
}
