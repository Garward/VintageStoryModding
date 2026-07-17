using System;
using VRPG.Client.UI;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VRPG.Client;

public sealed class HudElementVRPGSkillHotbar : HudElement
{
    private readonly RpgSkillHotbarConfig config;
    private readonly Action persist;
    private GuiElementVrpgSkillHotbar? hotbar;
    private SkillLoadoutPacket snapshot = new SkillLoadoutPacket();
    private RpgResourcePacket resources = new RpgResourcePacket();

    public HudElementVRPGSkillHotbar(ICoreClientAPI capi, RpgSkillHotbarConfig config, Action persist) : base(capi)
    {
        this.config = config;
        this.persist = persist;
        Compose();
    }

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => !config.Locked;

    public void SetSnapshot(SkillLoadoutPacket packet)
    {
        snapshot = packet ?? new SkillLoadoutPacket();
        hotbar?.SetSnapshot(snapshot);
    }

    public void SetResources(RpgResourcePacket packet)
    {
        resources = packet ?? new RpgResourcePacket();
        hotbar?.SetResources(resources);
    }

    public void SetLocked(bool locked)
    {
        config.Locked = locked;
        hotbar?.SetLocked(locked);
    }

    public void SetSlotCount(int slotCount)
    {
        int normalized = Math.Clamp(slotCount, 4, 8);
        if (config.SlotCount == normalized)
        {
            return;
        }

        config.SlotCount = normalized;
        Compose();
    }

    public override bool TryClose() => false;

    public override void OnMouseDown(MouseEvent args)
    {
        if (!config.Locked) base.OnMouseDown(args);
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (!config.Locked) base.OnMouseUp(args);
    }

    public override void OnMouseMove(MouseEvent args)
    {
        if (!config.Locked) base.OnMouseMove(args);
    }

    private void Compose()
    {
        double logicalWidth = capi.Render.FrameWidth / RuntimeEnv.GUIScale;
        double logicalHeight = capi.Render.FrameHeight / RuntimeEnv.GUIScale;
        int slot = Math.Max(42, config.SlotSize);
        int gap = Math.Max(3, config.Gap);
        int slotCount = Math.Clamp(config.SlotCount, 4, 8);
        double width = slot * slotCount + gap * (slotCount - 1);
        double height = slot + 17;
        if (config.X < 0) config.X = (int)Math.Round((logicalWidth - width) / 2.0);
        if (config.Y < 0) config.Y = (int)Math.Round(logicalHeight - height - 118.0);
        config.X = (int)Math.Round(Math.Clamp(config.X, 0.0, Math.Max(0.0, logicalWidth - width)));
        config.Y = (int)Math.Round(Math.Clamp(config.Y, 0.0, Math.Max(0.0, logicalHeight - height)));

        ElementBounds dialogBounds = ElementBounds.Fixed(config.X, config.Y, width, height);
        ElementBounds hotbarBounds = ElementBounds.Fixed(0, 0, width, height);
        GuiElementVrpgSkillHotbar nextHotbar = new GuiElementVrpgSkillHotbar(capi, hotbarBounds, config, OnMoved);
        GuiComposer nextComposer = capi.Gui
            .CreateCompo("vrpg-skill-hotbar", dialogBounds)
            .BeginChildElements(dialogBounds)
                .AddInteractiveElement(nextHotbar, "vrpgSkillHotbar")
            .EndChildElements()
            .Compose();
        GuiComposer? previousComposer = SingleComposer;
        hotbar = nextHotbar;
        SingleComposer = nextComposer;
        previousComposer?.Dispose();
        hotbar.SetSnapshot(snapshot);
        hotbar.SetResources(resources);
    }

    private void OnMoved(int x, int y, bool finished)
    {
        config.X = x;
        config.Y = y;
        if (SingleComposer != null)
        {
            SingleComposer.Bounds.fixedX = x;
            SingleComposer.Bounds.fixedY = y;
            SingleComposer.Bounds.CalcWorldBounds();
        }

        if (finished) persist();
    }
}
