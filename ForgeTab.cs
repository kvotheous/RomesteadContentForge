using Candide;                              // Globals (LanguageCode)
using Candide.CandideUI;                    // CandideThickness, CandideUiElement
using Candide.CandideUI.Components;         // CandideTextLabel, CandideHorizontalSeparator
using Candide.CandideUI.Components.Buttons; // CandideToggleIconLabelButton
using Candide.CandideUI.Containers;         // CandideGrid, CandidePanel, CandideVerticalStackPanel, CandideVerticalScrollViewer
using Candide.CandideUI.Helpers;            // TextToUiElementsHelper (favour effect text → rich UI)
using Candide.CandideUI.SkillsAndFavourUi;  // SkillsAndFavourWindow
using Candide.Combat;                       // ClientSpellManagerBridge
using Candide.GameModels;                   // GameState
using Candide.Graphics;                     // StyleHelper
using Candide.LegacyUI;                     // HorizontalAlignment
using CandideCreator.Shared.Graphics;       // IconFlag
using HarmonyLib;
using Shared.Data;                          // FavourDataBase
using Shared.Models.Auras;                  // ApplyAuraContext
using Shared.Models.Player;                 // PlayerCharacterModel
using Shared.Text;                          // StringDefinitions, GetTranslation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// "Forge" tab in the Skills & Favours window — a READ-ONLY info panel for Content Forge (v1).
//
// The vanilla window (Candide.CandideUI.SkillsAndFavourUi.SkillsAndFavourWindow) builds, in its constructor:
//   - _toggleButtonsGrid : CandideGrid with two CandideToggleIconLabelButton's (Skills @ GridColumn 0,
//     Favours @ GridColumn 1). The grid auto-sizes columns, so a third button at GridColumn 2 just works.
//   - _contentView : CandidePanel that hosts the active view. The Skills button's OnToggledOnAction calls the
//     private SetContent(Skills), which ClearChildren()'s _contentView and re-adds _skillsUserControl (also
//     force-untoggling the other button). The Favours button does NOT swap content — it closes everything and
//     opens the fullscreen FavoursWindow (PlayerUi.ShowFavoursWindow).
//
// Our integration (ctor postfix):
//   - add a third toggle button ("anvil" icon) to _toggleButtonsGrid at GridColumn 2;
//   - on toggle-on: silently untoggle the two vanilla buttons (SetToggleSilent — no actions fire), reset the
//     private _currentContent guard to Undefined so the NEXT Skills click always rebuilds (SetContent no-ops
//     when _currentContent == type), then swap our panel into _contentView;
//   - subscribe to the vanilla buttons' public ToggledOn events so clicking Skills/Favours silently untoggles
//     the Forge button (Skills' own SetContent clears our panel out of _contentView; Favours closes the
//     window entirely).
//
// Panel content: installed packs (ContentLoader.LoadedPacks), the character's pack skills with levels
// (ContentLoader.CustomSkillIds ∩ Skills.CharacterSkills), learned pack favours with their effect text
// (FavoursFeature.RegisteredIds ∩ Skills.Favours, described exactly like the vanilla favour tooltip), and a
// footer. Pure client-side UI — no actions, no save/netcode impact; everything is exception-guarded so a
// failure can never break the vanilla window (vanilla controls are only touched after they all resolved).
// ---------------------------------------------------------------------------------------------------------

[HarmonyPatch(typeof(SkillsAndFavourWindow), MethodType.Constructor,
    new[] { typeof(PlayerCharacterModel), typeof(bool) })]
internal static class ForgeTabPatch
{
    [HarmonyPostfix]
    private static void Postfix(SkillsAndFavourWindow __instance, PlayerCharacterModel playerModel)
    {
        try { ForgeTab.Attach(__instance, playerModel); }
        catch (Exception e) { ForgeTab.WarnOnce($"Forge tab attach failed (vanilla window unaffected): {e}"); }
    }
}

internal static class ForgeTab
{
    // The window's private layout members (decompile-verified names). Resolved once; if the game ever renames
    // them the tab silently doesn't appear (one warning) and the vanilla window keeps working untouched.
    private static readonly FieldInfo FToggleGrid =
        AccessTools.Field(typeof(SkillsAndFavourWindow), "_toggleButtonsGrid");
    private static readonly FieldInfo FContentView =
        AccessTools.Field(typeof(SkillsAndFavourWindow), "_contentView");
    private static readonly FieldInfo FSkillsBtn =
        AccessTools.Field(typeof(SkillsAndFavourWindow), "_skillsToggleButton");
    private static readonly FieldInfo FFavoursBtn =
        AccessTools.Field(typeof(SkillsAndFavourWindow), "_favoursToggleButton");
    private static readonly FieldInfo FCurrentContent =
        AccessTools.Field(typeof(SkillsAndFavourWindow), "_currentContent");

