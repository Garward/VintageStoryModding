using System.Collections.Generic;

namespace VRPG.Modules.Rpg.Players;

public sealed class RpgPlayerState
{
    public int Level { get; set; } = 1;
    public long Experience { get; set; }
    public long ExperienceToNextLevel { get; set; } = 1000;
    public int UnspentStatPoints { get; set; }
    public int UnspentTalentPoints { get; set; }
    public int RespecPoints { get; set; }
    public string StartingAttributeAffinity { get; set; } = "";
    public bool ShowCooldownNotifications { get; set; } = true;
    public bool ShowResourceNotifications { get; set; } = true;
    public float Mana { get; set; } = 100f;
    public float MaxMana { get; set; } = 100f;
    public float MagicShield { get; set; }
    public float MaxMagicShield { get; set; }
    public float Blood { get; set; }
    public float MaxBlood { get; set; } = 100f;
    public bool BloodUnlocked { get; set; }
    public bool ResourcesInitialized { get; set; }
    public Dictionary<string, int> BaseStats { get; set; } = new Dictionary<string, int>();
    public List<string> Talents { get; set; } = new List<string>();
    public Dictionary<string, int> SkillLevels { get; set; } = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
    public string[] EquippedSkills { get; set; } = new string[8];

    public void Normalize()
    {
        StartingAttributeAffinity ??= "";
        BaseStats ??= new Dictionary<string, int>();
        Talents ??= new List<string>();
        SkillLevels ??= new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        if (EquippedSkills == null || EquippedSkills.Length != 8)
        {
            string[] previous = EquippedSkills ?? System.Array.Empty<string>();
            EquippedSkills = new string[8];
            System.Array.Copy(previous, EquippedSkills, System.Math.Min(previous.Length, EquippedSkills.Length));
        }
    }

    public bool HasTalent(string code)
    {
        for (int i = 0; i < Talents.Count; i++)
        {
            if (string.Equals(Talents[i], code, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public int GetSkillLevel(string code)
    {
        return SkillLevels.TryGetValue(code, out int level) ? System.Math.Max(0, level) : 0;
    }
}
