using System;

namespace VRPG.Client.PlayerMenu.Components;

public sealed class MechanicalDisclosure
{
    public bool Expanded { get; private set; }
    public double VisibleFraction { get; private set; }
    public bool ChildrenInteractive => Expanded;
    public double RevealMilliseconds(bool reducedMotion) => reducedMotion ? 0 : 140;
    public void Toggle() => SetExpanded(!Expanded, true);
    public void Close() => SetExpanded(false, true);
    public void SetExpanded(bool expanded, bool reducedMotion)
    {
        Expanded = expanded;
        if (reducedMotion) VisibleFraction = expanded ? 1 : 0;
    }
    public bool Advance(float deltaSeconds)
    {
        double target = Expanded ? 1 : 0;
        if (VisibleFraction == target) return false;
        double step = Math.Max(0, deltaSeconds) / .14;
        VisibleFraction = Expanded ? Math.Min(target, VisibleFraction + step) : Math.Max(target, VisibleFraction - step);
        return true;
    }
}
