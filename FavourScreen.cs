using Candide;                              // Globals
using Candide.CandideUI;                    // CandideThickness, CandideUiElement, CandideUiSystem, VerticalAlignment, DragDirection, CandideExtendedInteractionsHandler, PlayerUi
using Candide.CandideUI.Components;         // CandideIcon, CandideTextLabel, CandideHorizontalSeparator, DraggablePanel
using Candide.CandideUI.Components.Buttons; // CandideIconButton, CandideToggleIconLabelButton
using Candide.CandideUI.Components.Holder;  // CandideHolderBase
using Candide.CandideUI.Containers;         // CandideWindow, WindowGraphics, CandidePanel, CandideGrid
using Candide.CandideUI.Helpers;            // TextToUiElementsHelper
using Candide.CandideUI.SkillsAndFavourUi;  // SkillsAndFavourWindow, CandideLinesFavourPanel, NewFavoursOverheadInfoControl
using Candide.CandideUI.Windows;            // CandideModalWindow
using Candide.Combat;                       // ClientSpellManagerBridge
using Candide.GameModels;                   // GameState
using Candide.GameModels.Managers;          // FavourManager, SkillsManager, FavourChangedEventArgs
using Candide.GameModels.Services;          // FavourService
using Candide.Graphics;                     // StyleHelper
using Candide.Input;                        // PlayerControllerKeyBinds
using Candide.LegacyUI;                     // HorizontalAlignment
using Candide.Multiplayer.Network;          // GetResponseHandler
using Candide.PlayerMode;                   // StandardModeManager
using Candide.Sound;                        // SoundPlayer
using CandideCreator.Shared.Graphics;       // IconFlag
using CandideServer.MessageModels.Favour;   // GetUnlockFavourRequest/Response
using HarmonyLib;
using Microsoft.Xna.Framework;
using Shared.Data;                          // FavourDataBase, FavourData
using Shared.Models.Auras;                  // ApplyAuraContext
using Shared.Models.Player;                 // PlayerCharacterModel
using Shared.Models.Skill;                  // Skills
using Shared.Text;                          // StringDefinitions, GetTranslation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RomesteadContentForge;

// ---------------------------------------------------------------------------------------------------------
// The Forge favour screen — a COMPLETELY CUSTOM fullscreen favour tree for pack nodes with `"screen": true`.
// Mirrors the vanilla FavoursWindow exactly (black fullscreen, draggable node grid, gray connection lines,
// favour-point counter top-left, close button top-right) but lays out OUR nodes on OUR grid, untouched by
// the vanilla tree. Reached via a fourth toggle button in the Skills & Favours window (same pattern as the
// vanilla Favours button: it closes the window and opens the fullscreen tree).
//
// Screen nodes live in FavourDataBase like every other favour (so the server accepts the unlock, and the
// aura/recipe/flag + strip-on-save pipelines work unchanged) but are NOT in SharedDataSetup.FavourGrid, so
// the vanilla tree never shows them. That also means FavourManager.IsFavourAvailable/CanUnlockFavour can't
// see them (both walk the vanilla grid) — availability here is our own check: parent learned (or no parent).
// The server doesn't enforce tree shape for ANY favour (TryUnlockFavour checks only points/exists/not-known,
// cost hardcoded 1) — identical trust model to the vanilla tree.
//
// Unlock flow IS the vanilla flow: confirmation modal -> FavourService.Get_UnlockFavour ("unlock-favour-get")
// -> FavourManager.SynchUpdateFavoursAndCurrentFavourPoints on response (fires SkillsManager.FavourChanged,
// which refreshes this screen). Co-op: host must have the pack, like everything else.
// ---------------------------------------------------------------------------------------------------------

internal sealed class ForgeFavourScreen : CandideWindow
{
    private readonly PlayerCharacterModel _model;
    private readonly NewFavoursOverheadInfoControl _points = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly CandideGrid _grid = new();
    private readonly CandideLinesFavourPanel _lines = new();
    private readonly List<Node> _nodes = new();
    private readonly DraggablePanel _drag;
    private GetResponseHandler _pending;
    private CandideModalWindow _loading;