    private static bool _warned;
    internal static void WarnOnce(string msg)
    {
        if (_warned) return;
        _warned = true;
        Plugin.Log.LogWarning(msg);
    }

    internal static void Attach(SkillsAndFavourWindow window, PlayerCharacterModel model)
    {
        var grid = FToggleGrid?.GetValue(window) as CandideGrid;
        var content = FContentView?.GetValue(window) as CandidePanel;
        var skillsBtn = FSkillsBtn?.GetValue(window) as CandideToggleIconLabelButton;
        var favoursBtn = FFavoursBtn?.GetValue(window) as CandideToggleIconLabelButton;
        if (grid == null || content == null || skillsBtn == null || favoursBtn == null)
        {
            WarnOnce("Forge tab: SkillsAndFavourWindow layout changed — tab not added.");
            return;
        }

        var forgeBtn = new CandideToggleIconLabelButton("anvil", IconFlag.Medium, 80)
        {
            Text = T("forge"),
            Padding = new CandideThickness(4, 3, 4, 5),
            GridColumn = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            LockWhenToggledOn = true,
        };
        forgeBtn.OnToggledOnAction = () => ShowForge(window, model, content, skillsBtn, favoursBtn);

        // Clicking a vanilla tab untoggles ours. Skills' SetContent already clears our panel out of
        // _contentView; the Favours button closes the whole window (it opens the fullscreen favours tree).
        skillsBtn.ToggledOn += (_, _) => forgeBtn.SetToggleSilent(false);
        favoursBtn.ToggledOn += (_, _) => forgeBtn.SetToggleSilent(false);

        grid.AddChild(forgeBtn);
    }

    private static void ShowForge(SkillsAndFavourWindow window, PlayerCharacterModel model,
        CandidePanel content, CandideToggleIconLabelButton skillsBtn, CandideToggleIconLabelButton favoursBtn)
    {
        try
        {
            // Silent: firing their toggle actions would swap content back / close the window.
            skillsBtn.SetToggleSilent(false);
            favoursBtn.SetToggleSilent(false);

            // SetContent(type) no-ops when its private _currentContent guard equals the requested type — reset
            // it to Undefined so the next Skills click always rebuilds the skills view over our panel.
            if (FCurrentContent != null)
                FCurrentContent.SetValue(window, Enum.ToObject(FCurrentContent.FieldType, 2)); // 2 = Undefined

            content.ClearChildren();
            content.AddChild(BuildPanel(model));
        }
        catch (Exception e) { WarnOnce($"Forge tab: panel build failed: {e}"); }
    }

    // ----------------------------------------------------------------------------------------------- panel

    private static CandideUiElement BuildPanel(PlayerCharacterModel model)
    {
        var stack = new CandideVerticalStackPanel { Width = 400 };

        // (a) installed packs
        Header(stack, T("hdr_packs"));
        if (ContentLoader.LoadedPacks.Count == 0) Line(stack, T("none"), dim: true);
        foreach (ContentLoader.LoadedPack p in ContentLoader.LoadedPacks)
            Line(stack, $"{p.Id} v{p.Version}"
                + (string.IsNullOrWhiteSpace(p.Author) ? "" : $" — {p.Author}"));

        // (b) the character's pack skills with levels
        Header(stack, T("hdr_skills"));
        int shown = 0;
        foreach (string id in ContentLoader.CustomSkillIds)
        {
            if (model?.Skills.CharacterSkills == null
                || !model.Skills.CharacterSkills.TryGetValue(id, out var cs) || cs == null) continue;
            Line(stack, $"{SkillName(id)} — {string.Format(T("level"), cs.Level)}");
            shown++;
        }
        if (shown == 0) Line(stack, T("none"), dim: true);

        // (c) learned pack favours + effect text (same description path as the vanilla favour tooltip)
        Header(stack, T("hdr_favours"));
        shown = 0;
        foreach (string id in FavoursFeature.RegisteredIds.OrderBy(s => s, StringComparer.Ordinal))
        {
            if (model?.Skills.Favours == null || !model.Skills.Favours.ContainsKey(id)) continue;
            if (!FavourDataBase.DataMap.TryGetValue(id, out var fd) || fd == null) continue;
            Line(stack, fd.Name.GetTranslation());
            AddFavourEffect(stack, fd);
            shown++;
        }
        if (shown == 0) Line(stack, T("none"), dim: true);

        // (d) footer
        stack.AddChild(new CandideHorizontalSeparator
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new CandideThickness(0, 10, 0, 4),
        });
        stack.AddChild(new CandideTextLabel
        {
            Text = string.Format(T("footer"), MyPluginInfo.PLUGIN_VERSION, ContentLoader.LoadedPacks.Count),
            TextColor = StyleHelper.ColorItemGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new CandideThickness(0, 0, 0, 4),
        });

