using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Belt block. Not placed directly by the player — produced by <see cref="VintageKinematics.Items.ItemBelt"/>
    /// which spans segments between two parallel pulley shafts. The variant <c>direction</c> ∈ n/e/s/w
    /// is the item-travel direction; the two driving shafts sit at the head and tail and run on the
    /// horizontal axis perpendicular to that direction.
    /// </summary>
    public class BlockBelt : Block, IKineticConnector
    {
        /// <summary>
        /// Belts only accept kinetic connections at the head/tail ends of the chain, where the
        /// driving pulley shaft sits. The shaft must run on the axis perpendicular to belt travel.
        /// </summary>
        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            Vec3i offset = new Vec3i(toPos.X - fromPos.X, toPos.Y - fromPos.Y, toPos.Z - fromPos.Z);
            int absSum = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
            if (absSum != 1) return null;
            if (offset.Y != 0) return null;

            string dir = Variant["direction"];
            EnumKineticAxis travelAxis = TravelAxis(dir);
            bool offsetAlongTravel = travelAxis == EnumKineticAxis.X ? offset.X != 0 : offset.Z != 0;

            // Chain-internal belt-to-belt: same direction belts sharing a face along the travel
            // axis always couple at 1:1 regardless of node axis. Middle+HasShaft segments expose
            // their inserted shaft's axis to the network, which may differ from the pulley axis
            // of their neighbours, so we can't apply the pulley-axis check here.
            if (offsetAlongTravel && IsBeltCode(other.BlockCode))
            {
                string otherDir = DirectionFromBeltCode(other.BlockCode);
                if (otherDir == dir) return new KineticConnectionResult(1f, 1);
                return null;
            }

            // External shaft entering at a pulley face: must be at the head/tail end (offset along
            // travel axis), running on the pulley axis (perpendicular to travel).
            EnumKineticAxis pulleyAxis = travelAxis == EnumKineticAxis.X ? EnumKineticAxis.Z : EnumKineticAxis.X;
            if (!offsetAlongTravel) return null;
            if (other.Axis != pulleyAxis) return null;

            return new KineticConnectionResult(1f, 1);
        }

        private static bool IsBeltCode(string code)
        {
            return code != null && code.StartsWith("belt-", StringComparison.Ordinal);
        }

        private static string DirectionFromBeltCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            int dash = code.LastIndexOf('-');
            if (dash < 0 || dash == code.Length - 1) return null;
            return code.Substring(dash + 1);
        }

        public static EnumKineticAxis TravelAxis(string direction) => direction switch
        {
            "e" or "w" => EnumKineticAxis.X,
            _ => EnumKineticAxis.Z
        };

        /// <summary>Unit vector pointing toward the head end (item travel direction) for a given variant.</summary>
        public static Vec3i HeadOffset(string direction) => direction switch
        {
            "n" => new Vec3i(0, 0, -1),
            "s" => new Vec3i(0, 0,  1),
            "e" => new Vec3i(1, 0,  0),
            "w" => new Vec3i(-1, 0, 0),
            _   => new Vec3i(0, 0,  1)
        };

        /// <summary>Direction code (n/e/s/w) that travels from <paramref name="from"/> toward <paramref name="to"/>.</summary>
        public static string DirectionFromOffset(Vec3i offset)
        {
            if (Math.Abs(offset.X) > Math.Abs(offset.Z))
            {
                return offset.X > 0 ? "e" : "w";
            }
            return offset.Z > 0 ? "s" : "n";
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            if (world.BlockAccessor.GetBlockEntity(pos) is BEBelt belt)
            {
                belt.OnAxisNeighborChanged(neibpos);
            }
        }

        /// <summary>
        /// Right-click on a Middle belt segment with a shaft in hand inserts the shaft. Sneak +
        /// right-click on a Middle belt that already has a shaft extracts it back. Other clicks
        /// fall through.
        /// </summary>
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer == null || blockSel == null)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEBelt belt)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            bool sneaking = byPlayer.Entity?.Controls?.Sneak == true;

            // Only middle segments accept shaft insertion (Start/End already have pulleys).
            if (belt.Part != EnumBeltPart.Middle)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            // Extract: sneak + has shaft + empty hand or any hand.
            if (sneaking && belt.HasShaft)
            {
                if (world.Side == EnumAppSide.Server)
                {
                    string axis = belt.InsertedShaftAxis ?? "y";
                    AssetLocation shaftCode = new AssetLocation("vintagekinematics", "shaft-" + axis);
                    Block shaftBlock = world.GetBlock(shaftCode);
                    if (shaftBlock != null)
                    {
                        ItemStack drop = new ItemStack(shaftBlock);
                        if (!byPlayer.InventoryManager.TryGiveItemstack(drop))
                        {
                            world.SpawnItemEntity(drop, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                        }
                    }
                    belt.SetShaft(false);
                }
                return true;
            }

            // Insert: not sneaking, hand has a shaft, no shaft yet.
            if (!sneaking && !belt.HasShaft && slot?.Itemstack?.Block is BlockShaft heldShaft)
            {
                if (world.Side == EnumAppSide.Server)
                {
                    string axis = heldShaft.Variant["axis"] ?? "y";
                    belt.SetShaft(true, axis);
                    slot.TakeOut(1);
                    slot.MarkDirty();
                }
                return true;
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
