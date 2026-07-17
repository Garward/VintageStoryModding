using System;
using Cairo;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Client.UI;

public sealed class GuiElementVrpgTalentEditor : GuiElement
{
    private readonly TalentGraphComponent graph = new TalentGraphComponent { AuthoringMode = true };
    private int textureId;
    private bool dragging;
    private bool moved;
    private double lastX;
    private double lastY;
    private string feedback;
    private bool feedbackError;
    private readonly Action<TalentTreeNodePacket?> selectionChanged;

    public GuiElementVrpgTalentEditor(ICoreClientAPI api, ElementBounds bounds, TalentTreeSnapshotPacket tree, string selectedNodeCode = "", string feedback = "", bool feedbackError = false, Action<TalentTreeNodePacket?>? selectionChanged = null) : base(api, bounds)
    {
        this.feedback = feedback;
        this.feedbackError = feedbackError;
        this.selectionChanged = selectionChanged ?? (_ => { });
        graph.SetTree(tree);
        if (!string.IsNullOrWhiteSpace(selectedNodeCode)) graph.Select(selectedNodeCode);
        graph.SetPlayerState(Array.Empty<string>(), int.MaxValue);
    }

    public string SelectedNodeCode => graph.SelectedNode?.Code ?? "";

    public void SetTree(TalentTreeSnapshotPacket tree, string nextFeedback = "", bool nextFeedbackError = false)
    {
        feedback = nextFeedback;
        feedbackError = nextFeedbackError;
        graph.SetTree(tree, preserveView: true);
        Redraw();
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (textureId > 0) api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        double x = args.X - Bounds.absX;
        double y = args.Y - Bounds.absY;
        if (InGraph(x, y))
        {
            dragging = true;
            moved = false;
            lastX = x;
            lastY = y;
            args.Handled = true;
        }
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (!dragging) return;
        double x = args.X - Bounds.absX;
        double y = args.Y - Bounds.absY;
        moved |= Math.Abs(x - lastX) > 1 || Math.Abs(y - lastY) > 1;
        graph.Pan(x - lastX, y - lastY, GraphWidth, GraphHeight);
        lastX = x;
        lastY = y;
        Redraw();
        args.Handled = true;
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!dragging) return;
        dragging = false;
        double x = args.X - Bounds.absX;
        double y = args.Y - Bounds.absY;
        if (!moved && graph.SelectAt(x, y))
        {
            selectionChanged(graph.SelectedNode);
            Redraw();
        }
        args.Handled = true;
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        double x = api.Input.MouseX - Bounds.absX;
        double y = api.Input.MouseY - Bounds.absY;
        if (!InGraph(x, y)) return;
        int direction = Math.Sign(args.deltaPrecise);
        if (direction == 0) direction = Math.Sign(args.delta);
        graph.ZoomAt(direction, x - (GraphX + GraphWidth / 2), y - (GraphY + GraphHeight / 2), GraphWidth, GraphHeight);
        Redraw();
        args.SetHandled();
    }

    public override void Dispose()
    {
        if (textureId > 0) api.Render.GLDeleteTexture(textureId);
        base.Dispose();
    }

    private double GraphX => scaled(18);
    private double GraphY => scaled(68);
    private double GraphWidth => Bounds.OuterWidth - scaled(350);
    private double GraphHeight => Bounds.OuterHeight - scaled(88);
    private bool InGraph(double x, double y) => x >= GraphX && x <= GraphX + GraphWidth && y >= GraphY && y <= GraphY + GraphHeight;

    private void Redraw()
    {
        if (Bounds.OuterWidthInt <= 0 || Bounds.OuterHeightInt <= 0) return;
        using ImageSurface surface = new ImageSurface(Format.Argb32, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        using Context ctx = genContext(surface);
        ctx.SetSourceRGBA(0.10, 0.025, 0.012, 0.98);
        ctx.Paint();
        ctx.SetSourceRGBA(1, 0.62, 0.05, 0.55);
        ctx.LineWidth = scaled(1);
        ctx.Rectangle(scaled(1), scaled(1), Bounds.OuterWidth - scaled(2), Bounds.OuterHeight - scaled(2));
        ctx.Stroke();
        DrawText(ctx, "Talent Tree Authoring", scaled(18), scaled(34), scaled(20), true, 1, 0.62, 0.05);
        DrawText(ctx, "Draft only · mouse wheel zooms · drag empty space to pan · click a node to edit", scaled(18), scaled(55), scaled(11), false, 0.70, 0.58, 0.44);
        ctx.SetSourceRGBA(0, 0, 0, 0.28);
        ctx.Rectangle(GraphX, GraphY, GraphWidth, GraphHeight);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(1, 0.62, 0.05, 0.28);
        ctx.Stroke();
        graph.Draw(ctx, GraphX, GraphY, GraphWidth, GraphHeight);

        double right = scaled(730);
        DrawText(ctx, "Saved tree", right, scaled(64), scaled(11), true, 0.98, 0.88, 0.70);
        DrawText(ctx, "Tree name", right, scaled(122), scaled(11), true, 0.98, 0.88, 0.70);
        TalentTreeNodePacket? selected = graph.SelectedNode;
        DrawText(ctx, "Selected Node", right, scaled(210), scaled(15), true, 1, 0.62, 0.05);
        DrawText(ctx, DisplayName(selected), right, scaled(234), scaled(14), true, 0.98, 0.88, 0.70);
        DrawText(ctx, selected?.Code ?? "Click a node in the graph", right, scaled(252), scaled(9), false, 0.65, 0.53, 0.40);
        DrawText(ctx, "Node name", right, scaled(276), scaled(11), true, 0.98, 0.88, 0.70);
        DrawText(ctx, "Stat modifiers", right, scaled(324), scaled(15), true, 1, 0.62, 0.05);
        DrawText(ctx, "Find stat", right, scaled(350), scaled(11), true, 0.98, 0.88, 0.70);
        DrawText(ctx, "Stat", right, scaled(404), scaled(11), true, 0.98, 0.88, 0.70);
        DrawText(ctx, "Operation", right, scaled(460), scaled(11), true, 0.98, 0.88, 0.70);
        DrawText(ctx, "Amount", scaled(884), scaled(460), scaled(11), true, 0.98, 0.88, 0.70);
        DrawText(ctx, "Increased adds · More multiplies · 0 removes.", right, scaled(548), scaled(9), false, 0.65, 0.53, 0.40);
        if (selected != null)
        {
            DrawText(ctx, "Current modifiers", right, scaled(570), scaled(11), true, 1, 0.62, 0.05);
            for (int i = 0; i < selected.Modifiers.Length && i < 2; i++)
                DrawText(ctx, "• " + selected.Modifiers[i], right, scaled(589 + i * 14), scaled(9), false, 0.72, 0.90, 0.55);
        }
        if (!string.IsNullOrWhiteSpace(feedback))
            DrawText(ctx, feedback, right, scaled(626), scaled(8), true, feedbackError ? 1.0 : 0.50, feedbackError ? 0.35 : 0.90, feedbackError ? 0.24 : 0.40);
        generateTexture(surface, ref textureId);
    }

    private static string DisplayName(TalentTreeNodePacket? node)
    {
        if (node == null) return "None";
        return string.IsNullOrWhiteSpace(node.Name) ? (node.Starter ? "Unnamed Starting Route" : "Stat Node") : node.Name;
    }

    private static void DrawText(Context ctx, string text, double x, double y, double size, bool bold, double r, double g, double b)
    {
        ctx.SelectFontFace("Lora", FontSlant.Normal, bold ? FontWeight.Bold : FontWeight.Normal);
        ctx.SetFontSize(size);
        ctx.SetSourceRGBA(r, g, b, 1);
        ctx.MoveTo(x, y);
        ctx.ShowText(text ?? "");
    }
}
