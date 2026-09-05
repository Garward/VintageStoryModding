using VRPG.Network;

namespace VRPG.Modules.Rpg.Movement;

public sealed record MovementEffectRequest(
    string EffectCode,
    MovementEffectMode Mode,
    MovementEffectAxes Axes,
    MovementVector Vector,
    int DurationMilliseconds = 0,
    float ResponsePerSecond = 12f,
    int Priority = 0,
    int CollisionGraceMilliseconds = 0);
