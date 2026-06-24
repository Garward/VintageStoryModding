using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Blocks;
using VintageKinematics.Network;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Position of a belt segment within its chain. <c>Solo</c> = standalone single-segment belt.
    /// </summary>
    public enum EnumBeltPart { Solo, Start, Middle, End }

    /// <summary>
    /// Belt segment block entity.
    /// Stores a back-pointer to the chain controller (the <see cref="EnumBeltPart.Start"/> segment),
    /// the segment's index in the chain, and total chain length. The controller is authoritative
    /// for shared state; non-controller segments delegate item queries up.
    /// </summary>
    public partial class BEBelt : BlockEntity
    {
        public const int MaxChainLength = 64;

        /// <summary>Position of the controller (Start segment). Equals own Pos if this is the controller.</summary>
        public BlockPos ControllerPos { get; private set; }
        /// <summary>0-based index in the chain.</summary>
        public int IndexInChain { get; private set; }
        /// <summary>Total number of segments in this chain (controller is authoritative).</summary>
        public int ChainLength { get; private set; } = 1;
        /// <summary>This segment's role in the chain.</summary>
        public EnumBeltPart Part { get; private set; } = EnumBeltPart.Solo;

        /// <summary>Direction variant of this belt block (n/e/s/w).</summary>
        public string Direction { get; private set; }

        /// <summary>True if a shaft is inserted through this segment (visual + future kinetic tap-off).</summary>
        public bool HasShaft { get; private set; }

        /// <summary>Axis (x/y/z) of the inserted shaft, for round-tripping on extraction. Empty when none.</summary>
        public string InsertedShaftAxis { get; private set; }

        /// <summary>
        /// Item-list — only populated on the controller. Items are in chain order with
        /// progress in [0, ChainLength], 0 = tail face of Start, ChainLength = head face of End.
        /// </summary>
        private readonly List<BeltItem> items = new();

        /// <summary>Read-only view of the controller's items. Empty list if not controller.</summary>
        public IReadOnlyList<BeltItem> Items => items;

        /// <summary>Y at which items rest (just above the top belt strip).</summary>
        public const float BeltTopY = 11f / 16f;

        private const float ItemCaptureMargin = 0.35f;
        private const float ItemEjectVelocityScale = 0.2f;
        private const float ItemInsertClearance = 0.3f;
        private const float ItemEndStopMargin = 0.05f;

        private BeltAnimationRenderer animationRenderer;
        private long tickListenerId;

        public void SetShaft(bool value, string axis = null)
        {
            if (HasShaft == value && InsertedShaftAxis == axis) return;
            HasShaft = value;
            InsertedShaftAxis = value ? axis : null;
            UpdateKineticState(triggerRebuild: true);
            MarkDirty(true);
        }

        /// <summary>True iff this segment is the controller (the chain's <see cref="EnumBeltPart.Start"/>).</summary>
        public bool IsController => ControllerPos != null && ControllerPos.Equals(Pos);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            Direction = Block?.Variant?["direction"];
            UpdateKineticState(triggerRebuild: false);
            if (ControllerPos == null) ControllerPos = Pos.Copy();
            if (api.Side == EnumAppSide.Server)
            {
                // Defer assembly one tick so neighbouring BEs are also initialized.
                RegisterDelayedCallback(_ => RebuildChain(), 50);
                tickListenerId = RegisterGameTickListener(OnServerTick, 50);
            }
            else if (api is ICoreClientAPI capi)
            {
                animationRenderer = new BeltAnimationRenderer(capi, this, GetBehavior<BEBehaviorKinetic>());
                capi.Event.RegisterRenderer(animationRenderer, EnumRenderStage.Opaque);
                tickListenerId = RegisterGameTickListener(OnClientTick, 50);
            }
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            Direction = Block?.Variant?["direction"];
            UpdateKineticState(triggerRebuild: false);
            if (Api?.Side == EnumAppSide.Server)
            {
                RebuildChain();
                RegisterDelayedCallback(_ =>
                {
                    Api?.ModLoader.GetModSystem<KineticNetworkManager>()?.OnPlaced(Pos);
                }, 50);
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeAnimationRenderer();
        }

        private void OnServerTick(float dt)
        {
            if (Direction == null) return;
            if (!IsController) return;

            bool changed = CaptureNearbyItems();

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            float rpm = kinetic?.ActualRPM ?? 0f;
            float velocity = ChainVelocity(rpm);

            if (velocity != 0f && items.Count > 0)
            {
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    items[i].Progress += velocity * dt;

                    if (IsAtBlockedExit(items[i].Progress, velocity, out bool atBlockedHeadEnd))
                    {
                        if (TryHandOffToNeighbor(items[i], atBlockedHeadEnd))
                        {
                            items.RemoveAt(i);
                            changed = true;
                            continue;
                        }

                        // Side automation sinks at the head/tail segment should still get a shot at parked
                        // items — otherwise a chest that refuses input would leave the item
                        // unrecoverable even when a perfectly good funnel sits next to the belt end.
                        if (TryTransferToAdjacentAutomationSink(items[i]))
                        {
                            items.RemoveAt(i);
                            changed = true;
                            continue;
                        }

                        items[i].Progress = atBlockedHeadEnd ? ChainLength - ItemEndStopMargin : ItemEndStopMargin;
                        changed = true;
                        continue;
                    }

                    int stackSizeBeforeTransfer = items[i].Stack?.StackSize ?? 0;
                    if (items[i].Progress >= 0f
                     && items[i].Progress <= ChainLength
                     && TryTransferToAdjacentAutomationSink(items[i]))
                    {
                        items.RemoveAt(i);
                        changed = true;
                    }
                    else if ((items[i].Stack?.StackSize ?? 0) != stackSizeBeforeTransfer)
                    {
                        changed = true;
                    }
                    else if (items[i].Progress < 0f || items[i].Progress > ChainLength)
                    {
                        bool atHeadEnd = items[i].Progress > ChainLength;
                        if (TryMovePastEnd(items[i], atHeadEnd, velocity))
                        {
                            items.RemoveAt(i);
                        }
                        changed = true;
                    }
                }
            }

            if (changed) MarkDirty(true);

            PushRiders(dt, velocity, clientLocalPlayerOnly: false);
        }

        private void OnClientTick(float dt)
        {
            // Mirror the server-side advance so the renderer interpolates smoothly between
            // dirty syncs. The server is authoritative — its next sync resets progress.
            if (Direction == null) return;
            if (!IsController) return;
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            float rpm = kinetic?.ActualRPM ?? 0f;
            float velocity = ChainVelocity(rpm);
            if (velocity != 0f)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    items[i].Progress += velocity * dt;
                    if (IsAtBlockedExit(items[i].Progress, velocity, out bool atBlockedHeadEnd))
                    {
                        items[i].Progress = atBlockedHeadEnd ? ChainLength - ItemEndStopMargin : ItemEndStopMargin;
                    }
                }
            }

            PushRiders(dt, velocity, clientLocalPlayerOnly: true);
        }

        /// <summary>Signed progress velocity (units/sec along the chain) for a given signed RPM.</summary>
        public float ChainVelocity(float signedRpm)
        {
            // 60 rpm == 1 block/sec along the belt. Simple game-feel constant; sign carries
            // through from RPM. HeadDirSign maps "positive RPM advances toward head" per variant.
            return signedRpm / 60f * HeadDirSign(Direction);
        }


        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.Append("Belt ").Append(Direction).Append(" — ").Append(Part)
               .Append(" (").Append(IndexInChain + 1).Append('/').Append(ChainLength).Append(')')
               .AppendLine();
        }
    }
}
