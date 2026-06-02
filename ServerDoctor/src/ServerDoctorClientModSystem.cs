using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ServerDoctor;

public sealed class ServerDoctorClientModSystem : ModSystem
{
    private ICoreClientAPI capi;
    private IClientNetworkChannel channel;
    private ServerDoctorOffenderOverlayRenderer renderer;
    private ServerDoctorOffenderDialog dialog;
    private bool overlayVisible = true;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        renderer = new ServerDoctorOffenderOverlayRenderer(api);
        dialog = new ServerDoctorOffenderDialog(api, ToggleOverlayFromDialog, ToggleProfilingFromDialog);

        channel = api.Network
            .RegisterChannel("serverdoctor")
            .RegisterMessageType<ServerDoctorOverlayPacket>()
            .RegisterMessageType<ServerDoctorControlPacket>()
            .RegisterMessageType<ServerDoctorControlResponsePacket>()
            .SetMessageHandler<ServerDoctorOverlayPacket>(OnOverlayPacket)
            .SetMessageHandler<ServerDoctorControlResponsePacket>(OnControlResponse);

        api.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "serverdoctor-offenders");
        api.Input.RegisterHotKey(
            "serverdoctoroverlay",
            "Open ServerDoctor offenders",
            GlKeys.Quote,
            HotkeyType.HelpAndOverlays,
            altPressed: false,
            ctrlPressed: true,
            shiftPressed: false);
        api.Input.SetHotKeyHandler("serverdoctoroverlay", ToggleDialog);
    }

    public override void Dispose()
    {
        if (capi != null && renderer != null)
        {
            capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
            capi.Input.HotKeys.Remove("serverdoctoroverlay");
        }

        renderer?.Dispose();
        dialog?.Dispose();
        channel = null;
        renderer = null;
        dialog = null;
        capi = null;
    }

    private void OnOverlayPacket(ServerDoctorOverlayPacket packet)
    {
        renderer?.SetSnapshot(packet);
        dialog?.SetSnapshot(packet);
    }

    private bool ToggleDialog(KeyCombination keyCombination)
    {
        if (dialog.IsOpened())
        {
            dialog.TryClose();
            return true;
        }

        RequestOpenDialog();
        return true;
    }

    private void RequestOpenDialog()
    {
        if (!HasControlServerPrivilege())
        {
            ShowNoOpMessage();
            return;
        }

        if (channel == null || !channel.Connected)
        {
            capi.TriggerIngameError(this, "serverdoctor-no-channel", "ServerDoctor server channel is not available.");
            return;
        }

        channel.SendPacket(new ServerDoctorControlPacket { Action = "open" });
    }

    private void OnControlResponse(ServerDoctorControlResponsePacket packet)
    {
        if (packet == null) return;

        if (!packet.Allowed)
        {
            ShowNoOpMessage();
            return;
        }

        if (!string.IsNullOrEmpty(packet.Message))
        {
            capi.ShowChatMessage(packet.Message);
        }

        renderer?.SetProfilerEnabled(packet.Enabled);
        dialog?.SetProfilerEnabled(packet.Enabled);

        if (packet.OpenDialog)
        {
            dialog.TryOpen();
        }
    }

    private bool ToggleProfilingFromDialog()
    {
        if (!HasControlServerPrivilege())
        {
            ShowNoOpMessage();
            return true;
        }

        if (channel == null || !channel.Connected)
        {
            capi.TriggerIngameError(this, "serverdoctor-no-channel", "ServerDoctor server channel is not available.");
            return true;
        }

        channel.SendPacket(new ServerDoctorControlPacket { Action = "toggleprofiling" });
        return true;
    }

    private bool ToggleOverlayFromDialog()
    {
        overlayVisible = !overlayVisible;
        renderer.SetVisible(overlayVisible);
        dialog.SetOverlayVisible(overlayVisible);
        return true;
    }

    private bool HasControlServerPrivilege()
    {
        IPlayer player = capi.World?.Player;
        return player?.Privileges != null && player.HasPrivilege("controlserver");
    }

    private void ShowNoOpMessage()
    {
        capi.TriggerIngameError(this, "serverdoctor-no-op", "You don't have OP.");
    }
}

internal sealed class ServerDoctorOffenderDialog : GuiDialog
{
    private enum SortMode
    {
        MsPerTick,
        Percent,
        Calls,
        Distance,
        Coordinates,
        Label
    }

