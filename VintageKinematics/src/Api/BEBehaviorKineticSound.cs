using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Rendering;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Client-only looping sound for kinetic blocks. Plays while the block's
    /// network is running (RPM ≥ minRPM, not conflicted, not overstressed),
    /// scales pitch/volume with RPM, and registers with
    /// <see cref="KineticSoundCoordinator"/> for distance-based dedup.
    /// </summary>
    public class BEBehaviorKineticSound : BlockEntityBehavior
    {
        private string soundPath;
        private float minRPM;
        private float volumeAt32RPM;
        private float pitchAt32RPM;
        private bool pitchScalesWithRPM;
        private string dedupKey;
        private int maxSimultaneous;

        private ILoadedSound activeSound;
        private KineticSoundCoordinator.Entry coordEntry;
        private BEBehaviorKinetic parent;

        /// <summary>Standard BlockEntityBehavior constructor.</summary>
        public BEBehaviorKineticSound(BlockEntity be) : base(be) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            if (api.Side != EnumAppSide.Client) return;

            soundPath          = properties?["sound"].AsString("") ?? "";
            minRPM             = properties?["minRPM"].AsFloat(4f) ?? 4f;
            volumeAt32RPM      = properties?["volumeAt32RPM"].AsFloat(1f) ?? 1f;
            pitchAt32RPM       = properties?["pitchAt32RPM"].AsFloat(1f) ?? 1f;
            pitchScalesWithRPM = properties?["pitchScalesWithRPM"].AsBool(true) ?? true;
            dedupKey           = properties?["dedupKey"].AsString(soundPath) ?? soundPath;
            maxSimultaneous    = properties?["maxSimultaneous"].AsInt(3) ?? 3;

            if (string.IsNullOrEmpty(soundPath))
            {
                api.Logger.Warning($"[VintageKinematics] BEBehaviorKineticSound at {Pos} has no sound path - inert");
                return;
            }

            parent = Blockentity.GetBehavior<BEBehaviorKinetic>();

            var capi = api as ICoreClientAPI;
            coordEntry = new KineticSoundCoordinator.Entry
            {
                NetworkId = parent?.NetworkId ?? 0L,
                DedupKey = dedupKey,
                Pos = new Vec3d(Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5),
                MaxSimultaneous = maxSimultaneous,
                Tag = this
            };
            capi.ModLoader.GetModSystem<KineticSoundCoordinator>()?.Register(coordEntry);

            Blockentity.RegisterGameTickListener(OnTick, 250);
        }

        private void OnTick(float dt)
        {
            if (parent == null) return;
            if (coordEntry != null) coordEntry.NetworkId = parent.NetworkId;

            float rpm = MathF.Abs(parent.CurrentRPM);
            bool blocked = parent.IsConflicted || (parent.Network?.IsOverstressed ?? false);
            bool shouldPlay = !blocked && rpm >= minRPM;

            if (shouldPlay)
            {
                if (activeSound == null && Api is ICoreClientAPI capi)
                {
                    activeSound = capi.World.LoadSound(new SoundParams
                    {
                        Location = new AssetLocation(soundPath),
                        ShouldLoop = true,
                        Position = new Vec3f((float)Pos.X + 0.5f, (float)Pos.Y + 0.5f, (float)Pos.Z + 0.5f),
                        DisposeOnFinish = false,
                        Volume = volumeAt32RPM
                    });
                    activeSound?.Start();
                }
                if (activeSound != null)
                {
                    activeSound.SetPitch(pitchScalesWithRPM ? pitchAt32RPM * (rpm / 32f) : pitchAt32RPM);
                    activeSound.SetVolume(volumeAt32RPM * MathF.Min(rpm / 32f, 1.5f));
                }
            }
            else if (activeSound != null)
            {
                activeSound.Stop();
                activeSound.Dispose();
                activeSound = null;
            }
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            activeSound?.Stop();
            activeSound?.Dispose();
            activeSound = null;
            if (Api is ICoreClientAPI capi)
                capi.ModLoader.GetModSystem<KineticSoundCoordinator>()?.Unregister(this);
        }
    }
}
