namespace VintageKinematics.Api
{
    /// <summary>
    /// What a kinetic block "is" structurally. Used by the network builder
    /// to decide ratios, direction flips, and connection rules.
    /// Use <see cref="Custom"/> for blocks the built-in rules don't fit.
    /// </summary>
    public enum EnumKineticRole : byte
    {
        /// <summary>Plain shaft segment, 1:1 along its axis.</summary>
        Shaft = 0,
        /// <summary>Small cogwheel — meshes 1:1 with another small cog and 2:1 with a large.</summary>
        SmallCogwheel = 1,
        /// <summary>Large cogwheel — meshes 1:2 with a small cog and 1:1 with a large.</summary>
        LargeCogwheel = 2,
        /// <summary>Bevel hub: redirects rotation 90° between perpendicular shafts.</summary>
        Gearbox = 3,
        /// <summary>Decorative encased segment, behaves as a shaft.</summary>
        EncasedShaft = 4,
        /// <summary>Player-driven hand crank source.</summary>
        HandCrank = 5,
        /// <summary>Mod-defined role; pair with <see cref="IKineticConnector"/> for custom rules.</summary>
        Custom = 255
    }
}
