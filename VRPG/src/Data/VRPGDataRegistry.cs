using VRPG.Data.Definitions;
using Vintagestory.API.Common;

namespace VRPG.Data;

public sealed class VRPGDataRegistry
{
    public VrpgDataStore<StatDefinition> Stats { get; } = new VrpgDataStore<StatDefinition>("vrpg/stats/");
    public VrpgDataStore<GearRarityDefinition> Rarities { get; } = new VrpgDataStore<GearRarityDefinition>("vrpg/rarities/");
    public VrpgDataStore<GearAffixDefinition> Affixes { get; } = new VrpgDataStore<GearAffixDefinition>("vrpg/affixes/");
    public VrpgDataStore<DamageTypeDefinition> DamageTypes { get; } = new VrpgDataStore<DamageTypeDefinition>("vrpg/damagetypes/");
    public VrpgDataStore<ClassDefinition> Classes { get; } = new VrpgDataStore<ClassDefinition>("vrpg/classes/");
    public VrpgDataStore<TalentNodeDefinition> Talents { get; } = new VrpgDataStore<TalentNodeDefinition>("vrpg/talents/");
    public VrpgDataStore<TalentTreeTemplateDefinition> TalentTreeTemplates { get; } = new VrpgDataStore<TalentTreeTemplateDefinition>("vrpg/talenttemplates/");
    public VrpgDataStore<SkillDefinition> Skills { get; } = new VrpgDataStore<SkillDefinition>("vrpg/skills/");
    public VrpgDataStore<SkillFxPresetDefinition> SkillFxPresets { get; } = new VrpgDataStore<SkillFxPresetDefinition>("vrpg/fx/impact/");
    public VrpgDataStore<StatusEffectDefinition> StatusEffects { get; } = new VrpgDataStore<StatusEffectDefinition>("vrpg/statuseffects/");
    public VrpgDataStore<StatFamilyDefinition> StatFamilies { get; } = new VrpgDataStore<StatFamilyDefinition>("vrpg/statfamilies/");
    public VrpgDataStore<ScalingDefinition> Scaling { get; } = new VrpgDataStore<ScalingDefinition>("vrpg/scaling/");
    public VrpgDataStore<LibraryEntryDefinition> Library { get; } = new VrpgDataStore<LibraryEntryDefinition>("vrpg/library/");
    public VrpgDataStore<DungeonThemeDefinition> DungeonThemes { get; } = new VrpgDataStore<DungeonThemeDefinition>("vrpg/dungeons/");
    public VrpgDataStore<DungeonRoomDefinition> DungeonRooms { get; } = new VrpgDataStore<DungeonRoomDefinition>("vrpg/rooms/");
    public VrpgDataStore<DungeonEncounterDefinition> DungeonEncounters { get; } = new VrpgDataStore<DungeonEncounterDefinition>("vrpg/encounters/");

    public void Load(ICoreAPI api)
    {
        Stats.Load(api);
        Rarities.Load(api);
        Affixes.Load(api);
        DamageTypes.Load(api);
        Classes.Load(api);
        Talents.Load(api);
        TalentTreeTemplates.Load(api);
        Skills.Load(api);
        SkillFxPresets.Load(api);
        StatusEffects.Load(api);
        StatFamilies.Load(api);
        Scaling.Load(api);
        Library.Load(api);
        DungeonThemes.Load(api);
        DungeonRooms.Load(api);
        DungeonEncounters.Load(api);
        SkillDefinitionValidator.Validate(api, this);
        DungeonContentValidator.ValidateOrThrow(DungeonThemes.All, DungeonRooms.All, DungeonEncounters.All);
    }

    public string Summary()
    {
        return $"{Stats.Count} stats, {StatFamilies.Count} stat-family files, {Scaling.Count} scaling files, {DamageTypes.Count} damage types, {Classes.Count} classes, {Rarities.Count} rarities, {Affixes.Count} affixes, {Talents.Count} talents, {TalentTreeTemplates.Count} talent templates, {Skills.Count} skills, {SkillFxPresets.Count} impact FX presets, {StatusEffects.Count} status effects, {Library.Count} library entries, {DungeonThemes.Count} dungeon themes, {DungeonRooms.Count} dungeon rooms, {DungeonEncounters.Count} dungeon encounters";
    }
}