    private const string StatusKey = "status";
    private const string MsKey = "ms";
    private const string PercentKey = "percent";
    private const string CallsKey = "calls";
    private const string DistanceKey = "distance";
    private const string CoordXKey = "coordx";
    private const string CoordYKey = "coordy";
    private const string CoordZKey = "coordz";
    private const string LabelKey = "label";
    private const string OverlayButtonKey = "overlaybutton";
    private const string ProfilingButtonKey = "profilingbutton";
    private const string ScrollbarKey = "scrollbar";
    private const int MaxRows = 100;
    private const int VisibleRows = 24;

    private readonly Func<bool> toggleOverlay;
    private readonly Func<bool> toggleProfiling;
    private ServerDoctorOverlayPacket snapshot = ServerDoctorOverlayPacketFactory.Empty(false);
    private SortMode sortMode = SortMode.MsPerTick;
    private bool overlayVisible = true;
    private int scrollOffsetRows;

    public override string ToggleKeyCombinationCode => "serverdoctoroverlay";

    public ServerDoctorOffenderDialog(ICoreClientAPI capi, Func<bool> toggleOverlay, Func<bool> toggleProfiling) : base(capi)
    {
        this.toggleOverlay = toggleOverlay;
        this.toggleProfiling = toggleProfiling;
        Compose();
    }

    public void SetSnapshot(ServerDoctorOverlayPacket packet)
    {
        snapshot = packet ?? ServerDoctorOverlayPacketFactory.Empty(false);
        ClampScrollOffset();
        Compose();
    }

