using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace VintageKinematics.Api
{
    public static class RecipeWildcardUtil
    {
        public static bool MatchCode(AssetLocation pattern, AssetLocation inputCode)
        {
            if (pattern == null || inputCode == null) return false;
            return WildcardUtil.Match(pattern, inputCode);
        }

        public static string GetWildcardValue(AssetLocation pattern, AssetLocation inputCode)
        {
            if (pattern == null || inputCode == null) return null;
            return WildcardUtil.GetWildcardValue(pattern, inputCode);
        }

        public static ItemStack ResolveOutputStack(IWorldAccessor world, JsonItemStack output, string captured, AssetLocation inputCode = null)
        {
            if (world == null || output?.Code == null) return null;

            if (captured != null && output.Code.Path?.Contains('*') == true)
            {
                string outputPath = output.Code.Path.Replace("*", captured);
                string outputDomain = output.Code.Domain == "*" ? "game" : output.Code.Domain;
                AssetLocation primary = new AssetLocation(outputDomain, outputPath);
                AssetLocation sameDomain = inputCode != null
                    && (output.Code.Domain == "*" || output.Code.Domain == "game")
                    && inputCode.Domain != "game"
                    ? new AssetLocation(inputCode.Domain, outputPath)
                    : null;

                ItemStack stack = ResolveConcreteOutput(world, output.Type, sameDomain) ?? ResolveConcreteOutput(world, output.Type, primary);
                if (stack != null) stack.StackSize = System.Math.Max(1, output.StackSize);
                return stack;
            }

            return output.ResolvedItemstack?.Clone();
        }

        public static ItemStack ResolveOutputStack(IWorldAccessor world, JsonItemStack output, AssetLocation ingredientPattern, AssetLocation inputCode)
        {
            if (world == null || output?.Code == null) return null;

            if (output.Code.Path?.Contains('*') == true)
            {
                AssetLocation[] candidates = GetWildcardOutputCandidates(output.Code, ingredientPattern, inputCode);
                for (int i = 0; i < candidates.Length; i++)
                {
                    ItemStack stack = ResolveConcreteOutput(world, output.Type, candidates[i]);
                    if (stack == null) continue;

                    stack.StackSize = System.Math.Max(1, output.StackSize);
                    return stack;
                }

                string captured = GetWildcardValue(ingredientPattern, inputCode);
                return ResolveOutputStack(world, output, captured, inputCode);
            }

            return output.ResolvedItemstack?.Clone();
        }

        public static AssetLocation[] GetWildcardOutputCandidates(AssetLocation outputCode, AssetLocation ingredientPattern, AssetLocation inputCode)
        {
            if (outputCode == null || inputCode == null) return System.Array.Empty<AssetLocation>();

            string outputPath = ReplaceOutputWildcards(outputCode.Path, ingredientPattern, inputCode);
            if (outputPath == null) return System.Array.Empty<AssetLocation>();

            List<AssetLocation> candidates = new List<AssetLocation>();
            if (outputCode.Domain == "*" || outputCode.Domain == "game")
            {
                candidates.Add(new AssetLocation(inputCode.Domain, outputPath));
                if (inputCode.Domain != "game")
                {
                    candidates.Add(new AssetLocation("game", outputPath));
                }
            }
            else
            {
                candidates.Add(new AssetLocation(outputCode.Domain, outputPath));
            }

            if (outputCode.Path == "plank-*")
            {
                candidates.Add(new AssetLocation("game", "plank-oak"));
            }

            return candidates.ToArray();
        }

        private static string ReplaceOutputWildcards(string outputPath, AssetLocation ingredientPattern, AssetLocation inputCode)
        {
            if (string.IsNullOrEmpty(outputPath) || outputPath.IndexOf('*') < 0) return outputPath;
            if (ingredientPattern == null || inputCode == null) return null;
            if (ingredientPattern.Domain != "*" && ingredientPattern.Domain != inputCode.Domain) return null;
            if (ingredientPattern.Path?.Contains('*') != true) return null;

            string pattern = Regex.Escape(ingredientPattern.Path).Replace(@"\*", @"(.*)");
            Match match = Regex.Match(inputCode.Path, @"^" + pattern + @"$", RegexOptions.None);
            if (!match.Success) return null;

            string replaced = outputPath;
            for (int i = 1; i < match.Groups.Count; i++)
            {
                int wildcardIndex = replaced.IndexOf('*');
                if (wildcardIndex < 0) break;

                string captured = match.Groups[i].Captures.Count > 0 ? match.Groups[i].Captures[0].Value : "";
                replaced = replaced.Remove(wildcardIndex, 1).Insert(wildcardIndex, captured);
            }

            return replaced.IndexOf('*') >= 0 ? null : replaced;
        }

        private static ItemStack ResolveConcreteOutput(IWorldAccessor world, EnumItemClass type, AssetLocation code)
        {
            if (code == null) return null;

            if (type == EnumItemClass.Block)
            {
                Block block = world.GetBlock(code);
                return block == null || block.IsMissing ? null : new ItemStack(block);
            }

            Item item = world.GetItem(code);
            return item == null || item.IsMissing ? null : new ItemStack(item);
        }
    }
}