    public ForgeFavourScreen(PlayerCharacterModel model) : base(WindowGraphics.None)
    {
        _model = model;
        Background = Color.Black;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var root = new CandidePanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // the tree: lines UNDER the node grid, both centered, pannable (mirrors NewFavoursUserControl)
        var tree = new CandidePanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        BuildNodes();
        tree.AddChild(_lines);
        tree.AddChild(_grid);
        _drag = new DraggablePanel { DragDirection = DragDirection.Both };
        _drag.SetChild(tree);
        root.AddChild(_drag);
        SetChild(root);

        // favour-point counter (top-left mini window) — the vanilla control, fed the vanilla way
        var head = new CandideWindow(WindowGraphics.Standard) { Margin = new CandideThickness(16, 16, 0, 0) };
        _points.OnFavourChanged(_model.Skills);
        _points.OnSkillsChanged(_model.Skills);
        head.SetChild(_points);
        root.AddChild(head);
        head.Visible = true;

        root.AddChild(new CandideIconButton("ui:no", IconFlag.Large)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new CandideThickness(0, 16, 16, 0),
            OnClickAction = Close,
        });
    }

    private void BuildNodes()
    {
        var screenIds = new HashSet<string>(FavoursFeature.ScreenDefs.Select(d => d.Id));
        foreach (FavourDef def in FavoursFeature.ScreenDefs)
        {
            if (!FavourDataBase.TryGetFavour(def.Id, out FavourData fd)) continue;
            var n = new Node(fd, def, OnNodeClicked)
            {
                GridColumn = def.Position[0],
                GridRow = def.Position[1],
                Margin = new CandideThickness(16, 16),
            };
            _nodes.Add(n);
            _grid.AddChild(n);
        }

        // connection lines — exact vanilla endpoint math (NewFavoursUserControl.Setup)
        float k = 0.75f;
        float cell = 81.75f * CandideUiSystem.Scale * k;
        var center = new Vector2(37 * CandideUiSystem.Scale) * k;
        _lines.Lines.Clear();
        foreach (FavourDef def in FavoursFeature.ScreenDefs)
        {
            if (string.IsNullOrWhiteSpace(def.Parent) || !screenIds.Contains(def.Parent)) continue;
            FavourDef parent = FavoursFeature.ScreenDefs.First(d => d.Id == def.Parent);
            _lines.Lines.Add((center + new Vector2(parent.Position[0], parent.Position[1]) * cell,
                              center + new Vector2(def.Position[0], def.Position[1]) * cell));
        }
    }

    // Our availability rule (vanilla IsFavourAvailable walks the vanilla grid and can't see screen nodes):
    // root nodes are always reachable; child nodes need the parent learned.
    internal static bool IsReachable(FavourDef def, in Skills skills) =>
        string.IsNullOrWhiteSpace(def.Parent) || skills.Favours.ContainsKey(def.Parent);

    private void OnNodeClicked(Node n)
    {
        if (Desktop == null) return;
        Skills skills = _model.Skills;
        if (skills.Favours.ContainsKey(n.Favour.Id) || !IsReachable(n.Def, skills) || skills.CurrentFavourPoints < 1)
            return;
        string favourId = n.Favour.Id;
        SoundPlayer.PlayEventOneShot("event:/interface/navigate");
        CandideModalWindow.ShowConfirmationModal(Desktop,
            StringDefinitions.GetFormatString("Gods_BuyFavourConfirmation", n.Favour.Name.GetTranslation(), "1"),
            delegate
            {
                if (_model.Skills.Favours.ContainsKey(favourId) || _model.Skills.CurrentFavourPoints < 1) return;
                _loading = CandideModalWindow.ShowLoadingModal(Desktop, 2f);
                _pending?.Remove();
                int before = _model.Skills.CurrentFavourPoints;
                _pending = FavourService.Get_UnlockFavour(new GetUnlockFavourRequest { FavourId = favourId },
                    delegate (GetUnlockFavourResponse r)
                    {
                        _loading?.Close();
                        FavourManager.SynchUpdateFavoursAndCurrentFavourPoints(r.CharacterId, r.UpdatedFavours, r.NewCurrentFavourPoints);
                        Plugin.Log.LogInfo($"favour unlock '{favourId}': points {before} -> {r.NewCurrentFavourPoints} (server-confirmed).");
                    },
                    delegate { _loading?.Close(); }, 2f);
            });
    }

    protected override void OnWindowShown()
    {
        CandideExtendedInteractionsHandler.ResetDraggedContent();
        PlayerControllerKeyBinds.InputBlockers.Add("FavoursWindow");   // same blocker key the vanilla tree uses
        SkillsManager.FavourChanged += OnFavourChanged;
        StandardModeManager.Instance.ShouldPauseInSinglePlayer = true;
        base.OnWindowShown();
    }

    protected override void OnWindowClosed()
    {
        CandideExtendedInteractionsHandler.ResetDraggedContent();
        PlayerControllerKeyBinds.InputBlockers.Remove("FavoursWindow");
        SkillsManager.FavourChanged -= OnFavourChanged;
        StandardModeManager.Instance.ShouldPauseInSinglePlayer = false;
        _pending?.Remove();
        _pending = null;
        base.OnWindowClosed();
    }

    private void OnFavourChanged(FavourChangedEventArgs e)
    {
        if (e?.CharacterModel != _model) return;
        _points.OnFavourChanged(_model.Skills);
        foreach (Node n in _nodes) n.UpdateState(_model.Skills);
    }

    public override CandideUiElement GetFocusable() => _nodes.FirstOrDefault()?.GetFocusable();

    // ------------------------------------------------------------------------------- the node widget

    // NewFavourHolder clone with our reachability rule and an own-built tooltip (the vanilla tooltip helper
    // calls IsFavourAvailable, which grid-walks and would always hide the cost line for screen nodes).
    private sealed class Node : CandidePanel
    {
        public readonly FavourData Favour;
        public readonly FavourDef Def;
        private readonly CandideHolderBase _holder;
        private readonly CandideIcon _check;

        public Node(FavourData favour, FavourDef def, Action<Node> onClicked)
        {
            Favour = favour;
            Def = def;
            _holder = new CandideHolderBase
            {
                GetTooltipContent = BuildTooltip,
                Padding = new CandideThickness(2, 2, 3, 3),
            };
            _holder.Clicked += delegate { onClicked(this); };
            _holder.SetChild(new CandideIcon(IconFlag.Large)
            {
                Icon = favour.Icon,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Scale = new Vector2(0.75f),
            });
            _check = new CandideIcon
            {
                Icon = "ui:yes",
                IsHud = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visible = false,
            };
            AddChild(_holder);
            AddChild(_check);
            UpdateState(GameState.LocalPlayer.Character.Character.Skills);
        }

        public void UpdateState(in Skills skills)
        {
            bool learned = skills.Favours.ContainsKey(Favour.Id);
            _check.Visible = learned;
            bool open = learned || IsReachable(Def, skills);
            Enabled = open;
            Opacity = open ? 1f : 0.5f;
        }

        public override CandideUiElement GetFocusable() => _holder;

        // same shape as CandideTooltipHelper.BuildFavourTooltipContent, minus the grid walk
        private IEnumerable<CandideUiElement> BuildTooltip()
        {
            var list = new List<CandideUiElement>
            {
                new CandideTextLabel { Text = Favour.Name.GetTranslation().ToUpper(), TextColor = StyleHelper.TextYellowColor },
            };
            try
            {
                var sb = new StringBuilder();
                Favour.GetDescription(sb, new ApplyAuraContext
                {
                    SpellBridge = ClientSpellManagerBridge.Instance,
                    OwnerEntityId = GameState.LocalPlayer?.EntityId,
                });
                if (sb.Length > 0) list.Add(TextToUiElementsHelper.ParseString(sb.ToString()));
            }
            catch { }
            Skills skills = GameState.LocalPlayer.Character.Character.Skills;
            if (IsReachable(Def, skills))
            {
                list.Add(new CandideHorizontalSeparator
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new CandideThickness(0, 9, 0, 6),
                });
                if (skills.Favours.ContainsKey(Favour.Id))
                    list.Add(new CandideTextLabel { StringId = "Skills_FavourAlreadyUnlocked", TextColor = StyleHelper.TextYellowColor });
                else
                    list.Add(TextToUiElementsHelper.ParseString("$S" + StringDefinitions.GetFormatString("Grid_FavourCost", "1") + "$r"));
            }
            return list;
        }
    }

    // -------------------------------------------------------------------------------------- opener

    // Mirrors PlayerUi.ShowFavoursWindow: close every non-overlay window, open fullscreen.
    internal static void Open()
    {
        if (Globals.Game.ModeManager != StandardModeManager.Instance) return;
        var desktop = Globals.Game.ModeManager.CandideDesktop;
        PlayerUi.CloseAllNonOverlay(desktop);
        new ForgeFavourScreen(GameState.LocalPlayer.Character.Character).Show(desktop);
    }
}

