namespace VintageKinematics.Api
{
    /// <summary>
    /// Reusable kinetic-network predicates for automation blocks, sensors, and logic helpers.
    /// </summary>
    public enum KineticConditionType
    {
        HasPower = 0,
        NoPower = 1,
        Overstressed = 2,
        Conflicted = 3,
        Blocked = 4,
        StressAbovePercent = 5,
        StressBelowPercent = 6,
        StressAboveSu = 7,
        StressBelowSu = 8,
        CapacityAboveSu = 9,
        CapacityBelowSu = 10,
        RpmAbove = 11,
        RpmBelow = 12
    }
}
