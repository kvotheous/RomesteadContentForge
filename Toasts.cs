using Candide.Multiplayer.Chat;        // ChatLogManager, ChatMessage
using Candide.UISystems.Notification;  // UiNotificationSystem
using HarmonyLib;
using System;

namespace RomesteadContentForge;

// Client-side top-screen toasts for the mod's event announcements. Event chat lines (arena matches,
// world events, handshake notes) land only in the chat log and are easy to miss — so we ALSO surface
// them as a UiNotificationSystem toast, the same banner vanilla uses for new quests / town reports.
//
// How: every incoming chat line — server broadcast ("chat-message" EventBus), player chat, and local
// lines — funnels through the client's private ChatLogManager.AddChatMessage(ChatMessage). We postfix
// it and, when the line's SenderName is one of OUR senders, queue a toast. Pure client-side: each
// modded client (host or co-op guest) toasts its own chat receive — no netcode, works in single-player.
// Vanilla clients without the mod simply see the chat line as before.
//
// Spam control: kill feeds in busy arenas can emit many lines per second, and the vanilla toast queue
// holds only 4 (overflow logs a warning per drop). We allow at most 1 toast per second and silently
// drop the rest — the chat log still has every line.
internal static class ChatToastPatch
{
    private const double MinSecondsBetweenToasts = 1.0;
    private static DateTime _lastToastUtc = DateTime.MinValue;
    private static bool _warned;   // log the first failure only — never spam per chat line

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ChatLogManager), "AddChatMessage")]
    private static void Postfix(ChatMessage msg)
    {
        try
        {
            string title = TitleForSender(msg.SenderName);
            if (title == null || string.IsNullOrWhiteSpace(msg.Message)) return;

            DateTime now = DateTime.UtcNow;
            if ((now - _lastToastUtc).TotalSeconds < MinSecondsBetweenToasts) return;  // drop, don't queue
            _lastToastUtc = now;

            // banner (top-center big text) or vanilla toast, per the [Notifications] Style config.
            BannerSystem.Announce(title, msg.Message);
        }
        catch (Exception e)
        {
            // A UI hiccup must never break chat. Warn once, then stay quiet.
            if (_warned) return;
            _warned = true;
            Plugin.Log.LogWarning($"Chat toast failed (toasts disabled-ish, chat unaffected): {e.Message}");
        }
    }

    // Exact sender strings used by our broadcasters, mapped to short toast titles:
    //   Events.cs        Broadcast(): sender "Arena"
    //   EventsPlus.cs    Broadcast(): sender "Content Forge Events"
    //   EventsCompetitive.cs   Broadcast(): sender "Content Forge Events"
    //   ModHandshake.cs  Broadcast(): sender "Content Forge"
    // All are sent as ChatMessageType.WorldEvent; matching the sender alone is sufficient (vanilla
    // never uses these names) and keeps working if a future sender uses another message type.
    private static string TitleForSender(string sender)
    {
        switch (sender)
        {
            case "Arena": return "Arena";
            case "Content Forge Events": return "Event";
            case "Content Forge": return "Content Forge";
            default: return null;
        }
    }
}
