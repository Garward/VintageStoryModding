namespace VRPG.Client;

public static class VrpgClientHudRuntime
{
    public static bool HideVanillaStatbar { get; set; }
    public static bool HideVanillaHotbar { get; set; }
    public static System.Func<bool>? IsCombatActionBarActive { get; set; }
    public static System.Func<int, bool, bool>? HandleCombatActionSlot { get; set; }
    public static System.Func<int, bool>? HandleCombatWeaponCycle { get; set; }

    public static bool CombatActionBarActive => IsCombatActionBarActive?.Invoke() == true;

    public static bool DispatchCombatActionSlot(int slot, bool pressed)
    {
        return HandleCombatActionSlot?.Invoke(slot, pressed) == true;
    }

    public static bool DispatchCombatWeaponCycle(int wheelDelta)
    {
        return HandleCombatWeaponCycle?.Invoke(wheelDelta) == true;
    }

    public static void Reset()
    {
        HideVanillaStatbar = false;
        HideVanillaHotbar = false;
        IsCombatActionBarActive = null;
        HandleCombatActionSlot = null;
        HandleCombatWeaponCycle = null;
    }
}
