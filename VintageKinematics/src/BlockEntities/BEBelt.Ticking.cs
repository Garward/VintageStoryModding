using VintageKinematics.Api;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
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
    }
}
