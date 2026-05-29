using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Rendering
{
    public sealed class ContraptionToolMovingPartProvider : IContraptionMovingPartProvider
    {
        private static readonly string[] SawAnimatedElements = Combine(
            Numbered("bladeStrip", 24),
            Numbered("bladeTooth", 24),
            Numbered("hubRound", 10));
        private static readonly string[] DrillAnimatedElements = Combine(
            Numbered("drillCore", 12),
            Numbered("drillFlute0_", 14),
            Numbered("drillFlute1_", 14),
            Numbered("drillTip", 8));

        public void CollectMovingParts(ContraptionMovingPartContext context, List<ContraptionMovingPartDefinition> parts)
        {
            string path = context.Block?.Code?.Path;
            if (string.IsNullOrEmpty(path)) return;

            if (path.StartsWith("contraptionsaw-", StringComparison.Ordinal))
            {
                parts.Add(new ContraptionMovingPartDefinition
                {
                    ElementNames = SawAnimatedElements,
                    Pivot = new Vec3f(0.5f, 0.5f, 2.2f / 16f),
                    Axis = EnumKineticAxis.X
                });
                return;
            }

            if (path.StartsWith("contraptiondrill-", StringComparison.Ordinal))
            {
                parts.Add(new ContraptionMovingPartDefinition
                {
                    ElementNames = DrillAnimatedElements,
                    Pivot = new Vec3f(0.5f, 0.5f, 0f),
                    Axis = EnumKineticAxis.Z,
                    Ratio = -1f
                });
            }
        }

        private static string[] Numbered(string prefix, int count)
        {
            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = prefix + i.ToString("00");
            }

            return names;
        }

        private static string[] Combine(params string[][] groups)
        {
            int count = 0;
            foreach (string[] group in groups)
            {
                count += group?.Length ?? 0;
            }

            string[] result = new string[count];
            int offset = 0;
            foreach (string[] group in groups)
            {
                if (group == null) continue;
                Array.Copy(group, 0, result, offset, group.Length);
                offset += group.Length;
            }

            return result;
        }
    }
}
