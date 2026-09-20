namespace VRPG.Client.PlayerMenu.Shell;

/// <summary>Approved material roles; static geometry is cached in the native composed surface.</summary>
public static class PlayerMenuTheme
{
    public const string Void = "070808", Iron950 = "0d1010", Iron900 = "141817", Iron800 = "1c211f", Iron700 = "292d29";
    public const string CopperDull = "9b6a42", CopperWorn = "bd8450", Heat = "ff9918", HeatHot = "ffc44f";
    public const string Text = "eadfce", Muted = "aa9d8b", Success = "8fcf76", Danger = "df6557";
    // Deterministic capture fallback. Live menus resolve both families from the renderer.
    public const string Font = "Lora";
    public const string Scrim = Void, ChassisOuter = "101311", ChassisInner = Iron900;
    public const string Plate = Iron800, Well = Iron950, InsetShadow = Void;
    public const string SelectedPlate = "30291c", HoverPlate = Iron700, DisabledText = "8f877a";
    public const double ScrimOpacity = .55, ChassisEdgeOpacity = .8, PlateEdgeOpacity = .5;
}
