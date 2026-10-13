#if !CF_SERVER
using Candide.GameModels.Managers;     // client assembly (GameStateManager) — not in the CF_SERVER build
using Candide.Multiplayer.Network;
#endif
using CandideServer;
using CandideServer.EventBus;
using CandideServer.MessageModels;
using CandideServer.Models;
using CandideServer.Server;
using CandideServer.ServerManagers;
using CandideServer.ServerServices;
using HarmonyLib;
using Shared.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RomesteadContentForge;

// Co-op mod/pack presence handshake. When a client finishes joining (full game state received), it sends
// the host its mod version + loaded pack list over the game's own network event bus: the server's
// EventBusManager is public and string-keyed, so we register a brand-new key ("cf-handshake") with
// BaseServer.Instance.NetworkEventBusManager.Subscribe and the client ships a plain string payload through
// NetworkManager.SendMessage — strings round-trip the CandideSerializer natively (vanilla's
// "reward-unlock-cheat" does exactly this), so no custom message model has to be registered.
//
// Host-side bookkeeping is keyed by ServerJoinedPlayerModel.Id (the int peer/player id — what
// PlayerServerManager.TryGetPlayerFromEntity hands back, so PvP code can gate on an EntityWrapper directly).
// A vanilla client simply never sends a report: "no report yet" = not modded, and a report arriving at any
// later moment flips them — no timers. Mismatches are logged and announced in chat (once per join).
internal static class ModHandshake
{
    private const string EventKey = "cf-handshake";
    private const string Prefix = "cf|"; // payload: "cf|<mod version>|<packId@version>;<packId@version>;..."

    private sealed class PeerReport
    {
        public string ModVersion;
        public readonly Dictionary<string, string> Packs = new(); // pack id -> version
    }

    // Players who sent a report this session (player id -> what they have). Reset per player on (re)login.
    private static readonly Dictionary<int, PeerReport> _reports = new();
    // Players already chat-announced as vanilla, so the warning fires once per join, not per PvP check.
    private static readonly HashSet<int> _announcedVanilla = new();
    private static bool _subscribed;

    // ---------- public API (host-side; consumed by PvP gating etc.) ----------

    // True when the player runs Content Forge AND has every pack the host has loaded (a different VERSION
    // of a pack still counts — it was warned about, but the content exists on their side).
    public static bool PeerHasPacks(int playerId) => IsFullyModded(playerId);

    public static bool IsFullyModded(int playerId)
    {
        try
        {
            if (playerId == ServerGlobals.HostingPlayerId) return true; // the host is us — trivially modded
            if (!_reports.TryGetValue(playerId, out PeerReport rep)) { AnnounceVanillaOnce(playerId); return false; }
            foreach (var kv in HostPacks())
                if (!rep.Packs.ContainsKey(kv.Key)) return false;
            return true;
        }
        catch (Exception e) { WarnOnce("query", $"Handshake query failed: {e.Message}"); return false; }
    }

    // Convenience for callers holding a player ENTITY (the usual case in combat code).
    public static bool IsFullyModded(EntityWrapper playerEntity)
        => PlayerServerManager.TryGetPlayerFromEntity(playerEntity, out ServerJoinedPlayerModel p) && IsFullyModded(p.Id);

    // Snapshot of every currently-joined player id that passes IsFullyModded.
    public static List<int> FullyModdedPlayers()
    {
        try { return PlayerServerService.JoinedPlayers.Keys.Where(IsFullyModded).ToList(); }
        catch { return new List<int>(); }
    }

    // ---------- client side ----------

    // Send our pack manifest to the host. Runs after every full-game-state load (i.e. on join), including
    // self-hosted/single-player — there the report just lands on our own server, which is harmless; on a
    // vanilla host the server logs "nothing subscribed" and drops it.
    public static void Report()
    {
        ContentLoader.EnsureParsed(Plugin.PacksDir);
        var sb = new StringBuilder(Prefix).Append(MyPluginInfo.PLUGIN_VERSION).Append('|');
        bool first = true;
        foreach (var p in ContentLoader.LoadedPacks)
        {
            if (!first) sb.Append(';');
            sb.Append(Clean(p.Id)).Append('@').Append(Clean(p.Version));
            first = false;
        }
        RomesteadShared.Net.SendMessage(EventKey, sb.ToString(), 2);
        Plugin.Log.LogInfo($"Handshake sent to host ({ContentLoader.LoadedPacks.Count} pack(s)).");
    }

    // Pack ids/versions are author-controlled strings; strip our delimiters so they can't corrupt the payload.
    private static string Clean(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace("|", "").Replace(";", "").Replace("@", "");

    // ---------- host side ----------

    // Register our custom event-bus key (idempotent). Called from the login patch, so it always happens
    // before any client's report can arrive (the client only reports after receiving the login response).
    internal static void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        // Subscribe<object> so a bad payload can't throw an InvalidCastException inside the bus dispatcher.
        BaseServer.Instance.NetworkEventBusManager.Subscribe<object>(EventKey, OnReport);
        Plugin.Log.LogInfo($"Handshake listener registered ('{EventKey}').");
    }

    // A player (re)logged in: forget any previous session's state for that id (peer ids can be reused), and
    // take the chance to announce earlier joiners who still haven't reported (i.e. vanilla clients).
    internal static void OnPlayerLogin(ServerJoinedPlayerModel joinedPlayer)
    {
        if (joinedPlayer == null) return;
        _reports.Remove(joinedPlayer.Id);
        _announcedVanilla.Remove(joinedPlayer.Id);
        foreach (int id in PlayerServerService.JoinedPlayers.Keys.ToList())
            if (id != joinedPlayer.Id && id != ServerGlobals.HostingPlayerId && !_reports.ContainsKey(id))
                AnnounceVanillaOnce(id);
    }

