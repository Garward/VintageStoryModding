using System;
using VRPG.Config;
using VRPG.Data;
using VRPG.Modules.Rpg.Combat;
using VRPG.Network;
using VRPG.Modules.Rpg.Talents;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Players;

public sealed class RpgResourceService
{
    private const string HealthTreeName = "health";
    private readonly ICoreServerAPI api;
    private readonly RpgModuleConfig config;
    private readonly RpgPlayerStore playerStore;
    private readonly IServerNetworkChannel channel;
    private readonly RpgResourceCalculator calculator;
    private readonly CombatStateService combatStates;

    public System.Func<IServerPlayer, float>? EvasiveStepCooldownRemaining { get; set; }

    public RpgResourceService(
        ICoreServerAPI api,
        RpgModuleConfig config,
        VRPGDataRegistry data,
        TalentTreeCatalog talents,
        RpgPlayerStore playerStore,
        IServerNetworkChannel channel,
        CombatStateService combatStates)
    {
        this.api = api;
        this.config = config;
        this.playerStore = playerStore;
        this.channel = channel;
        this.combatStates = combatStates;
        calculator = new RpgResourceCalculator(config, talents);
    }

    public void OnPlayerNowPlaying(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        EnsureDefaults(state);
        SyncPlayerCombatAttributes(player, state);
        ApplyHealthDefaults(player, state);
        SendSnapshot(player);
    }

    public void Tick(float dt)
    {
        IPlayer[] players = api.World.AllOnlinePlayers;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is not IServerPlayer player)
            {
                continue;
            }

