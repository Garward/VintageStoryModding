using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Entities;

namespace VintageKinematics.Api
{
    public readonly struct ContraptionMovingPartContext
    {
        public readonly ICoreClientAPI ClientApi;
        public readonly EntityVKContraption Contraption;
        public readonly Block Block;
        public readonly Vec3i LocalOffset;
        public readonly TreeAttribute BlockEntityTree;
        public readonly int SourceX;
        public readonly int SourceY;
        public readonly int SourceZ;

        public ContraptionMovingPartContext(ICoreClientAPI clientApi, EntityVKContraption contraption, Block block, Vec3i localOffset, TreeAttribute blockEntityTree, int sourceX, int sourceY, int sourceZ)
        {
            ClientApi = clientApi;
            Contraption = contraption;
            Block = block;
            LocalOffset = localOffset;
            BlockEntityTree = blockEntityTree;
            SourceX = sourceX;
            SourceY = sourceY;
            SourceZ = sourceZ;
        }
    }

    public sealed class ContraptionMovingPartDefinition
    {
        public string[] ElementNames { get; set; } = Array.Empty<string>();
        public Vec3f Pivot { get; set; } = new Vec3f(0.5f, 0.5f, 0.5f);
        public EnumKineticAxis Axis { get; set; } = EnumKineticAxis.Y;
        public float VisualRPM { get; set; } = 96f;
        public float Ratio { get; set; } = 1f;
        public float PhaseOffset { get; set; }
    }

    public interface IContraptionMovingPartProvider
    {
        void CollectMovingParts(ContraptionMovingPartContext context, List<ContraptionMovingPartDefinition> parts);
    }

    public static class ContraptionMovingPartRegistry
    {
        private static readonly object Sync = new object();
        private static readonly List<IContraptionMovingPartProvider> Providers = new List<IContraptionMovingPartProvider>();
        private static int version;

        public static int Version
        {
            get
            {
                lock (Sync) return version;
            }
        }

        public static IDisposable Subscribe(IContraptionMovingPartProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            lock (Sync)
            {
                Providers.Add(provider);
                version++;
            }

            return new Subscription(provider);
        }

        public static void CollectMovingParts(ContraptionMovingPartContext context, List<ContraptionMovingPartDefinition> parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));

            IContraptionMovingPartProvider[] providers;
            lock (Sync)
            {
                providers = Providers.ToArray();
            }

            foreach (IContraptionMovingPartProvider provider in providers)
            {
                provider.CollectMovingParts(context, parts);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private IContraptionMovingPartProvider provider;

            public Subscription(IContraptionMovingPartProvider provider)
            {
                this.provider = provider;
            }

            public void Dispose()
            {
                IContraptionMovingPartProvider current = provider;
                if (current == null) return;
                provider = null;

                lock (Sync)
                {
                    Providers.Remove(current);
                    version++;
                }
            }
        }
    }
}
