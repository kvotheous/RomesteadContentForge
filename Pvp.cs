using Candide.Entities.PlayerState;     // NetworkPlayerController
using Candide.GameModels;               // GameState (client world registry)
using Candide.Multiplayer;              // PlayerHelper
using CandideServer;                    // ServerGameState
using CandideServer.Entities;           // ServerEntityModel
using CandideServer.MessageModels;      // DealDamageMessage
using CandideServer.ServerManagers;     // EntityServerManager, PlayerServerManager
using CandideServer.ServerServices;     // ServerJoinedPlayerModel
using HarmonyLib;
using Shared.Combat;                    // DamageSourceType
using Shared.Combat.Projectiles;        // ProjectileSystem, ProjectileMask
using Shared.Combat.Projectiles.Models; // ProjectileHitData, SpellData
using Shared.Combat.Spells.Casting;     // CastSpell, SpellCastingContext
using Shared.Entity;                    // EntityWrapper
using Shared.Entity.Components;         // FactionMask
using Shared.Helpers;                   // CastSpellArgs
using Shared.Models.Spells;             // ProjectileHitFlags
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// PvP zones — a pack map with `"pvp": true` becomes a world where players can damage each other, and where
// dying spawns NO grave (no item loss; the arena death costs nothing).
//
// HOW PLAYER↔PLAYER DAMAGE IS BLOCKED IN VANILLA (and what we open): every player attack — melee cone,
// arrow, spell — is a "Casted" projectile resolved on the ATTACKER'S client by one collision routine,
// ProjectileSystem.OnProjectileHitEntities. Two gates stop PvP there:
//   1. faction — CastSpell snapshots canHit = NoSharedFaction at construction and both players carry
//      FactionMask.Player, so a player→player hit is skipped as "same faction" (PvpCastSpellPatch).
//   2. controller veto — NetworkPlayerController.OnProjectileHit returns false for any Casted projectile
//      owned by the LOCAL player, so your own shots never connect with remote players (PvpClientHitPatch).
// Downstream is already open: the server's DamageEntity applies + rebroadcasts network damage without any
// faction validation, and the victim's client applies it unconditionally. That hole is also why we HARDEN:
// the server now rejects player→player damage outside pvp worlds (PvpDamageGatePatch), so a hacked client
// can't PvP anywhere.
//
// Co-op caveat: hit detection is attacker-client-side, so every COMBATANT needs the mod (and the pack);
// an unmodded client can be hit and die in a pvp world but cannot deal pvp damage itself.
// ---------------------------------------------------------------------------------------------------------

// Shared pvp state + the world test both sides use. Parses nothing itself: ContentLoader adds the flagged
// map ids while reading packs.
internal static class PvpFeature
{
    // Map ids flagged `"pvp": true` in their pack (populated by ContentLoader during EnsureParsed).
    internal static readonly HashSet<string> PvpMapIds = new();

    // Fired on the SERVER when a player dies inside a pvp world: (victimName, killerName, worldId).
    // killerName = the last player to land a hit on the victim within the attribution window — null when a
    // mob/the environment finished them off (or no recent player hit). Consumed by EventsFeature (arenas).
    public static event Action<string, string, Guid> PlayerKilledInPvp;

    // worldId -> pvp? Custom maps are dungeon rooms whose interior worlds are created LAZILY (portal/dungeon
    // enter), and one map id can spawn several world instances over a session — so membership is resolved
    // LIVE from the world's MapName ("maps/cf/<id>", see MapsFeature.WorldName) in whichever world registry
    // this process has (host: both; pure client: GameState only; ServerGameState's dictionaries exist but
    // stay empty there). No creation hook to miss, and reloads/repeat instances work for free.
    private static HashSet<string> _pvpWorldNames;

