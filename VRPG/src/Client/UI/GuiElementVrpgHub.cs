using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Client.UI;

public sealed class GuiElementVrpgHub : GuiElement
{
    private const double HeaderHeight = 54.0;
    private const double Padding = 18.0;
    private const double RowHeight = 29.0;

    private OpenHubPacket packet;
    private readonly Action close;
    private readonly Action<LibraryEntryPacket[]> openLibrary;
    private readonly Action<string, int> adjustStat;
    private readonly Action<bool, bool> updateOptions;
    private readonly Action<bool, int> updateHotbar;
    private readonly Action<int, string> equipSkill;
    private readonly Action<string[], string[]> applyTalentPlan;
    private readonly Action openTalentEditor;
    private readonly VRPG.Client.Visuals.CombatVisualsConfig combatVisuals;
    private readonly Action combatVisualsChanged;
    private readonly List<ClickRegion> clickRegions = new List<ClickRegion>();
    private readonly List<HoverRegion> hoverRegions = new List<HoverRegion>();
    private int textureId;
    private int selectedTab;
    private int hoverTab = -1;
    private int selectedClassIndex;
    private int selectedSkillIndex;
    private int selectedOptionsCategory;
    private TooltipContent? hoverInfo;
    private readonly TalentGraphComponent talentGraph = new TalentGraphComponent();
    private bool draggingTalentTree;
    private bool talentDragMoved;
    private bool actionPending;
    private bool talentPlanApplyPending;
    private readonly HashSet<string> pendingTalentAllocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pendingTalentRefunds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private double dragStartX;
    private double dragStartY;
    private double lastMainX;
    private double lastMainY;
    private double lastMainW;
    private double lastMainH;

    private static readonly string[] Tabs = { "Stats", "Skills", "Talents", "Library", "Options" };
    private static readonly string[] BaseStatCodes = { "strength", "dexterity", "intelligence" };
    private static readonly string[] OptionsCategories = { "Notifications", "Combat Hotbar", "Combat Visuals" };

    public GuiElementVrpgHub(
        ICoreClientAPI api,
        ElementBounds bounds,
        OpenHubPacket packet,
        TalentTreeSnapshotPacket talentTree,
        Action close,
        Action<LibraryEntryPacket[]> openLibrary,
        Action<string, int> adjustStat,
        Action<bool, bool> updateOptions,
        Action<bool, int> updateHotbar,
        Action<int, string> equipSkill,
        Action<string[], string[]> applyTalentPlan,
        Action openTalentEditor,
        VRPG.Client.Visuals.CombatVisualsConfig combatVisuals,
        Action combatVisualsChanged)
        : base(api, bounds)
    {
        this.packet = packet;
        this.close = close;
        this.openLibrary = openLibrary;
        this.adjustStat = adjustStat;
        this.updateOptions = updateOptions;
        this.updateHotbar = updateHotbar;
        this.equipSkill = equipSkill;
        this.applyTalentPlan = applyTalentPlan;
        this.openTalentEditor = openTalentEditor;
        this.combatVisuals = combatVisuals;
        this.combatVisualsChanged = combatVisualsChanged;
        this.packet.Options ??= new HubOptionsPacket();
        talentGraph.SetTree(talentTree);
        UpdateTalentGraphPlayerState();
        MouseOverCursor = "pointer";
    }

    public void UpdatePacket(OpenHubPacket nextPacket)
    {
        nextPacket.Options ??= new HubOptionsPacket();
        packet = nextPacket;
        actionPending = false;
        if (talentPlanApplyPending)
        {
            if (!nextPacket.FeedbackError)
            {
                pendingTalentAllocations.Clear();
                pendingTalentRefunds.Clear();
            }
            talentPlanApplyPending = false;
        }
        selectedClassIndex = GameMath.Clamp(selectedClassIndex, 0, Math.Max(0, packet.Classes.Length - 1));
        UpdateTalentGraphPlayerState();
        Redraw();
    }

    public void UpdateTalentTree(TalentTreeSnapshotPacket talentTree)
    {
        talentGraph.SetTree(talentTree);
        pendingTalentAllocations.Clear();
        pendingTalentRefunds.Clear();
        UpdateTalentGraphPlayerState();
        Redraw();
    }

    public void UpdateTalentProgress(string[] talents)
    {
        packet.Talents = talents ?? Array.Empty<string>();
        UpdateTalentGraphPlayerState();
        Redraw();
    }

    public void UpdateResources(RpgResourcePacket resources)
    {
        packet.Resources = resources;
        UpdateTalentGraphPlayerState();
        Redraw();
    }

    private void UpdateTalentGraphPlayerState()
    {
        HashSet<string> effective = EffectiveTalentSet();
        talentGraph.SetPlayerPlan(
            packet.Talents,
            effective.ToArray(),
            pendingTalentAllocations.ToArray(),
            pendingTalentRefunds.ToArray(),
            PlannedTalentPoints());
    }

    private void QueueTalentClick(EnumMouseButton button)
    {
        TalentTreeNodePacket? node = talentGraph.SelectedNode;
        if (node == null || actionPending) return;
        string code = CanonicalTalentCode(node.Code);
        TalentGraphNodeState state = talentGraph.StateFor(node);

        if (button == EnumMouseButton.Left)
        {
            if (state == TalentGraphNodeState.PendingAllocation)
            {
                pendingTalentAllocations.Remove(code);
                PruneDisconnectedPendingAllocations();
            }
            else if (state == TalentGraphNodeState.PendingRefund)
            {
                pendingTalentRefunds.Remove(code);
                if (RawPlannedTalentPoints() < 0) pendingTalentRefunds.Add(code);
            }
            else if (state == TalentGraphNodeState.Available)
            {
                pendingTalentAllocations.Add(code);
            }
        }
        else if (button == EnumMouseButton.Right)
        {
            if (state == TalentGraphNodeState.PendingAllocation)
            {
                pendingTalentAllocations.Remove(code);
                PruneDisconnectedPendingAllocations();
            }
            else if (state == TalentGraphNodeState.PendingRefund)
            {
                pendingTalentRefunds.Remove(code);
                if (RawPlannedTalentPoints() < 0) pendingTalentRefunds.Add(code);
            }
            else if (state == TalentGraphNodeState.Allocated
                && pendingTalentRefunds.Count < Math.Max(0, packet.Resources?.RespecPoints ?? 0))
            {
                pendingTalentRefunds.Add(code);
                if (!IsEffectivePlanConnected()) pendingTalentRefunds.Remove(code);
            }
        }
        UpdateTalentGraphPlayerState();
    }

