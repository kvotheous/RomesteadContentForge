using CandideServer.MessageModels;
using CandideServer.Models;
using CandideServer.ServerManagers;
using CandideServer.ServerServices;
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// (ArenaDef lives in ContentPack.cs — the Validator compiles ContentPack standalone.)

// Server-side event machinery. One event type for now: duel-arena matches on top of PvpFeature's pvp
// worlds. Everything here runs in the host's in-process server; clients only ever see the chat lines.
//
// Scheduling: EventsFeature.Tick(dt) is called from the EXISTING per-tick server hook (CompanionPatch's
// postfix on EntityServerSystem.Update) — no Harmony patch of its own. Heavy work (presence scans) runs
// once per second, not per tick.
//
// Match flow per arena (durationSeconds > 0):
//   Idle ──(≥ minPlayers in a world of the arena's map)──▶ Running (announce start; count kills via
//   PvpFeature.PlayerKilledInPvp) ──(timer up, or ≤1 player left)──▶ winner announced + rewarded ──▶
//   Cooldown ──(world empties)──▶ Idle (re-armed).
// Winner = most kills; tie broken by whose LAST kill came first; if nobody scored, a sole survivor wins.
internal static class EventsFeature
{
    private enum ArenaState { Idle, Running, Cooldown }

    private sealed class ArenaRuntime
    {
        public ArenaDef Def;
        public ArenaState State = ArenaState.Idle;
        public Guid WorldId;                                  // the world the current match/cooldown is bound to
        public double EndsAt;                                 // _now at which a Running match times out
        public readonly Dictionary<string, int> Kills = new();      // killer name -> kills this match
        public readonly Dictionary<string, double> LastKill = new(); // killer name -> _now of their latest kill
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Def.Name)) return Def.Name;
                int i = Def.Id.LastIndexOf(':');
                return i >= 0 ? Def.Id.Substring(i + 1) : Def.Id;
            }
        }
    }

    private static readonly List<ArenaRuntime> _arenas = new();
    private static readonly Dictionary<string, ArenaRuntime> _byMap = new();  // map id -> its arena (1:1 in v1)
    private static readonly Dictionary<Guid, string> _worldMapCache = new();  // worldId -> room DataId (map id)
    private static bool _subscribed;
    private static double _now;          // server seconds, accumulated from Tick dt
    private static float _scanTimer;     // presence scans run once per ScanInterval, not per tick
    private const float ScanInterval = 1f;

    // Called by ContentLoader while parsing packs (idempotent per arena id — EnsureParsed runs once anyway).
    public static void RegisterArena(ArenaDef def)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.Id) || string.IsNullOrWhiteSpace(def.Map)) return;
        foreach (var a in _arenas) if (a.Def.Id == def.Id) return;
        if (_byMap.ContainsKey(def.Map))
        { Plugin.Log.LogWarning($"Arena '{def.Id}' skipped: map '{def.Map}' already belongs to another arena."); return; }
        if (def.MinPlayers < 2) def.MinPlayers = 2;
        if (def.RewardAmount < 1) def.RewardAmount = 1;
        var rt = new ArenaRuntime { Def = def };
        _arenas.Add(rt);
        _byMap[def.Map] = rt;
        Plugin.Log.LogInfo($"Arena '{def.Id}' registered on map '{def.Map}' " +
            (def.DurationSeconds > 0 ? $"({def.DurationSeconds}s matches, min {def.MinPlayers})." : "(endless free-for-all)."));
    }

    // Per-server-tick entry point — called from CompanionPatch's existing EntityServerSystem.Update postfix.
    public static void Tick(float dt)
    {
        if (_arenas.Count == 0) return;
        try
        {
            _now += dt;
            if (!_subscribed)
            {
                PvpFeature.PlayerKilledInPvp += OnPvpKill;
                _subscribed = true;
                foreach (var a in _arenas)
                    if (PvpFeature.PvpMapIds != null && !PvpFeature.PvpMapIds.Contains(a.Def.Map))
                        Plugin.Log.LogWarning($"Arena '{a.Def.Id}': map '{a.Def.Map}' is not flagged pvp:true — kills there won't be reported.");
            }
            _scanTimer -= dt;
            if (_scanTimer > 0f) return;
            _scanTimer = ScanInterval;
            Scan();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Events tick failed: {e.Message}"); }
    }

    // ---- kill attribution (fires from PvpFeature on every player death in a pvp world) ----

    private static void OnPvpKill(string victimName, string killerName, Guid worldId)
    {
        try
        {
            string map = MapIdForWorld(worldId);
            if (map == null || !_byMap.TryGetValue(map, out ArenaRuntime arena)) return;

            if (arena.Def.AnnounceKills)
                Broadcast(killerName != null
                    ? $"{killerName} slew {victimName} in {arena.DisplayName}!"
                    : $"{victimName} fell in {arena.DisplayName}.");

            if (killerName == null) return;

            if (arena.State == ArenaState.Running && worldId == arena.WorldId)
            {
                arena.Kills.TryGetValue(killerName, out int k);
                arena.Kills[killerName] = k + 1;
                arena.LastKill[killerName] = _now;
            }
            else if (arena.Def.DurationSeconds <= 0 && !string.IsNullOrWhiteSpace(arena.Def.WinnerReward))
            {
                // endless arena: the reward (if any) is paid per kill, straight to the killer
                GiveReward(killerName, arena);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Arena kill handling failed: {e.Message}"); }
    }

    // ---- once-per-second state machine ----

    private static void Scan()
    {
        foreach (ArenaRuntime arena in _arenas)
        {
            switch (arena.State)
            {
                case ArenaState.Idle:
                    if (arena.Def.DurationSeconds <= 0) break;  // endless arenas never run matches
                    TryStartMatch(arena);
                    break;

                case ArenaState.Running:
                    int present = CountPlayersInWorld(arena.WorldId);
                    if (present == 0) { AbortMatch(arena); break; }
                    if (_now >= arena.EndsAt || present <= 1) EndMatch(arena);
                    break;

                case ArenaState.Cooldown:
                    if (!PlayerServerManager.AnyPlayerInWorld(arena.WorldId))
                    {
                        arena.State = ArenaState.Idle;
                        arena.WorldId = Guid.Empty;
                        Plugin.Log.LogInfo($"Arena '{arena.Def.Id}' re-armed (world emptied).");
                    }
                    break;
            }
        }
    }

    private static void TryStartMatch(ArenaRuntime arena)
    {
        // Find a world of the arena's map holding enough players. Player count is tiny; a linear pass is fine.
        Guid bestWorld = Guid.Empty;
        int bestCount = 0;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            Guid w = p?.CharacterModel?.Character?.WorldId ?? Guid.Empty;
            if (w == Guid.Empty || MapIdForWorld(w) != arena.Def.Map) continue;
            int c = CountPlayersInWorld(w);
            if (c > bestCount) { bestCount = c; bestWorld = w; }
        }
        if (bestCount < arena.Def.MinPlayers) return;

        arena.State = ArenaState.Running;
        arena.WorldId = bestWorld;
        arena.EndsAt = _now + arena.Def.DurationSeconds;
        arena.Kills.Clear();
        arena.LastKill.Clear();
        Plugin.Log.LogInfo($"Arena '{arena.Def.Id}': match started ({bestCount} fighters, {arena.Def.DurationSeconds}s).");
        Broadcast($"A match has begun in {arena.DisplayName} — {bestCount} fighters enter!");
    }

    private static void EndMatch(ArenaRuntime arena)
    {
        string winner = PickWinner(arena);
        if (winner != null)
        {
            arena.Kills.TryGetValue(winner, out int k);
            Broadcast(k > 0
                ? $"{winner} is the champion of {arena.DisplayName} with {k} kill{(k == 1 ? "" : "s")}!"
                : $"{winner} stands alone — champion of {arena.DisplayName}!");
            if (!string.IsNullOrWhiteSpace(arena.Def.WinnerReward)) GiveReward(winner, arena);
        }
        else Broadcast($"The match in {arena.DisplayName} ends with no victor.");
        Plugin.Log.LogInfo($"Arena '{arena.Def.Id}': match ended, winner = {winner ?? "(none)"}. Cooling down.");
        arena.State = ArenaState.Cooldown;
    }

    private static void AbortMatch(ArenaRuntime arena)
    {
        Plugin.Log.LogInfo($"Arena '{arena.Def.Id}': match aborted (arena emptied). Cooling down.");
        arena.State = ArenaState.Cooldown;
    }

    // Most kills; ties broken by whose LAST kill landed first; zero kills all round -> sole survivor (if any).
    private static string PickWinner(ArenaRuntime arena)
    {
        string best = null; int bestKills = 0; double bestLast = double.MaxValue;
        foreach (var kv in arena.Kills)
        {
            arena.LastKill.TryGetValue(kv.Key, out double last);
            if (kv.Value > bestKills || (kv.Value == bestKills && last < bestLast))
            { best = kv.Key; bestKills = kv.Value; bestLast = last; }
        }
        if (best != null) return best;

        // nobody scored — a sole remaining player still takes the crown
        string lone = null; int n = 0;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            if ((p?.CharacterModel?.Character?.WorldId ?? Guid.Empty) != arena.WorldId) continue;
            if (++n > 1) return null;
            lone = p.CharacterModel.Character.Name;
        }
        return lone;
    }

    // ---- helpers ----

    private static void GiveReward(string playerName, ArenaRuntime arena)
    {
        try
        {
            foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            {
                if (p?.CharacterModel?.Character?.Name != playerName) continue;
                Guid inv = p.CharacterModel.Character.CharacterInventoryModel.InventoryId;
                SimpleInventoryServerManager.TryAddItemAmounts(inv, arena.Def.WinnerReward, arena.Def.RewardAmount,
                    null, allowPartialStack: true, null);
                return;
            }
            Plugin.Log.LogWarning($"Arena '{arena.Def.Id}': winner '{playerName}' not found for reward (disconnected?).");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Arena reward failed: {e.Message}"); }
    }

    private static int CountPlayersInWorld(Guid worldId)
    {
        int n = 0;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            if ((p?.CharacterModel?.Character?.WorldId ?? Guid.Empty) == worldId) n++;
        return n;
    }

    // Which pack map does this world render? Pack maps register as dungeon rooms (Maps.cs), so the room's
    // DataId IS the map id. Only positive results are cached (a world can resolve late during load).
    private static string MapIdForWorld(Guid worldId)
    {
        if (worldId == Guid.Empty) return null;
        if (_worldMapCache.TryGetValue(worldId, out string cached)) return cached;
        string id = null;
        try { id = DungeonServerManager.GetDungeonRoomFromWorldId(worldId)?.DataId; }
        catch { }
        if (id != null)
        {
            if (_worldMapCache.Count > 512) _worldMapCache.Clear();   // bound long-session growth
            _worldMapCache[worldId] = id;
        }
        return id;
    }

    private static void Broadcast(string text)
    {
        try { ChatLogServerService.SendChatLogMessage(ChatLogMessage.DirectText(text, ChatMessageType.WorldEvent, "Arena")); }
        catch (Exception e) { Plugin.Log.LogWarning($"Arena broadcast failed: {e.Message}"); }
    }
}