    public static bool IsPvpWorld(Guid worldId)
    {
        try
        {
            if (PvpMapIds.Count == 0 || worldId == Guid.Empty) return false;
            _pvpWorldNames ??= BuildWorldNames();
            if (ServerGameState.Worlds.TryGetValue(worldId, out var sw) && sw?.MapName != null)
                return _pvpWorldNames.Contains(sw.MapName);
            if (GameState.Worlds.TryGetValue(worldId, out var cw) && cw?.MapName != null)
                return _pvpWorldNames.Contains(cw.MapName);
        }
        catch { }   // combat hot path — never throw into a hit resolution
        return false;
    }

    // The interior-world map names the flagged maps generate. Built once on first use — packs parse long
    // before any interior world can exist, so the ids are always complete by then.
    private static HashSet<string> BuildWorldNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((MapDef def, _) in ContentLoader.Maps)
            if (def?.Id != null && PvpMapIds.Contains(def.Id))
                names.Add(MapsFeature.WorldName(def));
        return names;
    }

    // ---- kill attribution (server): remember the last player→player hit per victim, consume on death ----
    private const double AttributionWindowSeconds = 10.0;
    private static readonly Dictionary<int, (string Killer, DateTime At)> _lastPvpHit = new();

    internal static void NoteHit(int victimPlayerId, string killerName)
    {
        if (string.IsNullOrEmpty(killerName)) return;
        lock (_lastPvpHit) _lastPvpHit[victimPlayerId] = (killerName, DateTime.UtcNow);
    }

    internal static string TakeLastAttacker(int victimPlayerId)
    {
        lock (_lastPvpHit)
        {
            if (!_lastPvpHit.Remove(victimPlayerId, out var hit)) return null;
            return (DateTime.UtcNow - hit.At).TotalSeconds <= AttributionWindowSeconds ? hit.Killer : null;
        }
    }

    // ---- EXACT kill attribution: the server runs the full shared combat sim, and PlayerEntityKilled
    // receives the killing blow's DamageResult (one call site, Shared CheckIfKilled — feather saves never
    // reach it). null killer = the killing blow was definitively NOT another player (mob/hazard/self/summon).
    private static readonly Dictionary<int, string> _exactKill = new();

    internal static void NoteExactKill(int victimPlayerId, string killerNameOrNull)
    {
        lock (_exactKill) _exactKill[victimPlayerId] = killerNameOrNull;
    }

    internal static bool TakeExactKill(int victimPlayerId, out string killerNameOrNull)
    {
        lock (_exactKill) return _exactKill.Remove(victimPlayerId, out killerNameOrNull);
    }

    internal static void RaisePlayerKilledInPvp(string victimName, string killerName, Guid worldId)
    {
        try { PlayerKilledInPvp?.Invoke(victimName, killerName, worldId); }
        catch (Exception e) { Plugin.Log.LogWarning($"PvP kill event handler failed: {e.Message}"); }
    }
}

// (Shared — runs wherever a CastSpell is built) PvP gate 1/2: the FACTION gate. canHit is snapshotted into a
// READONLY field by the ctor, so we widen the incoming `canHit` argument in a prefix — for a PLAYER-owned
// cast in a pvp world, `|= SharedFaction` lets the projectile hit the owner's own faction (other players).
// Caster stays excluded (we never add ProjectileHitFlags.Caster) and friendly AoE/heals already carry
// SharedFaction, so they're untouched. Accepted side effect (keep simple): inside pvp worlds player attacks
// can also hit player summons — and a player's summons can hit other players.
internal static class PvpCastSpellPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CastSpell), MethodType.Constructor, new[] {
        typeof(ProjectileSystem), typeof(EntityWrapper), typeof(SpellCastingContext), typeof(SpellData),
        typeof(CastSpellArgs), typeof(DamageSourceType), typeof(Action<ProjectileHitData, CastSpell>),
        typeof(CastSpell.OnUpdateDelegate), typeof(ProjectileHitFlags) })]
    private static void Prefix(ProjectileSystem system, EntityWrapper owner, ref ProjectileHitFlags canHit)
    {
        try
        {
            if (owner == null || system == null) return;
            if ((canHit & ProjectileHitFlags.SharedFaction) != 0) return;   // already open (friendly AoE etc.)
            if ((owner.FactionMask & FactionMask.Player) == 0) return;      // only player-faction casts
            if (PvpFeature.IsPvpWorld(system.WorldId)) canHit |= ProjectileHitFlags.SharedFaction;
        }
        catch { }   // never break a cast
    }
}

