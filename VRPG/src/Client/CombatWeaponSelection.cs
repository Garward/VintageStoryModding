using System;
using System.Collections.Generic;

namespace VRPG.Client;

/// <summary>Pure wrapped selection for the combat weapon wheel.</summary>
public static class CombatWeaponSelection
{
    public static int Cycle(IReadOnlyList<int> weaponSlots, int currentSlot, int wheelDelta)
    {
        if (weaponSlots == null || weaponSlots.Count == 0 || wheelDelta == 0)
        {
            return currentSlot;
        }

        int currentIndex = -1;
        for (int i = 0; i < weaponSlots.Count; i++)
        {
            if (weaponSlots[i] == currentSlot)
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
        {
            return wheelDelta > 0 ? weaponSlots[^1] : weaponSlots[0];
        }

        int direction = wheelDelta > 0 ? -1 : 1;
        int next = (currentIndex + direction + weaponSlots.Count) % weaponSlots.Count;
        return weaponSlots[next];
    }
}