            RpgPlayerState state = playerStore.GetOrCreate(player);
            EnsureDefaults(state);
            SyncPlayerCombatAttributes(player, state);
            ApplyHealthDefaults(player, state);
            ApplyRegeneration(player, state, dt);
            SendSnapshot(player);
        }
    }

    public RpgResourcePacket BuildSnapshot(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        EnsureDefaults(state);
        SyncPlayerCombatAttributes(player, state);
        RpgResourceMaximums maximums = calculator.CalculateMaximums(state);
        RpgResourceRegeneration regeneration = calculator.CalculateRegeneration(state);

        float currentHealth = maximums.Health;
        float maxHealth = maximums.Health;
        ITreeAttribute? healthTree = player.Entity?.WatchedAttributes.GetTreeAttribute(HealthTreeName);
        if (healthTree != null)
        {
            currentHealth = healthTree.GetFloat("currenthealth", currentHealth);
            maxHealth = healthTree.GetFloat("maxhealth", maxHealth);
        }

        string primaryAttribute = CoreAttributeResolver.ResolvePrimary(state);
        return new RpgResourcePacket
        {
            Health = Math.Max(0f, currentHealth),
            MaxHealth = Math.Max(1f, maxHealth),
            Mana = Clamp(state.Mana, 0f, maximums.Mana),
            MaxMana = Math.Max(1f, maximums.Mana),
            MagicShield = Clamp(state.MagicShield, 0f, maximums.MagicShield),
            MaxMagicShield = Math.Max(0f, maximums.MagicShield),
            Blood = Clamp(state.Blood, 0f, maximums.Blood),
            MaxBlood = Math.Max(1f, maximums.Blood),
            BloodUnlocked = state.BloodUnlocked,
            Experience = Math.Max(0, state.Experience),
            ExperienceToNextLevel = Math.Max(1, state.ExperienceToNextLevel),
            Level = Math.Max(1, state.Level),
            HudEnabled = config.Hud.Enabled,
            HideVanillaStatbar = config.Hud.Enabled && config.Hud.HideVanillaStatbar,
            HealthRegenPerSecond = Math.Max(0f, regeneration.Health),
            ManaRegenPerSecond = Math.Max(0f, regeneration.Mana),
            MagicShieldRegenPerSecond = Math.Max(0f, regeneration.MagicShield),
            BloodRegenPerSecond = state.BloodUnlocked ? Math.Max(0f, regeneration.Blood) : 0f,
            UnspentStatPoints = Math.Max(0, state.UnspentStatPoints),
            UnspentTalentPoints = Math.Max(0, state.UnspentTalentPoints),
            RespecPoints = Math.Max(0, state.RespecPoints),
            CombatLockRemainingSeconds = combatStates.RemainingSeconds(player),
            PrimaryAttribute = primaryAttribute,
            StartingAttributeAffinity = CoreAttributeResolver.Normalize(state.StartingAttributeAffinity),
            EvasiveStepActive = primaryAttribute == CoreAttributeResolver.Dexterity,
            EvasiveStepCooldownRemainingSeconds = EvasiveStepCooldownRemaining?.Invoke(player) ?? 0f
        };
    }

    public void SendSnapshot(IServerPlayer player)
    {
        channel.SendPacket(BuildSnapshot(player), player);
    }

    public void RefreshAfterTalentTreeSave(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        EnsureDefaults(state);
        SyncPlayerCombatAttributes(player, state);
        ApplyHealthDefaults(player, state);
        SendSnapshot(player);
    }

    public bool TrySpend(IServerPlayer player, string resource, float amount, out string error, out bool insufficientResource)
    {
        error = "";
        insufficientResource = false;
        float cost = Math.Max(0f, amount);
        string normalized = NormalizeResource(resource);
        if (normalized == "" || normalized == "none" || cost <= 0f)
        {
            return true;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        EnsureDefaults(state);

        switch (normalized)
        {
            case "mana":
            case "mp":
                if (state.Mana + 0.001f < cost)
                {
                    error = $"Need {cost:0.#} mana; have {state.Mana:0.#}.";
                    insufficientResource = true;
                    return false;
                }

                state.Mana -= cost;
                break;
            case "blood":
                if (!state.BloodUnlocked)
                {
                    error = "Blood is not unlocked for this character.";
                    return false;
                }

                if (state.Blood + 0.001f < cost)
                {
                    error = $"Need {cost:0.#} blood; have {state.Blood:0.#}.";
                    insufficientResource = true;
                    return false;
                }

                state.Blood -= cost;
                break;
            default:
                error = "Unknown skill resource: " + resource;
                return false;
        }

        playerStore.Save();
        SendSnapshot(player);
        return true;
    }

    public void SetResource(IServerPlayer player, string resource, float current, float max)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        EnsureDefaults(state);

        float nextMax = Math.Max(1f, max);
        float nextCurrent = Clamp(current, 0f, nextMax);

        switch (NormalizeResource(resource))
        {
            case "health":
            case "hp":
                SetVanillaHealth(player, nextCurrent, nextMax);
                break;
            case "mana":
            case "mp":
                state.MaxMana = nextMax;
                state.Mana = nextCurrent;
                break;
            case "magicshield":
            case "shield":
                state.MaxMagicShield = Math.Max(0f, max);
                state.MagicShield = Clamp(current, 0f, state.MaxMagicShield);
                break;
            case "blood":
                state.MaxBlood = nextMax;
                state.Blood = nextCurrent;
                state.BloodUnlocked = true;
                break;
            default:
                throw new ArgumentException("Unknown resource: " + resource);
        }

        playerStore.Save();
        SendSnapshot(player);
    }

    public void SetExperience(IServerPlayer player, long experience, long toNextLevel)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        EnsureDefaults(state);
        state.Experience = Math.Max(0, experience);
        state.ExperienceToNextLevel = Math.Max(1, toNextLevel);
        playerStore.Save();
        SendSnapshot(player);
    }

    public void AddExperience(IServerPlayer player, long amount, int maxLevel, System.Func<int, long> experienceToNextLevel)
    {
        if (amount <= 0)
        {
            return;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        EnsureDefaults(state);

        int cap = Math.Max(1, maxLevel);
        state.Level = Math.Clamp(state.Level, 1, cap);
        if (state.Level >= cap)
        {
            state.Experience = 0;
            state.ExperienceToNextLevel = 1;
            playerStore.Save();
            SendSnapshot(player);
            return;
        }

        state.Experience += amount;
        state.ExperienceToNextLevel = Math.Max(1, experienceToNextLevel(state.Level));

        while (state.Level < cap && state.Experience >= state.ExperienceToNextLevel)
        {
            state.Experience -= state.ExperienceToNextLevel;
            state.Level++;
            state.UnspentStatPoints += Math.Max(0, config.BaseStatPointsPerLevel);
            state.UnspentTalentPoints += Math.Max(0, config.BaseTalentPointsPerLevel);
            state.ExperienceToNextLevel = state.Level >= cap ? 1 : Math.Max(1, experienceToNextLevel(state.Level));
        }

        if (state.Level >= cap)
        {
            state.Experience = 0;
            state.ExperienceToNextLevel = 1;
        }

        playerStore.Save();
        SendSnapshot(player);
    }

    private void EnsureDefaults(RpgPlayerState state)
    {
        if (state.ExperienceToNextLevel <= 0)
        {
            state.ExperienceToNextLevel = Math.Max(1, config.Resources.BaseExperienceToNextLevel);
        }

        RpgResourceMaximums maximums = calculator.CalculateMaximums(state);

        if (!state.ResourcesInitialized)
        {
            state.Mana = maximums.Mana;
            state.ResourcesInitialized = true;
        }

        state.Mana = Clamp(state.Mana, 0f, maximums.Mana);
        state.Blood = Clamp(state.Blood, 0f, maximums.Blood);
        state.MagicShield = Clamp(state.MagicShield, 0f, maximums.MagicShield);
    }

    private void ApplyRegeneration(IServerPlayer player, RpgPlayerState state, float dt)
    {
        float seconds = Math.Max(0f, dt);
        if (seconds <= 0f)
        {
            return;
        }

        RpgResourceMaximums maximums = calculator.CalculateMaximums(state);
        RpgResourceRegeneration regeneration = calculator.CalculateRegeneration(state);

        state.Mana = Regenerate(state.Mana, maximums.Mana, regeneration.Mana, seconds);
        state.MagicShield = Regenerate(state.MagicShield, maximums.MagicShield, regeneration.MagicShield, seconds);

        if (state.BloodUnlocked)
        {
            state.Blood = Regenerate(state.Blood, maximums.Blood, regeneration.Blood, seconds);
        }

        RegenerateHealth(player, regeneration.Health, seconds);
    }

    private static void SyncPlayerCombatAttributes(IServerPlayer player, RpgPlayerState state)
    {
        if (player.Entity == null)
        {
            return;
        }

        player.Entity.WatchedAttributes.SetInt("vrpgLevel", Math.Max(1, state.Level));
        player.Entity.WatchedAttributes.SetFloat("vrpgDamageMultiplier", 1f);
        player.Entity.WatchedAttributes.MarkPathDirty("vrpgLevel");
        player.Entity.WatchedAttributes.MarkPathDirty("vrpgDamageMultiplier");
    }

    private static float Regenerate(float current, float max, float perSecond, float seconds)
    {
        if (max <= 0f || perSecond <= 0f)
        {
            return Clamp(current, 0f, max);
        }

        return Clamp(current + perSecond * seconds, 0f, max);
    }

    private void ApplyHealthDefaults(IServerPlayer player, RpgPlayerState state)
    {
        ITreeAttribute? healthTree = player.Entity?.WatchedAttributes.GetTreeAttribute(HealthTreeName);
        if (healthTree == null)
        {
            return;
        }

        RpgResourceMaximums maximums = calculator.CalculateMaximums(state);
        float desiredMax = Math.Max(1f, maximums.Health);
        float maxHealth = healthTree.GetFloat("maxhealth", desiredMax);
        if (Math.Abs(maxHealth - desiredMax) <= 0.001f)
        {
            return;
        }

        float currentHealth = healthTree.GetFloat("currenthealth", maxHealth);
        float percent = maxHealth > 0f ? Clamp(currentHealth / maxHealth, 0f, 1f) : 1f;
        SetVanillaHealth(player, Math.Max(1f, desiredMax * percent), desiredMax);
    }

    private static void RegenerateHealth(IServerPlayer player, float perSecond, float seconds)
    {
        if (perSecond <= 0f || seconds <= 0f || player.Entity == null)
        {
            return;
        }

        ITreeAttribute? healthTree = player.Entity.WatchedAttributes.GetTreeAttribute(HealthTreeName);
        if (healthTree == null)
        {
            return;
        }

        float maxHealth = Math.Max(1f, healthTree.GetFloat("maxhealth", 1f));
        float currentHealth = healthTree.GetFloat("currenthealth", maxHealth);
        if (currentHealth <= 0f || currentHealth >= maxHealth)
        {
            return;
        }

        healthTree.SetFloat("currenthealth", Clamp(currentHealth + perSecond * seconds, 0f, maxHealth));
        player.Entity.WatchedAttributes.MarkPathDirty(HealthTreeName);
    }

    private static void SetVanillaHealth(IServerPlayer player, float current, float max)
    {
        if (player.Entity == null)
        {
            return;
        }

        ITreeAttribute? healthTree = player.Entity.WatchedAttributes.GetTreeAttribute(HealthTreeName);
        if (healthTree == null)
        {
            return;
        }

        healthTree.SetFloat("basemaxhealth", max);
        healthTree.SetFloat("maxhealth", max);
        healthTree.SetFloat("currenthealth", Clamp(current, 0f, max));
        player.Entity.WatchedAttributes.MarkPathDirty(HealthTreeName);
    }

    private static string NormalizeResource(string resource)
    {
        return (resource ?? "").Trim().Replace("_", "").Replace("-", "").ToLowerInvariant();
    }

    private static float Clamp(float value, float min, float max)
    {
        if (max < min)
        {
            max = min;
        }

        return Math.Max(min, Math.Min(max, value));
    }
}
