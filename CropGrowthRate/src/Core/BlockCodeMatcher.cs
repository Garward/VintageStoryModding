using System.Collections.Generic;

namespace CropGrowthRate.Core
{
    public static class BlockCodeMatcher
    {
        public static float ResolveOverride(string fullBlockCode, Dictionary<string, float> overrides)
        {
            if (overrides == null || overrides.Count == 0) return 1f;
            if (overrides.TryGetValue(fullBlockCode, out var exact)) return exact;
            foreach (var kv in overrides)
            {
                if (Matches(fullBlockCode, kv.Key)) return kv.Value;
            }
            return 1f;
        }

        public static bool Matches(string code, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (pattern == code) return true;
            int star = pattern.IndexOf('*');
            if (star < 0) return false;
            string prefix = pattern.Substring(0, star);
            string suffix = pattern.Substring(star + 1);
            return code.StartsWith(prefix) && code.EndsWith(suffix);
        }
    }
}
