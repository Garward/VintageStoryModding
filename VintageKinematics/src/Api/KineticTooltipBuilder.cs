using System;
using System.Text;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Standardized tooltip lines for kinetic blocks. Use these so every
    /// kinetic-powered block reads consistently in-game.
    /// </summary>
    public static class KineticTooltipBuilder
    {
        /// <summary>Appends a status line: idle, conflict, overstressed, or "RPM ↻/↺".</summary>
        public static void AppendStatus(StringBuilder dsc, IKineticNetworkInfo net, float currentRPM)
        {
            float absRPM = MathF.Abs(currentRPM);
            bool conflicted   = net?.IsConflicted ?? false;
            bool overstressed = net?.IsOverstressed ?? false;

            if (conflicted)
                dsc.AppendLine("Kinetic — CONFLICT (network halted)");
            else if (overstressed)
                dsc.AppendLine("Kinetic — OVERSTRESSED (network halted)");
            else if (absRPM < 0.01f)
                dsc.AppendLine("Kinetic — idle");
            else
            {
                char arrow = currentRPM >= 0 ? '↻' : '↺';
                dsc.AppendLine($"Kinetic — {absRPM:F1} RPM {arrow}");
            }
        }

        /// <summary>Appends "Stress: total/capacity Su" with a status tag if conflicted/overstressed.</summary>
        public static void AppendStress(StringBuilder dsc, IKineticNetworkInfo net)
        {
            float total    = net?.StressTotal ?? 0f;
            float capacity = net?.StressCapacity ?? 0f;
            string tag = (net?.IsOverstressed ?? false) ? " [OVERSTRESS]"
                       : (net?.IsConflicted   ?? false) ? " [CONFLICT]"
                       : "";
            dsc.AppendLine($"Stress: {total:F0}/{capacity:F0} Su{tag}");
        }

        /// <summary>Appends a "Work: cur/total (pct%)" line for a worker, if any.</summary>
        public static void AppendWorkProgress(StringBuilder dsc, BEBehaviorKineticWorker worker)
        {
            if (worker == null || worker.WorkPerCycle <= 0f) return;
            if (worker.FixedWorkRPM > 0f)
            {
                dsc.AppendLine($"Work speed: fixed {worker.FixedWorkRPM:F0} RPM once above {worker.MinRPM:F0} RPM");
            }
            float pct = 100f * worker.CurrentProgress / worker.WorkPerCycle;
            dsc.AppendLine($"Work: {worker.CurrentProgress:F0}/{worker.WorkPerCycle:F0} ({pct:F0}%)");
        }
    }
}
