using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Drives Vintage Story keyframe animations (the kind authored as &lt;animation&gt;
    /// blocks in shape JSON / .anim files) at a speed scaled by network RPM. Reads
    /// <c>animations</c> from the JSON properties; each entry pairs an animation
    /// code with minRPM gate, base speed at 32 RPM, and optional ease/blend tuning.
    /// Subclasses <see cref="BEBehaviorAnimatable"/> so it owns its own animation
    /// utility — coexists with <see cref="BEBehaviorKineticAnimator"/> as long as
    /// elements driven by keyframe animations are jointed in the shape JSON and
    /// elements driven by procedural rotation are not.
    /// </summary>
    public class BEBehaviorKineticAnimationDriver : BEBehaviorAnimatable
    {
        private class Entry
        {
            public AnimationMetaData Meta;
            public float MinRPM;
            public float SpeedAt32RPM;
            public bool ScalesWithRPM;
        }

        private List<Entry> entries;
        private BEBehaviorKinetic parent;

        /// <summary>Standard BlockEntityBehavior constructor.</summary>
        public BEBehaviorKineticAnimationDriver(BlockEntity be) : base(be) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            entries = new List<Entry>();

            var arr = properties?["animations"];
            if (arr != null && arr.Exists)
            {
                foreach (var a in arr.AsArray())
                {
                    string animation = a["animation"].AsString();
                    if (string.IsNullOrEmpty(animation)) continue;

                    string code = a["code"].AsString(animation);
                    string blendStr = a["blendMode"].AsString("Average");
                    System.Enum.TryParse(blendStr, true, out EnumAnimationBlendMode blend);

                    entries.Add(new Entry
                    {
                        Meta = new AnimationMetaData
                        {
                            Animation = animation,
                            Code = code,
                            AnimationSpeed = 1f,
                            EaseInSpeed = a["easeInSpeed"].AsFloat(10f),
                            EaseOutSpeed = a["easeOutSpeed"].AsFloat(10f),
                            BlendMode = blend
                        },
                        MinRPM = a["minRPM"].AsFloat(4f),
                        SpeedAt32RPM = a["speedAt32RPM"].AsFloat(1f),
                        ScalesWithRPM = a["speedScalesWithRPM"].AsBool(true)
                    });
                }
            }

            parent = Blockentity.GetBehavior<BEBehaviorKinetic>();
            if (parent == null)
            {
                api.Logger.Warning($"[VintageKinematics] BEBehaviorKineticAnimationDriver at {Pos} has no sibling Kinetic behavior - inert");
                return;
            }

            if (api.Side == EnumAppSide.Client)
            {
                animUtil.InitializeAnimator(Block.Code.ToShortString(), null, null, new Vec3f(0, Block.Shape?.rotateY ?? 0, 0));
                Blockentity.RegisterGameTickListener(OnTick, 250);
            }
        }

        private void OnTick(float dt)
        {
            if (parent == null || entries == null) return;
            float rpm = MathF.Abs(parent.CurrentRPM);
            bool gated = parent.IsConflicted || (parent.Network?.IsOverstressed ?? false);

            foreach (var e in entries)
            {
                if (gated || rpm < e.MinRPM)
                {
                    animUtil.StopAnimation(e.Meta.Code);
                    continue;
                }

                float speed = e.ScalesWithRPM ? e.SpeedAt32RPM * (rpm / 32f) : e.SpeedAt32RPM;
                e.Meta.AnimationSpeed = speed;

                if (!animUtil.StartAnimation(e.Meta))
                {
                    animUtil.activeAnimationsByAnimCode[e.Meta.Animation].AnimationSpeed = speed;
                }
            }
        }
    }
}
