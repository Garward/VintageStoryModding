using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Gui;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEFlywheel : BEKineticAnimated, IKineticConnector
    {
        public const int PacketIdOpenDialog = 5900;
        public const int PacketIdSetBurst = 5901;

        private const int TickMs = 200;
        private const float DefaultMaxStoredSeconds = 180f;
        private const float DefaultChargeStress = 64f;
        private const float DefaultDischargeStress = 64f;
        private const float DefaultChargeEfficiency = 0.75f;
        private const float DefaultLeakFullToEmptySeconds = 1800f;
        private const float DefaultMinReleaseSeconds = 0.25f;
        private const float DefaultMaxOutputRPM = 16f;
        private const float DefaultMaxBurstMultiplier = 8f;
        private static readonly float[] BurstSteps = { 1f, 2f, 4f, 8f, 16f };

        private float maxStoredSeconds = DefaultMaxStoredSeconds;
        private float chargeStress = DefaultChargeStress;
        private float dischargeStress = DefaultDischargeStress;
        private float chargeEfficiency = DefaultChargeEfficiency;
        private float leakSecondsPerSecond = DefaultMaxStoredSeconds / DefaultLeakFullToEmptySeconds;
        private float minReleaseSeconds = DefaultMinReleaseSeconds;
        private float maxOutputRPM = DefaultMaxOutputRPM;
        private float maxBurstMultiplier = DefaultMaxBurstMultiplier;

        private float storedSeconds;
        private float spinRPM;
        private int spinDirection = 1;
        private bool releaseMode;
        private float burstMultiplier = 1f;

        private GuiDialogFlywheel clientDialog;

        public float StoredEnergy01 => GameMath.Clamp(storedSeconds / maxStoredSeconds, 0f, 1f);

        public float StoredSeconds => storedSeconds;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            LoadStatsFromBlock();
            ApplyModeToKinetic(false);
            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, TickMs);
            }
        }

        private void LoadStatsFromBlock()
        {
            JsonObject stats = Block?.Attributes?["flywheel"];
            if (stats == null || !stats.Exists) return;

            maxStoredSeconds = MathF.Max(1f, stats["maxStoredSeconds"].AsFloat(DefaultMaxStoredSeconds));
            chargeStress = MathF.Max(0f, stats["chargeStress"].AsFloat(DefaultChargeStress));
            dischargeStress = MathF.Max(0f, stats["dischargeStress"].AsFloat(DefaultDischargeStress));
            chargeEfficiency = GameMath.Clamp(stats["chargeEfficiency"].AsFloat(DefaultChargeEfficiency), 0f, 1f);
            minReleaseSeconds = MathF.Max(0f, stats["minReleaseSeconds"].AsFloat(DefaultMinReleaseSeconds));
            maxOutputRPM = MathF.Max(KineticNetwork.MinAbsRPM, stats["maxOutputRPM"].AsFloat(DefaultMaxOutputRPM));
            maxBurstMultiplier = NormalizeBurstMultiplier(stats["maxBurstMultiplier"].AsFloat(DefaultMaxBurstMultiplier), 16f);
            burstMultiplier = NormalizeBurstMultiplier(burstMultiplier, maxBurstMultiplier);

            float leakFullToEmptySeconds = stats["leakFullToEmptySeconds"].AsFloat(DefaultLeakFullToEmptySeconds);
            leakSecondsPerSecond = leakFullToEmptySeconds > 0f ? maxStoredSeconds / leakFullToEmptySeconds : 0f;
            storedSeconds = MathF.Min(storedSeconds, maxStoredSeconds);

            BEBehaviorKineticSource source = GetBehavior<BEBehaviorKineticSource>();
            if (source != null && !source.IsActive)
            {
                source.TargetRPM = maxOutputRPM;
            }
        }

        public bool OnPlayerRightClick(IPlayer byPlayer, bool configure)
        {
            if (configure)
            {
                SendDialogState(byPlayer);
                return true;
            }

            ToggleBankRelease();
            return true;
        }

        private void SendDialogState(IPlayer byPlayer)
        {
            if (Api.Side != EnumAppSide.Server || byPlayer is not IServerPlayer serverPlayer) return;

            GetBankDialogStats(out int bankCount, out float bankMaxBurst, out float bankBurstOutput, out float bankRemainingSeconds);
            string title = Lang.Get("vintagekinematics:flywheel-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:flywheel-title") title = "Flywheel";

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(title);
            bw.Write(storedSeconds);
            bw.Write(maxStoredSeconds);
            bw.Write(releaseMode);
            bw.Write(burstMultiplier);
            bw.Write(bankMaxBurst);
            bw.Write(bankBurstOutput);
            bw.Write(bankRemainingSeconds);
            bw.Write(bankCount);
            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(serverPlayer, Pos, PacketIdOpenDialog, ms.ToArray());
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (!CheckClaim(player)) return;

            if (packetid == PacketIdSetBurst)
            {
                using var ms = new MemoryStream(data ?? Array.Empty<byte>());
                using var br = new BinaryReader(ms);
                SetBankBurstMultiplier(br.ReadSingle());
                SendDialogState(player);
            }
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != PacketIdOpenDialog) return;

            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            string title = br.ReadString();
            float dialogStoredSeconds = br.ReadSingle();
            float dialogMaxStoredSeconds = br.ReadSingle();
            bool dialogReleaseMode = br.ReadBoolean();
            float dialogBurstMultiplier = br.ReadSingle();
            float dialogMaxBurstMultiplier = br.ReadSingle();
            float dialogBurstOutput = br.ReadSingle();
            float dialogRemainingSeconds = br.ReadSingle();
            int dialogBankCount = br.ReadInt32();

            storedSeconds = dialogStoredSeconds;
            maxStoredSeconds = dialogMaxStoredSeconds;
            releaseMode = dialogReleaseMode;
            burstMultiplier = dialogBurstMultiplier;
            maxBurstMultiplier = dialogMaxBurstMultiplier;

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogFlywheel(
                    title,
                    Pos,
                    storedSeconds,
                    maxStoredSeconds,
                    releaseMode,
                    burstMultiplier,
                    maxBurstMultiplier,
                    dialogBurstOutput,
                    dialogRemainingSeconds,
                    dialogBankCount,
                    SendBurstPacket,
                    capi);
                clientDialog.OnClosed += OnDialogClosed;
                clientDialog.TryOpen();
            }
            else
            {
                clientDialog.UpdateState(storedSeconds, maxStoredSeconds, releaseMode, burstMultiplier, maxBurstMultiplier, dialogBurstOutput, dialogRemainingSeconds, dialogBankCount);
            }
        }

        private void SendBurstPacket(float newBurstMultiplier)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(newBurstMultiplier);
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSetBurst, ms.ToArray());
        }

        private void OnDialogClosed()
        {
            clientDialog = null;
        }

        private bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent flywheel packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
            return false;
        }

        public void ToggleBankRelease()
        {
            List<BEFlywheel> bank = CollectBank();
            bool targetRelease = !releaseMode;

            foreach (BEFlywheel flywheel in bank)
            {
                flywheel.SetReleaseMode(targetRelease, true);
            }
        }

        private void SetBankBurstMultiplier(float targetMultiplier)
        {
            List<BEFlywheel> bank = CollectBank();
            float bankMaxBurst = GetBankMaxBurstMultiplier(bank);
            float normalized = NormalizeBurstMultiplier(targetMultiplier, bankMaxBurst);

            foreach (BEFlywheel flywheel in bank)
            {
                flywheel.SetBurstMultiplier(normalized, true);
            }
        }

        private void SetBurstMultiplier(float targetMultiplier, bool rebuildNetwork)
        {
            float normalized = NormalizeBurstMultiplier(targetMultiplier, maxBurstMultiplier);
            if (MathF.Abs(burstMultiplier - normalized) < 0.001f) return;

            burstMultiplier = normalized;
            ApplyModeToKinetic(rebuildNetwork);
            if (Api?.Side == EnumAppSide.Server && rebuildNetwork && !releaseMode)
            {
                RebuildNetwork();
            }
            MarkDirty(true);
        }

        public void ToggleRelease()
        {
            SetReleaseMode(!releaseMode, true);
        }

        private void SetReleaseMode(bool targetRelease, bool rebuildNetwork)
        {
            BEBehaviorKineticSource source = GetBehavior<BEBehaviorKineticSource>();

            if (targetRelease && storedSeconds <= minReleaseSeconds)
            {
                return;
            }

            if (releaseMode == targetRelease) return;

            releaseMode = targetRelease;
            if (!releaseMode && source != null)
            {
                source.DecaySeconds = 0f;
                source.ResetTimedProgress();
            }
            ApplyModeToKinetic(rebuildNetwork);
            if (!releaseMode)
            {
                Api.ModLoader.GetModSystem<KineticNetworkManager>()?.OnSourceChanged(Pos, 0f);
            }
            MarkDirty(true);
        }

        private List<BEFlywheel> CollectBank()
        {
            var bank = new List<BEFlywheel>();
            var seen = new HashSet<BlockPos>();
            AddBankMember(this, bank, seen);

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            Vec3i axis = EnumKineticAxisExtensions.UnitVector(kinetic?.Axis ?? EnumKineticAxis.X);
            CollectBankDirection(axis, bank, seen);
            CollectBankDirection(new Vec3i(-axis.X, -axis.Y, -axis.Z), bank, seen);
            return bank;
        }

        private void CollectBankDirection(Vec3i step, List<BEFlywheel> bank, HashSet<BlockPos> seen)
        {
            BlockPos pos = Pos.Copy();
            BEBehaviorKinetic ownKinetic = GetBehavior<BEBehaviorKinetic>();

            while (true)
            {
                pos = new BlockPos(pos.X + step.X, pos.Y + step.Y, pos.Z + step.Z, pos.dimension);
                BEFlywheel next = MultiblockHelper.GetMultiblockAwareBE(Api.World, pos) as BEFlywheel;
                if (next == null) return;

                BEBehaviorKinetic otherKinetic = next.GetBehavior<BEBehaviorKinetic>();
                if (ownKinetic == null || otherKinetic == null || otherKinetic.Axis != ownKinetic.Axis) return;
                if (!AddBankMember(next, bank, seen)) return;
            }
        }

        private static bool AddBankMember(BEFlywheel flywheel, List<BEFlywheel> bank, HashSet<BlockPos> seen)
        {
            if (flywheel?.Pos == null || !seen.Add(flywheel.Pos.Copy())) return false;
            bank.Add(flywheel);
            return true;
        }

        private void OnServerTick(float dt)
        {
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            BEBehaviorKineticSource source = GetBehavior<BEBehaviorKineticSource>();
            if (kinetic == null || source == null) return;

            ApplyModeToKinetic(false);

            KineticNetwork net = kinetic.Network as KineticNetwork;
            bool validNetwork = IsValidStorageNetwork(net);
            bool externalDrive = net?.SourcePos != null
                && !net.SourcePos.Equals(Pos)
                && validNetwork
                && MathF.Abs(kinetic.ActualRPM) >= KineticNetwork.MinAbsRPM;

            if (!releaseMode && externalDrive)
            {
                ChargeFromExternalDrive(kinetic, source, dt);
                return;
            }

            if (releaseMode && storedSeconds > minReleaseSeconds)
            {
                Discharge(source, kinetic, dt);
                return;
            }

            if (source.IsActive)
            {
                StopDischarge(source);
            }
            if (releaseMode && storedSeconds <= minReleaseSeconds)
            {
                releaseMode = false;
                ApplyModeToKinetic(true);
            }
            if (!validNetwork)
            {
                ApplyIdleLeak(dt);
            }
        }

        private void ChargeFromExternalDrive(BEBehaviorKinetic kinetic, BEBehaviorKineticSource source, float dt)
        {
            if (source.IsActive)
            {
                source.DecaySeconds = 0f;
                source.ResetTimedProgress();
            }

            float rpm = MathF.Abs(kinetic.ActualRPM);
            spinRPM = MathF.Max(spinRPM, rpm);
            spinDirection = kinetic.ActualRPM < 0f ? -1 : 1;

            float inputPower = chargeStress * rpm;
            float ratedOutputPower = dischargeStress * maxOutputRPM;
            float secondsGained = ratedOutputPower > 0f ? inputPower / ratedOutputPower * chargeEfficiency * dt : 0f;
            storedSeconds = MathF.Min(maxStoredSeconds, storedSeconds + secondsGained);
            MarkDirty(true);
        }

        private void Discharge(BEBehaviorKineticSource source, BEBehaviorKinetic kinetic, float dt)
        {
            float outputRPM = GameMath.Clamp(spinRPM, KineticNetwork.MinAbsRPM, maxOutputRPM);
            source.TargetRPM = outputRPM;
            source.Wind(1.25f, spinDirection);

            storedSeconds -= outputRPM / maxOutputRPM * dt * burstMultiplier;
            spinRPM = outputRPM;

            if (storedSeconds <= minReleaseSeconds || spinRPM < KineticNetwork.MinAbsRPM)
            {
                storedSeconds = MathF.Max(0f, storedSeconds);
                releaseMode = false;
                ApplyModeToKinetic(true);
                StopDischarge(source);
            }

            MarkDirty(true);
        }

        private void ApplyIdleLeak(float dt)
        {
            if (storedSeconds <= 0f) return;
            storedSeconds = MathF.Max(0f, storedSeconds - leakSecondsPerSecond * dt);
            spinRPM *= MathF.Pow(0.985f, dt);
        }

        private static bool IsValidStorageNetwork(KineticNetwork net)
        {
            return net != null
                && net.NodeCount > 1
                && !net.IsConflicted
                && !net.IsOverstressed;
        }

        private void StopDischarge(BEBehaviorKineticSource source)
        {
            source.DecaySeconds = 0f;
            source.ResetTimedProgress();
            Api.ModLoader.GetModSystem<KineticNetworkManager>()?.OnSourceChanged(Pos, 0f);
        }

        private void ApplyModeToKinetic(bool rebuildNetwork)
        {
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null) return;

            float desired = releaseMode ? -dischargeStress * burstMultiplier : chargeStress;
            if (MathF.Abs(kinetic.StressImpact - desired) < 0.001f) return;

            kinetic.StressImpact = desired;

            if (Api?.Side == EnumAppSide.Server && rebuildNetwork)
            {
                RebuildNetwork();
            }
        }

        private void RebuildNetwork()
        {
            KineticNetworkManager mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
            mgr?.OnRemoved(Pos);
            mgr?.OnPlaced(Pos);
        }

        private void GetBankDialogStats(out int bankCount, out float bankMaxBurst, out float bankBurstOutput, out float bankRemainingSeconds)
        {
            List<BEFlywheel> bank = CollectBank();
            bankCount = bank.Count;
            bankMaxBurst = GetBankMaxBurstMultiplier(bank);
            bankBurstOutput = 0f;
            bankRemainingSeconds = float.MaxValue;

            foreach (BEFlywheel flywheel in bank)
            {
                bankBurstOutput += flywheel.dischargeStress * flywheel.maxOutputRPM * flywheel.burstMultiplier;
                bankRemainingSeconds = MathF.Min(bankRemainingSeconds, flywheel.EstimatedRemainingSeconds());
            }

            if (bankRemainingSeconds == float.MaxValue) bankRemainingSeconds = 0f;
        }

        private static float GetBankMaxBurstMultiplier(List<BEFlywheel> bank)
        {
            float max = 16f;
            foreach (BEFlywheel flywheel in bank)
            {
                max = MathF.Min(max, flywheel.maxBurstMultiplier);
            }
            return NormalizeBurstMultiplier(max, 16f);
        }

        private float EstimatedRemainingSeconds()
        {
            return burstMultiplier > 0f ? storedSeconds / burstMultiplier : 0f;
        }

        private static float NormalizeBurstMultiplier(float requested, float maxAllowed)
        {
            float max = MathF.Max(1f, maxAllowed);
            float selected = 1f;
            foreach (float step in BurstSteps)
            {
                if (step <= requested + 0.001f && step <= max + 0.001f)
                {
                    selected = step;
                }
            }
            return selected;
        }

        public void SetStoredSecondsFromItem(float seconds)
        {
            storedSeconds = GameMath.Clamp(seconds, 0f, maxStoredSeconds);
            spinRPM = storedSeconds > minReleaseSeconds ? maxOutputRPM : 0f;
            spinDirection = 1;
            releaseMode = false;
            burstMultiplier = NormalizeBurstMultiplier(burstMultiplier, maxBurstMultiplier);
            ApplyModeToKinetic(false);
            MarkDirty(true);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);

            float percent = StoredEnergy01 * 100f;
            float ratedSu = dischargeStress * maxOutputRPM;
            float burstSu = ratedSu * burstMultiplier;
            BlockFacing output = OutputFacing();

            sb.AppendLine($"Flywheel charge: {storedSeconds:F0}/{maxStoredSeconds:F0}s ({percent:F0}%)");
            sb.AppendLine(releaseMode ? "Mode: releasing stored rotation" : "Mode: charging buffer");
            sb.AppendLine($"Input side: {output.Opposite.Code}; output side: {output.Code}");
            sb.AppendLine($"Burst setting: {burstMultiplier:F0}x");
            sb.AppendLine($"Burst output: {burstSu:F0} SU @ {maxOutputRPM:F0} RPM");
            if (storedSeconds > minReleaseSeconds)
            {
                sb.AppendLine($"Estimated run time: ~{EstimatedRemainingSeconds():F0}s");
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("storedSeconds", storedSeconds);
            tree.SetFloat("spinRPM", spinRPM);
            tree.SetInt("spinDirection", spinDirection);
            tree.SetBool("releaseMode", releaseMode);
            tree.SetFloat("burstMultiplier", burstMultiplier);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            storedSeconds = tree.GetFloat("storedSeconds", 0f);
            if (storedSeconds <= 0f)
            {
                float legacyEnergy = tree.GetFloat("storedEnergy", 0f);
                storedSeconds = legacyEnergy / (dischargeStress * maxOutputRPM);
            }
            spinRPM = tree.GetFloat("spinRPM", 0f);
            spinDirection = tree.GetInt("spinDirection", 1);
            releaseMode = tree.GetBool("releaseMode", false);
            burstMultiplier = NormalizeBurstMultiplier(tree.GetFloat("burstMultiplier", 1f), 16f);
            if (spinDirection != -1) spinDirection = 1;
            ApplyModeToKinetic(false);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null) return null;
            if (!fromPos.Equals(KineticPortPos())) return null;

            Vec3i offset = new Vec3i(toPos.X - fromPos.X, toPos.Y - fromPos.Y, toPos.Z - fromPos.Z);
            int absSum = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
            if (absSum != 1) return null;

            EnumKineticAxis offsetAxis = EnumKineticAxisExtensions.FromVec(offset);
            if (offsetAxis != kinetic.Axis) return null;

            BEFlywheel otherFlywheel = MultiblockHelper.GetMultiblockAwareBE(Api.World, toPos) as BEFlywheel;
            if (otherFlywheel != null)
            {
                BEBehaviorKinetic otherKinetic = otherFlywheel.GetBehavior<BEBehaviorKinetic>();
                if (otherKinetic != null && otherKinetic.Axis == kinetic.Axis)
                {
                    return new KineticConnectionResult(1f, 1);
                }
            }

            Vec3i output = OutputFacing().Normali;
            bool outputSide = offset.X == output.X && offset.Y == output.Y && offset.Z == output.Z;
            bool inputSide = offset.X == -output.X && offset.Y == -output.Y && offset.Z == -output.Z;

            if (releaseMode ? outputSide : inputSide)
            {
                return new KineticConnectionResult(1f, 1);
            }

            return null;
        }

        private BlockFacing OutputFacing()
        {
            string side = Block?.Variant?["side"];
            return side switch
            {
                "n" => BlockFacing.NORTH,
                "e" => BlockFacing.EAST,
                "s" => BlockFacing.SOUTH,
                "w" => BlockFacing.WEST,
                _ => FallbackOutputFacing()
            };
        }

        private BlockFacing FallbackOutputFacing()
        {
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            return kinetic?.Axis == EnumKineticAxis.Z ? BlockFacing.SOUTH : BlockFacing.EAST;
        }

        private BlockPos KineticPortPos()
        {
            JsonObject offset = Block?.Attributes?["kineticShaftControllerOffset"];
            if (offset == null || !offset.Exists) return Pos;

            return new BlockPos(
                Pos.X + offset["x"].AsInt(),
                Pos.Y + offset["y"].AsInt(),
                Pos.Z + offset["z"].AsInt(),
                Pos.dimension);
        }
    }
}