// The fourth toggle button in the Skills & Favours window ("Pack Favours"), only when a pack defines screen
// nodes. Same pattern as the vanilla Favours button: opening the fullscreen tree closes this window, so no
// untoggle bookkeeping is needed.
[HarmonyPatch(typeof(SkillsAndFavourWindow), MethodType.Constructor,
    new[] { typeof(PlayerCharacterModel), typeof(bool) })]
internal static class ForgeFavourScreenTabPatch
{
    [HarmonyPostfix]
    private static void Postfix(SkillsAndFavourWindow __instance)
    {
        try
        {
            if (FavoursFeature.ScreenDefs.Count == 0) return;
            var grid = AccessTools.Field(typeof(SkillsAndFavourWindow), "_toggleButtonsGrid")
                ?.GetValue(__instance) as CandideGrid;
            if (grid == null) return;
            bool tr = false;
            try { tr = Globals.LanguageCode == "tr_TR"; } catch { }
            grid.AddChild(new CandideToggleIconLabelButton("favours", IconFlag.Medium, 80)
            {
                Text = tr ? "Paket Lütufları" : "Pack Favours",
                Padding = new CandideThickness(4, 3, 4, 5),
                GridColumn = 3,                                   // 0 Skills, 1 Favours, 2 Forge tab
                HorizontalAlignment = HorizontalAlignment.Stretch,
                LockWhenToggledOn = true,
                OnToggledOnAction = ForgeFavourScreen.Open,
            });
        }
        catch (Exception e) { ForgeTab.WarnOnce($"Pack-favours tab attach failed (vanilla window unaffected): {e}"); }
    }
}
