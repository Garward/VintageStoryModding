namespace VintageKinematics.Api
{
    /// <summary>
    /// Why a forge press is not pressing right now, or <see cref="Pressing"/> when it can.
    /// Ordered from most-fundamental missing prerequisite to last quantitative gate so the
    /// player is shown the single most actionable reason.
    /// </summary>
    public enum EnumForgePressStatus
    {
        Pressing,
        NoInput,
        NoRecipeSelected,
        WrongMetal,
        WrongDie,
        NoMatchingRecipe,
        NotEnoughInput,
        TooCold,
        NoPower,
        Overstressed,
        Conflicted
    }

    /// <summary>
    /// Plain facts gathered from the block entity, decoupled from the engine so the
    /// decision is unit-testable.
    /// </summary>
    public struct ForgePressStatusInputs
    {
        public bool HasInput;

        /// <summary>A recipe fully matched the selected operation + input + die.</summary>
        public bool RecipeMatched;

        /// <summary>At least one recipe exists for the selected operation.</summary>
        public bool HasSelectedOperationRecipe;

        /// <summary>The input ingot satisfies a selected-operation recipe's ingredient (die ignored).</summary>
        public bool SelectedOpInputMatches;

        /// <summary>A selected-operation recipe requires a die.</summary>
        public bool SelectedOpRequiresDie;

        /// <summary>The inserted die satisfies a selected-operation recipe's die requirement (input ignored).</summary>
        public bool SelectedOpDieMatches;

        public int InputStackSize;
        public int RequiredQuantity;
        public float InputTemperature;
        public float RequiredTemperature;

        public bool HasKinetic;
        public bool IsConflicted;
        public bool IsOverstressed;
        public bool HasMinRpm;
    }

    /// <summary>
    /// Per-gate result of probing the selected operation's recipes: each flag reports whether
    /// some recipe satisfies that gate independently, so a partial match can be explained.
    /// </summary>
    public struct ForgePressOperationProbe
    {
        /// <summary>At least one recipe exists for the operation.</summary>
        public bool Exists;

        /// <summary>The input satisfies some recipe's ingredient (die ignored).</summary>
        public bool InputMatches;

        /// <summary>Some recipe for the operation requires a die.</summary>
        public bool RequiresDie;

        /// <summary>The inserted die satisfies some recipe's die requirement (input ignored).</summary>
        public bool DieMatches;
    }

    /// <summary>
    /// Pure decision: turns gathered facts into the single reason a forge press is idle.
    /// </summary>
    public static class ForgePressStatusEvaluator
    {
        public static EnumForgePressStatus Evaluate(in ForgePressStatusInputs f)
        {
            if (!f.HasInput) return EnumForgePressStatus.NoInput;

            if (!f.RecipeMatched)
            {
                if (!f.HasSelectedOperationRecipe) return EnumForgePressStatus.NoRecipeSelected;
                if (!f.SelectedOpInputMatches) return EnumForgePressStatus.WrongMetal;
                if (f.SelectedOpRequiresDie && !f.SelectedOpDieMatches) return EnumForgePressStatus.WrongDie;
                return EnumForgePressStatus.NoMatchingRecipe;
            }

            if (f.InputStackSize < f.RequiredQuantity) return EnumForgePressStatus.NotEnoughInput;
            if (f.InputTemperature < f.RequiredTemperature) return EnumForgePressStatus.TooCold;

            if (!f.HasKinetic) return EnumForgePressStatus.NoPower;
            if (f.IsConflicted) return EnumForgePressStatus.Conflicted;
            if (f.IsOverstressed) return EnumForgePressStatus.Overstressed;
            if (!f.HasMinRpm) return EnumForgePressStatus.NoPower;

            return EnumForgePressStatus.Pressing;
        }
    }
}
