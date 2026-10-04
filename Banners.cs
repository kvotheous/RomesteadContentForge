using BepInEx.Configuration;           // ConfigFile, ConfigEntry
using Candide;                          // Globals
using Candide.CandideUI.UiInput;
using Candide.CandideUI;                // CandideDesktop, CandideThickness, VerticalAlignment, CandideUiSystem
using Candide.CandideUI.Components;     // CandideTextLabel, CandideTextStyle
using Candide.CandideUI.Containers;     // CandideWindow, WindowGraphics
using Candide.LegacyUI;                 // HorizontalAlignment
using Candide.UISystems.Notification;   // UiNotificationSystem (our per-frame hook host)
using HarmonyLib;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// Center-screen announcement banners — big red text near the top-center of the screen, the "event style"
// the vanilla toast (bottom-right NotificationWindow, anchor hardcoded) can't give us. Used for arena/event
// announcements and the two-handed eject messages instead of toasts (configurable).
//
// Rendering: same trick UiNotificationSystem itself uses — a private CandideDesktop drawn over the game.
// We piggyback its per-frame Update(GameTime)/Draw() statics (called from the game loop every frame) with
// postfixes, so the banner needs no loop hook of its own. Queue: one banner at a time, max 4 waiting.
//
// Config (BepInEx\config\RomesteadContentForge.cfg, [Notifications]):
//   Style          banner | toast        where mod announcements go (default banner)
//   BannerColor    "R,G,B"               text color (default 235,60,50)
//   BannerYPercent 0..80                 vertical position, % of screen height from the top (default 16)
//   BannerSeconds  1..15                 how long a banner stays (default 3.5; last 0.5s fades out)
// ---------------------------------------------------------------------------------------------------------

internal static class ForgeConfig
{
    internal static ConfigEntry<string> Style;
    internal static ConfigEntry<string> BannerColor;
    internal static ConfigEntry<int> BannerYPercent;
    internal static ConfigEntry<float> BannerSeconds;

    internal static bool UseBanner => !string.Equals(Style?.Value, "toast", StringComparison.OrdinalIgnoreCase);

    internal static void Bind(ConfigFile cfg)
    {
        Style = cfg.Bind("Notifications", "Style", "banner",
            "Where mod announcements (events, arena, two-handed ejects) appear: banner = big text top-center, toast = vanilla notification window (bottom-right).");
        BannerColor = cfg.Bind("Notifications", "BannerColor", "235,60,50", "Banner text color as R,G,B (0-255).");
        BannerYPercent = cfg.Bind("Notifications", "BannerYPercent", 16, "Banner vertical position: % of screen height from the top (0-80).");
        BannerSeconds = cfg.Bind("Notifications", "BannerSeconds", 3.5f, "Seconds a banner stays on screen (1-15; the last 0.5s fades out).");
    }

    internal static Color ParseBannerColor()
    {
        try
        {
            string[] p = (BannerColor?.Value ?? "").Split(',');
            if (p.Length >= 3 && byte.TryParse(p[0].Trim(), out byte r)
                && byte.TryParse(p[1].Trim(), out byte g) && byte.TryParse(p[2].Trim(), out byte b))
                return new Color(r, g, b);
        }
        catch { }
        return new Color(235, 60, 50);
    }
}

internal static class BannerSystem
{
    private const int MaxQueue = 4;
    private static readonly Queue<string> Pending = new();
    private static readonly CandideDesktop Desktop = new();
    private static CandideWindow _window;
    private static CandideTextLabel _label;
    private static float _timer;

    // Show `text` as a banner, or fall back to a vanilla toast when Style=toast.
    internal static void Announce(string title, string message)
    {
        try
        {
            if (ForgeConfig.UseBanner)
            {
                string text = string.IsNullOrWhiteSpace(message) ? title
                    : (string.IsNullOrWhiteSpace(title) ? message : $"{title} — {message}");
                lock (Pending) { if (Pending.Count < MaxQueue) Pending.Enqueue(text); }
            }
            else
                UiNotificationSystem.ShowNotification(title, message, null, "sam", null);
        }
        catch { }
    }

    internal static void Update(GameTime gameTime)
    {
        if (_window == null)
        {
            string next = null;
            lock (Pending) { if (Pending.Count > 0) next = Pending.Dequeue(); }
            if (next == null) return;
            ShowNow(next);
        }

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _timer -= dt;
        if (_timer <= 0f)
        {
            _window.Close();
            _window = null;
            _label = null;
            return;
        }
        if (_timer < 0.5f) _window.Opacity = _timer / 0.5f;   // fade out

        Desktop.DesktopWidth = Globals.Game.UiRenderTarget.Width;
        Desktop.DesktopHeight = Globals.Game.UiRenderTarget.Height;
        Desktop.Update(gameTime);
    }

    internal static void Draw()
    {
        if (_window != null) Desktop.Draw();
    }

    private static void ShowNow(string text)
    {
        float seconds = Math.Clamp(ForgeConfig.BannerSeconds?.Value ?? 3.5f, 1f, 15f);
        int pct = Math.Clamp(ForgeConfig.BannerYPercent?.Value ?? 16, 0, 80);
        int topMargin = (int)(Globals.Game.UiRenderTarget.Height * (pct / 100f) / Math.Max(1, CandideUiSystem.Scale));

        _label = new CandideTextLabel
        {
            Text = text,
            TextStyle = CandideTextStyle.TitleBold,
            TextColor = ForgeConfig.ParseBannerColor(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _window = new CandideWindow(WindowGraphics.None)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new CandideThickness(0, topMargin, 0, 0),
        };
        _window.SetChild(_label);
        _timer = seconds;
        Desktop.DesktopWidth = Globals.Game.UiRenderTarget.Width;
        Desktop.DesktopHeight = Globals.Game.UiRenderTarget.Height;
        _window.Visible = true;
        _window.Show(Desktop);
    }
}

// Drive the banner from the game loop: UiNotificationSystem.Update/Draw are statics the game calls every
// frame for its own toasts — the perfect chokepoint, no render-pipeline patch needed.
internal static class BannerPumpPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UiNotificationSystem), nameof(UiNotificationSystem.Update))]
    private static void UpdatePostfix(GameTime gameTime)
    {
        try { BannerSystem.Update(gameTime); } catch { }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UiNotificationSystem), nameof(UiNotificationSystem.Draw))]
    private static void DrawPostfix()
    {
        try { BannerSystem.Draw(); } catch { }
    }
}
