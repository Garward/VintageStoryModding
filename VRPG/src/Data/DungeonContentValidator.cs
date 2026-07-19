using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Data;

public static class DungeonContentValidator
{
    private static readonly HashSet<string> RoomRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "start", "normal", "junction", "combat", "boss"
    };

    private static readonly HashSet<string> Sides = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "north", "east", "south", "west"
    };

    private static readonly HashSet<string> ZoneKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "discovery", "encounter", "boss"
    };

    private static readonly HashSet<string> AnchorKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mob-spawn", "player-start", "gate"
    };

    public static void ValidateOrThrow(
        IReadOnlyCollection<DungeonThemeDefinition> themes,
        IReadOnlyCollection<DungeonRoomDefinition> rooms,
        IReadOnlyCollection<DungeonEncounterDefinition> encounters)
    {
        IReadOnlyList<string> errors = CollectErrors(themes, rooms, encounters);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "VRPG dungeon data validation failed:\n- " + string.Join("\n- ", errors));
        }
    }

    public static IReadOnlyList<string> CollectErrors(
        IReadOnlyCollection<DungeonThemeDefinition> themes,
        IReadOnlyCollection<DungeonRoomDefinition> rooms,
        IReadOnlyCollection<DungeonEncounterDefinition> encounters)
    {
        var errors = new List<string>();
        Dictionary<string, DungeonThemeDefinition> themesByCode = Index(themes, "theme", errors);
        Dictionary<string, DungeonRoomDefinition> roomsByCode = Index(rooms, "room", errors);
        Dictionary<string, DungeonEncounterDefinition> encountersByCode = Index(encounters, "encounter", errors);

        foreach (DungeonThemeDefinition theme in themes)
        {
            ValidateTheme(theme, rooms, encountersByCode, errors);
        }

        foreach (DungeonRoomDefinition room in rooms)
        {
            ValidateRoom(room, themesByCode, roomsByCode, encountersByCode, errors);
        }

        foreach (DungeonEncounterDefinition encounter in encounters)
        {
            ValidateEncounter(encounter, themesByCode, errors);
        }

        return errors;
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> records, string kind, List<string> errors)
        where T : class, IVrpgDataRecord
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (T record in records)
        {
            string code = record.Code ?? "";
            if (string.IsNullOrWhiteSpace(code) || !code.Contains(':'))
            {
                errors.Add($"{kind} '{DisplayCode(code)}' must have a namespaced code.");
                continue;
            }

            if (!result.TryAdd(code, record))
            {
                errors.Add($"duplicate dungeon {kind} code '{code}'.");
            }
        }

        return result;
    }

    private static void ValidateTheme(
        DungeonThemeDefinition theme,
        IReadOnlyCollection<DungeonRoomDefinition> rooms,
        IReadOnlyDictionary<string, DungeonEncounterDefinition> encounters,
        List<string> errors)
    {
        string label = "theme " + DisplayCode(theme.Code);
        if (theme.CellSize != 32)
        {
            errors.Add($"{label}: cellSize must be 32 for the MVP chunk-aligned grammar.");
        }

        if (theme.RoomHeight <= 0 || theme.RoomHeight > 32 || theme.FloorY < 1)
        {
            errors.Add($"{label}: roomHeight must be 1-32 and floorY must be positive.");
        }

        if (theme.StandardDoorWidth <= 0 || theme.StandardDoorWidth > theme.CellSize
            || theme.StandardDoorHeight <= 0 || theme.StandardDoorHeight > theme.RoomHeight)
        {
            errors.Add($"{label}: standard door dimensions must fit the room envelope.");
        }

        if (theme.TargetPlacements == null
            || theme.TargetPlacements.Min < 4
            || theme.TargetPlacements.Max < theme.TargetPlacements.Min
            || theme.TargetPlacements.Max > 64)
        {
            errors.Add($"{label}: targetPlacements must be an ordered range from 4 through 64.");
        }

        if (theme.MinimumBossDistance < 1
            || theme.TargetPlacements != null && theme.MinimumBossDistance >= theme.TargetPlacements.Max)
        {
            errors.Add($"{label}: minimumBossDistance must fit below the maximum placement count.");
        }

        if (theme.LoopChance < 0f || theme.LoopChance > 1f
            || theme.BossUnlockPercent <= 0f || theme.BossUnlockPercent > 1f
            || theme.GenerationRadiusCells < 2)
        {
            errors.Add($"{label}: loopChance and bossUnlockPercent must be normalized, and generationRadiusCells must be at least 2.");
        }

        if (theme.RoomThemes == null || theme.RoomThemes.Length == 0)
        {
            errors.Add($"{label}: roomThemes must select at least one room family.");
        }

        foreach (string pool in theme.EncounterPools ?? Array.Empty<string>())
        {
            if (!encounters.ContainsKey(pool))
            {
                errors.Add($"{label}: unknown encounter pool '{pool}'.");
            }
        }

        if (!string.IsNullOrWhiteSpace(theme.BossEncounter) && !encounters.ContainsKey(theme.BossEncounter))
        {
            errors.Add($"{label}: unknown bossEncounter '{theme.BossEncounter}'.");
        }

        List<DungeonRoomDefinition> matching = new List<DungeonRoomDefinition>();
        foreach (DungeonRoomDefinition room in rooms)
        {
            if (SharesValue(room.Themes, theme.RoomThemes ?? Array.Empty<string>()))
            {
                matching.Add(room);
            }
        }

        if (!HasRole(matching, "start") || !HasOrdinaryRoom(matching) || !HasRole(matching, "boss"))
        {
            errors.Add($"{label}: selected room families need start, ordinary expansion, and boss pieces.");
        }
    }

    private static void ValidateRoom(
        DungeonRoomDefinition room,
        IReadOnlyDictionary<string, DungeonThemeDefinition> themes,
        IReadOnlyDictionary<string, DungeonRoomDefinition> rooms,
        IReadOnlyDictionary<string, DungeonEncounterDefinition> encounters,
        List<string> errors)
    {
        string label = "room " + DisplayCode(room.Code);
        if (string.IsNullOrWhiteSpace(room.Schematic))
        {
            errors.Add($"{label}: schematic is required.");
        }

        if (room.Weight <= 0 || room.CompletionWeight < 0f)
        {
            errors.Add($"{label}: weight must be positive and completionWeight cannot be negative.");
        }

        if (room.Footprint == null
            || room.Footprint.WidthCells < 1 || room.Footprint.WidthCells > 2
            || room.Footprint.LengthCells < 1 || room.Footprint.LengthCells > 2
            || room.Footprint.WidthCells > 1 && room.Footprint.LengthCells > 1)
        {
            errors.Add($"{label}: footprint must be 1x1, 1x2, or 2x1 cells.");
            return;
        }

        if (room.Themes == null || room.Themes.Length == 0)
        {
            errors.Add($"{label}: themes must contain at least one theme code.");
        }

        foreach (string themeCode in room.Themes ?? Array.Empty<string>())
        {
            bool knownFamily = false;
            foreach (DungeonThemeDefinition theme in themes.Values)
            {
                if (Contains(theme.RoomThemes, themeCode))
                {
                    knownFamily = true;
                    break;
                }
            }

            if (!knownFamily)
            {
                errors.Add($"{label}: room theme family '{themeCode}' is not selected by any dungeon theme.");
            }
        }

        if (room.Roles == null || room.Roles.Length == 0)
        {
            errors.Add($"{label}: at least one role is required.");
        }

        foreach (string role in room.Roles ?? Array.Empty<string>())
        {
            if (!RoomRoles.Contains(role))
            {
                errors.Add($"{label}: unknown role '{role}'.");
            }
        }

        ValidateRotations(room, label, errors);
        ValidateConnectors(room, rooms, label, errors);
        ValidateZonesAndAnchors(room, themes, encounters, label, errors);

        if (room.HasRole("boss") && room.Connectors.Length != 1)
        {
            errors.Add($"{label}: boss rooms require exactly one connector so the boss gate cannot be bypassed.");
        }

        if (room.HasRole("start") && room.CountsForBossUnlock
            || room.HasRole("boss") && room.CountsForBossUnlock)
        {
            errors.Add($"{label}: start and boss rooms cannot count toward boss unlock progress.");
        }
    }

    private static void ValidateRotations(DungeonRoomDefinition room, string label, List<string> errors)
    {
        if (room.AllowedRotations == null || room.AllowedRotations.Length == 0)
        {
            errors.Add($"{label}: allowedRotations cannot be empty.");
            return;
        }

        var seen = new HashSet<int>();
        foreach (int rotation in room.AllowedRotations)
        {
            if ((rotation != 0 && rotation != 90 && rotation != 180 && rotation != 270) || !seen.Add(rotation))
            {
                errors.Add($"{label}: rotations must be unique values from 0, 90, 180, and 270.");
                break;
            }
        }
    }

    private static void ValidateConnectors(
        DungeonRoomDefinition room,
        IReadOnlyDictionary<string, DungeonRoomDefinition> rooms,
        string label,
        List<string> errors)
    {
        if (room.Connectors == null || room.Connectors.Length == 0)
        {
            errors.Add($"{label}: at least one connector is required.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DungeonConnectorDefinition connector in room.Connectors)
        {
            if (string.IsNullOrWhiteSpace(connector.Id) || !ids.Add(connector.Id))
            {
                errors.Add($"{label}: connector IDs must be non-empty and unique.");
            }

            if (!Sides.Contains(connector.Side) || string.IsNullOrWhiteSpace(connector.Socket))
            {
                errors.Add($"{label}/{connector.Id}: side and socket are invalid.");
                continue;
            }

            int edgeLength = string.Equals(connector.Side, "north", StringComparison.OrdinalIgnoreCase)
                || string.Equals(connector.Side, "south", StringComparison.OrdinalIgnoreCase)
                ? room.Footprint.WidthCells
                : room.Footprint.LengthCells;
            if (connector.EdgeCell < 0 || connector.EdgeCell >= edgeLength
                || connector.Offset < 0 || connector.Offset >= 32
                || connector.FloorOffset < 0
                || connector.Width <= 0 || connector.Height <= 0)
            {
                errors.Add($"{label}/{connector.Id}: connector aperture is outside the room envelope.");
            }

            ValidateRoomCodes(connector.AllowRoomCodes, rooms, label, connector.Id, "allowRoomCodes", errors);
            ValidateRoomCodes(connector.DenyRoomCodes, rooms, label, connector.Id, "denyRoomCodes", errors);
        }
    }

    private static void ValidateZonesAndAnchors(
        DungeonRoomDefinition room,
        IReadOnlyDictionary<string, DungeonThemeDefinition> themes,
        IReadOnlyDictionary<string, DungeonEncounterDefinition> encounters,
        string label,
        List<string> errors)
    {
        int height = 32;
        foreach (string family in room.Themes)
        {
            foreach (DungeonThemeDefinition theme in themes.Values)
            {
                if (Contains(theme.RoomThemes, family))
                {
                    height = Math.Min(height, theme.RoomHeight);
                }
            }
        }

        int width = room.Footprint.WidthCells * 32;
        int length = room.Footprint.LengthCells * 32;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DungeonZoneDefinition zone in room.Zones ?? Array.Empty<DungeonZoneDefinition>())
        {
            if (string.IsNullOrWhiteSpace(zone.Id) || !ids.Add(zone.Id))
            {
                errors.Add($"{label}: zone IDs must be non-empty and unique.");
            }

            if (!ZoneKinds.Contains(zone.Kind) || !ValidBox(zone.Min, zone.Max, width, height, length))
            {
                errors.Add($"{label}/{zone.Id}: zone kind or bounds are invalid.");
            }

            if (!string.IsNullOrWhiteSpace(zone.EncounterPool) && !encounters.ContainsKey(zone.EncounterPool))
            {
                errors.Add($"{label}/{zone.Id}: unknown encounterPool '{zone.EncounterPool}'.");
            }
        }

        foreach (DungeonAnchorDefinition anchor in room.Anchors ?? Array.Empty<DungeonAnchorDefinition>())
        {
            if (string.IsNullOrWhiteSpace(anchor.Id) || !ids.Add(anchor.Id))
            {
                errors.Add($"{label}: anchor IDs must be non-empty and unique.");
            }

            if (!AnchorKinds.Contains(anchor.Kind) || !ValidPoint(anchor.Position, width, height, length))
            {
                errors.Add($"{label}/{anchor.Id}: anchor kind or position is invalid.");
            }
        }

        foreach (DungeonZoneDefinition zone in room.Zones ?? Array.Empty<DungeonZoneDefinition>())
        {
            if (!zone.RequiredForCompletion
                || string.IsNullOrWhiteSpace(zone.EncounterPool)
                || !encounters.TryGetValue(zone.EncounterPool, out DungeonEncounterDefinition? encounter))
            {
                continue;
            }

            ValidateEncounterAnchors(room, zone, encounter, label, errors);
        }
    }

    private static void ValidateEncounter(
        DungeonEncounterDefinition encounter,
        IReadOnlyDictionary<string, DungeonThemeDefinition> themes,
        List<string> errors)
    {
        string label = "encounter " + DisplayCode(encounter.Code);
        if (encounter.Themes == null || encounter.Themes.Length == 0)
        {
            errors.Add($"{label}: themes must contain at least one room family.");
        }
        else
        {
            foreach (string family in encounter.Themes)
            {
                bool selected = false;
                foreach (DungeonThemeDefinition theme in themes.Values)
                {
                    if (Contains(theme.RoomThemes, family))
                    {
                        selected = true;
                        break;
                    }
                }

                if (!selected)
                {
                    errors.Add($"{label}: room family '{family}' is not selected by a dungeon theme.");
                }
            }
        }

        if (encounter.Variants == null || encounter.Variants.Length == 0)
        {
            errors.Add($"{label}: at least one weighted variant is required.");
            return;
        }

        var variantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DungeonEncounterVariantDefinition variant in encounter.Variants)
        {
            if (string.IsNullOrWhiteSpace(variant.Id) || !variantIds.Add(variant.Id)
                || variant.Weight <= 0 || variant.Waves == null || variant.Waves.Length == 0)
            {
                errors.Add($"{label}: variants need unique IDs, positive weight, and at least one wave.");
                continue;
            }

            var waveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DungeonEncounterWaveDefinition wave in variant.Waves)
            {
                if (string.IsNullOrWhiteSpace(wave.Id) || !waveIds.Add(wave.Id)
                    || wave.DelaySeconds < 0f || wave.Spawns == null || wave.Spawns.Length == 0)
                {
                    errors.Add($"{label}/{variant.Id}: waves need unique IDs, non-negative delay, and spawns.");
                    continue;
                }

                foreach (DungeonEncounterSpawnDefinition spawn in wave.Spawns)
                {
                    if (string.IsNullOrWhiteSpace(spawn.CreatureCode)
                        || spawn.Count <= 0 || spawn.PerAnchorCap <= 0
                        || spawn.ConcurrentCap <= 0 || spawn.ConcurrentCap > 256
                        || spawn.MinimumPlayerDistance < 0f)
                    {
                        errors.Add($"{label}/{variant.Id}/{wave.Id}: spawn values are invalid.");
                    }
                }
            }
        }
    }

    private static void ValidateEncounterAnchors(
        DungeonRoomDefinition room,
        DungeonZoneDefinition zone,
        DungeonEncounterDefinition encounter,
        string roomLabel,
        List<string> errors)
    {
        foreach (DungeonEncounterVariantDefinition variant in encounter.Variants ?? Array.Empty<DungeonEncounterVariantDefinition>())
        {
            foreach (DungeonEncounterWaveDefinition wave in variant.Waves ?? Array.Empty<DungeonEncounterWaveDefinition>())
            {
                foreach (DungeonEncounterSpawnDefinition spawn in wave.Spawns ?? Array.Empty<DungeonEncounterSpawnDefinition>())
                {
                    int compatible = 0;
                    foreach (DungeonAnchorDefinition anchor in room.Anchors ?? Array.Empty<DungeonAnchorDefinition>())
                    {
                        if (string.Equals(anchor.Kind, "mob-spawn", StringComparison.OrdinalIgnoreCase)
                            && ContainsAll(anchor.Tags, spawn.AnchorTags))
                        {
                            compatible++;
                        }
                    }

                    int simultaneous = Math.Min(spawn.Count, spawn.ConcurrentCap);
                    if (compatible == 0 || compatible * spawn.PerAnchorCap < simultaneous)
                    {
                        errors.Add(
                            $"{roomLabel}/{zone.Id}: encounter {encounter.Code}/{variant.Id}/{wave.Id} "
                            + $"needs {simultaneous} simultaneous '{string.Join(",", spawn.AnchorTags)}' spawns, "
                            + $"but {compatible} compatible anchors at cap {spawn.PerAnchorCap} are available.");
                    }
                }
            }
        }
    }

    private static void ValidateRoomCodes(
        IEnumerable<string> codes,
        IReadOnlyDictionary<string, DungeonRoomDefinition> rooms,
        string roomLabel,
        string connectorId,
        string field,
        List<string> errors)
    {
        foreach (string code in codes ?? Array.Empty<string>())
        {
            if (!rooms.ContainsKey(code))
            {
                errors.Add($"{roomLabel}/{connectorId}: {field} references unknown room '{code}'.");
            }
        }
    }

    private static bool ValidBox(int[] min, int[] max, int width, int height, int length)
    {
        return min != null && max != null && min.Length == 3 && max.Length == 3
            && min[0] >= 0 && min[1] >= 0 && min[2] >= 0
            && max[0] >= min[0] && max[1] >= min[1] && max[2] >= min[2]
            && max[0] < width && max[1] <= height && max[2] < length;
    }

    private static bool ValidPoint(int[] point, int width, int height, int length)
    {
        return point != null && point.Length == 3
            && point[0] >= 0 && point[0] < width
            && point[1] >= 0 && point[1] <= height
            && point[2] >= 0 && point[2] < length;
    }

    private static bool HasRole(IEnumerable<DungeonRoomDefinition> rooms, string role)
    {
        foreach (DungeonRoomDefinition room in rooms)
        {
            if (room.HasRole(role))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOrdinaryRoom(IEnumerable<DungeonRoomDefinition> rooms)
    {
        foreach (DungeonRoomDefinition room in rooms)
        {
            if (!room.HasRole("start") && !room.HasRole("boss"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SharesValue(IEnumerable<string> left, IEnumerable<string> right)
    {
        foreach (string value in left ?? Array.Empty<string>())
        {
            if (Contains(right, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(IEnumerable<string> values, string wanted)
    {
        foreach (string value in values ?? Array.Empty<string>())
        {
            if (string.Equals(value, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAll(IEnumerable<string> values, IEnumerable<string> wanted)
    {
        foreach (string value in wanted ?? Array.Empty<string>())
        {
            if (!Contains(values, value))
            {
                return false;
            }
        }

        return true;
    }

    private static string DisplayCode(string code)
    {
        return string.IsNullOrWhiteSpace(code) ? "<missing code>" : code;
    }
}
