namespace VintageKinematics.Api
{
    public enum KineticActivatorMode : byte
    {
        RepeatWhileRotating = 0,
        OnceUntilStopped = 1,
        OncePerDirection = 2,
        PulseBurst = 3
    }
}
