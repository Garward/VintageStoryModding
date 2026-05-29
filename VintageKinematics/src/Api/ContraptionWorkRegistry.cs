using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Entities;

namespace VintageKinematics.Api
{
    public readonly struct ContraptionWorkContext
    {
        public readonly IWorldAccessor World;
        public readonly ICoreAPI Api;
        public readonly EntityVKContraption Contraption;
        public readonly Block ToolBlock;
        public readonly Vec3i LocalOffset;
        public readonly TreeAttribute BlockEntityTree;
        public readonly BlockPos ToolWorldPos;
        public readonly float Rpm;
        public readonly float Dt;
        public readonly double MoveX;
        public readonly double MoveY;
        public readonly double MoveZ;

        public float WorkRate => MathF.Abs(Rpm) * Dt;

        public ContraptionWorkContext(IWorldAccessor world, EntityVKContraption contraption, Block toolBlock, Vec3i localOffset, TreeAttribute blockEntityTree, BlockPos toolWorldPos, float rpm, float dt, double moveX, double moveY, double moveZ, ICoreAPI api = null)
        {
            World = world;
            Api = api;
            Contraption = contraption;
            ToolBlock = toolBlock;
            LocalOffset = localOffset;
            BlockEntityTree = blockEntityTree;
            ToolWorldPos = toolWorldPos;
            Rpm = rpm;
            Dt = dt;
            MoveX = moveX;
            MoveY = moveY;
            MoveZ = moveZ;
        }

        public bool AddProgress(string key, float amount, float required, out float progress)
        {
            progress = 0f;
            return Contraption != null && Contraption.AddContraptionWorkProgress(key, amount, required, out progress);
        }

        public void ResetProgress(string key)
        {
            Contraption?.ResetContraptionWorkProgress(key);
        }

        public void RequestMovementPause(string key, long durationMs, string reason = null)
        {
            Contraption?.RequestMovementPause(key, durationMs, reason);
        }

        public bool ShouldRunVisualPulse(string key, long intervalMs)
        {
            return Contraption == null || Contraption.ShouldRunContraptionWorkVisual(key, intervalMs);
        }

        public bool ShouldRunSoundPulse(long intervalMs)
        {
            return Contraption == null || Contraption.ShouldRunContraptionWorkSound(intervalMs);
        }

        public float AdvanceVisualProgress(string key, float targetProgress)
        {
            return Contraption == null ? Math.Clamp(targetProgress, 0f, 1f) : Contraption.AdvanceContraptionWorkVisualProgress(key, targetProgress);
        }

        public void DepositOutput(ItemStack stack, BlockPos fallbackPos)
        {
            Vec3d at = fallbackPos == null
                ? ToolWorldPos?.ToVec3d().Add(0.5, 0.5, 0.5)
                : fallbackPos.ToVec3d().Add(0.5, 0.5, 0.5);
            Contraption?.DepositOutput(stack, at);
        }
    }

    public interface IContraptionWorkProvider
    {
        float GetActiveStressImpact(ContraptionWorkContext context);
        void DoContraptionWork(ContraptionWorkContext context);
    }

    public static class ContraptionWorkRegistry
    {
        private static readonly object Sync = new object();
        private static readonly List<IContraptionWorkProvider> Providers = new List<IContraptionWorkProvider>();

        public static IDisposable Subscribe(IContraptionWorkProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            lock (Sync)
            {
                Providers.Add(provider);
            }

            return new Subscription(provider);
        }

        public static void DoContraptionWork(ContraptionWorkContext context)
        {
            IContraptionWorkProvider[] providers;
            lock (Sync)
            {
                providers = Providers.ToArray();
            }

            foreach (IContraptionWorkProvider provider in providers)
            {
                provider.DoContraptionWork(context);
            }
        }

        public static float GetActiveStressImpact(ContraptionWorkContext context)
        {
            IContraptionWorkProvider[] providers;
            lock (Sync)
            {
                providers = Providers.ToArray();
            }

            float stressImpact = 0f;
            foreach (IContraptionWorkProvider provider in providers)
            {
                stressImpact += MathF.Max(0f, provider.GetActiveStressImpact(context));
            }

            return stressImpact;
        }

        private sealed class Subscription : IDisposable
        {
            private IContraptionWorkProvider provider;

            public Subscription(IContraptionWorkProvider provider)
            {
                this.provider = provider;
            }

            public void Dispose()
            {
                IContraptionWorkProvider current = provider;
                if (current == null) return;
                provider = null;

                lock (Sync)
                {
                    Providers.Remove(current);
                }
            }
        }
    }
}