    public void SetProfilerEnabled(bool enabled)
    {
        if (snapshot == null)
        {
            snapshot = ServerDoctorOverlayPacketFactory.Empty(enabled);
        }
        else
        {
            snapshot.Enabled = enabled;
            if (!enabled)
            {
                snapshot.Entries?.Clear();
                snapshot.CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        Compose();
    }

    public void SetOverlayVisible(bool visible)
    {
        overlayVisible = visible;
        Compose();
    }

    public override void OnGuiOpened()
    {
        Compose();
    }

    private void Compose()
    {
        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle).WithFixedAlignmentOffset(-20, 0);
        ElementBounds bgBounds = ElementStdBounds.DialogBackground().WithFixedPadding(GuiStyle.ElementToDialogPadding);

        ElementBounds titleBounds = ElementBounds.Fixed(0, 0, 1080, 32);
        ElementBounds sortMsBounds = ElementBounds.Fixed(0, 42, 110, 28);
        ElementBounds sortPercentBounds = ElementBounds.Fixed(120, 42, 95, 28);
        ElementBounds sortCallsBounds = ElementBounds.Fixed(225, 42, 85, 28);
        ElementBounds sortDistBounds = ElementBounds.Fixed(320, 42, 105, 28);
        ElementBounds sortCoordBounds = ElementBounds.Fixed(435, 42, 115, 28);
        ElementBounds sortLabelBounds = ElementBounds.Fixed(560, 42, 85, 28);
        ElementBounds profilingBounds = ElementBounds.Fixed(790, 42, 135, 28);
        ElementBounds overlayBounds = ElementBounds.Fixed(935, 42, 125, 28);
        ElementBounds statusBounds = ElementBounds.Fixed(0, 82, 1080, 44);
        ElementBounds rowsBounds = ElementBounds.Fixed(0, 148, 1054, 354);
        ElementBounds scrollbarBounds = ElementBounds.Fixed(1060, 148, 20, 354);
        CairoFont tableFont = CairoFont.WhiteDetailText().WithFontSize(12);
        CairoFont tableHeaderFont = CairoFont.WhiteDetailText().WithFontSize(12).WithColor(new double[] { 1.0, 0.82, 0.46, 1.0 });

        SingleComposer = capi.Gui
            .CreateCompo("serverdoctor-offenders", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("ServerDoctor offenders", () => TryClose(), bounds: titleBounds)
            .BeginChildElements(bgBounds)
                .AddButton(ButtonLabel("ms/tick", SortMode.MsPerTick), () => SetSort(SortMode.MsPerTick), sortMsBounds, EnumButtonStyle.Small)
                .AddButton(ButtonLabel("% tick", SortMode.Percent), () => SetSort(SortMode.Percent), sortPercentBounds, EnumButtonStyle.Small)
                .AddButton(ButtonLabel("calls", SortMode.Calls), () => SetSort(SortMode.Calls), sortCallsBounds, EnumButtonStyle.Small)
                .AddButton(ButtonLabel("distance", SortMode.Distance), () => SetSort(SortMode.Distance), sortDistBounds, EnumButtonStyle.Small)
                .AddButton(ButtonLabel("coords", SortMode.Coordinates), () => SetSort(SortMode.Coordinates), sortCoordBounds, EnumButtonStyle.Small)
                .AddButton(ButtonLabel("label", SortMode.Label), () => SetSort(SortMode.Label), sortLabelBounds, EnumButtonStyle.Small)
                .AddButton(snapshot.Enabled ? "profile on" : "profile off", () => toggleProfiling(), profilingBounds, EnumButtonStyle.Small, ProfilingButtonKey)
                .AddButton(overlayVisible ? "overlay on" : "overlay off", () => toggleOverlay(), overlayBounds, EnumButtonStyle.Small, OverlayButtonKey)
                .AddStaticCustomDraw(rowsBounds, (ctx, surface, bounds) => DrawRowSeparators(ctx, bounds, tableFont))
                .AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, ScrollbarKey)
                .AddDynamicText("", tableFont, statusBounds, StatusKey)
                .AddStaticText("ms/tick", tableHeaderFont, ElementBounds.Fixed(0, 130, 60, 16))
                .AddStaticText("%tick", tableHeaderFont, ElementBounds.Fixed(72, 130, 48, 16))
                .AddStaticText("calls", tableHeaderFont, ElementBounds.Fixed(136, 130, 50, 16))
                .AddStaticText("dist", tableHeaderFont, ElementBounds.Fixed(205, 130, 50, 16))
                .AddStaticText("/tp x", tableHeaderFont, ElementBounds.Fixed(275, 130, 54, 16))
                .AddStaticText("y", tableHeaderFont, ElementBounds.Fixed(340, 130, 34, 16))
                .AddStaticText("z", tableHeaderFont, ElementBounds.Fixed(390, 130, 54, 16))
                .AddStaticText("label", tableHeaderFont, ElementBounds.Fixed(470, 130, 580, 16))
                .AddDynamicText("", tableFont.Clone(), ElementBounds.Fixed(0, 148, 60, 354), MsKey)
                .AddDynamicText("", tableFont.Clone(), ElementBounds.Fixed(72, 148, 48, 354), PercentKey)
                .AddDynamicText("", tableFont.Clone(), ElementBounds.Fixed(136, 148, 50, 354), CallsKey)
                .AddDynamicText("", tableFont.Clone(), ElementBounds.Fixed(205, 148, 50, 354), DistanceKey)
                .AddDynamicText("", tableFont.Clone(), ElementBounds.Fixed(275, 148, 54, 354), CoordXKey)
                .AddDynamicText("", tableFont.Clone(), ElementBounds.Fixed(340, 148, 34, 354), CoordYKey)
                .AddDynamicText("", tableFont.Clone(), ElementBounds.Fixed(390, 148, 54, 354), CoordZKey)
                .AddDynamicText("", tableFont.Clone(), ElementBounds.Fixed(470, 148, 580, 354), LabelKey)
            .EndChildElements()
            .Compose();

        UpdateScrollbar(tableFont);
        UpdateText();
    }

    private bool SetSort(SortMode mode)
    {
        sortMode = mode;
        scrollOffsetRows = 0;
        Compose();
        return true;
    }

    private string ButtonLabel(string label, SortMode mode)
    {
        return sortMode == mode ? label + " *" : label;
    }

    private void DrawRowSeparators(Context ctx, ElementBounds bounds, CairoFont font)
    {
        int rows = VisibleEntryCount();
        if (rows <= 1) return;

        double lineHeight = font.GetFontExtents().Height * font.LineHeightMultiplier;
        double left = bounds.drawX;
        double right = bounds.drawX + bounds.InnerWidth;
        double firstDataBoundary = bounds.drawY + lineHeight - 2.0;

        ctx.Save();
        ctx.LineWidth = 1;
        ctx.SetSourceRGBA(1.0, 0.82, 0.46, 0.22);

        for (int i = 0; i < rows - 1; i++)
        {
            double y = Math.Floor(firstDataBoundary + lineHeight * i) + 0.5;
            ctx.MoveTo(left, y);
            ctx.LineTo(right, y);
            ctx.Stroke();
        }

        ctx.Restore();
    }

    private int VisibleEntryCount()
    {
        if (snapshot == null || !snapshot.Enabled || snapshot.Entries == null) return 0;
        return Math.Min(VisibleRows, Math.Min(MaxRows, snapshot.Entries.Count) - scrollOffsetRows);
    }

    private void UpdateText()
    {
        GuiComposer composer = SingleComposer;
        if (composer == null || !composer.Composed) return;

        if (snapshot == null || !snapshot.Enabled)
        {
            SetStatus("No active ServerDoctor tick profile snapshot. Vanilla default normal is 30 TPS.\nRun /serverdoctor tick on, then /serverdoctor tick dump or wait for the 10s report.");
            ClearColumns();
            return;
        }

        List<ServerDoctorOverlayEntry> entries = snapshot.Entries ?? new List<ServerDoctorOverlayEntry>();
        long ageMs = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - snapshot.CreatedUnixMs);
        int cappedCount = Math.Min(MaxRows, entries.Count);
        int firstVisible = cappedCount == 0 ? 0 : scrollOffsetRows + 1;
        int lastVisible = Math.Min(cappedCount, scrollOffsetRows + VisibleRows);
        float targetTps = TargetTickRate(snapshot);
        float budgetMs = targetTps <= 0 ? 33.3f : 1000f / targetTps;
        SetStatus(string.Format("TPS: {0:0.0}/{1:0.0}   tick ms avg/max: {2:0.0}/{3:0.0}   budget: {4:0.0}ms\nSnapshot age: {5:0.0}s   entries: {6}   showing: {7}-{8} of {9}   sort: {10}",
            snapshot.ObservedTickRate, targetTps, snapshot.AverageActiveMilliseconds, snapshot.MaxActiveMilliseconds, budgetMs, ageMs / 1000.0, entries.Count, firstVisible, lastVisible, cappedCount, sortMode));

        if (entries.Count == 0)
        {
            SetStatus("No profiler entries in the latest snapshot.\nLeave profiling on until the next 10s report or run /serverdoctor tick dump.");
            ClearColumns();
            return;
        }

        StringBuilder ms = new StringBuilder();
        StringBuilder percent = new StringBuilder();
        StringBuilder calls = new StringBuilder();
        StringBuilder distanceText = new StringBuilder();
        StringBuilder coordX = new StringBuilder();
        StringBuilder coordY = new StringBuilder();
        StringBuilder coordZ = new StringBuilder();
        StringBuilder labels = new StringBuilder();

        foreach (ServerDoctorOverlayEntry entry in Sort(entries).Take(MaxRows).Skip(scrollOffsetRows).Take(VisibleRows))
        {
            ms.AppendFormat("{0:0.000}\n", entry.MillisecondsPerTick);
            percent.AppendFormat("{0:0.0}\n", entry.PercentOfActiveTick);
            calls.AppendFormat("{0}\n", entry.Calls);

            if (entry.HasCoordinates)
            {
                double distance = DistanceToPlayer(entry);
                Vec3i coords = ToLocalCoords(entry);
                distanceText.AppendFormat("{0:0.0}\n", distance);
                coordX.AppendFormat("{0}\n", coords.X);
                coordY.AppendFormat("{0}\n", coords.Y);
                coordZ.AppendFormat("{0}\n", coords.Z);
            }
            else
            {
                distanceText.Append("-\n");
                coordX.Append("-\n");
                coordY.Append("-\n");
                coordZ.Append("-\n");
            }

            labels.AppendLine(Trim(entry.Label, 92));
        }

        composer.GetDynamicText(MsKey).SetNewText(ms.ToString(), autoHeight: false);
        composer.GetDynamicText(PercentKey).SetNewText(percent.ToString(), autoHeight: false);
        composer.GetDynamicText(CallsKey).SetNewText(calls.ToString(), autoHeight: false);
        composer.GetDynamicText(DistanceKey).SetNewText(distanceText.ToString(), autoHeight: false);
        composer.GetDynamicText(CoordXKey).SetNewText(coordX.ToString(), autoHeight: false);
        composer.GetDynamicText(CoordYKey).SetNewText(coordY.ToString(), autoHeight: false);
        composer.GetDynamicText(CoordZKey).SetNewText(coordZ.ToString(), autoHeight: false);
        composer.GetDynamicText(LabelKey).SetNewText(labels.ToString(), autoHeight: false);
    }

