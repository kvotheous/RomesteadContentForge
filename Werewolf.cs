using Candide;                              // Globals (client)
using Candide.CandideUI;                    // CandideThickness, VerticalAlignment
using Candide.CandideUI.Components;         // CandideTextLabel
using Candide.CandideUI.Components.Buttons; // CandideLabelButton
using Candide.CandideUI.Containers;         // CandideWindow, WindowGraphics, CandideVerticalStackPanel
using Candide.GameModels.Managers;          // GameStateManager (client join hook)
using Candide.LegacyUI;                     // HorizontalAlignment
using Candide.Multiplayer.Network;          // NetworkManager (client -> server messages)
using Candide.UISystems.Notification;       // UiNotificationSystem (client main-thread pump host)
using CandideServer;                        // ServerGlobals
using CandideServer.EventBus;               // EventBusManager, EventBusArgs
using CandideServer.MessageModels;          // ChatLogMessage, PlayerChatLogMessage
using CandideServer.Models;                 // ChatMessageType
using CandideServer.Server;                 // BaseServer
using CandideServer.ServerManagers;         // DungeonServerManager, PlayerServerManager, SimpleInventoryServerManager
using CandideServer.ServerServices;         // PlayerServerService, ChatLogServerService, ServerJoinedPlayerModel
using HarmonyLib;
using Microsoft.Xna.Framework;              // Vector3
using Shared.Combat;                        // SharedCombatHandler, HitInfo, DamageTypesArray, DamageSourceType
using Shared.Entity;                        // EntityWrapper
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace RomesteadContentForge;

// (EventDef lives in ContentPack.cs — the Validator compiles ContentPack standalone.)

// ---------------------------------------------------------------------------------------------------------
// "Werewolf Village" — a Mafia/Werewolf social-deduction game mode (internal/WEREWOLF_DESIGN.md). One event
// type ("werewolf") on a pack map: wolves secretly pick victims at night through a small action window, the
// village lynches by vote at day, last side standing wins. Built from machinery the mod already ships:
//
//   runtime   — EventsCompetitive mold: state machine ticked from CompanionPatch's EntityServerSystem.Update
//               postfix (WerewolfFeature.Tick), presence via JoinedPlayers + DungeonServerManager world->map
//               resolution, rewards via TryAddItemAmounts, chat via ChatLogServerService.
//   messages  — the proven handshake channel. Client->server: NetworkManager.SendMessage(key, string, 2) +
//               server NetworkEventBusManager.Subscribe (new keys default AuthorizationCheck=true; the
//               SENDER is resolved the vanilla way, PlayerServerService.GetPlayerInfoFromPeerId(args.Peer.Id)
//               — peer id is NOT the player id). Server->client: BaseServer.SendHostMessage(key, string,
//               peer) per recipient; the HOST player is delivered by a direct local enqueue (same process,
//               no self-send) — all client handling is queued and pumped on the UI thread.
//   execute   — the "player-die-cheat" precedent (cs2:31792): server-side SharedCombatHandler.ApplyDamage
//               with a lethal true-damage HitInfo and NO owner. The server bridge then broadcasts the
//               entity-deal-damage itself (OnEntityTookDamage -> Send_DealDamage, SyncStrategy.Everyone) and
//               OnPlayerDied broadcasts "player-died", which the victim's own client obeys (LocalPlayerDied)
//               — the whole vanilla death pipeline, no melee, no pvp gates. Phoenix feather can eat the kill
//               (ServerPlayerEntityController.OnOutOfHealth returns false): if the victim is still alive a
//               couple seconds later we re-execute ONCE, then give up with a log line.
//   graves    — the map must be pvp:true, so PvpGravePatch already skips the grave (no item loss). During a
//               match every participant is put on the SAME EventsCompetitive team, so the existing server
//               damage gate swallows player->player melee in the world — wolves can't cheese-kill by sword.
//               (Consequence: don't bind a teamBattle/arena to the same map — documented in the design.)
//   ghosts    — dead players respawn into the map's walled spectator pen: postfix on RespawnPlayer (vanilla
//               respawn fully runs first — home spawn + "respawn-player" broadcast — then MovePlayerCharacter
//               authoritatively moves the ghost into the pen, world switch included; same call the vanilla
//               "player-teleport" cheat uses). Ghost chat is gated server-side: prefix on the vanilla chat
//               receive swallows the message and re-sends it as "[Ghost] name: ..." only to other ghosts.
//               Ghosts are excluded from alive lists, role actions, votes and win counts.
//   client UI — role/phase/info banners ride BannerSystem (en + tr_TR, TwoHanded convention); the night-pick
//               and vote windows are ONE small CandideWindow (top-left, WindowGraphics.Standard, a vertical
//               stack of CandideLabelButtons) shown on the live desktop with NO input blockers and NO pause
//               — players keep walking around while they choose. A click sends one string and closes.
//
// Phases: Idle -> Starting(5s) -> Night -> Dawn(5s) -> Day -> Vote -> (win check) -> Night ... -> Cooldown.
// Match start gate: >= minPlayers in the map's world AND every present player fully modded (handshake), with
// a single chat warning per arming attempt otherwise. Disconnect / leaving the map world / dying to a mob
// mid-match all count as death (no corpse) and re-check the win. Win: wolves==0 -> village; wolves >= alive
// non-wolves -> wolves. Winners (their whole side, dead or alive, still online) get the pack reward.
// Wolf count: ev.wolves, or auto 1 (<7 players) / 2 (<10) / 3. Seer: ev.seer && >= 5 players.
// ---------------------------------------------------------------------------------------------------------

internal static class WerewolfFeature
{
    private enum Phase { Idle, Starting, Night, Dawn, Day, Vote, Cooldown }
    private enum Role { Villager, Wolf, Seer }

    private const float StartingSeconds = 5f;
    private const float DawnSeconds = 5f;
    private const float CooldownSeconds = 30f;
    private const float ExecuteRetrySeconds = 2f;   // phoenix-feather guard: re-execute once after this
    private const int SkipVote = -2;