// (client) PvP gate 2/2: the CONTROLLER veto. NetworkPlayerController.OnProjectileHit returns false for any
// Casted projectile owned by the LOCAL player — your own attacks never connect with a remote player, before
// the faction flags are even consulted. In a pvp world we lift the veto (__result = true) so the hit lands;
// the damage then travels the normal attacker-authoritative deal-damage route.
internal static class PvpClientHitPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NetworkPlayerController), nameof(NetworkPlayerController.OnProjectileHit))]
    private static bool Prefix(int id, CastSpell spell1, ref bool __result)
    {
        try
        {
            ProjectileSystem system = spell1?.System;
            if (system == null) return true;
            if ((system.Mask[id] & ProjectileMask.Casted) == 0) return true;   // vanilla returns true anyway
            if (!PlayerHelper.IsLocalPlayer(spell1.Owner)) return true;        // remote-owned: vanilla path
            if (!PvpFeature.IsPvpWorld(system.WorldId)) return true;
            __result = true;
            return false;
        }
        catch { return true; }
    }
}

// (server) No grave in pvp worlds. SpawnGrave dumps the dying player's whole inventory into a grave entity;
// skipping it means the player respawns with everything (RespawnPlayer only resets health/position/Dead).
// playerModel.LastGraveId is deliberately left untouched — a still-standing grave from an earlier REAL death
// keeps working. SpawnGrave is private static, so it's targeted by name.
internal static class PvpGravePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerServerManager), "SpawnGrave")]
    private static bool Prefix(ServerJoinedPlayerModel playerModel)
    {
        try
        {
            var ch = playerModel?.CharacterModel?.Character;
            if (ch != null && PvpFeature.IsPvpWorld(ch.WorldId)) return false;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"PvP grave check failed: {e.Message}"); }
        return true;
    }
}

// (server) Hardening + kill attribution. DamageEntity applies NETWORK damage with zero faction validation
// and rebroadcasts it to every client — without this gate a hacked client could PvP anywhere. Player→player
// damage outside pvp worlds is swallowed whole (no apply, no rebroadcast); inside a pvp world it passes, and
// the attacker is remembered so the death event can name the killer.
internal static class PvpDamageGatePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(EntityServerManager), nameof(EntityServerManager.DamageEntity))]
    private static bool Prefix(DealDamageMessage message)
    {
        try
        {
            if (message == null || !message.OwnerId.HasValue || message.OwnerId.Value == message.VictimId) return true;
            if (!ServerGameState.Entities.TryGetValue(message.VictimId, out ServerEntityModel victim)
                || victim?.EntityWrapper == null) return true;
            if (!PlayerServerManager.TryGetPlayerFromEntity(victim.EntityWrapper, out ServerJoinedPlayerModel victimPlayer))
                return true;
            if (!ServerGameState.Entities.TryGetValue(message.OwnerId.Value, out ServerEntityModel source)
                || source?.EntityWrapper == null) return true;
            if (!PlayerServerManager.TryGetPlayerFromEntity(source.EntityWrapper, out ServerJoinedPlayerModel sourcePlayer))
                return true;
            if (sourcePlayer.Id == victimPlayer.Id) return true;   // self-damage is the player's own business

            if (PvpFeature.IsPvpWorld(victim.WorldId))
            {
                // Fair-fight rule: both sides must be fully modded (mod + every host pack — see ModHandshake).
                // A vanilla client can't deal pvp damage anyway (attacker-side gates); this also stops them TAKING it.
                if (!ModHandshake.IsFullyModded(sourcePlayer.Id) || !ModHandshake.IsFullyModded(victimPlayer.Id))
                    return false;
                // teamBattle: swallow same-team damage server-side (attacker's client still shows a
                // predicted hit that never applies/rebroadcasts — cosmetic; teams are host-only state).
                if (EventsPlus2Feature.SameTeam(sourcePlayer.Id, victimPlayer.Id)) return false;
                PvpFeature.NoteHit(victimPlayer.Id, sourcePlayer.CharacterModel?.Character?.Name);
                return true;
            }
            return false;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"PvP damage gate failed: {e.Message}"); return true; }
    }
}