    private void SetStatus(string text)
    {
        SingleComposer.GetDynamicText(StatusKey).SetNewText(text, autoHeight: false);
    }

    private void ClearColumns()
    {
        SingleComposer.GetDynamicText(MsKey).SetNewText("", autoHeight: false);
        SingleComposer.GetDynamicText(PercentKey).SetNewText("", autoHeight: false);
        SingleComposer.GetDynamicText(CallsKey).SetNewText("", autoHeight: false);
        SingleComposer.GetDynamicText(DistanceKey).SetNewText("", autoHeight: false);
        SingleComposer.GetDynamicText(CoordXKey).SetNewText("", autoHeight: false);
        SingleComposer.GetDynamicText(CoordYKey).SetNewText("", autoHeight: false);
        SingleComposer.GetDynamicText(CoordZKey).SetNewText("", autoHeight: false);
        SingleComposer.GetDynamicText(LabelKey).SetNewText("", autoHeight: false);
    }

    private void OnNewScrollbarValue(float value)
    {
        int count = CurrentScrollableRowCount();
        int maxOffset = Math.Max(0, count - VisibleRows);
        int newOffset = Math.Max(0, Math.Min(maxOffset, (int)Math.Round(value / RowHeightPixels())));
        if (newOffset == scrollOffsetRows) return;

        scrollOffsetRows = newOffset;
        UpdateText();
    }