    private HashSet<string> EffectiveTalentSet()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < packet.Talents.Length; i++) result.Add(CanonicalTalentCode(packet.Talents[i]));
        result.ExceptWith(pendingTalentRefunds);
        result.UnionWith(pendingTalentAllocations);
        return result;
    }

    private int PlannedTalentPoints()
    {
        return Math.Max(0, RawPlannedTalentPoints());
    }

    private int RawPlannedTalentPoints()
    {
        int points = Math.Max(0, packet.Resources?.UnspentTalentPoints ?? 0);
        foreach (string code in pendingTalentRefunds) points += TalentPointCost(code);
        foreach (string code in pendingTalentAllocations) points -= TalentPointCost(code);
        return points;
    }

    private int TalentPointCost(string code)
    {
        TalentTreeNodePacket? node = Array.Find(talentGraph.Snapshot.Nodes, entry => SameTalentCode(entry.Code, code));
        return node?.Starter == true ? 0 : Math.Max(1, node?.Cost ?? 1);
    }

    private void PruneDisconnectedPendingAllocations()
    {
        HashSet<string> effective = EffectiveTalentSet();
        HashSet<string> reachable = ReachableFromStarter(effective);
        string[] queued = pendingTalentAllocations.ToArray();
        for (int i = 0; i < queued.Length; i++)
        {
            if (!reachable.Contains(queued[i])) pendingTalentAllocations.Remove(queued[i]);
        }
    }

    private bool IsEffectivePlanConnected()
    {
        HashSet<string> effective = EffectiveTalentSet();
        return effective.Count == 0 || ReachableFromStarter(effective).Count == effective.Count;
    }

    private HashSet<string> ReachableFromStarter(HashSet<string> effective)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] starters = talentGraph.Snapshot.Nodes
            .Where(node => node.Starter && effective.Contains(CanonicalTalentCode(node.Code)))
            .Select(node => CanonicalTalentCode(node.Code)).ToArray();
        if (starters.Length != 1) return visited;
        var queue = new Queue<string>();
        visited.Add(starters[0]);
        queue.Enqueue(starters[0]);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            for (int i = 0; i < talentGraph.Snapshot.Nodes.Length; i++)
            {
                TalentTreeNodePacket candidate = talentGraph.Snapshot.Nodes[i];
                string candidateCode = CanonicalTalentCode(candidate.Code);
                if (!effective.Contains(candidateCode) || visited.Contains(candidateCode) || !TalentNodesLinked(current, candidate)) continue;
                visited.Add(candidateCode);
                queue.Enqueue(candidateCode);
            }
        }
        return visited;
    }

    private bool TalentNodesLinked(string code, TalentTreeNodePacket candidate)
    {
        TalentTreeNodePacket? current = Array.Find(talentGraph.Snapshot.Nodes, node => SameTalentCode(node.Code, code));
        if (current == null) return false;
        return current.Links.Any(link => SameTalentCode(link, candidate.Code))
            || candidate.Links.Any(link => SameTalentCode(link, current.Code));
    }

    private static string CanonicalTalentCode(string code)
    {
        string value = (code ?? "").Trim();
        return value.Contains(':') ? value : "vrpg:" + value;
    }

    private static bool SameTalentCode(string left, string right)
    {
        return string.Equals(CanonicalTalentCode(left), CanonicalTalentCode(right), StringComparison.OrdinalIgnoreCase);
    }

    public override bool Focusable => true;

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (textureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
        }
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        double x = args.X - Bounds.absX;
        double y = args.Y - Bounds.absY;

        if (draggingTalentTree)
        {
            talentDragMoved |= Math.Abs(x - dragStartX) > 1.0 || Math.Abs(y - dragStartY) > 1.0;
            talentGraph.Pan(x - dragStartX, y - dragStartY, lastMainW, lastMainH);
            dragStartX = x;
            dragStartY = y;
            Redraw();
            args.Handled = true;
            return;
        }

        int nextHoverTab = -1;
        TooltipContent? nextHover = null;
        for (int i = 0; i < clickRegions.Count; i++)
        {
            if (clickRegions[i].Kind == ClickKind.Tab && clickRegions[i].Contains(x, y))
            {
                nextHoverTab = clickRegions[i].Index;
                break;
            }
        }

        for (int i = 0; i < hoverRegions.Count; i++)
        {
            if (hoverRegions[i].Contains(x, y))
            {
                nextHover = hoverRegions[i].Info;
                break;
            }
        }

        if (nextHoverTab != hoverTab || !HoverEquals(nextHover, hoverInfo))
        {
            hoverTab = nextHoverTab;
            hoverInfo = nextHover;
            Redraw();
        }
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        double x = args.X - Bounds.absX;
        double y = args.Y - Bounds.absY;
        if (args.Button == EnumMouseButton.Left && selectedTab == 2 && PointInMainPanel(x, y))
        {
            draggingTalentTree = true;
            talentDragMoved = false;
            dragStartX = x;
            dragStartY = y;
        }

        args.Handled = true;
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Left && args.Button != EnumMouseButton.Right)
        {
            draggingTalentTree = false;
            return;
        }

        double x = args.X - Bounds.absX;
        double y = args.Y - Bounds.absY;
        bool wasDragging = draggingTalentTree && talentDragMoved;
        draggingTalentTree = false;

        if (args.Button == EnumMouseButton.Left && CloseAt(x, y))
        {
            close();
            args.Handled = true;
            api.Gui.PlaySound("menubutton_press");
            return;
        }

        if (wasDragging)
        {
            args.Handled = true;
            return;
        }

        if (selectedTab == 2 && PointInMainPanel(x, y) && talentGraph.SelectAt(x, y))
        {
            QueueTalentClick(args.Button);
            Redraw();
            args.Handled = true;
            api.Gui.PlaySound("menubutton_press");
            return;
        }

        if (args.Button != EnumMouseButton.Left)
        {
            args.Handled = true;
            return;
        }

        for (int i = 0; i < clickRegions.Count; i++)
        {
            ClickRegion region = clickRegions[i];
            if (!region.Contains(x, y))
            {
                continue;
            }

            switch (region.Kind)
            {
                case ClickKind.Tab:
                    selectedTab = region.Index;
                    break;
                case ClickKind.OpenLibrary:
                    openLibrary(packet.Entries);
                    break;
                case ClickKind.Class:
                    selectedClassIndex = GameMath.Clamp(region.Index, 0, Math.Max(0, packet.Classes.Length - 1));
                    selectedSkillIndex = 0;
                    break;
                case ClickKind.Skill:
                    selectedSkillIndex = Math.Max(0, region.Index);
                    break;
                case ClickKind.SkillSlot:
                    EquipSelectedSkill(region.Index);
                    break;
                case ClickKind.ApplyTalentPlan:
                    ApplyTalentPlan();
                    break;
                case ClickKind.CancelTalentPlan:
                    pendingTalentAllocations.Clear();
                    pendingTalentRefunds.Clear();
                    UpdateTalentGraphPlayerState();
                    break;
                case ClickKind.OpenTalentEditor:
                    if (packet.CanEditTalentTree)
                    {
                        openTalentEditor();
                    }
                    break;
                case ClickKind.StatDecrease:
                    adjustStat(BaseStatCodes[GameMath.Clamp(region.Index, 0, BaseStatCodes.Length - 1)], -1);
                    break;
                case ClickKind.StatIncrease:
                    adjustStat(BaseStatCodes[GameMath.Clamp(region.Index, 0, BaseStatCodes.Length - 1)], 1);
                    break;
                case ClickKind.ToggleCooldownNotifications:
                    packet.Options.ShowCooldownNotifications = !packet.Options.ShowCooldownNotifications;
                    updateOptions(packet.Options.ShowCooldownNotifications, packet.Options.ShowResourceNotifications);
                    break;
                case ClickKind.ToggleResourceNotifications:
                    packet.Options.ShowResourceNotifications = !packet.Options.ShowResourceNotifications;
                    updateOptions(packet.Options.ShowCooldownNotifications, packet.Options.ShowResourceNotifications);
                    break;
                case ClickKind.ToggleHotbarLock:
                    packet.Options.SkillHotbarLocked = !packet.Options.SkillHotbarLocked;
                    updateHotbar(packet.Options.SkillHotbarLocked, VisibleHotbarSlots());
                    break;
                case ClickKind.DecreaseHotbarSlots:
                    packet.Options.SkillHotbarSlots = Math.Max(4, VisibleHotbarSlots() - 1);
                    updateHotbar(packet.Options.SkillHotbarLocked, packet.Options.SkillHotbarSlots);
                    break;
                case ClickKind.IncreaseHotbarSlots:
                    packet.Options.SkillHotbarSlots = Math.Min(8, VisibleHotbarSlots() + 1);
                    updateHotbar(packet.Options.SkillHotbarLocked, packet.Options.SkillHotbarSlots);
                    break;
                case ClickKind.OptionsCategory:
                    selectedOptionsCategory = GameMath.Clamp(region.Index, 0, OptionsCategories.Length - 1);
                    break;
                case ClickKind.ToggleCombatText:
                    combatVisuals.CombatTextEnabled = !combatVisuals.CombatTextEnabled;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleDamageNumbers:
                    combatVisuals.DamageNumbers = !combatVisuals.DamageNumbers;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleEventWords:
                    combatVisuals.EventWords = !combatVisuals.EventWords;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleMergeNumbers:
                    combatVisuals.MergeNumbers = !combatVisuals.MergeNumbers;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleStatusAuras:
                    combatVisuals.StatusAuras = !combatVisuals.StatusAuras;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleDegradationPolicy:
                    combatVisuals.DegradationPolicy = IsOwnFirst() ? "uniform" : "own-first";
                    combatVisualsChanged();
                    break;
                case ClickKind.CycleTelegraphOpacity:
                    combatVisuals.TelegraphOpacity = combatVisuals.TelegraphOpacity > 0.75f ? 0.5f
                        : combatVisuals.TelegraphOpacity > 0.35f ? 0.25f : 1f;
                    combatVisualsChanged();
                    break;
                case ClickKind.CycleIntensity:
                    combatVisuals.Intensity = combatVisuals.Intensity > 0.9f ? 0.75f
                        : combatVisuals.Intensity > 0.6f ? 0.5f : 1f;
                    combatVisualsChanged();
                    break;
            }

            Redraw();
            args.Handled = true;
            api.Gui.PlaySound("menubutton_press");
            return;
        }
    }

    private void EquipSelectedSkill(int slotIndex)
    {
        if (actionPending || packet.Classes.Length == 0)
        {
            return;
        }

        HubClassPacket cls = packet.Classes[GameMath.Clamp(selectedClassIndex, 0, packet.Classes.Length - 1)];
        HubSkillPacket[] skills = cls.Skills.OrderBy(skill => skill.RequiredLevel).ThenBy(skill => skill.Name).ToArray();
        if (skills.Length == 0)
        {
            return;
        }

        HubSkillPacket selected = skills[GameMath.Clamp(selectedSkillIndex, 0, skills.Length - 1)];
        if (selected.LearnedLevel <= 0)
        {
            return;
        }

        int slot = GameMath.Clamp(slotIndex, 0, VisibleHotbarSlots() - 1) + 1;
        string code = selected.EquippedSlot == slot ? "" : selected.Code;
        actionPending = true;
        equipSkill(slot, code);
    }

    private void ApplyTalentPlan()
    {
        if (actionPending || pendingTalentAllocations.Count + pendingTalentRefunds.Count == 0)
        {
            return;
        }
        actionPending = true;
        talentPlanApplyPending = true;
        applyTalentPlan(pendingTalentAllocations.ToArray(), pendingTalentRefunds.ToArray());
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (selectedTab != 2 || !PointInMainPanel(api.Input.MouseX - Bounds.absX, api.Input.MouseY - Bounds.absY))
        {
            return;
        }

        int direction = Math.Sign(args.deltaPrecise);
        if (direction == 0)
        {
            direction = Math.Sign(args.delta);
        }

        if (direction == 0)
        {
            return;
        }

        double mouseLocalX = api.Input.MouseX - Bounds.absX - (lastMainX + lastMainW / 2.0);
        double mouseLocalY = api.Input.MouseY - Bounds.absY - (lastMainY + lastMainH / 2.0);
        talentGraph.ZoomAt(direction, mouseLocalX, mouseLocalY, lastMainW, lastMainH);
        Redraw();
        args.SetHandled();
    }

    public override void Dispose()
    {
        base.Dispose();
        if (textureId > 0)
        {
            api.Render.GLDeleteTexture(textureId);
            textureId = 0;
        }
    }

    private void Redraw()
    {
        if (Bounds.OuterWidthInt <= 0 || Bounds.OuterHeightInt <= 0)
        {
            return;
        }

        using ImageSurface surface = new ImageSurface((Format)0, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        using Context ctx = genContext(surface);
        Draw(ctx, Bounds.OuterWidth, Bounds.OuterHeight);
        generateTexture(surface, ref textureId);
    }

    private void Draw(Context ctx, double width, double height)
    {
        clickRegions.Clear();
        hoverRegions.Clear();
        DrawBackdrop(ctx, width, height);
        DrawHeader(ctx, width);

        double contentY = scaled(HeaderHeight);
        double footerY = height - scaled(27.0);
        double contentBottom = footerY - scaled(16.0);
        double mainX = scaled(Padding);
        double mainW = width - scaled(Padding * 2.0);
        double panelY = contentY;
        double panelH = contentBottom - panelY;
        lastMainX = mainX;
        lastMainY = panelY;
        lastMainW = mainW;
        lastMainH = panelH;

        if (selectedTab == 1)
        {
            DrawClassSkillsWide(ctx, mainX, panelY, mainW, panelH);
        }
        else if (selectedTab == 2)
        {
            DrawTalentPageWide(ctx, mainX, panelY, mainW, panelH);
        }
        else
        {
            DrawMainPanel(ctx, mainX, panelY, mainW, panelH);
        }

        DrawFooter(ctx, scaled(Padding), footerY, width - scaled(Padding * 2.0));
        DrawTooltip(ctx, width, height);
    }

    private void DrawHeader(Context ctx, double width)
    {
        double logoX = scaled(28.0);
        double logoW = scaled(180.0);
        DrawText(ctx, "VRPG Hub", logoX, scaled(35.0), scaled(20.0), bold: true, ColorGold(), maxWidth: logoW);

        double tabsX = logoX + logoW + scaled(18.0);
        double tabsRight = width - scaled(58.0);
        double tabGap = scaled(10.0);
        double tabW = (tabsRight - tabsX - tabGap * (Tabs.Length - 1)) / Tabs.Length;
        for (int i = 0; i < Tabs.Length; i++)
        {
            double tabX = tabsX + i * (tabW + tabGap);
            DrawHeaderTab(ctx, tabX, scaled(14.0), tabW, Tabs[i], i == selectedTab || i == hoverTab);
            clickRegions.Add(new ClickRegion(ClickKind.Tab, i, tabX, scaled(5.0), tabW, scaled(42.0)));
        }

        DrawCloseButton(ctx, width - scaled(34.0), scaled(18.0));
    }

    private void DrawMainPanel(Context ctx, double x, double y, double width, double height)
    {
        DrawPane(ctx, x, y, width, height);
        switch (selectedTab)
        {
            case 0:
                DrawStats(ctx, x, y, width, height);
                break;
            case 1:
                DrawClassSkills(ctx, x, y, width, height);
                break;
            case 2:
                DrawTalentTree(ctx, x, y, width, height);
                break;
            case 3:
                DrawLibrarySummary(ctx, x, y, width, height);
                break;
            case 4:
                DrawOptions(ctx, x, y, width, height);
                break;
        }
    }

    private void DrawOptions(Context ctx, double x, double y, double width, double height)
    {
        double outerPad = scaled(14.0);
        double navW = scaled(212.0);
        double bodyY = y + outerPad;
        double bodyH = height - outerPad * 2.0;
        double navX = x + outerPad;
        double pageX = navX + navW + scaled(22.0);
        double pageW = x + width - outerPad - pageX;

        DrawOptionsNavigation(ctx, navX, bodyY, navW, bodyH);
        DrawVerticalSeparator(ctx, navX + navW + scaled(10.0), bodyY, bodyH);

        switch (selectedOptionsCategory)
        {
            case 1:
                DrawHotbarOptions(ctx, pageX, bodyY, pageW, bodyH);
                break;
            case 2:
                DrawCombatVisualOptions(ctx, pageX, bodyY, pageW, bodyH);
                break;
            default:
                DrawNotificationOptions(ctx, pageX, bodyY, pageW, bodyH);
                break;
        }
    }

    private void DrawCombatVisualOptions(Context ctx, double x, double y, double width, double height)
    {
        // Compact layout: 8 rows must fit inside a body as short as ~435 logical px
        // (dialog min height 560) — see GuiDialogVRPGHub.Compose. A compact row height
        // is passed to DrawOptionRow so this page alone uses a tighter rhythm than the
        // other options pages, which only have 1-2 rows and keep the default 76px rows.
        DrawText(ctx, "Combat Visuals", x, y + scaled(22.0), scaled(16.0), bold: true, ColorGold(), maxWidth: width);
        DrawText(
            ctx,
            "Client-side visuals only. Gameplay-critical telegraphs and timing cues are never hidden.",
            x, y + scaled(38.0), scaled(10.0), bold: false, ColorMuted(), maxWidth: width);

        double rowY = y + scaled(50.0);
        double rowH = scaled(42.0);
        double step = scaled(46.0);
        DrawOptionRow(ctx, x, rowY, width, "Combat text",
            "Master switch for floating damage numbers and event words.",
            combatVisuals.CombatTextEnabled, ClickKind.ToggleCombatText, rowH);
        DrawOptionRow(ctx, x, rowY + step, width, "Damage numbers",
            "Show merged damage and healing numbers over targets.",
            combatVisuals.DamageNumbers, ClickKind.ToggleDamageNumbers, rowH);
        DrawOptionRow(ctx, x, rowY + step * 2, width, "Event words",
            "Show BREAK, COUNTER, and similar state words over targets.",
            combatVisuals.EventWords, ClickKind.ToggleEventWords, rowH);
        DrawOptionRow(ctx, x, rowY + step * 3, width, "Merge rapid hits",
            "Combine rapid hits on one target into a single growing number.",
            combatVisuals.MergeNumbers, ClickKind.ToggleMergeNumbers, rowH);
        DrawOptionRow(ctx, x, rowY + step * 4, width, "Status auras",
            "Loop subtle particles around enemies carrying statuses.",
            combatVisuals.StatusAuras, ClickKind.ToggleStatusAuras, rowH);
        DrawOptionRow(ctx, x, rowY + step * 5, width, "Protect my own effects: "
            + (IsOwnFirst() ? "on" : "off (uniform)"),
            "Under heavy load, degrade cosmetic and other players' effects before your own.",
            IsOwnFirst(), ClickKind.ToggleDegradationPolicy, rowH);
        DrawOptionRow(ctx, x, rowY + step * 6, width, "Telegraph opacity: "
            + (int)Math.Round(combatVisuals.TelegraphOpacity * 100) + "%",
            "Strength of ground discs and rings. Click to cycle 100 / 50 / 25%.",
            combatVisuals.TelegraphOpacity > 0.3f, ClickKind.CycleTelegraphOpacity, rowH);
        DrawOptionRow(ctx, x, rowY + step * 7, width, "Effect intensity: "
            + (int)Math.Round(combatVisuals.Intensity * 100) + "%",
            "Overall particle quantity. Click to cycle 100 / 75 / 50%.",
            combatVisuals.Intensity > 0.6f, ClickKind.CycleIntensity, rowH);
    }

    private bool IsOwnFirst()
    {
        return !string.Equals(combatVisuals.DegradationPolicy, "uniform", StringComparison.OrdinalIgnoreCase);
    }

    private void DrawHotbarOptions(Context ctx, double x, double y, double width, double height)
    {
        DrawText(ctx, "Combat Hotbar", x, y + scaled(31.0), scaled(20.0), bold: true, ColorGold(), maxWidth: width);
        DrawText(ctx, "The hotbar always reflects the server loadout and the bindings configured in Vintage Story controls.", x, y + scaled(58.0), scaled(12.0), bold: false, ColorMuted(), maxWidth: width);
        DrawOptionRow(
            ctx,
            x,
            y + scaled(92.0),
            width,
            "Lock hotbar position",
            packet.Options.SkillHotbarLocked
                ? "Locked against accidental movement. Turn this off to drag the hotbar while a cursor is visible."
                : "Unlocked. Drag the hotbar to place it, then lock it here.",
            packet.Options.SkillHotbarLocked,
            ClickKind.ToggleHotbarLock);

        DrawHotbarSlotCountRow(ctx, x, y + scaled(180.0), width);

        DrawText(ctx, "Assignment", x, y + scaled(279.0), scaled(15.0), bold: true, ColorGold(), maxWidth: width);
        DrawWrappedText(ctx, "Open Skills, select any learned ability, and click a visible Combat Loadout slot. Hidden slots retain their assignments.", x, y + scaled(306.0), width, scaled(12.0), ColorText(), 3);
        DrawText(ctx, "Bindings", x, y + scaled(371.0), scaled(15.0), bold: true, ColorGold(), maxWidth: width);
        DrawWrappedText(ctx, "Find the VRPG-prefixed skill-slot controls in Vintage Story's Controls menu. The Skills page and HUD resolve the current bindings automatically.", x, y + scaled(398.0), width, scaled(12.0), ColorText(), 3);
    }

    private void DrawHotbarSlotCountRow(Context ctx, double x, double y, double width)
    {
        const double logicalRowHeight = 76.0;
        double rowHeight = scaled(logicalRowHeight);
        RoundedRectangle(ctx, x, y, width, rowHeight, scaled(3.0));
        ctx.SetSourceRGBA(0.08, 0.025, 0.01, 0.52);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(VrpgGuiTheme.GoldR, VrpgGuiTheme.GoldG, VrpgGuiTheme.GoldB, 0.34);
        ctx.Stroke();

        DrawText(ctx, "Visible skill slots", x + scaled(14.0), y + scaled(27.0), scaled(15.0), bold: true, ColorText(), maxWidth: width - scaled(190.0));
        DrawText(ctx, "Choose 4–8 slots. Reducing the size does not clear hidden skills.", x + scaled(14.0), y + scaled(53.0), scaled(11.0), bold: false, ColorMuted(), maxWidth: width - scaled(190.0));

        int count = VisibleHotbarSlots();
        double plusX = x + width - scaled(48.0);
        double minusX = plusX - scaled(86.0);
        double buttonY = y + scaled(22.0);
        DrawStatButton(ctx, minusX, buttonY, "−", count > 4, false);
        DrawText(ctx, count.ToString(), minusX + scaled(60.0), y + scaled(46.0), scaled(18.0), bold: true, ColorText(), center: true, maxWidth: scaled(38.0));
        DrawStatButton(ctx, plusX, buttonY, "+", count < 8, false);
        if (count > 4) clickRegions.Add(new ClickRegion(ClickKind.DecreaseHotbarSlots, 0, minusX, buttonY, scaled(34.0), scaled(32.0)));
        if (count < 8) clickRegions.Add(new ClickRegion(ClickKind.IncreaseHotbarSlots, 0, plusX, buttonY, scaled(34.0), scaled(32.0)));
    }

    private void DrawOptionsNavigation(Context ctx, double x, double y, double width, double height)
    {
        DrawPane(ctx, x, y, width, height);
        DrawText(ctx, "Options", x + scaled(14.0), y + scaled(31.0), scaled(20.0), bold: true, ColorGold(), maxWidth: width - scaled(28.0));
        DrawText(ctx, "Categories", x + scaled(14.0), y + scaled(65.0), scaled(12.0), bold: true, ColorMuted(), maxWidth: width - scaled(28.0));

        for (int i = 0; i < OptionsCategories.Length; i++)
        {
            double rowY = y + scaled(89.0) + i * scaled(40.0);
            bool selected = i == selectedOptionsCategory;
            DrawSelection(ctx, x + scaled(8.0), rowY - scaled(23.0), width - scaled(16.0), scaled(34.0), selected);
            DrawText(ctx, OptionsCategories[i], x + scaled(18.0), rowY, scaled(14.0), bold: selected, selected ? ColorGold() : ColorText(), maxWidth: width - scaled(36.0));
            clickRegions.Add(new ClickRegion(ClickKind.OptionsCategory, i, x + scaled(8.0), rowY - scaled(26.0), width - scaled(16.0), scaled(38.0)));
        }

        DrawText(ctx, "Settings are grouped by", x + scaled(14.0), y + height - scaled(54.0), scaled(10.0), bold: false, ColorMuted(), maxWidth: width - scaled(28.0));
        DrawText(ctx, "system to keep each page", x + scaled(14.0), y + height - scaled(39.0), scaled(10.0), bold: false, ColorMuted(), maxWidth: width - scaled(28.0));
        DrawText(ctx, "focused as VRPG grows.", x + scaled(14.0), y + height - scaled(24.0), scaled(10.0), bold: false, ColorMuted(), maxWidth: width - scaled(28.0));
    }

    private void DrawNotificationOptions(Context ctx, double x, double y, double width, double height)
    {
        DrawText(ctx, "Notifications", x, y + scaled(31.0), scaled(20.0), bold: true, ColorGold(), maxWidth: width);
        DrawText(
            ctx,
            "Choose which failed ability warnings appear in chat. These preferences are saved for your player profile.",
            x,
            y + scaled(58.0),
            scaled(12.0),
            bold: false,
            ColorMuted(),
            maxWidth: width);

        DrawText(ctx, "Ability Failure Messages", x, y + scaled(96.0), scaled(15.0), bold: true, ColorText(), maxWidth: width);

        double firstRowY = y + scaled(115.0);
        DrawOptionRow(
            ctx,
            x,
            firstRowY,
            width,
            "Cooldown warnings",
            "Show a message when an activated skill is still on cooldown.",
            packet.Options.ShowCooldownNotifications,
            ClickKind.ToggleCooldownNotifications);
        DrawOptionRow(
            ctx,
            x,
            firstRowY + scaled(92.0),
            width,
            "Resource warnings",
            "Show a message when an ability needs more mana or blood.",
            packet.Options.ShowResourceNotifications,
            ClickKind.ToggleResourceNotifications);

        double noteY = firstRowY + scaled(220.0);
        DrawText(ctx, "Built-in Spam Protection", x, noteY, scaled(15.0), bold: true, ColorGold(), maxWidth: width);
        DrawText(
            ctx,
            "Warnings are rate-limited even while enabled. Rapid repeated inputs are collapsed, followed by a short quiet period.",
            x,
            noteY + scaled(27.0),
            scaled(12.0),
            bold: false,
            ColorMuted(),
            maxWidth: width);
    }

    private void DrawOptionRow(
        Context ctx,
        double x,
        double y,
        double width,
        string title,
        string description,
        bool enabled,
        ClickKind clickKind,
        double? compactRowHeight = null)
    {
        // compactRowHeight lets a page with more rows than the default 76px/row rhythm
        // allows (e.g. Combat Visuals, which has 8 rows) shrink this row's footprint
        // while keeping title, description and the toggle hit-region all aligned to
        // the same drawn rectangle.
        bool compact = compactRowHeight.HasValue;
        double rowH = compactRowHeight ?? scaled(76.0);
        RoundedRectangle(ctx, x, y, width, rowH, scaled(3.0));
        ctx.SetSourceRGBA(0.08, 0.025, 0.01, 0.52);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(VrpgGuiTheme.GoldR, VrpgGuiTheme.GoldG, VrpgGuiTheme.GoldB, 0.34);
        ctx.Stroke();

        double titleSize = compact ? scaled(13.0) : scaled(15.0);
        double titleY = y + (compact ? scaled(15.0) : scaled(27.0));
        double descSize = compact ? scaled(10.0) : scaled(11.0);
        double descY = y + (compact ? scaled(30.0) : scaled(53.0));
        DrawText(ctx, title, x + scaled(14.0), titleY, titleSize, bold: true, ColorText(), maxWidth: width - scaled(150.0));
        DrawText(ctx, description, x + scaled(14.0), descY, descSize, bold: false, ColorMuted(), maxWidth: width - scaled(150.0));

        double toggleW = scaled(92.0);
        double toggleH = compact ? scaled(28.0) : scaled(36.0);
        double toggleFontSize = compact ? scaled(11.0) : scaled(13.0);
        double toggleTextYOffset = compact ? scaled(19.0) : scaled(24.0);
        double toggleX = x + width - toggleW - scaled(14.0);
        double toggleY = y + (rowH - toggleH) / 2.0;
        RoundedRectangle(ctx, toggleX, toggleY, toggleW, toggleH, scaled(3.0));
        ctx.SetSourceRGBA(enabled ? VrpgGuiTheme.GreenR : 0.24, enabled ? VrpgGuiTheme.GreenG : 0.17, enabled ? VrpgGuiTheme.GreenB : 0.13, enabled ? 0.27 : 0.72);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(enabled ? VrpgGuiTheme.GreenR : VrpgGuiTheme.GoldR, enabled ? VrpgGuiTheme.GreenG : VrpgGuiTheme.GoldG, enabled ? VrpgGuiTheme.GreenB : VrpgGuiTheme.GoldB, 0.72);
        ctx.Stroke();
        DrawText(ctx, enabled ? "ON" : "OFF", toggleX + toggleW / 2.0, toggleY + toggleTextYOffset, toggleFontSize, bold: true, enabled ? ColorGreen() : ColorMuted(), center: true, maxWidth: toggleW);
        clickRegions.Add(new ClickRegion(clickKind, 0, x, y, width, rowH));
    }

    private void DrawStats(Context ctx, double x, double y, double width, double height)
    {
        double pad = scaled(18.0);
        double gap = scaled(24.0);
        double leftW = (width - pad * 2.0 - gap) * 0.58;
        double rightX = x + pad + leftW + gap;
        double rightW = x + width - pad - rightX;

        DrawText(ctx, "Core Stats", x + pad, y + scaled(30.0), scaled(19.0), bold: true, ColorGold(), maxWidth: leftW);

        RpgResourcePacket? res = Resources();
        Dictionary<string, int> stats = BaseStats();
        string primaryAttribute = NormalizeCode(res?.PrimaryAttribute ?? "");
        string primaryStatus = PrimaryAttributeStatus(res, primaryAttribute);
        DrawText(ctx, primaryStatus, x + pad, y + scaled(54.0), scaled(12.0), bold: true, res?.EvasiveStepActive == true ? ColorGreen() : ColorMuted(), maxWidth: leftW * 0.72);
        int availablePoints = Math.Max(0, res?.UnspentStatPoints ?? 0);
        float combatSeconds = Math.Max(0f, res?.CombatLockRemainingSeconds ?? 0f);
        bool combatLocked = combatSeconds > 0f;
        string allocationStatus = combatLocked
            ? "Stats locked · combat " + Math.Ceiling(combatSeconds).ToString("0") + "s"
            : availablePoints + " point" + (availablePoints == 1 ? "" : "s") + " available";
        DrawText(ctx, allocationStatus, x + pad + leftW, y + scaled(30.0), scaled(12.0), bold: true, combatLocked || availablePoints > 0 ? ColorGold() : ColorMuted(), right: true, maxWidth: leftW * 0.48);

        double rowY = y + scaled(76.0);
        for (int i = 0; i < BaseStatCodes.Length; i++)
        {
            string code = BaseStatCodes[i];
            string name = StatName(code);
            int value = GetStatValue(stats, code);
            double cardY = rowY + i * scaled(60.0);
            bool decreaseWarns = primaryAttribute == "dexterity" && value > 0 && ProjectedPrimaryAttribute(code, -1) != "dexterity";
            bool increaseWarns = primaryAttribute == "dexterity" && ProjectedPrimaryAttribute(code, 1) != "dexterity";
            DrawStatAllocationRow(
                ctx,
                x + pad,
                cardY,
                leftW,
                name,
                value,
                i,
                code == primaryAttribute,
                !combatLocked && value > 0,
                !combatLocked && availablePoints > 0,
                decreaseWarns,
                increaseWarns);
            hoverRegions.Add(new HoverRegion(x + pad, cardY, leftW, scaled(48.0),
                BuildStatTooltip(code, name, value)));
        }

        double derivedY = rowY + scaled(205.0);
        DrawText(ctx, "Recovery", x + pad, derivedY, scaled(15.0), bold: true, ColorGold(), maxWidth: leftW);
        if (res != null)
        {
            double fieldGap = scaled(10.0);
            double fieldW = (leftW - fieldGap) / 2.0;
            DrawSmallField(ctx, x + pad, derivedY + scaled(31.0), fieldW, "Health Regen", FormatRate(res.HealthRegenPerSecond));
            DrawSmallField(ctx, x + pad + fieldW + fieldGap, derivedY + scaled(31.0), fieldW, "Mana Regen", FormatRate(res.ManaRegenPerSecond));
            DrawSmallField(ctx, x + pad, derivedY + scaled(65.0), fieldW, "Shield Regen", FormatRate(res.MagicShieldRegenPerSecond));
            if (res.BloodUnlocked)
            {
                DrawSmallField(ctx, x + pad + fieldW + fieldGap, derivedY + scaled(65.0), fieldW, "Blood Regen", FormatRate(res.BloodRegenPerSecond));
            }
        }

        DrawVerticalSeparator(ctx, rightX - gap / 2.0, y + scaled(18.0), height - scaled(36.0));
        DrawCharacterSummary(ctx, rightX, y, rightW, height, res);
    }

    private void DrawCharacterSummary(Context ctx, double x, double y, double width, double height, RpgResourcePacket? resources)
    {
        DrawText(ctx, "Character", x, y + scaled(30.0), scaled(19.0), bold: true, ColorGold(), maxWidth: width);
        int level = Math.Max(1, resources?.Level ?? 1);
        DrawText(ctx, "Level " + level, x, y + scaled(59.0), scaled(17.0), bold: true, ColorText(), maxWidth: width);

        if (resources != null)
        {
            double barY = y + scaled(95.0);
            DrawResourceBar(ctx, x, barY, width, "Health", resources.Health, resources.MaxHealth, 0.86, 0.05, 0.14);
            DrawResourceBar(ctx, x, barY + scaled(47.0), width, "Mana", resources.Mana, resources.MaxMana, 0.17, 0.42, 0.95);
            if (resources.MaxMagicShield > 0f)
            {
                DrawResourceBar(ctx, x, barY + scaled(94.0), width, "Shield", resources.MagicShield, resources.MaxMagicShield, 0.64, 0.36, 0.95);
            }

            double progressY = y + scaled(250.0);
            DrawText(ctx, "Progression", x, progressY, scaled(15.0), bold: true, ColorGold(), maxWidth: width);
            DrawSmallField(ctx, x, progressY + scaled(31.0), width, "Experience", resources.Experience + " / " + resources.ExperienceToNextLevel);
            DrawSmallField(ctx, x, progressY + scaled(65.0), width, "Stat Points", resources.UnspentStatPoints.ToString());
            DrawSmallField(ctx, x, progressY + scaled(99.0), width, "Talent Points", resources.UnspentTalentPoints.ToString());
            DrawSmallField(ctx, x, progressY + scaled(133.0), width, "Evasive Step", EvasiveStepStatus(resources));
        }
        else
        {
            DrawWrappedText(ctx, "Resource data is sent by the server RPG module.", x, y + scaled(95.0), width, scaled(13.0), ColorText(), 3);
        }

    }

    private void DrawClassSkills(Context ctx, double x, double y, double width, double height)
    {
        DrawText(ctx, "Classes", x + scaled(14.0), y + scaled(28.0), scaled(18.0), bold: true, ColorGold());
        if (packet.Classes.Length == 0)
        {
            DrawText(ctx, "No class data loaded.", x + scaled(14.0), y + scaled(66.0), scaled(14.0), bold: false, ColorText(), maxWidth: width - scaled(28.0));
            return;
        }

        double classW = scaled(145.0);
        double listY = y + scaled(62.0);
        for (int i = 0; i < packet.Classes.Length; i++)
        {
            double rowY = listY + i * scaled(38.0);
            bool selected = i == selectedClassIndex;
            DrawSelection(ctx, x + scaled(12.0), rowY - scaled(22.0), classW - scaled(18.0), scaled(31.0), selected);
            DrawText(ctx, packet.Classes[i].Name, x + scaled(21.0), rowY, scaled(13.0), bold: selected, selected ? ColorGold() : ColorText(), maxWidth: classW - scaled(34.0));
            clickRegions.Add(new ClickRegion(ClickKind.Class, i, x + scaled(12.0), rowY - scaled(24.0), classW - scaled(18.0), scaled(34.0)));
        }

        double skillX = x + classW;
        double skillW = width - classW - scaled(14.0);
        HubClassPacket cls = packet.Classes[GameMath.Clamp(selectedClassIndex, 0, packet.Classes.Length - 1)];
        DrawText(ctx, cls.Name + " Abilities", skillX, y + scaled(28.0), scaled(17.0), bold: true, ColorGold(), maxWidth: skillW);

        double skillY = y + scaled(70.0);
        foreach (HubSkillPacket skill in cls.Skills.OrderBy(skill => skill.RequiredLevel))
        {
            DrawSkillCard(ctx, skillX, skillY, skillW, skill);
            hoverRegions.Add(new HoverRegion(skillX, skillY - scaled(18.0), skillW, scaled(56.0), TooltipContent.Simple(skill.Name, skill.Description)));
            skillY += scaled(66.0);
            if (skillY > y + height - scaled(26.0)) break;
        }
    }

    private void DrawClassSkillsWide(Context ctx, double x, double y, double width, double height)
    {
        DrawPane(ctx, x, y, width, height);
        if (packet.Classes.Length == 0)
        {
            DrawText(ctx, "Classes", x + scaled(14.0), y + scaled(28.0), scaled(18.0), bold: true, ColorGold());
            DrawText(ctx, "No class data loaded.", x + scaled(14.0), y + scaled(66.0), scaled(14.0), bold: false, ColorText(), maxWidth: width - scaled(28.0));
            return;
        }

        double pad = scaled(14.0);
        double classW = scaled(176.0);
        double skillW = Math.Min(scaled(330.0), Math.Max(scaled(282.0), width * 0.32));
        double detailX = x + pad + classW + scaled(14.0) + skillW + scaled(14.0);
        double detailW = x + width - pad - detailX;
        double listX = x + pad;
        double skillX = x + pad + classW + scaled(14.0);

        DrawText(ctx, "Classes", listX, y + scaled(28.0), scaled(18.0), bold: true, ColorGold());
        DrawText(ctx, "Choose a skill family", listX, y + scaled(48.0), scaled(10.0), bold: false, ColorMuted(), maxWidth: classW);
        double classY = y + scaled(82.0);
        for (int i = 0; i < packet.Classes.Length; i++)
        {
            HubClassPacket classEntry = packet.Classes[i];
            double rowY = classY + i * scaled(52.0);
            bool selected = i == selectedClassIndex;
            DrawSelection(ctx, listX, rowY - scaled(28.0), classW, scaled(44.0), selected);
            DrawIconTile(ctx, listX + scaled(6.0), rowY - scaled(23.0), scaled(34.0), classEntry.Icon, classEntry.Color, !selected);
            DrawText(ctx, classEntry.Name, listX + scaled(48.0), rowY - scaled(4.0), scaled(13.0), bold: selected, selected ? ColorGold() : ColorText(), maxWidth: classW - scaled(54.0));
            DrawText(ctx, classEntry.Skills.Count(skill => skill.LearnedLevel > 0) + "/" + classEntry.Skills.Length + " learned", listX + scaled(48.0), rowY + scaled(11.0), scaled(9.0), bold: false, ColorMuted(), maxWidth: classW - scaled(54.0));
            clickRegions.Add(new ClickRegion(ClickKind.Class, i, listX, rowY - scaled(30.0), classW, scaled(48.0)));
        }

        DrawVerticalSeparator(ctx, skillX - scaled(8.0), y + scaled(14.0), height - scaled(28.0));

        HubClassPacket cls = packet.Classes[GameMath.Clamp(selectedClassIndex, 0, packet.Classes.Length - 1)];
        HubSkillPacket[] skills = cls.Skills.OrderBy(skill => skill.RequiredLevel).ToArray();
        selectedSkillIndex = GameMath.Clamp(selectedSkillIndex, 0, Math.Max(0, skills.Length - 1));

        DrawText(ctx, cls.Name + " Skills", skillX, y + scaled(28.0), scaled(18.0), bold: true, ColorGold(), maxWidth: skillW);
        DrawText(ctx, "Select a learned skill, then assign it to a slot.", skillX, y + scaled(48.0), scaled(10.0), bold: false, ColorMuted(), maxWidth: skillW);
        if (skills.Length == 0)
        {
            DrawWrappedText(ctx, "Prototype class · no authored skills are loaded yet.", skillX, y + scaled(92.0), skillW, scaled(12.0), ColorMuted(), 3);
        }
        double skillY = y + scaled(86.0);
        for (int i = 0; i < skills.Length; i++)
        {
            HubSkillPacket skill = skills[i];
            bool selected = i == selectedSkillIndex;
            if (selected)
            {
                DrawSelection(ctx, skillX - scaled(3.0), skillY - scaled(30.0), skillW + scaled(6.0), scaled(68.0), true);
            }

            DrawSkillCard(ctx, skillX, skillY, skillW, skill);
            clickRegions.Add(new ClickRegion(ClickKind.Skill, i, skillX - scaled(3.0), skillY - scaled(32.0), skillW + scaled(6.0), scaled(72.0)));
            hoverRegions.Add(new HoverRegion(skillX - scaled(3.0), skillY - scaled(32.0), skillW + scaled(6.0), scaled(72.0), BuildSkillTooltip(skill)));
            skillY += scaled(77.0);
            if (skillY > y + height - scaled(112.0)) break;
        }

        DrawVerticalSeparator(ctx, detailX - scaled(8.0), y + scaled(14.0), height - scaled(28.0));
        DrawClassSkillDetail(ctx, detailX, y, detailW, height, cls, skills.Length > 0 ? skills[selectedSkillIndex] : null);
    }

    private void DrawClassSkillDetail(Context ctx, double x, double y, double width, double height, HubClassPacket cls, HubSkillPacket? skill)
    {
        DrawIconTile(ctx, x, y + scaled(12.0), scaled(42.0), cls.Icon, cls.Color, false);
        DrawText(ctx, cls.Name, x + scaled(52.0), y + scaled(35.0), scaled(18.0), bold: true, ColorGold(), maxWidth: width - scaled(52.0));
        double descY = DrawWrappedText(ctx, cls.Description, x, y + scaled(72.0), width, scaled(11.0), ColorMuted(), 3);

        if (skill != null)
        {
            double skillY = Math.Max(descY + scaled(15.0), y + scaled(132.0));
            DrawIconTile(ctx, x, skillY - scaled(18.0), scaled(44.0), skill.Icon, skill.Color, skill.LearnedLevel <= 0);
            DrawText(ctx, skill.Name, x + scaled(54.0), skillY + scaled(4.0), scaled(17.0), bold: true, ColorGold(), maxWidth: width - scaled(54.0));
            string rank = skill.LearnedLevel > 0 ? "Rank " + skill.LearnedLevel + "/" + Math.Max(1, skill.MaxLevel) : "Locked · level " + Math.Max(1, skill.RequiredLevel);
            DrawText(ctx, rank, x + scaled(54.0), skillY + scaled(23.0), scaled(10.0), bold: true, skill.LearnedLevel > 0 ? ColorGreen() : ColorMuted(), maxWidth: width - scaled(54.0));
            double textY = DrawWrappedText(ctx, skill.Description, x, skillY + scaled(52.0), width, scaled(11.0), ColorText(), 4);

            double metricY = textY + scaled(16.0);
            double metricGap = scaled(8.0);
            double metricW = (width - metricGap) / 2.0;
            string damageLabel = string.Equals(skill.TimingMode, "instant", StringComparison.OrdinalIgnoreCase) ? "Damage" : "Damage / hit";
            DrawSmallField(ctx, x, metricY, metricW, damageLabel, FormatNumber(skill.Damage) + " " + ShortCode(skill.DamageType));
            DrawSmallField(ctx, x + metricW + metricGap, metricY, metricW, "Cooldown", skill.CooldownSeconds.ToString("0.#") + "s");
            DrawSmallField(ctx, x, metricY + scaled(34.0), metricW, "Cost", SkillCostText(skill));
            DrawSmallField(ctx, x + metricW + metricGap, metricY + scaled(34.0), metricW, "Delivery", SkillDeliveryText(skill));
            DrawSmallField(ctx, x, metricY + scaled(68.0), metricW, "Range", SkillRangeText(skill));
            DrawSmallField(ctx, x + metricW + metricGap, metricY + scaled(68.0), metricW, SkillShapeLabel(skill), SkillShapeText(skill));

            double tagY = metricY + scaled(112.0);
            double tagX = x;
            for (int i = 0; i < skill.Tags.Length; i++)
            {
                string tag = skill.Tags[i];
                double tagW = Math.Max(scaled(42.0), Math.Min(scaled(90.0), scaled(16.0) + tag.Length * scaled(6.0)));
                if (tagX + tagW > x + width)
                {
                    tagX = x;
                    tagY += scaled(24.0);
                }

                DrawTagPill(ctx, tagX, tagY, tagW, tag);
                tagX += tagW + scaled(6.0);
            }
        }

        double slotsY = y + height - scaled(94.0);
        DrawText(ctx, "Combat Loadout", x, slotsY, scaled(13.0), bold: true, ColorGold(), maxWidth: width);
        DrawText(ctx, "Click a slot to assign the selected learned skill.", x, slotsY + scaled(18.0), scaled(9.0), bold: false, ColorMuted(), maxWidth: width);
        double slotY = slotsY + scaled(30.0);
        int slots = VisibleHotbarSlots();
        double slotGap = scaled(7.0);
        double slotW = (width - slotGap * (slots - 1)) / slots;
        for (int i = 0; i < slots; i++)
        {
            HubSkillPacket? equipped = packet.Classes.SelectMany(entry => entry.Skills).FirstOrDefault(entry => entry.EquippedSlot == i + 1);
            double slotX = x + i * (slotW + slotGap);
            RoundedRectangle(ctx, slotX, slotY, slotW, scaled(48.0), scaled(2.0));
            ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.28);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.95, 0.58, 0.12, equipped != null ? 0.66 : 0.30);
            ctx.Stroke();
            DrawText(ctx, BindingForSlot(i), slotX + slotW / 2.0, slotY + scaled(13.0), scaled(8.0), bold: true, ColorMuted(), center: true, maxWidth: slotW - scaled(4.0));
            if (equipped != null)
            {
                double[] accent = ParseHexColor(equipped.Color);
                double iconSize = scaled(24.0);
                VrpgIconPainter.Draw(ctx, equipped.Icon, slotX + (slotW - iconSize) / 2.0, slotY + scaled(18.0), iconSize, accent[0], accent[1], accent[2]);
            }
            else
            {
                DrawText(ctx, "EMPTY", slotX + slotW / 2.0, slotY + scaled(36.0), scaled(8.0), bold: true, ColorMuted(), center: true, maxWidth: slotW - scaled(4.0));
            }
            clickRegions.Add(new ClickRegion(ClickKind.SkillSlot, i, slotX, slotY, slotW, scaled(48.0)));
        }
    }

    private static void DrawVerticalSeparator(Context ctx, double x, double y, double height)
    {
        ctx.SetSourceRGBA(1.0, 0.62, 0.06, 0.22);
        ctx.LineWidth = scaled(1.0);
        ctx.MoveTo(x, y);
        ctx.LineTo(x, y + height);
        ctx.Stroke();
    }

    private void DrawTagPill(Context ctx, double x, double y, double width, string tag)
    {
        RoundedRectangle(ctx, x, y - scaled(13.0), width, scaled(20.0), scaled(2.0));
        ctx.SetSourceRGBA(0.70, 0.22, 0.05, 0.30);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1.0, 0.62, 0.06, 0.42);
        ctx.Stroke();
        DrawText(ctx, tag, x + width / 2.0, y + scaled(1.0), scaled(9.0), bold: true, ColorMuted(), center: true, maxWidth: width - scaled(8.0));
    }

    private void DrawTalentPageWide(Context ctx, double x, double y, double width, double height)
    {
        DrawPane(ctx, x, y, width, height);
        double pad = scaled(14.0);
        double gap = scaled(22.0);
        double detailW = Math.Min(scaled(285.0), Math.Max(scaled(245.0), width * 0.28));
        double treeW = width - detailW - gap;
        double detailX = x + treeW + gap;

        DrawTalentTree(ctx, x, y, treeW, height);
        DrawVerticalSeparator(ctx, detailX - gap / 2.0, y + scaled(18.0), height - scaled(36.0));
        DrawTalentSelectionDetail(ctx, detailX, y, detailW - pad, height);

        lastMainX = x + pad;
        lastMainY = y + scaled(76.0);
        lastMainW = treeW - pad * 2.0;
        lastMainH = height - scaled(96.0);
    }

    private void DrawTalentSelectionDetail(Context ctx, double x, double y, double width, double height)
    {
        DrawText(ctx, "Selected Talent", x, y + scaled(30.0), scaled(18.0), bold: true, ColorGold(), maxWidth: width);
        if (talentGraph.Count == 0 || talentGraph.SelectedNode == null)
        {
            DrawWrappedText(ctx, "No talent nodes are loaded.", x, y + scaled(68.0), width, scaled(13.0), ColorText(), 3);
            return;
        }

        TalentTreeNodePacket node = talentGraph.SelectedNode;
        TalentGraphNodeState state = talentGraph.StateFor(node);
        string stateText = state switch
        {
            TalentGraphNodeState.Allocated => "Allocated",
            TalentGraphNodeState.PendingAllocation => "Planned allocation",
            TalentGraphNodeState.PendingRefund => "Planned refund",
            TalentGraphNodeState.Available => "Available",
            TalentGraphNodeState.ConnectedNoPoints => "Needs points",
            _ => "Not connected"
        };

        bool hasAuthoredIdentity = node.Starter || !string.IsNullOrWhiteSpace(node.Name);
        double effectY;
        if (hasAuthoredIdentity)
        {
            DrawText(ctx, TalentNodeTitle(node), x, y + scaled(69.0), scaled(17.0), bold: true, ColorText(), maxWidth: width);
            if (node.Starter)
            {
                DrawText(ctx, Humanize(node.Foundation) + " starting point", x, y + scaled(91.0), scaled(10.0), bold: true, ColorGold(), maxWidth: width);
            }

            effectY = y + scaled(node.Starter ? 119.0 : 99.0);
        }
        else
        {
            effectY = y + scaled(70.0);
        }

        DrawText(ctx, "Effects", x, effectY, scaled(12.0), bold: true, ColorGold(), maxWidth: width);
        double cursorY = effectY + scaled(26.0);
        if (node.Modifiers.Length == 0)
        {
            DrawText(ctx, "No effects assigned", x, cursorY, scaled(12.0), bold: false, ColorMuted(), maxWidth: width);
            cursorY += scaled(23.0);
        }
        else
        {
            for (int i = 0; i < node.Modifiers.Length && i < 4; i++)
            {
                DrawText(ctx, FormatModifierText(node.Modifiers[i]), x, cursorY, scaled(16.0), bold: true, ColorGreen(), maxWidth: width);
                cursorY += scaled(25.0);
            }
        }

        if (HasUsefulTalentDescription(node.Description))
        {
            cursorY = DrawWrappedText(ctx, node.Description, x, cursorY + scaled(8.0), width, scaled(11.0), ColorText(), 4);
        }

        string costText = node.Starter
            ? "Free"
            : Math.Max(1, node.Cost) + " point" + (Math.Max(1, node.Cost) == 1 ? "" : "s");
        DrawText(ctx, costText + " · " + stateText, x, cursorY + scaled(15.0), scaled(11.0), bold: true, ColorMuted(), maxWidth: width);

        int changes = pendingTalentAllocations.Count + pendingTalentRefunds.Count;
        int remainingRespec = Math.Max(0, (packet.Resources?.RespecPoints ?? 0) - pendingTalentRefunds.Count);
        double planY = y + height - scaled(132.0);
        DrawSmallField(ctx, x, planY, width, "Plan", "+" + pendingTalentAllocations.Count + " / −" + pendingTalentRefunds.Count);
        DrawSmallField(ctx, x, planY + scaled(34.0), width, "After plan", PlannedTalentPoints() + " talent · " + remainingRespec + " respec");
        double buttonY = planY + scaled(72.0);
        DrawActionButton(ctx, x, buttonY, width, scaled(31.0), actionPending ? "APPLYING…" : "APPLY " + changes + " CHANGE" + (changes == 1 ? "" : "S"), changes > 0 && !actionPending, ClickKind.ApplyTalentPlan);
        if (changes > 0)
            DrawActionButton(ctx, x, buttonY + scaled(37.0), width, scaled(25.0), "CANCEL PLAN", !actionPending, ClickKind.CancelTalentPlan, secondary: true);
    }

    private void DrawTalentTree(Context ctx, double x, double y, double width, double height)
    {
        string treeTitle = string.IsNullOrWhiteSpace(talentGraph.Snapshot.TreeName) ? "Talent Tree" : talentGraph.Snapshot.TreeName;
        DrawText(ctx, treeTitle, x + scaled(14.0), y + scaled(28.0), scaled(18.0), bold: true, ColorGold(), maxWidth: width - scaled(packet.CanEditTalentTree ? 190.0 : 28.0));
        DrawText(ctx, "Left click plans · right click plans a refund · Apply commits.", x + scaled(14.0), y + scaled(52.0), scaled(12.0), bold: false, ColorMuted(), maxWidth: width - scaled(28.0));
        if (packet.CanEditTalentTree)
        {
            double editWidth = scaled(154.0);
            DrawActionButton(ctx, x + width - editWidth - scaled(14.0), y + scaled(14.0), editWidth, scaled(29.0), "ADMIN · EDIT TREE", true, ClickKind.OpenTalentEditor, secondary: true);
        }

        double treeX = x + scaled(14.0);
        double treeY = y + scaled(76.0);
        double treeW = width - scaled(28.0);
        double treeH = height - scaled(96.0);
        RoundedRectangle(ctx, treeX, treeY, treeW, treeH, scaled(2.0));
        ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.20);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 0.62, 0.06, 0.22);
        ctx.Stroke();

        ctx.Save();
        ctx.Rectangle(treeX, treeY, treeW, treeH);
        ctx.Clip();

        talentGraph.Draw(ctx, treeX, treeY, treeW, treeH);
        for (int i = 0; i < talentGraph.VisibleNodes.Count; i++)
        {
            TalentGraphNodeVisual visual = talentGraph.VisibleNodes[i];
            hoverRegions.Add(new HoverRegion(
                visual.X - visual.Radius,
                visual.Y - visual.Radius,
                visual.Radius * 2.0,
                visual.Radius * 2.0,
                BuildTalentTooltip(visual.Node, visual.State)));
        }

        ctx.Restore();
        DrawTalentLegend(ctx, treeX + scaled(8.0), treeY + treeH - scaled(47.0));
        DrawText(ctx, "Zoom " + talentGraph.Zoom.ToString("0.00") + "x", treeX + treeW - scaled(12.0), treeY + treeH - scaled(12.0), scaled(11.0), bold: true, ColorMuted(), right: true);
    }

    private void DrawLibrarySummary(Context ctx, double x, double y, double width, double height)
    {
        double pad = scaled(20.0);
        DrawText(ctx, "Library", x + pad, y + scaled(32.0), scaled(20.0), bold: true, ColorGold());
        DrawText(ctx, "Browse generated documentation for every loaded VRPG system and content pack.", x + pad, y + scaled(58.0), scaled(13.0), bold: false, ColorMuted(), maxWidth: width - pad * 2.0);

        double buttonY = y + scaled(112.0);
        double buttonW = Math.Min(scaled(430.0), width - pad * 2.0);
        DrawSelection(ctx, x + pad, buttonY - scaled(24.0), buttonW, scaled(42.0), true);
        DrawText(ctx, "Open Full Library", x + pad + scaled(16.0), buttonY + scaled(2.0), scaled(16.0), bold: true, ColorGold(), maxWidth: buttonW - scaled(32.0));
        clickRegions.Add(new ClickRegion(ClickKind.OpenLibrary, 0, x + pad, buttonY - scaled(27.0), buttonW, scaled(48.0)));

        double gridY = y + scaled(190.0);
        double gridGap = scaled(14.0);
        double fieldW = (width - pad * 2.0 - gridGap) / 2.0;
        DrawText(ctx, "Loaded Records", x + pad, gridY - scaled(26.0), scaled(15.0), bold: true, ColorGold(), maxWidth: width - pad * 2.0);
        DrawSmallField(ctx, x + pad, gridY, fieldW, "Entries", packet.Entries.Length.ToString());
        DrawSmallField(ctx, x + pad + fieldW + gridGap, gridY, fieldW, "Status Effects", CountCategory("status_effects").ToString());
        DrawSmallField(ctx, x + pad, gridY + scaled(38.0), fieldW, "Classes", packet.Classes.Length.ToString());
        DrawSmallField(ctx, x + pad + fieldW + gridGap, gridY + scaled(38.0), fieldW, "Talents", talentGraph.Count.ToString());

        DrawWrappedText(ctx,
            "The library is generated from the same data records used by gameplay, so authored skills, stats, damage types, status effects, and dungeon content stay discoverable in game.",
            x + pad,
            gridY + scaled(101.0),
            Math.Min(scaled(700.0), width - pad * 2.0),
            scaled(12.0),
            ColorText(),
            4);
    }

    private void DrawFooter(Context ctx, double x, double y, double width)
    {
        ctx.SetSourceRGBA(1, 0.62, 0.05, 0.30);
        ctx.MoveTo(x, y - scaled(9.0));
        ctx.LineTo(x + width, y - scaled(9.0));
        ctx.LineWidth = scaled(1.0);
        ctx.Stroke();
        string hint = selectedTab switch
        {
            0 => "Hover a stat for scaling details. Stat changes are locked during combat.",
            1 => "Skills are learned with admin commands in this slice; loadout assignments are persistent.",
            2 => "Choose one starting point, then grow through connected nodes.",
            3 => "Library entries are generated from the gameplay data registry.",
            _ => "Options are saved to your player profile."
        };
        double[] color = ColorMuted();
        if (actionPending)
        {
            hint = "Updating server state…";
            color = ColorGold();
        }
        else if (!string.IsNullOrWhiteSpace(packet.FeedbackMessage))
        {
            hint = packet.FeedbackMessage;
            color = packet.FeedbackError ? ColorError() : ColorGreen();
        }

        DrawText(ctx, hint, x, y + scaled(8.0), scaled(11.0), bold: actionPending || !string.IsNullOrWhiteSpace(packet.FeedbackMessage), color, maxWidth: width);
    }

    private void DrawBackdrop(Context ctx, double width, double height)
    {
        RoundedRectangle(ctx, scaled(2.0), scaled(2.0), width - scaled(4.0), height - scaled(4.0), scaled(2.0));
        using (LinearGradient gradient = new LinearGradient(0, 0, width, height))
        {
            gradient.AddColorStop(0, new Color(0.10, 0.035, 0.022, 0.98));
            gradient.AddColorStop(0.5, new Color(0.20, 0.065, 0.032, 0.98));
            gradient.AddColorStop(1, new Color(0.065, 0.030, 0.023, 0.98));
            ctx.SetSource(gradient);
            ctx.FillPreserve();
        }

        ctx.LineWidth = scaled(2.0);
        ctx.SetSourceRGBA(VrpgGuiTheme.BorderR, VrpgGuiTheme.BorderG, VrpgGuiTheme.BorderB, 0.95);
        ctx.Stroke();
    }

    private void DrawHeaderTab(Context ctx, double x, double y, double width, string text, bool active)
    {
        ctx.SetSourceRGBA(1.0, 0.62, 0.05, active ? 0.92 : 0.35);
        ctx.LineWidth = scaled(active ? 1.4 : 1.0);
        ctx.MoveTo(x, y + scaled(26.0));
        ctx.LineTo(x + width, y + scaled(26.0));
        ctx.Stroke();
        DrawText(ctx, text, x + width / 2.0, y + scaled(20.0), scaled(21.0), bold: true, active ? ColorGold() : ColorMuted(), center: true);
    }

    private void DrawPane(Context ctx, double x, double y, double width, double height)
    {
        RoundedRectangle(ctx, x, y, width, height, scaled(3.0));
        ctx.SetSourceRGBA(0.06, 0.028, 0.020, 0.60);
        ctx.FillPreserve();
        ctx.LineWidth = scaled(1.0);
        ctx.SetSourceRGBA(0.95, 0.58, 0.12, 0.45);
        ctx.Stroke();
    }

    private void DrawSelection(Context ctx, double x, double y, double width, double height, bool selected)
    {
        RoundedRectangle(ctx, x, y, width, height, scaled(2.0));
        using LinearGradient gradient = new LinearGradient(x, y, x + width, y);
        gradient.AddColorStop(0, new Color(0.70, 0.22, 0.05, selected ? 0.56 : 0.28));
        gradient.AddColorStop(1, new Color(1.00, 0.63, 0.08, selected ? 0.26 : 0.12));
        ctx.SetSource(gradient);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 0.68, 0.16, selected ? 0.70 : 0.35);
        ctx.Stroke();
    }

    private void DrawSmallField(Context ctx, double x, double y, double width, string label, string value)
    {
        RoundedRectangle(ctx, x, y - scaled(16.0), width, scaled(27.0), scaled(2.0));
        ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.18);
        ctx.Fill();
        DrawText(ctx, label, x + scaled(8.0), y, scaled(11.0), bold: true, ColorMuted(), maxWidth: width * 0.48);
        DrawText(ctx, value, x + width - scaled(8.0), y, scaled(11.0), bold: true, ColorText(), right: true, maxWidth: width * 0.45);
    }

    private void DrawStatAllocationRow(
        Context ctx,
        double x,
        double y,
        double width,
        string name,
        int value,
        int statIndex,
        bool primary,
        bool canDecrease,
        bool canIncrease,
        bool decreaseWarns,
        bool increaseWarns)
    {
        double height = scaled(48.0);
        RoundedRectangle(ctx, x, y, width, height, scaled(2.0));
        ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.18);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 0.62, 0.06, 0.22);
        ctx.Stroke();
        DrawText(ctx, name, x + scaled(12.0), y + scaled(30.0), scaled(15.0), bold: true, ColorGold(), maxWidth: width - scaled(210.0));
        if (primary)
        {
            DrawText(ctx, "PRIMARY", x + scaled(128.0), y + scaled(29.0), scaled(9.0), bold: true, ColorGreen(), maxWidth: scaled(64.0));
        }

        double plusX = x + width - scaled(42.0);
        double minusX = plusX - scaled(76.0);
        double buttonY = y + scaled(8.0);
        DrawStatButton(ctx, minusX, buttonY, "−", canDecrease, decreaseWarns);
        DrawText(ctx, value.ToString(), minusX + scaled(54.0), y + scaled(30.0), scaled(16.0), bold: true, ColorText(), center: true, maxWidth: scaled(34.0));
        DrawStatButton(ctx, plusX, buttonY, "+", canIncrease, increaseWarns);

        if (canDecrease)
        {
            clickRegions.Add(new ClickRegion(ClickKind.StatDecrease, statIndex, minusX, buttonY, scaled(34.0), scaled(32.0)));
        }

        if (canIncrease)
        {
            clickRegions.Add(new ClickRegion(ClickKind.StatIncrease, statIndex, plusX, buttonY, scaled(34.0), scaled(32.0)));
        }
    }

    private static void DrawStatButton(Context ctx, double x, double y, string label, bool enabled, bool warns)
    {
        RoundedRectangle(ctx, x, y, scaled(34.0), scaled(32.0), scaled(2.0));
        ctx.SetSourceRGBA(warns ? 0.55 : 0.35, warns ? 0.20 : 0.12, 0.03, enabled ? 0.68 : 0.20);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1.0, warns ? 0.36 : 0.62, 0.06, enabled ? 0.82 : 0.20);
        ctx.Stroke();
        DrawText(ctx, label, x + scaled(17.0), y + scaled(23.0), scaled(18.0), bold: true, enabled ? ColorGold() : ColorMuted(), center: true);
    }

    private void DrawSkillCard(Context ctx, double x, double y, double width, HubSkillPacket skill)
    {
        RoundedRectangle(ctx, x, y - scaled(28.0), width, scaled(66.0), scaled(2.0));
        ctx.SetSourceRGBA(0.0, 0.0, 0.0, 0.18);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 0.62, 0.06, 0.25);
        ctx.Stroke();
        DrawIconTile(ctx, x + scaled(7.0), y - scaled(21.0), scaled(42.0), skill.Icon, skill.Color, skill.LearnedLevel <= 0);
        DrawText(ctx, skill.Name, x + scaled(58.0), y - scaled(5.0), scaled(14.0), bold: true, skill.LearnedLevel > 0 ? ColorGold() : ColorMuted(), maxWidth: width - scaled(132.0));
        string status = skill.LearnedLevel > 0 ? "Rank " + skill.LearnedLevel + "/" + Math.Max(1, skill.MaxLevel) : "Locked · level " + Math.Max(1, skill.RequiredLevel);
        DrawText(ctx, status, x + scaled(58.0), y + scaled(14.0), scaled(9.0), bold: true, skill.LearnedLevel > 0 ? ColorGreen() : ColorMuted(), maxWidth: width - scaled(70.0));
        string slot = skill.EquippedSlot > 0 ? "SLOT " + skill.EquippedSlot : "";
        DrawText(ctx, slot, x + width - scaled(10.0), y - scaled(5.0), scaled(9.0), bold: true, ColorGold(), right: true, maxWidth: scaled(54.0));
        DrawText(ctx, SkillCardDamageText(skill) + "  ·  " + skill.CooldownSeconds.ToString("0.#") + "s", x + scaled(58.0), y + scaled(31.0), scaled(9.0), bold: false, ColorMuted(), maxWidth: width - scaled(68.0));
    }

    private void DrawIconTile(Context ctx, double x, double y, double size, string icon, string color, bool muted)
    {
        double[] accent = ParseHexColor(color);
        RoundedRectangle(ctx, x, y, size, size, scaled(3.0));
        ctx.SetSourceRGBA(accent[0], accent[1], accent[2], muted ? 0.10 : 0.24);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(accent[0], accent[1], accent[2], muted ? 0.24 : 0.78);
        ctx.LineWidth = scaled(1.2);
        ctx.Stroke();
        VrpgIconPainter.Draw(ctx, icon, x + scaled(3.0), y + scaled(3.0), size - scaled(6.0), accent[0], accent[1], accent[2], muted ? 0.38 : 1.0);
    }

    private void DrawActionButton(
        Context ctx,
        double x,
        double y,
        double width,
        double height,
        string label,
        bool enabled,
        ClickKind kind,
        bool secondary = false)
    {
        RoundedRectangle(ctx, x, y, width, height, scaled(2.0));
        ctx.SetSourceRGBA(secondary ? 0.12 : 0.36, secondary ? 0.05 : 0.14, 0.02, enabled ? 0.66 : 0.20);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1.0, 0.62, 0.06, enabled ? (secondary ? 0.46 : 0.84) : 0.20);
        ctx.Stroke();
        DrawText(ctx, label, x + width / 2.0, y + height * 0.66, scaled(10.0), bold: true,
            enabled ? (secondary ? ColorMuted() : ColorGold()) : ColorMuted(), center: true, maxWidth: width - scaled(12.0));
        if (enabled)
        {
            clickRegions.Add(new ClickRegion(kind, 0, x, y, width, height));
        }
    }

    private string BindingForSlot(int slot)
    {
        int index = GameMath.Clamp(slot, 0, VrpgHotkeys.SkillCodes.Length - 1);
        HotKey? hotKey = api.Input.GetHotKeyByCode(VrpgHotkeys.SkillCodes[index]);
        return hotKey?.CurrentMapping?.ToString() ?? "Slot " + (index + 1);
    }

    private static string SkillCostText(HubSkillPacket skill)
    {
        if (skill.ResourceCost <= 0f || string.Equals(skill.ResourceType, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "Free";
        }

        string rate = string.Equals(skill.ResourceCostMode, "per_second", StringComparison.OrdinalIgnoreCase) ? "/s" : "";
        return FormatNumber(skill.ResourceCost) + " " + Humanize(skill.ResourceType) + rate;
    }

    private static string SkillDeliveryText(HubSkillPacket skill)
    {
        string delivery;
        if (!string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase))
        {
            delivery = Humanize(skill.Delivery);
        }
        else
        {
            delivery = string.Equals(skill.ProjectileImpactMode, "ground", StringComparison.OrdinalIgnoreCase)
                ? "Projectile · ground"
                : "Projectile · direct";
        }

        if (string.Equals(skill.TimingMode, "sequence", StringComparison.OrdinalIgnoreCase))
        {
            return delivery + " · " + Math.Max(2, skill.HitCount) + " hits";
        }

        return string.Equals(skill.TimingMode, "channel", StringComparison.OrdinalIgnoreCase)
            ? delivery + " · hold"
            : delivery;
    }

    private static string SkillCardDamageText(HubSkillPacket skill)
    {
        string damage = FormatNumber(skill.Damage) + " dmg";
        if (string.Equals(skill.TimingMode, "sequence", StringComparison.OrdinalIgnoreCase))
        {
            return damage + "/hit ×" + Math.Max(2, skill.HitCount);
        }

        return string.Equals(skill.TimingMode, "channel", StringComparison.OrdinalIgnoreCase)
            ? damage + "/tick"
            : damage;
    }

    private static string SkillRangeText(HubSkillPacket skill)
    {
        return string.Equals(skill.Delivery, "circle", StringComparison.OrdinalIgnoreCase)
            ? "Self"
            : skill.Range.ToString("0.#") + "m";
    }

    private static string SkillShapeLabel(HubSkillPacket skill)
    {
        return (skill.Delivery ?? "").StartsWith("melee_", StringComparison.OrdinalIgnoreCase)
            ? "Shape"
            : "Radius";
    }

    private static string SkillShapeText(HubSkillPacket skill)
    {
        if (string.Equals(skill.Delivery, "melee_arc", StringComparison.OrdinalIgnoreCase))
        {
            return skill.MeleeArcDegrees.ToString("0.#") + "° arc";
        }

        if (string.Equals(skill.Delivery, "melee_line", StringComparison.OrdinalIgnoreCase))
        {
            return skill.MeleeWidth.ToString("0.#") + "m wide";
        }

        if (string.Equals(skill.Delivery, "melee_single", StringComparison.OrdinalIgnoreCase))
        {
            return "Single · " + skill.MeleeWidth.ToString("0.#") + "m aim";
        }

        return skill.Radius.ToString("0.#") + "m";
    }

    private static string ShortCode(string value)
    {
        string safe = value ?? "";
        int colon = safe.IndexOf(':');
        return Humanize(colon >= 0 ? safe.Substring(colon + 1) : safe);
    }

    private static string Humanize(string value)
    {
        string cleaned = ShortNamespace(value).Replace('_', ' ').Replace('-', ' ').Trim();
        if (cleaned.Length == 0)
        {
            return "None";
        }

        return char.ToUpperInvariant(cleaned[0]) + cleaned.Substring(1);
    }

    private static string ShortNamespace(string value)
    {
        string safe = value ?? "";
        int colon = safe.IndexOf(':');
        return colon >= 0 ? safe.Substring(colon + 1) : safe;
    }

    private static double[] ParseHexColor(string value)
    {
        string hex = (value ?? "").Trim().TrimStart('#');
        if (hex.Length >= 6
            && int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int red)
            && int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out int green)
            && int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out int blue))
        {
            return new[] { red / 255.0, green / 255.0, blue / 255.0, 1.0 };
        }

        return ColorGold();
    }

    private void DrawResourceBar(Context ctx, double x, double y, double width, string label, float current, float max, double r, double g, double b)
    {
        float safeMax = Math.Max(1f, max);
        double fill = Math.Max(0.0, Math.Min(1.0, current / safeMax));
        DrawText(ctx, label, x, y, scaled(12.0), bold: true, ColorText(), maxWidth: width * 0.45);
        DrawText(ctx, FormatNumber(current) + " / " + FormatNumber(safeMax), x + width, y, scaled(12.0), bold: true, ColorText(), right: true, maxWidth: width * 0.50);
        RoundedRectangle(ctx, x, y + scaled(7.0), width, scaled(14.0), scaled(2.0));
        ctx.SetSourceRGBA(0, 0, 0, 0.48);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(0.92, 0.75, 0.50, 0.42);
        ctx.Stroke();
        RoundedRectangle(ctx, x + scaled(2.0), y + scaled(9.0), Math.Max(0.0, (width - scaled(4.0)) * fill), scaled(10.0), scaled(1.0));
        ctx.SetSourceRGBA(r, g, b, 0.92);
        ctx.Fill();
    }

    private void DrawTalentLegend(Context ctx, double x, double y)
    {
        double boxW = scaled(402.0);
        double boxH = scaled(42.0);
        RoundedRectangle(ctx, x, y, boxW, boxH, scaled(3.0));
        // The legend overlays the graph. Keep its field opaque so underlying edges
        // cannot masquerade as connection marks between the legend samples.
        ctx.SetSourceRGBA(0.025, 0.018, 0.015, 0.98);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1.0, 0.62, 0.06, 0.20);
        ctx.Stroke();

        double rowOne = y + scaled(12.0);
        double rowTwo = y + scaled(31.0);
        DrawTalentLegendItem(ctx, x + scaled(10.0), rowOne, "Available", TalentGraphNodeState.Available);
        DrawTalentLegendItem(ctx, x + scaled(108.0), rowOne, "Planned", TalentGraphNodeState.PendingAllocation);
        DrawTalentLegendItem(ctx, x + scaled(196.0), rowOne, "Allocated", TalentGraphNodeState.Allocated);
        DrawTalentLegendItem(ctx, x + scaled(10.0), rowTwo, "Needs points", TalentGraphNodeState.ConnectedNoPoints);
        DrawTalentLegendItem(ctx, x + scaled(108.0), rowTwo, "Refund plan", TalentGraphNodeState.PendingRefund);
        DrawTalentLegendItem(ctx, x + scaled(214.0), rowTwo, "Not connected", TalentGraphNodeState.Blocked);
        DrawTalentLegendItem(ctx, x + scaled(326.0), rowTwo, "Free start", TalentGraphNodeState.Available, starter: true);
    }

    private void DrawTalentLegendItem(Context ctx, double x, double y, string label, TalentGraphNodeState state, bool starter = false)
    {
        DrawTalentLegendDot(ctx, x, y, state, starter);
        DrawText(ctx, label, x + scaled(11.0), y + scaled(4.0), scaled(9.0), bold: true, ColorMuted());
    }

    private static void DrawTalentLegendDot(Context ctx, double x, double y, TalentGraphNodeState state, bool starter)
    {
        double centerX = x + scaled(4.0);
        if (starter)
        {
            ctx.Arc(centerX, y, scaled(6.2), 0, Math.PI * 2.0);
            ctx.SetSourceRGBA(0.26, 0.13, 0.02, 0.96);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(1.0, 0.72, 0.10, 0.95);
            ctx.LineWidth = scaled(1.2);
            ctx.Stroke();
        }

        ctx.Arc(centerX, y, scaled(4.0), 0, Math.PI * 2.0);
        switch (state)
        {
            case TalentGraphNodeState.PendingAllocation:
                ctx.SetSourceRGBA(0.12, 0.48, 0.72, 1.0);
                break;
            case TalentGraphNodeState.PendingRefund:
                ctx.SetSourceRGBA(0.62, 0.12, 0.08, 1.0);
                break;
            case TalentGraphNodeState.Allocated:
                ctx.SetSourceRGBA(0.25, 0.58, 0.13, 1.0);
                break;
            case TalentGraphNodeState.Available:
                ctx.SetSourceRGBA(0.24, 0.18, 0.04, 1.0);
                break;
            case TalentGraphNodeState.ConnectedNoPoints:
                ctx.SetSourceRGBA(0.13, 0.11, 0.06, 0.95);
                break;
            default:
                ctx.SetSourceRGBA(0.055, 0.045, 0.045, 0.86);
                break;
        }
        ctx.FillPreserve();
        double outlineAlpha = state == TalentGraphNodeState.Available ? 0.78
            : state == TalentGraphNodeState.ConnectedNoPoints ? 0.42
            : 0.24;
        ctx.SetSourceRGBA(1.0, 0.62, 0.06, outlineAlpha);
        ctx.LineWidth = scaled(1.0);
        ctx.Stroke();
    }

    private void DrawCloseButton(Context ctx, double x, double y)
    {
        ctx.LineWidth = scaled(2.0);
        ctx.SetSourceRGBA(0.98, 0.88, 0.70, 0.92);
        ctx.MoveTo(x - scaled(7.0), y - scaled(7.0));
        ctx.LineTo(x + scaled(7.0), y + scaled(7.0));
        ctx.MoveTo(x + scaled(7.0), y - scaled(7.0));
        ctx.LineTo(x - scaled(7.0), y + scaled(7.0));
        ctx.Stroke();
    }

    private void DrawTooltip(Context ctx, double width, double height)
    {
        if (hoverInfo == null)
        {
            return;
        }

        TooltipRenderer.Draw(ctx, hoverInfo, Bounds.OuterWidth, Bounds.OuterHeight, api.Input.MouseX - Bounds.absX, api.Input.MouseY - Bounds.absY);
    }

    private sealed class TooltipRenderer
    {
        public static void Draw(Context ctx, TooltipContent content, double boundsWidth, double boundsHeight, double mouseX, double mouseY)
        {
            double tooltipW = scaled(310.0);
            double titleHeight = scaled(30.0);
            double bodyHeight = content.Lines.Sum(line =>
                scaled(line.GapBefore)
                + Math.Max(1, WrappedLineCount(ctx, line.Text, tooltipW - scaled(24.0), scaled(line.FontSize))) * scaled(line.FontSize * 1.3));
            double tooltipH = Math.Min(scaled(260.0), titleHeight + scaled(18.0) + bodyHeight);
            double tooltipX = Math.Min(boundsWidth - tooltipW - scaled(12.0), mouseX + scaled(18.0));
            double tooltipY = Math.Min(boundsHeight - tooltipH - scaled(12.0), mouseY + scaled(18.0));
            tooltipX = Math.Max(scaled(12.0), tooltipX);
            tooltipY = Math.Max(scaled(12.0), tooltipY);

            RoundedRectangle(ctx, tooltipX, tooltipY, tooltipW, tooltipH, scaled(2.0));
            ctx.SetSourceRGBA(0.03, 0.015, 0.012, 0.94);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(1, 0.75, 0.20, 0.85);
            ctx.LineWidth = scaled(1.2);
            ctx.Stroke();

            DrawText(ctx, content.Title, tooltipX + scaled(12.0), tooltipY + scaled(24.0), scaled(14.0), bold: true, ColorGold(), maxWidth: tooltipW - scaled(24.0));
            double y = tooltipY + scaled(50.0);
            for (int i = 0; i < content.Lines.Length; i++)
            {
                TooltipLine line = content.Lines[i];
                double fontSize = scaled(line.FontSize);
                double lineHeight = scaled(line.FontSize * 1.3);
                y += scaled(line.GapBefore);
                foreach (string wrapped in WrapText(ctx, line.Text, tooltipW - scaled(24.0), fontSize, line.Bold))
                {
                    DrawText(ctx, wrapped, tooltipX + scaled(12.0), y, fontSize, bold: line.Bold, line.Color);
                    y += lineHeight;
                    if (y > tooltipY + tooltipH - scaled(10.0)) return;
                }
            }
        }

        private static int WrappedLineCount(Context ctx, string text, double maxWidth, double size)
        {
            int count = 0;
            foreach (string _ in WrapText(ctx, text, maxWidth, size, false))
            {
                count++;
            }

            return Math.Max(1, count);
        }
    }

    private double DrawWrappedText(Context ctx, string text, double x, double y, double maxWidth, double size, double[] color, int maxLines)
    {
        foreach (string line in WrapText(ctx, text ?? "", maxWidth, size, false).Take(maxLines))
        {
            DrawText(ctx, line, x, y, size, bold: false, color);
            y += size * 1.28;
        }

        return y;
    }

    private static IEnumerable<string> WrapText(Context ctx, string text, double maxWidth, double size, bool bold)
    {
        SelectFont(ctx, size, bold);
        var current = "";
        foreach (string word in text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = string.IsNullOrEmpty(current) ? word : current + " " + word;
            if (ctx.TextExtents(next).Width > maxWidth && current.Length > 0)
            {
                yield return current;
                current = word;
            }
            else
            {
                current = next;
            }
        }

        if (current.Length > 0)
        {
            yield return current;
        }
    }

    private RpgResourcePacket? Resources() => packet.Resources;

    private Dictionary<string, int> BaseStats()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < packet.BaseStats.Length; i++)
        {
            result[NormalizeCode(packet.BaseStats[i].Code)] = packet.BaseStats[i].Value;
        }

        return result;
    }

    private static int GetStatValue(Dictionary<string, int> stats, string code)
    {
        return stats.TryGetValue(NormalizeCode(code), out int value) ? value : 0;
    }

    private string StatName(string code)
    {
        LibraryEntryPacket? entry = packet.Entries.FirstOrDefault(item => SameCode(item.Code, code) || SameCode(item.Code, "vrpg:" + code));
        return entry?.Name ?? char.ToUpperInvariant(code[0]) + code.Substring(1);
    }

    private string StatSummary(string code)
    {
        LibraryEntryPacket? entry = packet.Entries.FirstOrDefault(item => SameCode(item.Code, code) || SameCode(item.Code, "vrpg:" + code));
        return entry?.Summary ?? "";
    }

    private TooltipContent BuildStatTooltip(string code, string name, int value)
    {
        var lines = new List<TooltipLine>();
        string summary = StatSummary(code).Trim();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            AddStatSummaryLines(lines, summary);
        }

        float combatSeconds = Math.Max(0f, Resources()?.CombatLockRemainingSeconds ?? 0f);
        if (combatSeconds > 0f)
        {
            lines.Add(new TooltipLine("Stat changes locked during combat: " + Math.Ceiling(combatSeconds).ToString("0") + "s", ColorGold(), bold: true));
        }

        string primary = NormalizeCode(Resources()?.PrimaryAttribute ?? "");
        if (code == primary)
        {
            lines.Add(new TooltipLine("Current primary attribute", ColorGreen(), bold: true));
        }

        if (code == NormalizeCode(Resources()?.StartingAttributeAffinity ?? ""))
        {
            lines.Add(new TooltipLine("Starting affinity: wins tied primary values", ColorMuted(), bold: true));
        }

        AddPrimaryChangeWarning(lines, code, 1, primary, "Adding a point");
        if (value > 0)
        {
            AddPrimaryChangeWarning(lines, code, -1, primary, "Refunding a point");
        }

        lines.Add(new TooltipLine("Allocated points: " + value, ColorMuted(), bold: true));
        return new TooltipContent(name, lines.ToArray());
    }

    private void AddPrimaryChangeWarning(List<TooltipLine> lines, string code, int delta, string currentPrimary, string action)
    {
        string projected = ProjectedPrimaryAttribute(code, delta);
        if (projected == currentPrimary)
        {
            return;
        }

        if (currentPrimary == "dexterity" && projected != "dexterity")
        {
            lines.Add(new TooltipLine(action + " will disable Reflex and Evasive Step.", ColorGold(), bold: true));
        }
        else if (currentPrimary != "dexterity" && projected == "dexterity")
        {
            lines.Add(new TooltipLine(action + " will enable Reflex and Evasive Step.", ColorGreen(), bold: true));
        }
        else if (!string.IsNullOrWhiteSpace(projected))
        {
            lines.Add(new TooltipLine(action + " changes primary attribute to " + StatName(projected) + ".", ColorGold(), bold: true));
        }
    }

    private string ProjectedPrimaryAttribute(string changedCode, int delta)
    {
        Dictionary<string, int> stats = BaseStats();
        int currentTotal = BaseStatCodes.Sum(code => GetStatValue(stats, code));
        string affinity = NormalizeCode(Resources()?.StartingAttributeAffinity ?? "");
        if (currentTotal == 0 && string.IsNullOrWhiteSpace(affinity) && delta > 0)
        {
            return changedCode;
        }

        stats[changedCode] = Math.Max(0, GetStatValue(stats, changedCode) + delta);
        int highest = BaseStatCodes.Max(code => GetStatValue(stats, code));
        if (highest <= 0)
        {
            return string.IsNullOrWhiteSpace(affinity) && delta > 0 ? changedCode : affinity;
        }

        if (BaseStatCodes.Contains(affinity) && GetStatValue(stats, affinity) == highest)
        {
            return affinity;
        }

        return BaseStatCodes.First(code => GetStatValue(stats, code) == highest);
    }

    private string PrimaryAttributeStatus(RpgResourcePacket? resources, string primaryAttribute)
    {
        if (string.IsNullOrWhiteSpace(primaryAttribute))
        {
            return "Primary unchosen · your first allocated point sets starting affinity";
        }

        string primaryName = StatName(primaryAttribute);
        return resources?.EvasiveStepActive == true
            ? "Primary: " + primaryName + " · Reflex and automatic Evasive Step active"
            : "Primary: " + primaryName + " · Reflex inactive (requires primary Dexterity)";
    }

    private int VisibleHotbarSlots()
    {
        return GameMath.Clamp(packet.Options?.SkillHotbarSlots ?? 4, 4, 8);
    }

    private static string EvasiveStepStatus(RpgResourcePacket resources)
    {
        if (!resources.EvasiveStepActive)
        {
            return "Requires primary DEX";
        }

        return resources.EvasiveStepCooldownRemainingSeconds > 0.05f
            ? "Automatic · " + resources.EvasiveStepCooldownRemainingSeconds.ToString("0.0") + "s"
            : "Ready · automatic";
    }

    private static void AddStatSummaryLines(List<TooltipLine> lines, string summary)
    {
        const string GrantsMarker = "Each point grants";
        int grantsIndex = summary.IndexOf(GrantsMarker, StringComparison.OrdinalIgnoreCase);
        if (grantsIndex < 0)
        {
            lines.Add(new TooltipLine(summary, ColorText()));
            return;
        }

        string introduction = summary.Substring(0, grantsIndex).Trim().TrimEnd('.');
        if (introduction.Length > 0)
        {
            lines.Add(new TooltipLine(introduction + ".", ColorText()));
        }

        lines.Add(new TooltipLine("Each allocated point grants:", ColorMuted(), bold: true));
        string effects = summary.Substring(grantsIndex + GrantsMarker.Length).Trim().TrimStart(':').Trim().TrimEnd('.');
        string normalized = effects.Replace(", and ", ", ", StringComparison.OrdinalIgnoreCase);
        string[] entries = normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            string effect = entries[i];
            if (effect.StartsWith("and ", StringComparison.OrdinalIgnoreCase))
            {
                effect = effect.Substring(4).Trim();
            }

            lines.Add(new TooltipLine(effect, ColorGreen()));
        }
    }

    private TooltipContent BuildSkillTooltip(HubSkillPacket skill)
    {
        var lines = new List<TooltipLine>
        {
            new TooltipLine(skill.Description, ColorText()),
            new TooltipLine("Required level: " + Math.Max(1, skill.RequiredLevel), ColorMuted(), bold: true)
        };

        if (skill.Tags.Length > 0)
        {
            lines.Add(new TooltipLine("Tags: " + string.Join(", ", skill.Tags), ColorGold(), bold: true));
        }

        if (string.Equals(skill.TimingMode, "sequence", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new TooltipLine($"{Math.Max(2, skill.HitCount)} independent hits · {skill.HitIntervalSeconds:0.##}s apart", ColorGreen(), bold: true));
        }
        else if (string.Equals(skill.TimingMode, "channel", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new TooltipLine($"Hold to channel · hit every {skill.HitIntervalSeconds:0.##}s", ColorGreen(), bold: true));
        }

        return new TooltipContent(skill.Name, lines.ToArray());
    }

    private TooltipContent BuildTalentTooltip(TalentTreeNodePacket node, TalentGraphNodeState state)
    {
        var lines = new List<TooltipLine>();

        for (int i = 0; i < node.Modifiers.Length; i++)
        {
            lines.Add(new TooltipLine(FormatModifierText(node.Modifiers[i]), ColorGreen(), bold: true, fontSize: 16.0));
        }

        if (node.Modifiers.Length == 0)
        {
            lines.Add(new TooltipLine("No effects assigned", ColorMuted(), bold: true, fontSize: 14.0));
        }

        if (HasUsefulTalentDescription(node.Description))
        {
            lines.Add(new TooltipLine(node.Description, ColorText(), gapBefore: 6.0));
        }

        string cost = node.Starter
            ? "Free"
            : Math.Max(1, node.Cost) + " point" + (Math.Max(1, node.Cost) == 1 ? "" : "s");
        string status;
        string instruction;
        double[] instructionColor;

        switch (state)
        {
            case TalentGraphNodeState.Allocated:
                status = "Allocated";
                instruction = "Right click to plan refund";
                instructionColor = ColorGreen();
                break;
            case TalentGraphNodeState.PendingAllocation:
                status = "Planned allocation";
                instruction = "Left click to cancel plan";
                instructionColor = ColorGold();
                break;
            case TalentGraphNodeState.PendingRefund:
                status = "Planned refund";
                instruction = "Right click to cancel plan";
                instructionColor = ColorError();
                break;
            case TalentGraphNodeState.Available:
                status = node.Starter ? "Starting route" : "Available";
                instruction = "Left click to plan allocation";
                instructionColor = ColorGold();
                break;
            case TalentGraphNodeState.ConnectedNoPoints:
                status = "Needs talent points";
                instruction = "Earn another point to allocate this talent";
                instructionColor = ColorMuted();
                break;
            default:
                status = "Not connected";
                instruction = "Allocate a directly connected talent first";
                instructionColor = ColorMuted();
                break;
        }

        lines.Add(new TooltipLine(cost + " · " + status, ColorMuted(), bold: true, fontSize: 11.0, gapBefore: 7.0));
        lines.Add(new TooltipLine(instruction, instructionColor, bold: true, fontSize: 11.0));

        return new TooltipContent(TalentNodeTitle(node), lines.ToArray());
    }

    private static bool HasUsefulTalentDescription(string? description)
    {
        return !string.IsNullOrWhiteSpace(description)
            && !string.Equals(description.Trim(), "Unassigned talent node.", StringComparison.OrdinalIgnoreCase);
    }

    private static string TalentNodeTitle(TalentTreeNodePacket node)
    {
        if (!string.IsNullOrWhiteSpace(node.Name)) return node.Name;
        return node.Starter ? Humanize(node.Foundation) : "Talent";
    }

    private static string FormatModifierText(string modifier)
    {
        if (string.IsNullOrWhiteSpace(modifier))
        {
            return "";
        }

        return modifier.Replace("vrpg:", "").Replace("_", " ");
    }

    private int CountCategory(string category)
    {
        return packet.Entries.Count(entry => CategoryMatches(entry, category));
    }

    private static bool CategoryMatches(LibraryEntryPacket entry, string category)
    {
        string normalized = NormalizeCode(entry.Category);
        switch (category)
        {
            case "status_effects":
                return normalized == "status effects" || normalized == "effects/status";
            default:
                return normalized == NormalizeCode(category) || normalized.StartsWith(NormalizeCode(category) + "/");
        }
    }

    private bool PointInMainPanel(double x, double y)
    {
        return x >= lastMainX && x <= lastMainX + lastMainW && y >= lastMainY && y <= lastMainY + lastMainH;
    }

    private bool CloseAt(double x, double y)
    {
        double closeX = Bounds.OuterWidth - scaled(34.0);
        double closeY = scaled(18.0);
        return Math.Abs(x - closeX) <= scaled(18.0) && Math.Abs(y - closeY) <= scaled(18.0);
    }

    private static bool SameCode(string left, string right)
    {
        return string.Equals(NormalizeCode(left), NormalizeCode(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCode(string value)
    {
        value = (value ?? "").Trim();
        if (value.StartsWith("vrpg:", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring("vrpg:".Length);
        }

        return value.Replace('_', ' ').ToLowerInvariant();
    }

    private static bool HoverEquals(TooltipContent? left, TooltipContent? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (left.Title != right.Title || left.Lines.Length != right.Lines.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Lines.Length; i++)
        {
            if (left.Lines[i].Text != right.Lines[i].Text)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatRate(float value) => FormatNumber(value) + "/s";

    private static string FormatNumber(float value)
    {
        return Math.Abs(value - MathF.Round(value)) < 0.05f ? MathF.Round(value).ToString("0") : value.ToString("0.0");
    }

    private static void DrawText(Context ctx, string text, double x, double y, double size, bool bold, double[] color, bool center = false, bool right = false, double maxWidth = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        SelectFont(ctx, size, bold);
        string display = maxWidth > 0 ? Ellipsize(ctx, text, maxWidth) : text;
        TextExtents extents = ctx.TextExtents(display);
        double drawX = x;
        if (center) drawX -= extents.Width / 2.0;
        if (right) drawX -= extents.Width;

        ctx.SetSourceRGBA(0, 0, 0, 0.42);
        ctx.MoveTo(drawX + 1.0, y + 1.0);
        ctx.ShowText(display);
        ctx.SetSourceRGBA(color[0], color[1], color[2], color[3]);
        ctx.MoveTo(drawX, y);
        ctx.ShowText(display);
    }

    private static void SelectFont(Context ctx, double size, bool bold)
    {
        ctx.SelectFontFace("Lora", FontSlant.Normal, bold ? FontWeight.Bold : FontWeight.Normal);
        ctx.SetFontSize(size);
    }

    private static string Ellipsize(Context ctx, string text, double maxWidth)
    {
        if (ctx.TextExtents(text).Width <= maxWidth) return text;
        const string suffix = "...";
        for (int length = text.Length - 1; length > 0; length--)
        {
            string candidate = text.Substring(0, length).TrimEnd() + suffix;
            if (ctx.TextExtents(candidate).Width <= maxWidth)
            {
                return candidate;
            }
        }

        return suffix;
    }

    private static void RoundedRectangle(Context ctx, double x, double y, double width, double height, double radius)
    {
        if (width <= 0.0 || height <= 0.0) return;
        radius = Math.Min(radius, Math.Min(width, height) / 2.0);
        ctx.NewSubPath();
        ctx.Arc(x + width - radius, y + radius, radius, -Math.PI / 2.0, 0.0);
        ctx.Arc(x + width - radius, y + height - radius, radius, 0.0, Math.PI / 2.0);
        ctx.Arc(x + radius, y + height - radius, radius, Math.PI / 2.0, Math.PI);
        ctx.Arc(x + radius, y + radius, radius, Math.PI, 3.0 * Math.PI / 2.0);
        ctx.ClosePath();
    }

    private static double[] ColorGold() => new[] { VrpgGuiTheme.GoldR, VrpgGuiTheme.GoldG, VrpgGuiTheme.GoldB, 1.0 };
    private static double[] ColorText() => new[] { VrpgGuiTheme.TextR, VrpgGuiTheme.TextG, VrpgGuiTheme.TextB, 1.0 };
    private static double[] ColorMuted() => new[] { VrpgGuiTheme.MutedR, VrpgGuiTheme.MutedG, VrpgGuiTheme.MutedB, 1.0 };
    private static double[] ColorGreen() => new[] { VrpgGuiTheme.GreenR, VrpgGuiTheme.GreenG, VrpgGuiTheme.GreenB, 1.0 };
    private static double[] ColorError() => new[] { 1.0, 0.35, 0.24, 1.0 };

    private enum ClickKind
    {
        Tab,
        OpenLibrary,
        Class,
        Skill,
        ApplyTalentPlan,
        CancelTalentPlan,
        OpenTalentEditor,
        SkillSlot,
        StatDecrease,
        StatIncrease,
        OptionsCategory,
        ToggleCooldownNotifications,
        ToggleResourceNotifications,
        ToggleHotbarLock,
        DecreaseHotbarSlots,
        IncreaseHotbarSlots,
        ToggleCombatText,
        ToggleDamageNumbers,
        ToggleEventWords,
        ToggleMergeNumbers,
        ToggleStatusAuras,
        ToggleDegradationPolicy,
        CycleTelegraphOpacity,
        CycleIntensity
    }

    private sealed class ClickRegion
    {
        public ClickRegion(ClickKind kind, int index, double x, double y, double width, double height)
        {
            Kind = kind;
            Index = index;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public ClickKind Kind { get; }
        public int Index { get; }
        private double X { get; }
        private double Y { get; }
        private double Width { get; }
        private double Height { get; }

        public bool Contains(double x, double y)
        {
            return x >= X && x <= X + Width && y >= Y && y <= Y + Height;
        }
    }

    private sealed class HoverRegion
    {
        public HoverRegion(double x, double y, double width, double height, TooltipContent info)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Info = info;
        }

        public TooltipContent Info { get; }
        private double X { get; }
        private double Y { get; }
        private double Width { get; }
        private double Height { get; }

        public bool Contains(double x, double y)
        {
            return x >= X && x <= X + Width && y >= Y && y <= Y + Height;
        }
    }

    private sealed class TooltipContent
    {
        public TooltipContent(string title, TooltipLine[] lines)
        {
            Title = title;
            Lines = lines;
        }

        public string Title { get; }
        public TooltipLine[] Lines { get; }

        public static TooltipContent Simple(string title, string body)
        {
            return new TooltipContent(title, new[] { new TooltipLine(body, ColorText()) });
        }
    }

    private sealed class TooltipLine
    {
        public TooltipLine(string text, double[] color, bool bold = false, double fontSize = 12.0, double gapBefore = 0.0)
        {
            Text = text;
            Color = color;
            Bold = bold;
            FontSize = fontSize;
            GapBefore = gapBefore;
        }

        public string Text { get; }
        public double[] Color { get; }
        public bool Bold { get; }
        public double FontSize { get; }
        public double GapBefore { get; }
    }
}
