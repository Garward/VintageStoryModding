using VRPG.Client.UI;
using VRPG.Config;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VRPG.Client;

/// <summary>Owns the unified bottom combat HUD while Vanilla hotbar behavior remains active behind it.</summary>
public sealed class HudElementVRPGCombatConsole : HudElement
{
    private readonly RpgHudConfig hudConfig;
    private readonly RpgSkillHotbarConfig skillConfig;
    private GuiElementVrpgCombatConsole? console;
    private RpgResourcePacket resources = new RpgResourcePacket
    {
        Health = 100f,
        MaxHealth = 100f,
        Mana = 100f,
        MaxMana = 100f,
        ExperienceToNextLevel = 1000,
        Level = 1,
        HudEnabled = true
    };
    private SkillLoadoutPacket skills = new SkillLoadoutPacket();
    private int lastFrameWidth;
    private int lastFrameHeight;
    private double lastGuiScale;
    private float lastConsoleScale;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGCombatConsole(
        ICoreClientAPI capi,
        RpgHudConfig hudConfig,
        RpgSkillHotbarConfig skillConfig) : base(capi)
    {
        this.hudConfig = hudConfig;
        this.skillConfig = skillConfig;
        Compose();
    }

    public void SetResources(RpgResourcePacket packet)
    {
        resources = packet ?? new RpgResourcePacket();
        console?.SetResources(resources);
    }

    public void SetSkills(SkillLoadoutPacket packet)
    {
        skills = packet ?? new SkillLoadoutPacket();
        console?.SetSkills(skills);
    }

    public void SetSkillSlotCount(int slotCount)
    {
        skillConfig.SlotCount = System.Math.Clamp(slotCount, 4, 8);
        console?.SetSkillSlotCount(skillConfig.SlotCount);
    }

    public void SetActionPressed(int slot, bool pressed)
    {
        console?.SetActionPressed(slot, pressed);
    }

    public override void OnOwnPlayerDataReceived()
    {
        Compose();
        TryOpen();
    }

    public override bool TryClose() => false;
    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveMouseEvents() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        if (!hudConfig.Enabled || !hudConfig.UnifiedCombatConsole)
        {
            return;
        }

        if (lastFrameWidth != capi.Render.FrameWidth || lastFrameHeight != capi.Render.FrameHeight)
        {
            Compose();
        }
        else if (System.Math.Abs(lastGuiScale - RuntimeEnv.GUIScale) > 0.001
            || System.Math.Abs(lastConsoleScale - hudConfig.CombatConsoleScale) > 0.001f)
        {
            Compose();
        }

        base.OnRenderGUI(deltaTime);
    }

    private void Compose()
    {
        lastFrameWidth = capi.Render.FrameWidth;
        lastFrameHeight = capi.Render.FrameHeight;
        lastGuiScale = RuntimeEnv.GUIScale;
        lastConsoleScale = hudConfig.CombatConsoleScale;
        CombatConsoleSize size = CombatConsoleSizing.Resolve(lastFrameWidth, lastGuiScale, lastConsoleScale);

        ElementBounds dialogBounds = ElementBounds.Fixed(0, 0, size.FixedWidth, size.FixedHeight)
            .WithFixedAlignmentOffset(0, 5 * size.EffectiveLocalScale);
        dialogBounds.Alignment = EnumDialogArea.CenterBottom;
        ElementBounds consoleBounds = ElementBounds.Fixed(0, 0, size.FixedWidth, size.FixedHeight);

        GuiElementVrpgCombatConsole nextConsole = new GuiElementVrpgCombatConsole(
            capi,
            consoleBounds,
            hudConfig,
            skillConfig,
            size.DrawScale);
        GuiComposer nextComposer = capi.Gui
            .CreateCompo("vrpg-combat-console", dialogBounds)
            .AddInteractiveElement(nextConsole, "vrpgCombatConsole")
            .Compose();

        GuiComposer? previousComposer = SingleComposer;
        console = nextConsole;
        SingleComposer = nextComposer;
        previousComposer?.Dispose();
        console.SetResources(resources);
        console.SetSkills(skills);
    }
}