// (server) Death → kill report for EventsFeature. OnPlayerDied ALSO re-runs for already-dead players (it
// then just moves the grave), so the prefix snapshots Dead and the postfix only reports the real transition.
// Killer = the last player to hit the victim within the attribution window; "last player hit" is as precise
// as this game's client-authoritative damage allows without a transpiler — a mob landing the actual killing
// blow within the window is credited to that player (documented, accepted).
internal static class PvpDeathEventPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerServerManager), nameof(PlayerServerManager.OnPlayerDied))]
    private static void Prefix(ServerJoinedPlayerModel playerModel, ref bool __state)
        => __state = playerModel == null || playerModel.Dead;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerServerManager), nameof(PlayerServerManager.OnPlayerDied))]
    private static void Postfix(ServerJoinedPlayerModel playerModel, bool __state)
    {
        try
        {
            if (__state) return;   // was already dead — not a new death
            var ch = playerModel?.CharacterModel?.Character;
            if (ch == null) return;
            // Always consume BOTH records (no stale carryover). Exact beats heuristic: when the server
            // simulated the killing blow (PvpExactKillPatch), trust it — including its "not a player"
            // verdict (null), so a mob stealing the kill is no longer credited to the last player hit.
            // The heuristic only fills in for deaths the server never simulated (client player-died path).
            bool hadExact = PvpFeature.TakeExactKill(playerModel.Id, out string exactKiller);
            string heuristic = PvpFeature.TakeLastAttacker(playerModel.Id);
            string killer = hadExact ? exactKiller : heuristic;
            if (!PvpFeature.IsPvpWorld(ch.WorldId)) return;
            PvpFeature.RaisePlayerKilledInPvp(ch.Name, killer, ch.WorldId);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"PvP death event failed: {e.Message}"); }
    }
}

// (server) EXACT killing-blow attribution. The host's in-process server runs the full shared combat
// simulation on its own player entities: Shared CheckIfKilled → ServerPlayerEntityController.OnKilled →
// PlayerServerManager.PlayerEntityKilled(entity, info) — info.DamageResult IS the killing blow (single
// call site; phoenix-feather saves return false at OnOutOfHealth and never get here). OnPlayerDied runs
// INSIDE this call, so the prefix always lands before the death postfix consumes the record. Covers
// player hits AND player-applied DoT ticks (server aura ticks carry the owner as SourceEntity). Summon
// killing blows resolve to the summon's OWNER via the cf-owner stamp (Fixes2).
internal static class PvpExactKillPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerServerManager), nameof(PlayerServerManager.PlayerEntityKilled))]
    private static void Prefix(EntityWrapper entity, EntityKillInfo info)
    {
        try
        {
            if (!PlayerServerManager.TryGetPlayerFromEntity(entity, out ServerJoinedPlayerModel victim) || victim == null)
                return;
            EntityWrapper src = info.DamageResult?.SourceEntity ?? info.DamageResult?.DamageInfo.SourceEntity;
            string killer = null;
            if (src != null
                && PlayerServerManager.TryGetPlayerFromEntity(src, out ServerJoinedPlayerModel killerPlayer)
                && killerPlayer != null && killerPlayer.Id != victim.Id)
                killer = killerPlayer.CharacterModel?.Character?.Name;
            // Summon killing blow: vanilla carries no owner on the summon — resolve our cf-owner stamp.
            if (killer == null && src != null
                && Fixes2.TryResolveSummonOwner(src, out ServerJoinedPlayerModel sOwner) && sOwner.Id != victim.Id)
                killer = sOwner.CharacterModel?.Character?.Name;
            PvpFeature.NoteExactKill(victim.Id, killer);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"PvP exact-kill capture failed: {e.Message}"); }
    }
}