    private sealed class MatchRuntime
    {
        public EventDef Def;
        public Phase Phase = Phase.Idle;
        public Guid WorldId;
        public double PhaseEndsAt;
        public int Round;                                        // night number, 1-based
        public bool ArmWarned;                                   // unmodded-player warning fired this arming attempt
        public readonly Dictionary<int, Role> Roles = new();     // playerId -> role (this match's participants)
        public readonly HashSet<int> Ghosts = new();             // dead/left participants + mid-match late joiners
        public readonly HashSet<int> ToldSpectate = new();       // late joiners already banner'd
        public int NightPick = -1;                               // wolves' victim this night (last write wins)
        public int SeerPick = -1;
        public string LastVictimName;                            // for the dawn announcement
        public readonly Dictionary<int, int> Votes = new();      // voter -> target playerId (SkipVote = skip)
        public readonly Dictionary<int, (double RetryAt, bool Retried)> PendingKills = new();
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Def.Name)) return Def.Name;
                int i = Def.Id.LastIndexOf(':');
                return i >= 0 ? Def.Id.Substring(i + 1) : Def.Id;
            }
        }
        public bool Running => Phase == Phase.Night || Phase == Phase.Dawn || Phase == Phase.Day || Phase == Phase.Vote;
    }

    private static readonly List<MatchRuntime> _events = new();
    private static readonly Dictionary<Guid, string> _worldMapCache = new();   // worldId -> room DataId (map id)
    private static readonly Random _rng = new();
    private static bool _subscribed;
    private static double _now;           // server seconds, accumulated from Tick dt
    private static float _scanTimer;
    private const float ScanInterval = 1f;

    // ---- registration (called by ContentLoader's event fork, BEFORE the competitive/plus features) ----

    public static bool RegisterEvent(EventDef def)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.Id) || string.IsNullOrWhiteSpace(def.Type)) return false;
        if (!string.Equals(def.Type.Trim(), "werewolf", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var e in _events) if (e.Def.Id == def.Id) return true;   // duplicate id: already registered

        if (string.IsNullOrWhiteSpace(def.Map))
        { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: werewolf needs a map."); return true; }
        if (!PvpFeature.PvpMapIds.Contains(def.Map))
        {
            Plugin.Log.LogWarning($"Event '{def.Id}' skipped: werewolf needs a pvp:true map " +
                $"('{def.Map}' isn't flagged — set \"pvp\": true on that map; it kills the grave on death).");
            return true;
        }
        if (def.GhostPen == null || def.GhostPen.Length < 2)
        { Plugin.Log.LogWarning($"Event '{def.Id}' skipped: werewolf needs ghostPen [x,y] (map px, doodad space)."); return true; }
        if (def.MinPlayers < 2) def.MinPlayers = 4;     // explicit 2/3 = debug modes; unset/1 -> the real minimum
        if (def.NightSeconds < 10) def.NightSeconds = 45;
        if (def.DaySeconds < 10) def.DaySeconds = 120;
        if (def.VoteSeconds < 10) def.VoteSeconds = 30;
        if (def.RewardAmount < 1) def.RewardAmount = 1;

        _events.Add(new MatchRuntime { Def = def });
        Plugin.Log.LogInfo($"Event '{def.Id}' registered (werewolf, map '{def.Map}', min {def.MinPlayers}).");
        return true;
    }

    // ---- per-server-tick entry point (chained after EventsCompetitiveFeature.Tick in CompanionPatch) ----

    public static void Tick(float dt)
    {
        if (_events.Count == 0) return;
        try
        {
            _now += dt;
            EnsureSubscribed();
            _scanTimer -= dt;
            if (_scanTimer > 0f) return;
            _scanTimer = ScanInterval;
            Scan();
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf tick failed: {e.Message}"); }
    }

    // Register the action keys on the server bus (idempotent; new keys default AuthorizationCheck=true, so
    // only joined peers reach the handlers). Subscribe<object> so a bad payload can't throw in the dispatcher.
    private static void EnsureSubscribed()
    {
        if (_subscribed || BaseServer.Instance == null) return;
        _subscribed = true;
        BaseServer.Instance.NetworkEventBusManager.Subscribe<object>("cf-ww-pick", OnPick);
        BaseServer.Instance.NetworkEventBusManager.Subscribe<object>("cf-ww-see", OnSee);
        BaseServer.Instance.NetworkEventBusManager.Subscribe<object>("cf-ww-vote", OnVote);
        Plugin.Log.LogInfo("Werewolf listeners registered (cf-ww-pick/see/vote).");
    }

    // ---- once-per-second state machine ----

    private static void Scan()
    {
        // Reload / empty server: abort everything to Idle (EventsPlus safety rule — runtime-only state).
        if (PlayerServerService.JoinedPlayers.Count == 0)
        {
            foreach (var rt in _events)
                if (rt.Phase != Phase.Idle) Abort(rt);
            return;
        }

        foreach (MatchRuntime rt in _events)
        {
            try { ScanMatch(rt); }
            catch (Exception e) { Plugin.Log.LogWarning($"Event '{rt.Def.Id}' scan failed: {e.Message}"); }
        }
    }

    private static void ScanMatch(MatchRuntime rt)
    {
        switch (rt.Phase)
        {
            case Phase.Idle:
                TryArm(rt);
                return;

            case Phase.Starting:
                if (CountPlayersInWorld(rt.WorldId) < rt.Def.MinPlayers)
                {
                    Broadcast(rt, "Not enough players remain — the night is called off.");
                    Abort(rt);
                    return;
                }
                if (_now >= rt.PhaseEndsAt) StartMatch(rt);
                return;

            case Phase.Night:
            case Phase.Dawn:
            case Phase.Day:
            case Phase.Vote:
                CheckPendingKills(rt);
                HandleLeavers(rt);                 // disconnect / left world / died to a mob -> death
                if (rt.Phase == Phase.Cooldown) return;   // HandleLeavers can end the match
                HandleLateJoiners(rt);
                if (_now < rt.PhaseEndsAt)
                {
                    // early vote close: every living participant has voted
                    if (rt.Phase == Phase.Vote && rt.Votes.Count >= CountAlive(rt)) ResolveVote(rt);
                    return;
                }
                if (rt.Phase == Phase.Night) ResolveNight(rt);
                else if (rt.Phase == Phase.Dawn) BeginDay(rt);
                else if (rt.Phase == Phase.Day) BeginVote(rt);
                else ResolveVote(rt);
                return;

            case Phase.Cooldown:
                // Re-arm only after the cooldown AND once the world has emptied (the arena convention) —
                // walking out and back in starts the next match.
                if (_now >= rt.PhaseEndsAt && !PlayerServerManager.AnyPlayerInWorld(rt.WorldId))
                {
                    rt.Phase = Phase.Idle;
                    rt.WorldId = Guid.Empty;
                    Plugin.Log.LogInfo($"Event '{rt.Def.Id}' re-armed (world emptied).");
                }
                return;
        }
    }

    // ---- arming ----

    private static void TryArm(MatchRuntime rt)
    {
        Guid world = FindBestWorld(rt.Def.Map, rt.Def.MinPlayers);
        if (world == Guid.Empty) { rt.ArmWarned = false; return; }

        // Fair-play gate: every present player must run the mod + every host pack (windows/banners are
        // invisible to vanilla clients — they could neither act nor be informed). Warn once per attempt.
        var present = PlayerIdsInWorld(world);
        foreach (int pid in present)
            if (!ModHandshake.IsFullyModded(pid))
            {
                if (!rt.ArmWarned)
                {
                    rt.ArmWarned = true;
                    Broadcast(rt, $"{NameOf(pid)} has no Content Forge (or is missing packs) — " +
                        "the Werewolf match cannot start until everyone present is fully modded.");
                }
                return;
            }

        rt.Phase = Phase.Starting;
        rt.WorldId = world;
        rt.PhaseEndsAt = _now + StartingSeconds;
        rt.ArmWarned = false;
        Plugin.Log.LogInfo($"Event '{rt.Def.Id}': arming in world {world} ({present.Count} players).");
        Broadcast(rt, $"{rt.DisplayName} — {present.Count} players gathered. The game begins...");
        foreach (int pid in present) SendTo(pid, "cf-ww-phase", $"start:{(int)StartingSeconds}");
    }

    private static void StartMatch(MatchRuntime rt)
    {
        var pids = PlayerIdsInWorld(rt.WorldId);
        rt.Roles.Clear();
        rt.Ghosts.Clear();
        rt.ToldSpectate.Clear();
        rt.PendingKills.Clear();
        rt.Round = 0;

        // Role lottery: Fisher-Yates, then deal wolves / seer / villagers off the top.
        for (int i = pids.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (pids[i], pids[j]) = (pids[j], pids[i]);
        }
        int wolves = rt.Def.Wolves > 0 ? rt.Def.Wolves : (pids.Count < 7 ? 1 : pids.Count < 10 ? 2 : 3);
        wolves = Math.Min(wolves, Math.Max(1, pids.Count - 1));      // always at least one non-wolf
        bool seer = rt.Def.Seer && pids.Count >= 5;
        for (int i = 0; i < pids.Count; i++)
            rt.Roles[pids[i]] = i < wolves ? Role.Wolf : (seer && i == wolves ? Role.Seer : Role.Villager);

        // One shared "team" for the whole match: the existing pvp server damage gate (Pvp.cs) consults
        // EventsCompetitiveFeature.Teams and swallows same-team hits — no sword kills during the match.
        foreach (int pid in pids) EventsCompetitiveFeature.Teams[pid] = 0;

        // Private role delivery. Fellow wolves learn each other (classic rules): "wolf:Anna,Marcus".
        foreach (var kv in rt.Roles)
        {
            string payload;
            if (kv.Value == Role.Wolf)
            {
                var mates = new List<string>();
                foreach (var other in rt.Roles)
                    if (other.Value == Role.Wolf && other.Key != kv.Key) mates.Add(CleanName(NameOf(other.Key)));
                payload = mates.Count > 0 ? "wolf:" + string.Join(",", mates) : "wolf";
            }
            else payload = kv.Value == Role.Seer ? "seer" : "villager";
            SendTo(kv.Key, "cf-ww-role", payload);
        }

        Plugin.Log.LogInfo($"Event '{rt.Def.Id}': match started — {pids.Count} players, {wolves} wolf/wolves" +
            (seer ? ", seer" : "") + ".");
        BeginNight(rt);
    }

    // ---- phase transitions ----

    private static void BeginNight(MatchRuntime rt)
    {
        rt.Phase = Phase.Night;
        rt.PhaseEndsAt = _now + rt.Def.NightSeconds;
        rt.Round++;
        rt.NightPick = -1;
        rt.SeerPick = -1;
        rt.LastVictimName = null;
        Broadcast(rt, $"Night {rt.Round} falls over {rt.DisplayName}. Beware...");
        SendToParticipants(rt, "cf-ww-phase", $"night:{rt.Def.NightSeconds}");

        // Action windows: wolves pick among the alive non-wolves; the seer inspects any other living soul.
        string prey = BuildList(rt, exclude: Role.Wolf, excludePid: -1);
        foreach (var kv in rt.Roles)
        {
            if (rt.Ghosts.Contains(kv.Key)) continue;
            if (kv.Value == Role.Wolf && prey.Length > 0) SendTo(kv.Key, "cf-ww-window", "pick:" + prey);
            else if (kv.Value == Role.Seer)
            {
                string others = BuildList(rt, exclude: null, excludePid: kv.Key);
                if (others.Length > 0) SendTo(kv.Key, "cf-ww-window", "see:" + others);
            }
        }
    }

    private static void ResolveNight(MatchRuntime rt)
    {
        foreach (var kv in rt.Roles)
            if (!rt.Ghosts.Contains(kv.Key) && kv.Value != Role.Villager) SendTo(kv.Key, "cf-ww-window", "close");

        // Seer learns first (even about tonight's victim — they inspected a living person).
        if (rt.SeerPick >= 0 && rt.Roles.TryGetValue(rt.SeerPick, out Role seen))
        {
            int seerId = -1;
            foreach (var kv in rt.Roles) if (kv.Value == Role.Seer) { seerId = kv.Key; break; }
            if (seerId >= 0 && !rt.Ghosts.Contains(seerId))
                SendTo(seerId, "cf-ww-info", $"seer:{CleanName(NameOf(rt.SeerPick))}={(seen == Role.Wolf ? "wolf" : "clear")}");
        }

        // The kill. Victim validated at pick time; re-validate (they may have left/died since).
        if (rt.NightPick >= 0 && rt.Roles.ContainsKey(rt.NightPick) && !rt.Ghosts.Contains(rt.NightPick))
        {
            rt.LastVictimName = NameOf(rt.NightPick);
            Execute(rt, rt.NightPick);
        }

        rt.Phase = Phase.Dawn;
        rt.PhaseEndsAt = _now + DawnSeconds;
        string victim = rt.LastVictimName == null ? null : CleanName(rt.LastVictimName);
        SendToParticipants(rt, "cf-ww-phase", victim == null ? "dawn:" : $"dawn:{victim}");
        Broadcast(rt, victim == null
            ? "The village wakes — a quiet night, nobody died."
            : $"The village wakes — {victim} was found dead!");
        CheckWin(rt);
    }

    private static void BeginDay(MatchRuntime rt)
    {
        rt.Phase = Phase.Day;
        rt.PhaseEndsAt = _now + rt.Def.DaySeconds;
        SendToParticipants(rt, "cf-ww-phase", $"day:{rt.Def.DaySeconds}");
        Broadcast(rt, $"Day breaks — discuss! The vote opens in {rt.Def.DaySeconds}s.");
    }

    private static void BeginVote(MatchRuntime rt)
    {
        rt.Phase = Phase.Vote;
        rt.PhaseEndsAt = _now + rt.Def.VoteSeconds;
        rt.Votes.Clear();
        SendToParticipants(rt, "cf-ww-phase", $"vote:{rt.Def.VoteSeconds}");
        Broadcast(rt, $"The vote is open ({rt.Def.VoteSeconds}s) — a majority of votes cast hangs someone.");
        string all = BuildList(rt, exclude: null, excludePid: -1);
        foreach (var kv in rt.Roles)
            if (!rt.Ghosts.Contains(kv.Key) && all.Length > 0) SendTo(kv.Key, "cf-ww-window", "vote:" + all);
    }

    private static void ResolveVote(MatchRuntime rt)
    {
        foreach (var kv in rt.Roles)
            if (!rt.Ghosts.Contains(kv.Key)) SendTo(kv.Key, "cf-ww-window", "close");

        // Majority of votes CAST (skip counts as cast): strictly more than half. Tie / skip-majority = nobody.
        var tally = new Dictionary<int, int>();
        int cast = 0;
        foreach (var kv in rt.Votes)
        {
            if (rt.Ghosts.Contains(kv.Key) || !rt.Roles.ContainsKey(kv.Key)) continue;       // ghost votes never land
            if (kv.Value != SkipVote && (!rt.Roles.ContainsKey(kv.Value) || rt.Ghosts.Contains(kv.Value))) continue;
            cast++;
            if (kv.Value == SkipVote) continue;
            tally.TryGetValue(kv.Value, out int n);
            tally[kv.Value] = n + 1;
        }
        int target = -1, best = 0;
        foreach (var kv in tally)
            if (kv.Value > best) { best = kv.Value; target = kv.Key; }

        if (target >= 0 && best * 2 > cast)
        {
            string name = CleanName(NameOf(target));
            string role = rt.Roles[target] == Role.Wolf ? "wolf" : rt.Roles[target] == Role.Seer ? "seer" : "villager";
            Broadcast(rt, $"The village hanged {name} ({best}/{cast} votes) — they were a {role.ToUpperInvariant()}!");
            SendToParticipants(rt, "cf-ww-phase", $"lynch:{name}={role}");
            Execute(rt, target);
        }
        else
        {
            Broadcast(rt, "The village cannot agree — nobody hangs today.");
            SendToParticipants(rt, "cf-ww-phase", "nolynch");
        }
        if (!CheckWin(rt)) BeginNight(rt);
    }

    // ---- the execute primitive (server, no melee) ----

    // Replicates the vanilla "player-die-cheat" verbatim: a lethal TRUE-damage HitInfo applied server-side
    // with no owner. Downstream is all vanilla: the server bridge broadcasts the entity-deal-damage to every
    // client, CheckIfKilled -> PlayerEntityKilled -> OnPlayerDied broadcasts "player-died" (the victim's own
    // client runs LocalPlayerDied), the pvp world skips the grave, and 5s later RespawnPlayer fires — where
    // our postfix moves the new ghost into the pen.
    private static void Execute(MatchRuntime rt, int playerId)
    {
        rt.Ghosts.Add(playerId);                                   // out of every list from this instant
        rt.PendingKills[playerId] = (_now + ExecuteRetrySeconds, false);
        SendTo(playerId, "cf-ww-info", "dead");
        DoLethalDamage(playerId);
    }

    private static void DoLethalDamage(int playerId)
    {
        try
        {
            if (!PlayerServerService.JoinedPlayers.TryGetValue(playerId, out ServerJoinedPlayerModel p)
                || p == null || p.Dead) return;
            EntityWrapper entity = p.Entity;
            if (entity == null) return;
            SharedCombatHandler.ApplyDamage(entity, new HitInfo
            {
                Damage = new DamageTypesArray(entity.Health + 10f)
            }, null, null, null, DamageSourceType.Impact, null);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf execute failed for #{playerId}: {e.Message}"); }
    }

    // Phoenix feather (or any out-of-health save) can eat the execute: re-execute once, then give up loudly.
    private static void CheckPendingKills(MatchRuntime rt)
    {
        if (rt.PendingKills.Count == 0) return;
        List<int> done = null;
        List<int> retry = null;
        foreach (var kv in rt.PendingKills)
        {
            if (!PlayerServerService.JoinedPlayers.TryGetValue(kv.Key, out ServerJoinedPlayerModel p) || p == null
                || p.Dead || _now < kv.Value.RetryAt)
            {
                if (p == null || p.Dead) (done ??= new List<int>()).Add(kv.Key);
                continue;
            }
            if (!kv.Value.Retried) (retry ??= new List<int>()).Add(kv.Key);
            else
            {
                (done ??= new List<int>()).Add(kv.Key);
                Plugin.Log.LogWarning($"Werewolf: execute on #{kv.Key} failed twice (out-of-health save?) — giving up; they stay a ghost.");
            }
        }
        if (done != null) foreach (int pid in done) rt.PendingKills.Remove(pid);
        if (retry != null)
            foreach (int pid in retry)
            {
                rt.PendingKills[pid] = (_now + ExecuteRetrySeconds, true);
                Plugin.Log.LogInfo($"Werewolf: #{pid} survived the execute (phoenix feather?) — re-executing once.");
                DoLethalDamage(pid);
            }
    }

    // ---- mid-match population changes ----

    // A living participant who disconnected, left the map world, or died outside our script (mob/hazard in
    // the pvp world) counts as dead — no corpse to find, just gone. Ghost respawn redirect still applies if
    // they died but stayed; if they left, the pen no longer concerns them.
    private static void HandleLeavers(MatchRuntime rt)
    {
        List<int> gone = null;
        foreach (var kv in rt.Roles)
        {
            int pid = kv.Key;
            if (rt.Ghosts.Contains(pid)) continue;
            bool present = PlayerServerService.JoinedPlayers.TryGetValue(pid, out ServerJoinedPlayerModel p)
                && p?.CharacterModel?.Character != null
                && (p.Dead || p.CharacterModel.Character.WorldId == rt.WorldId);
            bool diedNaturally = present && p.Dead;
            if (present && !diedNaturally) continue;
            (gone ??= new List<int>()).Add(pid);
            Broadcast(rt, diedNaturally
                ? $"{CleanName(NameOf(pid))} has died — the village mourns."
                : $"{CleanName(NameOf(pid))} has abandoned the village (counts as dead).");
        }
        if (gone == null) return;
        foreach (int pid in gone) rt.Ghosts.Add(pid);
        CheckWin(rt);
    }

    // Late joiners spectate: ghost-set immediately (chat gated, never listed/targeted) + a one-time banner.
    private static void HandleLateJoiners(MatchRuntime rt)
    {
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            if ((p?.CharacterModel?.Character?.WorldId ?? Guid.Empty) != rt.WorldId) continue;
            if (rt.Roles.ContainsKey(p.Id) || !rt.ToldSpectate.Add(p.Id)) continue;
            rt.Ghosts.Add(p.Id);
            SendTo(p.Id, "cf-ww-info", "spectate");
        }
    }

    // ---- win / end ----

    private static int CountAlive(MatchRuntime rt)
    {
        int n = 0;
        foreach (var kv in rt.Roles) if (!rt.Ghosts.Contains(kv.Key)) n++;
        return n;
    }

    // True when the match ended. Wolves dead -> village; wolves >= alive non-wolves -> wolves overrun.
    private static bool CheckWin(MatchRuntime rt)
    {
        if (!rt.Running && rt.Phase != Phase.Starting) return true;
        int wolves = 0, others = 0;
        foreach (var kv in rt.Roles)
        {
            if (rt.Ghosts.Contains(kv.Key)) continue;
            if (kv.Value == Role.Wolf) wolves++; else others++;
        }
        if (wolves == 0) { EndMatch(rt, villageWins: true); return true; }
        if (wolves >= others) { EndMatch(rt, villageWins: false); return true; }
        return false;
    }

    private static void EndMatch(MatchRuntime rt, bool villageWins)
    {
        Broadcast(rt, villageWins
            ? $"All wolves are dead — the VILLAGE wins {rt.DisplayName}!"
            : $"The wolves overrun {rt.DisplayName} — the WEREWOLVES win!");
        SendToParticipants(rt, "cf-ww-phase", villageWins ? "win:village" : "win:wolves");
        foreach (var kv in rt.Roles) SendTo(kv.Key, "cf-ww-window", "close");

        // The whole winning SIDE gets the reward — dead or alive, they played their part (online only).
        if (!string.IsNullOrWhiteSpace(rt.Def.Reward))
            foreach (var kv in rt.Roles)
                if ((kv.Value == Role.Wolf) != villageWins) RewardPlayer(kv.Key, rt);

        Plugin.Log.LogInfo($"Event '{rt.Def.Id}': match ended — {(villageWins ? "village" : "wolves")} win. Cooling down.");
        Cleanup(rt);
        rt.Phase = Phase.Cooldown;
        rt.PhaseEndsAt = _now + CooldownSeconds;
    }

    private static void Abort(MatchRuntime rt)
    {
        if (rt.Phase != Phase.Idle && rt.Phase != Phase.Cooldown)
            Plugin.Log.LogInfo($"Event '{rt.Def.Id}': match aborted (players left / reload). Back to Idle.");
        Cleanup(rt);
        rt.Phase = Phase.Idle;
        rt.WorldId = Guid.Empty;
        rt.ArmWarned = false;
    }

    // Clear ghosts (respawns go home again), the shared no-friendly-fire team, and per-match state.
    private static void Cleanup(MatchRuntime rt)
    {
        foreach (var kv in rt.Roles) EventsCompetitiveFeature.Teams.Remove(kv.Key);
        rt.Roles.Clear();
        rt.Ghosts.Clear();
        rt.ToldSpectate.Clear();
        rt.Votes.Clear();
        rt.PendingKills.Clear();
        rt.NightPick = -1;
        rt.SeerPick = -1;
        rt.LastVictimName = null;
        rt.Round = 0;
    }

    // ---- client -> server action handlers (server bus; AuthorizationCheck means a joined peer) ----

    private static int SenderPlayerId(EventBusArgs args)
    {
        var info = PlayerServerService.GetPlayerInfoFromPeerId(args.Peer.Id);
        return info?.JoinedPlayer?.Id ?? -1;
    }

    private static void OnPick(object payload, EventBusArgs args)
    {
        try
        {
            if (payload is not string s || !int.TryParse(s, out int target)) return;
            int sender = SenderPlayerId(args);
            foreach (MatchRuntime rt in _events)
            {
                if (rt.Phase != Phase.Night || !rt.Roles.TryGetValue(sender, out Role role)) continue;
                if (role != Role.Wolf || rt.Ghosts.Contains(sender)) return;
                if (!rt.Roles.TryGetValue(target, out Role tr) || tr == Role.Wolf || rt.Ghosts.Contains(target)) return;
                rt.NightPick = target;   // pack consensus v1: the last wolf to pick decides
                return;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf pick failed: {e.Message}"); }
    }

    private static void OnSee(object payload, EventBusArgs args)
    {
        try
        {
            if (payload is not string s || !int.TryParse(s, out int target)) return;
            int sender = SenderPlayerId(args);
            foreach (MatchRuntime rt in _events)
            {
                if (rt.Phase != Phase.Night || !rt.Roles.TryGetValue(sender, out Role role)) continue;
                if (role != Role.Seer || rt.Ghosts.Contains(sender)) return;
                if (target == sender || !rt.Roles.ContainsKey(target) || rt.Ghosts.Contains(target)) return;
                rt.SeerPick = target;
                return;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf see failed: {e.Message}"); }
    }

    private static void OnVote(object payload, EventBusArgs args)
    {
        try
        {
            if (payload is not string s) return;
            int sender = SenderPlayerId(args);
            bool skip = string.Equals(s, "skip", StringComparison.OrdinalIgnoreCase);
            if (!skip && !int.TryParse(s, out int _)) return;
            int target = skip ? SkipVote : int.Parse(s);
            foreach (MatchRuntime rt in _events)
            {
                if (rt.Phase != Phase.Vote || !rt.Roles.ContainsKey(sender)) continue;
                if (rt.Ghosts.Contains(sender)) return;
                if (!skip && (!rt.Roles.ContainsKey(target) || rt.Ghosts.Contains(target))) return;
                rt.Votes[sender] = target;   // re-vote allowed until the window closes
                return;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf vote failed: {e.Message}"); }
    }

    // ---- consulted by the Harmony patches below ----

    // Ghost respawn redirect: pen position + map world for a registered ghost of a RUNNING match.
    internal static bool TryGetGhostPen(int playerId, out Vector3 pen, out Guid worldId)
    {
        pen = default;
        worldId = Guid.Empty;
        foreach (MatchRuntime rt in _events)
        {
            if (!rt.Running || !rt.Ghosts.Contains(playerId)) continue;
            pen = new Vector3(rt.Def.GhostPen[0], rt.Def.GhostPen[1], 0f);
            worldId = rt.WorldId;
            return true;
        }
        return false;
    }

    // Ghost chat gate: is this player a ghost of a running match, and who are their fellow ghosts?
    internal static bool TryGetGhostAudience(int playerId, out List<int> ghosts)
    {
        ghosts = null;
        foreach (MatchRuntime rt in _events)
        {
            if (!rt.Running || !rt.Ghosts.Contains(playerId)) continue;
            ghosts = new List<int>(rt.Ghosts);
            return true;
        }
        return false;
    }

    // ---- messaging helpers ----

    // One string to one player. Remote peers ride SendHostMessage (the personal-quest channel); the HOST
    // player is delivered locally (same process — the client pump picks it up on the UI thread next frame).
    private static void SendTo(int playerId, string key, string payload)
    {
        try
        {
            if (playerId == ServerGlobals.HostingPlayerId) { WerewolfClient.HandleLocal(key, payload); return; }
            if (PlayerServerService.JoinedPlayers.TryGetValue(playerId, out ServerJoinedPlayerModel p) && p != null)
                BaseServer.Instance.SendHostMessage(key, payload, p.Peer);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf send '{key}' to #{playerId} failed: {e.Message}"); }
    }

    // Participants = everyone with a role, ghosts included (they keep watching the story unfold).
    private static void SendToParticipants(MatchRuntime rt, string key, string payload)
    {
        foreach (var kv in rt.Roles) SendTo(kv.Key, key, payload);
    }

    // "Name=id,Name=id" of living participants; exclude one role (wolves don't eat wolves) and/or one player.
    private static string BuildList(MatchRuntime rt, Role? exclude, int excludePid)
    {
        var sb = new StringBuilder();
        foreach (var kv in rt.Roles)
        {
            if (rt.Ghosts.Contains(kv.Key) || kv.Key == excludePid) continue;
            if (exclude.HasValue && kv.Value == exclude.Value) continue;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(CleanName(NameOf(kv.Key))).Append('=').Append(kv.Key);
        }
        return sb.ToString();
    }

    // Player names ride inside our ','/'='/':' delimited payloads — strip the delimiters.
    private static string CleanName(string s)
        => string.IsNullOrEmpty(s) ? "?" : s.Replace(",", "").Replace("=", "").Replace(":", "");

    private static void RewardPlayer(int playerId, MatchRuntime rt)
    {
        try
        {
            if (!PlayerServerService.JoinedPlayers.TryGetValue(playerId, out ServerJoinedPlayerModel p)
                || p?.CharacterModel?.Character == null) return;   // disconnected — no reward
            SimpleInventoryServerManager.TryAddItemAmounts(p.CharacterModel.Character.CharacterInventoryModel.InventoryId,
                rt.Def.Reward, rt.Def.RewardAmount, null, allowPartialStack: true, null);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf reward failed: {e.Message}"); }
    }

    private static void Broadcast(MatchRuntime rt, string text)
    {
        try { ChatLogServerService.SendChatLogMessage(ChatLogMessage.DirectText(text, ChatMessageType.WorldEvent, "Werewolf")); }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf broadcast failed: {e.Message}"); }
    }

    // ---- world helpers (same shapes as Events.cs / EventsCompetitive.cs) ----

    private static Guid FindBestWorld(string mapId, int minPlayers)
    {
        Guid bestWorld = Guid.Empty;
        int bestCount = 0;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
        {
            Guid w = p?.CharacterModel?.Character?.WorldId ?? Guid.Empty;
            if (w == Guid.Empty || MapIdForWorld(w) != mapId) continue;
            int c = CountPlayersInWorld(w);
            if (c > bestCount) { bestCount = c; bestWorld = w; }
        }
        return bestCount >= minPlayers ? bestWorld : Guid.Empty;
    }

    private static List<int> PlayerIdsInWorld(Guid worldId)
    {
        var pids = new List<int>();
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            if ((p?.CharacterModel?.Character?.WorldId ?? Guid.Empty) == worldId) pids.Add(p.Id);
        pids.Sort();
        return pids;
    }

    private static int CountPlayersInWorld(Guid worldId)
    {
        int n = 0;
        foreach (ServerJoinedPlayerModel p in PlayerServerService.JoinedPlayers.Values)
            if ((p?.CharacterModel?.Character?.WorldId ?? Guid.Empty) == worldId) n++;
        return n;
    }

    private static string MapIdForWorld(Guid worldId)
    {
        if (worldId == Guid.Empty) return null;
        if (_worldMapCache.TryGetValue(worldId, out string cached)) return cached;
        string id = null;
        try { id = DungeonServerManager.GetDungeonRoomFromWorldId(worldId)?.DataId; }
        catch { }
        if (id != null)
        {
            if (_worldMapCache.Count > 512) _worldMapCache.Clear();
            _worldMapCache[worldId] = id;
        }
        return id;
    }

    private static string NameOf(int playerId)
    {
        if (PlayerServerService.JoinedPlayers.TryGetValue(playerId, out ServerJoinedPlayerModel p))
        {
            string n = p?.CharacterModel?.Character?.Name;
            if (!string.IsNullOrEmpty(n)) return n;
        }
        return $"Player #{playerId}";
    }
}

// (server) Ghost respawn redirect. Vanilla RespawnPlayer fully runs first (Dead=false, home spawn,
// "respawn-player" broadcast); the postfix then authoritatively moves a registered ghost of a running match
// into the map's spectator pen — MovePlayerCharacter handles the world switch and tells the player's client
// (the exact call the vanilla "player-teleport" path uses, cs2:41115).
internal static class WerewolfRespawnPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerServerManager), nameof(PlayerServerManager.RespawnPlayer))]
    private static void Postfix(ServerJoinedPlayerModel joinedPlayer)
    {
        try
        {
            if (joinedPlayer == null) return;
            if (!WerewolfFeature.TryGetGhostPen(joinedPlayer.Id, out Vector3 pen, out Guid worldId)) return;
            PlayerServerManager.MovePlayerCharacter(joinedPlayer, pen, worldId, sendToPlayer: true);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf ghost respawn redirect failed: {e.Message}"); }
    }
}

// (server) Ghost chat gate — dead players leak night information. A chat message from a ghost of a running
// match is swallowed (never broadcast) and re-sent as "[Ghost] name: ..." only to the other ghosts, peer by
// peer over the vanilla "chat-message" channel (the worldseed-command precedent; the host's peer is a normal
// loopback connection). Sender identity is resolved the vanilla way — peer id -> joined player.
internal static class WerewolfGhostChatPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ChatLogServerService), nameof(ChatLogServerService.OnReceive_PlayerChatMessage))]
    private static bool Prefix(PlayerChatLogMessage msg, EventBusArgs args)
    {
        try
        {
            var info = PlayerServerService.GetPlayerInfoFromPeerId(args.Peer.Id);
            if (info == null) return true;
            int pid = info.Value.JoinedPlayer.Id;
            if (!WerewolfFeature.TryGetGhostAudience(pid, out List<int> ghosts)) return true;

            string text = msg?.Message?.Text ?? "";
            string name = info.Value.Character?.Character?.Name ?? $"Player #{pid}";
            ChatLogMessage relay = ChatLogMessage.DirectText($"[Ghost] {name}: {text}", ChatMessageType.Normal, "Ghosts");
            foreach (int ghost in ghosts)
                if (PlayerServerService.JoinedPlayers.TryGetValue(ghost, out ServerJoinedPlayerModel p) && p != null)
                    BaseServer.Instance.SendHostMessage("chat-message", relay, p.Peer);
            return false;   // the living hear nothing
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf ghost chat gate failed: {e.Message}"); return true; }
    }
}

// ---------------------------------------------------------------------------------------------------------
// CLIENT — message handling + the action window. Everything the server sends lands in a thread-safe inbox
// (network dispatch AND the host's direct local delivery from the server thread) and is handled on the UI
// thread by the per-frame pump (a second postfix on UiNotificationSystem.Update — the same chokepoint the
// banner system rides; multiple Harmony postfixes coexist). Banners are localized en + tr_TR (TwoHanded
// convention); chat lines from the server stay English like the rest of the mod's server announcements.
// ---------------------------------------------------------------------------------------------------------

internal static class WerewolfClient
{
    private static readonly ConcurrentQueue<(string Key, string Payload)> _inbox = new();
    private static bool _subscribed;

    private static bool Tr
    {
        get { try { return Globals.LanguageCode == "tr_TR"; } catch { return false; } }
    }

    // Subscribe our keys on the client's network bus (idempotent; called after every full-game-state load,
    // i.e. on join — before any match message can possibly arrive, since the server only targets players
    // standing in the event map's world).
    internal static void EnsureSubscribed()
    {
        if (_subscribed) return;
        EventBusManager bus = Globals.Game?.NetworkEventBusManager;
        if (bus == null) return;
        _subscribed = true;
        bus.Subscribe<object>("cf-ww-role", o => Enqueue("cf-ww-role", o));
        bus.Subscribe<object>("cf-ww-info", o => Enqueue("cf-ww-info", o));
        bus.Subscribe<object>("cf-ww-phase", o => Enqueue("cf-ww-phase", o));
        bus.Subscribe<object>("cf-ww-window", o => Enqueue("cf-ww-window", o));
        Plugin.Log.LogInfo("Werewolf client listeners registered (cf-ww-*).");
    }

    private static void Enqueue(string key, object payload)
    {
        if (payload is string s) _inbox.Enqueue((key, s));
    }

    // Host-side direct delivery (the server runtime calls this instead of a network self-send).
    internal static void HandleLocal(string key, string payload) => _inbox.Enqueue((key, payload));

    // Drained every frame on the UI thread (WerewolfClientPumpPatch).
    internal static void Pump()
    {
        while (_inbox.TryDequeue(out (string Key, string Payload) m))
        {
            try { Handle(m.Key, m.Payload); }
            catch (Exception e) { Plugin.Log.LogWarning($"Werewolf client message '{m.Key}' failed: {e.Message}"); }
        }
    }

    private static void Handle(string key, string payload)
    {
        bool tr = Tr;
        string title = tr ? "Kurt Adam" : "Werewolf";
        switch (key)
        {
            case "cf-ww-role":
            {
                // "wolf" | "wolf:Anna,Marcus" | "seer" | "villager"
                if (payload.StartsWith("wolf", StringComparison.Ordinal))
                {
                    int colon = payload.IndexOf(':');
                    string mates = colon >= 0 ? payload.Substring(colon + 1) : null;
                    string msg = tr ? "Sen KURT ADAMSIN — geceleri kurbanını seç!" : "You are the WEREWOLF — pick your victims at night!";
                    if (!string.IsNullOrEmpty(mates))
                        msg += tr ? $" Sürün: {mates}" : $" Your pack: {mates}";
                    BannerSystem.Announce(title, msg);
                }
                else if (payload == "seer")
                    BannerSystem.Announce(title, tr ? "Sen KÂHİNSİN — her gece birini incele." : "You are the SEER — inspect someone each night.");
                else
                    BannerSystem.Announce(title, tr ? "Bir KÖYLÜSÜN — kurtları bul ve astır." : "You are a VILLAGER — find the wolves and hang them.");
                return;
            }

            case "cf-ww-info":
            {
                if (payload == "dead")
                    BannerSystem.Announce(title, tr ? "Öldün — seyirci ağılından izleyebilirsin." : "You are dead — you may haunt the spectator pen.");
                else if (payload == "spectate")
                    BannerSystem.Announce(title, tr ? "Kurt Adam oyunu sürüyor — seyircisin." : "A Werewolf match is in progress — you are a spectator.");
                else if (payload.StartsWith("seer:", StringComparison.Ordinal))
                {
                    // "seer:<Name>=wolf|clear"
                    string body = payload.Substring(5);
                    int eq = body.LastIndexOf('=');
                    if (eq <= 0) return;
                    string name = body.Substring(0, eq);
                    bool wolf = body.Substring(eq + 1) == "wolf";
                    BannerSystem.Announce(title, wolf
                        ? (tr ? $"{name} bir KURT ADAM!" : $"{name} is a WEREWOLF!")
                        : (tr ? $"{name} kurt değil." : $"{name} is NOT a wolf."));
                }
                else BannerSystem.Announce(title, payload);   // forward-compatible free text
                return;
            }

            case "cf-ww-phase":
            {
                int colon = payload.IndexOf(':');
                string kind = colon >= 0 ? payload.Substring(0, colon) : payload;
                string arg = colon >= 0 ? payload.Substring(colon + 1) : "";
                switch (kind)
                {
                    case "start":
                        BannerSystem.Announce(title, tr ? "Oyun başlıyor — roller dağıtılıyor..." : "The game begins — dealing roles...");
                        return;
                    case "night":
                        BannerSystem.Announce(title, tr ? $"Gece çöküyor ({arg}sn)..." : $"Night falls ({arg}s)...");
                        return;
                    case "dawn":
                        BannerSystem.Announce(title, string.IsNullOrEmpty(arg)
                            ? (tr ? "Köy uyanıyor — sakin bir gece." : "The village wakes — a quiet night.")
                            : (tr ? $"Köy uyanıyor — {arg} ölü bulundu!" : $"The village wakes — {arg} was found dead!"));
                        return;
                    case "day":
                        BannerSystem.Announce(title, tr ? "Gün doğdu — tartışın!" : "Day breaks — discuss!");
                        return;
                    case "vote":
                        BannerSystem.Announce(title, tr ? $"Oylama açık ({arg}sn)!" : $"The vote is open ({arg}s)!");
                        return;
                    case "lynch":
                    {
                        // "lynch:<Name>=<wolf|villager|seer>"
                        int eq = arg.LastIndexOf('=');
                        if (eq <= 0) return;
                        string name = arg.Substring(0, eq);
                        string role = arg.Substring(eq + 1);
                        string roleText = role == "wolf" ? (tr ? "KURT ADAM" : "a WEREWOLF")
                            : role == "seer" ? (tr ? "KÂHİN" : "the SEER") : (tr ? "KÖYLÜ" : "a VILLAGER");
                        BannerSystem.Announce(title, tr ? $"{name} asıldı — {roleText} çıktı!" : $"{name} was hanged — they were {roleText}!");
                        return;
                    }
                    case "nolynch":
                        BannerSystem.Announce(title, tr ? "Kimse asılmadı." : "Nobody was hanged.");
                        return;
                    case "win":
                        BannerSystem.Announce(title, arg == "village"
                            ? (tr ? "KÖY kazandı!" : "The VILLAGE wins!")
                            : (tr ? "KURT ADAMLAR kazandı!" : "The WEREWOLVES win!"));
                        return;
                }
                return;
            }

            case "cf-ww-window":
            {
                if (payload == "close") { WerewolfWindow.CloseOpen(); return; }
                int colon = payload.IndexOf(':');
                if (colon <= 0) return;
                string mode = payload.Substring(0, colon);
                if (mode != "pick" && mode != "see" && mode != "vote") return;
                var entries = new List<(string Name, string Id)>();
                foreach (string part in payload.Substring(colon + 1).Split(','))
                {
                    int eq = part.LastIndexOf('=');
                    if (eq <= 0) continue;
                    entries.Add((part.Substring(0, eq), part.Substring(eq + 1)));
                }
                if (entries.Count > 0) WerewolfWindow.Open(mode, entries);
                return;
            }
        }
    }
}

// The night-action / vote window: a SMALL top-left CandideWindow (standard graphics) with a title and one
// text button per candidate (+ Skip in vote mode). Shown on the live game desktop — NO CloseAllNonOverlay,
// NO input blockers, NO single-player pause: players keep walking around while they decide. A click sends
// one string on the matching channel and closes; "close" from the server (phase end) also closes it.
internal sealed class WerewolfWindow : CandideWindow
{
    private static WerewolfWindow _open;
    private readonly string _sendKey;

    internal static void Open(string mode, List<(string Name, string Id)> entries)
    {
        try
        {
            CloseOpen();
            var desktop = Globals.Game?.ModeManager?.CandideDesktop;
            if (desktop == null) return;
            var w = new WerewolfWindow(mode, entries);
            _open = w;
            w.Visible = true;
            w.Show(desktop);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf window open failed: {e.Message}"); }
    }

    internal static void CloseOpen()
    {
        try { _open?.Close(); } catch { }
        _open = null;
    }

    private WerewolfWindow(string mode, List<(string Name, string Id)> entries) : base(WindowGraphics.Standard)
    {
        bool tr = false;
        try { tr = Globals.LanguageCode == "tr_TR"; } catch { }
        _sendKey = mode == "vote" ? "cf-ww-vote" : mode == "see" ? "cf-ww-see" : "cf-ww-pick";
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new CandideThickness(16, 96, 0, 0);

        var stack = new CandideVerticalStackPanel();
        stack.AddChild(new CandideTextLabel
        {
            Text = mode == "pick" ? (tr ? "Kurbanını seç..." : "Choose your victim...")
                 : mode == "see" ? (tr ? "Birini incele..." : "Inspect a villager...")
                 : (tr ? "Kim asılsın?" : "Vote to hang..."),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        foreach ((string name, string id) in entries)
        {
            string targetId = id;   // capture per button
            stack.AddChild(new CandideLabelButton(120)
            {
                Text = name,
                Margin = new CandideThickness(0, 4, 0, 0),
                OnClickAction = () => Choose(targetId),
            });
        }
        if (mode == "vote")
            stack.AddChild(new CandideLabelButton(120)
            {
                Text = tr ? "Pas" : "Skip",
                Margin = new CandideThickness(0, 8, 0, 0),
                OnClickAction = () => Choose("skip"),
            });
        SetChild(stack);
    }

    private void Choose(string idOrSkip)
    {
        try { NetworkManager.SendMessage(_sendKey, idOrSkip, 2); }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf action send failed: {e.Message}"); }
        CloseOpen();
    }

    protected override void OnWindowClosed()
    {
        if (_open == this) _open = null;
        base.OnWindowClosed();
    }
}

// CLIENT: subscribe the cf-ww-* keys once the join completed (the same chokepoint the handshake report
// uses — multiple postfixes on one method coexist in Harmony).
internal static class WerewolfClientHookPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameStateManager), nameof(GameStateManager.ReceiveFullGameState))]
    private static void Postfix()
    {
        try { WerewolfClient.EnsureSubscribed(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Werewolf client subscribe failed: {e.Message}"); }
    }
}

// CLIENT: drain the message inbox on the UI thread every frame (the banner system's own pump chokepoint).
internal static class WerewolfClientPumpPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UiNotificationSystem), nameof(UiNotificationSystem.Update))]
    private static void Postfix()
    {
        try { WerewolfClient.Pump(); } catch { }
    }
}
