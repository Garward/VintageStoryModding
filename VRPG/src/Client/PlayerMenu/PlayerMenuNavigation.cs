namespace VRPG.Client.PlayerMenu;

/// <summary>Resolves input into a deterministic player-menu action.</summary>
public static class PlayerMenuNavigation
{
    public static PlayerMenuNavigationAction Resolve(PlayerMenuEntryPoint entryPoint, bool menuIsOpen)
    {
        if (menuIsOpen)
        {
            return PlayerMenuNavigationAction.Close();
        }

        return entryPoint switch
        {
            PlayerMenuEntryPoint.InventoryMenu => PlayerMenuNavigationAction.Open(PlayerMenuPage.Inventory),
            PlayerMenuEntryPoint.CharacterMenu => PlayerMenuNavigationAction.Open(
                PlayerMenuPage.Inventory,
                expandEquipment: true),
            PlayerMenuEntryPoint.LegacyHubMenu => PlayerMenuNavigationAction.Open(PlayerMenuPage.Stats),
            _ => PlayerMenuNavigationAction.Open(PlayerMenuPage.Inventory)
        };
    }
}

/// <summary>A requested change to the unified player menu.</summary>
public readonly record struct PlayerMenuNavigationAction(
    bool ShouldOpen,
    bool ShouldClose,
    PlayerMenuPage Page,
    bool ExpandEquipment)
{
    public static PlayerMenuNavigationAction Open(PlayerMenuPage page, bool expandEquipment = false)
    {
        return new PlayerMenuNavigationAction(
            ShouldOpen: true,
            ShouldClose: false,
            Page: page,
            ExpandEquipment: expandEquipment);
    }

    public static PlayerMenuNavigationAction Close()
    {
        return new PlayerMenuNavigationAction(
            ShouldOpen: false,
            ShouldClose: true,
            Page: PlayerMenuPage.Inventory,
            ExpandEquipment: false);
    }
}
