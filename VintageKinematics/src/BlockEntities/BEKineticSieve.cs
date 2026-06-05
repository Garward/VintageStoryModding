using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Gui;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Horizontal 1x1x3 sieve drum. The shared sieve base owns inventory, packets, output
    /// pushing, weighted rolls, vanilla pannable fallback, and yield scaling.
    /// </summary>
    public class BEKineticSieve : BEKineticSieveProcessorBase
    {
        public const int SlotOutputLast = 9;
        public const int InventorySize = 10;
        public const int PacketIdOpenDialog = 5600;

        public BEKineticSieve() : base("kineticsieve", InventorySize, SlotOutputLast) { }

        protected override int OpenDialogPacketId => PacketIdOpenDialog;
        protected override string TitleLangCode => "vintagekinematics:kineticsieve-title";
        protected override string FallbackTitle => "Kinetic Sieve";
        protected override float EffectVolume => 0.6f;
        protected override int EffectParticleCount => 12;

        protected override IOFaceMap BuildIOFaceMap()
        {
            BlockFacing inputFace = DrumEastRailFace();
            BlockFacing outputSideFace = DrumWestRailFace();
            IOFaceMap map = new IOFaceMap(Pos);

            foreach (BlockPos cell in AllCells())
            {
                map.MapInput(cell, BlockFacing.UP, SlotInput);
                map.MapInput(cell, inputFace, SlotInput);
                for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
                {
                    map.MapOutput(cell, outputSideFace, i);
                    map.MapOutput(cell, BlockFacing.DOWN, i);
                }
            }

            return map;
        }

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            return new GuiDialogKineticSieve(title, MachineInventory, Pos, capi);
        }

        protected override float PanningYieldMultiplier(VintageKinematicsConfig cfg)
        {
            return cfg?.ResolveKineticSievePanningYield() ?? 1f;
        }

        protected override Vec3d OutputDropPosition()
        {
            BlockPos mid = MiddleCellPos();
            return new Vec3d(mid.X + 0.5, mid.Y + 0.1, mid.Z + 0.5);
        }

        protected override Vec3d EffectPosition()
        {
            BlockPos mid = MiddleCellPos();
            return new Vec3d(mid.X + 0.5, mid.Y + 0.5, mid.Z + 0.5);
        }

        private BlockFacing DrumEastRailFace()
        {
            string side = Block?.Variant?["side"] ?? "n";
            switch (side)
            {
                case "n": return BlockFacing.EAST;
                case "e": return BlockFacing.SOUTH;
                case "s": return BlockFacing.WEST;
                case "w": return BlockFacing.NORTH;
                default:  return BlockFacing.EAST;
            }
        }

        private BlockFacing DrumWestRailFace()
        {
            string side = Block?.Variant?["side"] ?? "n";
            switch (side)
            {
                case "n": return BlockFacing.WEST;
                case "e": return BlockFacing.NORTH;
                case "s": return BlockFacing.EAST;
                case "w": return BlockFacing.SOUTH;
                default:  return BlockFacing.WEST;
            }
        }

        private BlockPos[] AllCells()
        {
            BlockPos mid = MiddleCellPos();
            int dx = mid.X - Pos.X;
            int dz = mid.Z - Pos.Z;
            BlockPos far = new BlockPos(Pos.X + 2 * dx, Pos.Y, Pos.Z + 2 * dz, Pos.dimension);
            return new[] { Pos, mid, far };
        }

        private BlockPos MiddleCellPos()
        {
            string side = Block?.Variant?["side"];
            int dx = 0, dz = 0;
            switch (side)
            {
                case "n": dz =  1; break;
                case "s": dz = -1; break;
                case "e": dx = -1; break;
                case "w": dx =  1; break;
            }
            return new BlockPos(Pos.X + dx, Pos.Y, Pos.Z + dz, Pos.dimension);
        }
    }
}