    private static void OnReport(object payload, EventBusArgs args)
    {
        try
        {
            if (payload is not string s || !s.StartsWith(Prefix)) return;
            // The network PEER id is not the player id (a dedicated server numbers peers from 0 but players
            // from 1) — resolve the joined player that owns this peer. Fallback keeps the old behavior.
            int playerId = PlayerServerService.GetPlayerInfoFromPeerId(args.Peer.Id)?.Item2?.Id ?? args.Peer.Id;
            string[] parts = s.Split('|', 3);
            var rep = new PeerReport { ModVersion = parts.Length > 1 ? parts[1] : "?" };
            if (parts.Length > 2 && parts[2].Length > 0)
                foreach (string entry in parts[2].Split(';'))
                {
                    int at = entry.LastIndexOf('@');
                    if (at > 0) rep.Packs[entry.Substring(0, at)] = entry.Substring(at + 1);
                    else if (entry.Length > 0) rep.Packs[entry] = "";
                }
            _reports[playerId] = rep;
            _announcedVanilla.Remove(playerId);

            string name = NameOf(playerId);
            Dictionary<string, string> host = HostPacks();
            var missing = host.Keys.Where(id => !rep.Packs.ContainsKey(id)).ToList();
            var verDiff = host.Where(kv => rep.Packs.TryGetValue(kv.Key, out string v) && v != kv.Value)
                              .Select(kv => $"{kv.Key} (host {kv.Value}, them {rep.Packs[kv.Key]})").ToList();
            var extra = rep.Packs.Keys.Where(id => !host.ContainsKey(id)).ToList();

            Plugin.Log.LogInfo($"Handshake from {name} (#{playerId}): Content Forge {rep.ModVersion}, " +
                $"{rep.Packs.Count} pack(s); missing {missing.Count}, version-mismatched {verDiff.Count}, extra {extra.Count}.");
            if (rep.ModVersion != MyPluginInfo.PLUGIN_VERSION)
                Plugin.Log.LogWarning($"{name} runs Content Forge {rep.ModVersion} (host has {MyPluginInfo.PLUGIN_VERSION}).");
            if (extra.Count > 0)
                Plugin.Log.LogWarning($"{name} has packs the host does not: {string.Join(", ", extra)}.");

            if (missing.Count > 0)
                Announce($"{name} is missing packs: {string.Join(", ", missing)} — that content is invisible to them.");
            if (verDiff.Count > 0)
                Announce($"{name} has mismatched pack versions: {string.Join(", ", verDiff)}.");
        }
        catch (Exception e) { WarnOnce("receive", $"Handshake receive failed: {e.Message}"); }
    }

    // First time an actually-joined player is found report-less (a later login, or a PvP check), tell the
    // host's chat once. A report arriving afterwards clears the flag path via _reports.
    private static void AnnounceVanillaOnce(int playerId)
    {
        try
        {
            if (_announcedVanilla.Contains(playerId)) return;
            if (!PlayerServerService.JoinedPlayers.ContainsKey(playerId)) return; // unknown/gone — stay quiet
            _announcedVanilla.Add(playerId);
            string name = NameOf(playerId);
            Plugin.Log.LogInfo($"{name} (#{playerId}) sent no handshake — treating them as unmodded.");
            Announce($"{name} has no Content Forge — custom content is invisible to them.");
        }
        catch (Exception e) { WarnOnce("vanilla", $"Handshake vanilla announce failed: {e.Message}"); }
    }

    private static void Announce(string text)
    {
        try { ChatLogServerService.SendChatLogMessage(ChatLogMessage.DirectText(text, ChatMessageType.WorldEvent, "Content Forge")); }
        catch (Exception e) { WarnOnce("chat", $"Handshake chat announce failed: {e.Message}"); }
    }

    private static string NameOf(int playerId)
    {
        try
        {
            if (PlayerServerService.JoinedPlayers.TryGetValue(playerId, out ServerJoinedPlayerModel p))
            {
                string n = p.CharacterModel?.Character?.Name;
                if (!string.IsNullOrEmpty(n)) return n;
            }
        }
        catch { }
        return $"Player #{playerId}";
    }

    // The host's own manifest (pack id -> version), parsed lazily.
    private static Dictionary<string, string> HostPacks()
    {
        ContentLoader.EnsureParsed(Plugin.PacksDir);
        var d = new Dictionary<string, string>();
        foreach (var p in ContentLoader.LoadedPacks) d[p.Id] = p.Version ?? "";
        return d;
    }

    private static readonly HashSet<string> _warned = new();
    private static void WarnOnce(string key, string message)
    {
        if (_warned.Add(key)) Plugin.Log.LogWarning(message);
    }
}

// HOST: hook every player login (the same chokepoint PlaceableRecipePatch uses). Registers the handshake
// listener (always before that player's report can arrive — they only report after the login response) and
// resets/sweeps the per-player handshake state.
internal static class HandshakeServerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ServerGameStateService), nameof(ServerGameStateService.GetFullGameStateForLogin))]
    private static void Postfix(ServerJoinedPlayerModel joinedPlayerModel)
    {
        try
        {
            ModHandshake.EnsureSubscribed();
            ModHandshake.OnPlayerLogin(joinedPlayerModel);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"Handshake login hook failed: {e.Message}"); }
    }
}

#if !CF_SERVER
// CLIENT: the moment we've received the host's full game state (i.e. the join completed), send our report.
internal static class HandshakeClientPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameStateManager), nameof(GameStateManager.ReceiveFullGameState))]
    private static void Postfix()
    {
        try { ModHandshake.Report(); }
        catch (Exception e) { Plugin.Log.LogWarning($"Handshake report failed: {e.Message}"); }
    }
}
#endif
