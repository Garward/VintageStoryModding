using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.BlockEntities
{
    public class BECounterweightDrive : BEKineticAnimated
    {
        public const float MaxChargeSeconds = 90f;
        public const float WindSecondsPerSecond = 8f;
        public const float ClickWindSeconds = 8f;

        private float storedSeconds;
        private int lastWindSecond = -1;

        public float StoredSeconds => storedSeconds;

        public void AddCharge(float seconds)
        {
            if (seconds <= 0f) return;
            GetBehavior<BEBehaviorKineticSource>()?.ResetTimedProgress();
            storedSeconds = GameMath.Min(MaxChargeSeconds, storedSeconds + seconds);
            MarkDirty(true);
        }

        public void BeginWinding()
        {
            lastWindSecond = 0;
            AddCharge(ClickWindSeconds);
        }

        public void ContinueWinding(float secondsUsed)
        {
            int whole = (int)secondsUsed;
            if (whole <= lastWindSecond) return;
            lastWindSecond = whole;
            AddCharge(WindSecondsPerSecond);
        }

        public void EndWinding()
        {
            lastWindSecond = -1;
        }

        public bool Release(int direction)
        {
            if (storedSeconds <= 0.05f) return false;
            BEBehaviorKineticSource src = GetBehavior<BEBehaviorKineticSource>();
            if (src == null) return false;

            src.Wind(storedSeconds, direction);
            storedSeconds = 0f;
            MarkDirty(true);
            return true;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            BEBehaviorKineticSource src = GetBehavior<BEBehaviorKineticSource>();
            float activeSeconds = src?.EstimatedRemainingSeconds() ?? 0f;
            if (activeSeconds > 0.05f)
            {
                sb.AppendLine(Lang.Get("vintagekinematics:counterweightdrive-active-info", activeSeconds));
            }
            sb.AppendLine(Lang.Get("vintagekinematics:counterweightdrive-charge-info", storedSeconds, MaxChargeSeconds));
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("storedSeconds", storedSeconds);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            storedSeconds = tree.GetFloat("storedSeconds", 0f);
        }
    }
}