    private void UpdateScrollbar(CairoFont font)
    {
        GuiComposer composer = SingleComposer;
        if (composer == null || !composer.Composed) return;

        int count = CurrentScrollableRowCount();
        double rowHeight = RowHeightPixels(font);
        float visibleHeight = (float)(VisibleRows * rowHeight);
        float totalHeight = (float)(Math.Max(VisibleRows, count) * rowHeight);
        composer.GetScrollbar(ScrollbarKey).SetHeights(visibleHeight, totalHeight);
    }

    private void ClampScrollOffset()
    {
        int maxOffset = Math.Max(0, CurrentScrollableRowCount() - VisibleRows);
        if (scrollOffsetRows > maxOffset) scrollOffsetRows = maxOffset;
        if (scrollOffsetRows < 0) scrollOffsetRows = 0;
    }

    private int CurrentScrollableRowCount()
    {
        if (snapshot == null || !snapshot.Enabled || snapshot.Entries == null) return 0;
        return Math.Min(MaxRows, snapshot.Entries.Count);
    }

    private static double RowHeightPixels(CairoFont font = null)
    {
        return font == null ? 14.0 : font.GetFontExtents().Height * font.LineHeightMultiplier;
    }

    private IEnumerable<ServerDoctorOverlayEntry> Sort(IEnumerable<ServerDoctorOverlayEntry> entries)
    {
        switch (sortMode)
        {
            case SortMode.Percent:
                return entries.OrderByDescending(e => e.PercentOfActiveTick);
            case SortMode.Calls:
                return entries.OrderByDescending(e => e.Calls);
            case SortMode.Distance:
                return entries.OrderBy(e => e.HasCoordinates ? 0 : 1).ThenBy(e => e.HasCoordinates ? DistanceToPlayer(e) : double.MaxValue);
            case SortMode.Coordinates:
                return entries
                    .OrderBy(e => e.HasCoordinates ? 0 : 1)
                    .ThenBy(e => e.HasCoordinates ? ToLocalCoords(e).X : int.MaxValue)
                    .ThenBy(e => e.HasCoordinates ? ToLocalCoords(e).Y : int.MaxValue)
                    .ThenBy(e => e.HasCoordinates ? ToLocalCoords(e).Z : int.MaxValue);
            case SortMode.Label:
                return entries.OrderBy(e => e.Label ?? "");
            default:
                return entries.OrderByDescending(e => e.MillisecondsPerTick);
        }
    }

