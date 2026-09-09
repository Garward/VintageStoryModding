using System;
using System.Collections.Generic;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

/// <summary>Read-only, bounded equipment snapshot inside Gear. Expansion belongs to the menu session.</summary>
public sealed class EquipmentDisclosure
{
    public IReadOnlyList<PlayerItemEntryPacket> Entries { get; private set; } = Array.Empty<PlayerItemEntryPacket>();
    public int? Total { get; private set; }
    public void Apply(PlayerItemQueryResult result) { Entries = result.EquipmentEntries; Total = result.EquipmentTotal; }
    public void Reset() { Entries = Array.Empty<PlayerItemEntryPacket>(); Total = null; }
}
