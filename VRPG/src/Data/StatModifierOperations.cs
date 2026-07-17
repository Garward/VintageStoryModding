using System;

namespace VRPG.Data;

public static class StatModifierOperations
{
    public const string Add = "add";
    public const string Increased = "increased";
    public const string More = "more";

    public static string Normalize(string? operation)
    {
        string value = (operation ?? Add).Trim().ToLowerInvariant();
        return value switch
        {
            "percent" or "increase" or Increased => Increased,
            "mul" or "multiply" or More => More,
            _ => Add
        };
    }

    public static bool IsPercent(string? operation)
    {
        string normalized = Normalize(operation);
        return normalized == Increased || normalized == More;
    }
}
