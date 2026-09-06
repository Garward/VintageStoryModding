using System;
using System.Linq;
using Newtonsoft.Json;
using VRPG.Inventory;

namespace VRPG.Modules.Rpg.Players;

/// <summary>Observes mutable authoritative build values; resource ticks never change this revision.</summary>
internal sealed class RpgBuildRevision
{
    private string snapshot = "";
    private long revision;

    public long Observe(RpgPlayerState state, PlayerEquipmentSnapshot equipment)
    {
        string current = JsonConvert.SerializeObject(new
        {
            state.Level, state.StartingAttributeAffinity, state.UnspentStatPoints,
            state.UnspentTalentPoints, state.RespecPoints,
            Stats = state.BaseStats.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            Talents = state.Talents.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            Skills = state.SkillLevels.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            state.EquippedSkills,
            Equipment = equipment.Entries.OrderBy(x => x.RoleCode, StringComparer.Ordinal)
                .Select(x => new { x.RoleCode, x.EntryId }).ToArray()
        });
        if (!string.Equals(snapshot, current, StringComparison.Ordinal))
        {
            revision = checked(revision + 1);
            snapshot = current;
        }
        return revision;
    }
}