    private double DistanceToPlayer(ServerDoctorOverlayEntry entry)
    {
        EntityPlayer player = capi.World?.Player?.Entity;
        if (player == null) return 0;

        double dx = entry.X + 0.5 - player.Pos.X;
        double dy = entry.Y + 0.5 - player.Pos.Y;
        double dz = entry.Z + 0.5 - player.Pos.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private Vec3i ToLocalCoords(ServerDoctorOverlayEntry entry)
    {
        return new BlockPos(entry.X, entry.Y, entry.Z).ToLocalPosition(capi);
    }

    private static float TargetTickRate(ServerDoctorOverlayPacket packet)
    {
        return packet.TargetTickRate > 0 ? packet.TargetTickRate : 30f;
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= maxLength) return value;
        return value.Substring(0, maxLength - 3) + "...";
    }
}

internal sealed class ServerDoctorOffenderOverlayRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly Matrixf modelViewMatrix = new Matrixf();
    private MeshRef cubeMesh;
    private ServerDoctorOverlayPacket snapshot = ServerDoctorOverlayPacketFactory.Empty(false);
    private bool visible = true;

    public double RenderOrder => 0.72;

    public int RenderRange => 5000;

    public ServerDoctorOffenderOverlayRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void SetVisible(bool visible)
    {
        this.visible = visible;
    }

    public void SetSnapshot(ServerDoctorOverlayPacket packet)
    {
        snapshot = packet ?? ServerDoctorOverlayPacketFactory.Empty(false);
    }

    public void SetProfilerEnabled(bool enabled)
    {
        if (snapshot == null)
        {
            snapshot = ServerDoctorOverlayPacketFactory.Empty(enabled);
            return;
        }

        snapshot.Enabled = enabled;
        if (!enabled)
        {
            snapshot.Entries?.Clear();
            snapshot.CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!visible || snapshot == null || !snapshot.Enabled || snapshot.Entries == null || snapshot.Entries.Count == 0) return;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - snapshot.CreatedUnixMs > 30000) return;
        if (!HasCoordinateEntries(snapshot.Entries)) return;

        EnsureMesh();

        EntityPlayer player = capi.World?.Player?.Entity;
        if (player == null || cubeMesh == null) return;

        IRenderAPI rapi = capi.Render;
        IShaderProgram shader = rapi.GetEngineShader(EnumShaderProgram.Wireframe);
        shader.Use();

        rapi.GlToggleBlend(true, EnumBlendMode.Standard);
        rapi.GLDepthMask(false);
        rapi.GlDisableCullFace();
        rapi.GLDisableDepthTest();
        rapi.LineWidth = 3f;
        rapi.BindTexture2d(capi.BlockTextureAtlas.AtlasTextures[0].TextureId);

        shader.Uniform("origin", Vec3f.Zero);
        shader.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);

        for (int i = 0; i < snapshot.Entries.Count; i++)
        {
            ServerDoctorOverlayEntry entry = snapshot.Entries[i];
            if (!entry.HasCoordinates) continue;

            float heat = Math.Min(1f, Math.Max(0.25f, entry.PercentOfActiveTick / 25f));
            Vec4f color = new Vec4f(1f, 0.18f + 0.55f * (1f - heat), 0.04f, 0.75f);

            modelViewMatrix
                .Identity()
                .Set(rapi.CameraMatrixOrigin)
                .Translate(entry.X - player.CameraPos.X, entry.Y - player.CameraPos.Y, entry.Z - player.CameraPos.Z);

            shader.Uniform("colorIn", color);
            shader.UniformMatrix("modelViewMatrix", modelViewMatrix.Values);
            rapi.RenderMesh(cubeMesh);
        }

        shader.Stop();
        rapi.GLEnableDepthTest();
        rapi.GlEnableCullFace();
        rapi.GLDepthMask(true);
    }

    public void Dispose()
    {
        cubeMesh?.Dispose();
        cubeMesh = null;
    }

    private void EnsureMesh()
    {
        if (cubeMesh != null) return;

        MeshData mesh = LineMeshUtil.GetCube();
        mesh.Scale(new Vec3f(), 0.5f, 0.5f, 0.5f);
        mesh.Translate(0.5f, 0.5f, 0.5f);
        mesh.Flags = new int[mesh.VerticesCount];
        for (int i = 0; i < mesh.Flags.Length; i++)
        {
            mesh.Flags[i] = 256;
        }

        cubeMesh = capi.Render.UploadMesh(mesh);
        mesh.Dispose();
    }

    private static bool HasCoordinateEntries(List<ServerDoctorOverlayEntry> entries)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].HasCoordinates) return true;
        }

        return false;
    }
}