        var scroll = new CandideVerticalScrollViewer
        {
            Width = 400,
            Height = 290,                     // window height is fixed (420) — scroll long lists
            ShowVerticalScrollBar = true,
        };
        scroll.SetChild(stack);
        return scroll;
    }

    private static void Header(CandideVerticalStackPanel stack, string text) =>
        stack.AddChild(new CandideTextLabel
        {
            Text = text.ToUpperInvariant(),
            TextColor = StyleHelper.TextYellowColor,
            Margin = new CandideThickness(0, 10, 0, 2),
        });

    private static void Line(CandideVerticalStackPanel stack, string text, bool dim = false)
    {
        var label = new CandideTextLabel
        {
            Text = text ?? "",
            Wrap = true,
            Width = 380,
            Margin = new CandideThickness(8, 1, 0, 1),
        };
        if (dim) label.TextColor = StyleHelper.ColorItemGray;
        stack.AddChild(label);
    }

    // The favour's effect, described exactly like the vanilla tooltip (stat-aura text, "unlocks recipe", or
    // its explicit description). Rendered through the game's own rich-text parser so $-tags resolve.
    private static void AddFavourEffect(CandideVerticalStackPanel stack, FavourData fd)
    {
        string desc;
        try
        {
            var sb = new StringBuilder();
            fd.GetDescription(sb, new ApplyAuraContext
            {
                SpellBridge = ClientSpellManagerBridge.Instance,
                OwnerEntityId = GameState.LocalPlayer?.EntityId,
            });
            desc = sb.ToString().Trim();
        }
        catch { desc = null; }
        if (string.IsNullOrWhiteSpace(desc)) return;

        try
        {
            CandideUiElement el = TextToUiElementsHelper.ParseString(desc);
            el.Width = 364;
            el.Margin = new CandideThickness(20, 0, 0, 3);
            stack.AddChild(el);
        }
        catch { Line(stack, desc, dim: true); }   // parser hiccup — show the raw text instead
    }

    private static string SkillName(string skillId)
    {
        try
        {
            string s = StringDefinitions.GetString(skillId + "*skills:name");
            return string.IsNullOrWhiteSpace(s) ? skillId : s;
        }
        catch { return skillId; }
    }

    // -------------------------------------------------------------------------------------- localization

    // Tiny self-contained table keyed off the game's language code (Globals.LanguageCode, e.g. "en",
    // "tr_TR"); unknown languages and missing keys fall back to English.
    private static string T(string key)
    {
        string lang = null;
        try { lang = Globals.LanguageCode; } catch { }
        if (lang != null && Strings.TryGetValue(lang, out var table) && table.TryGetValue(key, out var s))
            return s;
        return Strings["en"].TryGetValue(key, out var en) ? en : key;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new()
        {
            ["forge"] = "Forge",
            ["hdr_packs"] = "Installed packs",
            ["hdr_skills"] = "Pack skills",
            ["hdr_favours"] = "Learned pack favours",
            ["none"] = "(none)",
            ["level"] = "Lv {0}",
            ["footer"] = "Content Forge {0} — {1} pack(s) loaded",
        },
        ["tr_TR"] = new()
        {
            ["forge"] = "Demirhane",
            ["hdr_packs"] = "Yüklü paketler",
            ["hdr_skills"] = "Paket becerileri",
            ["hdr_favours"] = "Öğrenilen paket lütufları",
            ["none"] = "(yok)",
            ["level"] = "Sv {0}",
            ["footer"] = "Content Forge {0} — {1} paket yüklü",
        },
    };
}
