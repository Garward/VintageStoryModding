using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
            // axis always couple at 1:1 regardless of node axis.
            if (offsetAlongTravel && IsBeltCode(other.BlockCode))
            {
                string otherDir = DirectionFromBeltCode(other.BlockCode);
                if (otherDir == dir) return new KineticConnectionResult(1f, 1);
                return null;
            }

            // Anything else: no opinion. The cell past head/tail (offset along travel, non-belt)
            // is empty pulley-space and must not propagate kinetics — returning null leaves the
            // default coaxial rule as the only path to an edge, and that rule only fires for
            // pulleyAxis-aligned neighbours coaxial with the pulley axle (i.e. side faces). The
            // travel-axis cell past the chain end isn't along the pulley axis, so no edge forms
            // there.
            return null;
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
            if (EnsureBeltRunEntities(world, pos)) return;
            if (world.BlockAccessor.GetBlockEntity(pos) is BEBelt belt)
            {
                belt.OnAxisNeighborChanged(neibpos);
            }
        }

        public override void OnEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)
        {
            if (EnsureBeltRunEntities(world, pos)) return;
            if (world.BlockAccessor.GetBlockEntity(pos) is BEBelt belt)
            {
                belt.PushRiderInBlock(entity);
            }
            base.OnEntityInside(world, entity, pos);
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

            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;

            if (EnsureBeltRunEntities(world, blockSel.Position)) return true;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEBelt belt)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            bool sneaking = byPlayer.Entity?.Controls?.Sneak == true;

            // Empty-hand right-click on any belt segment scoops every item currently on the chain
            // into the player's inventory. The escape hatch when items are parked at a blocked
            // exit (e.g., the adjacent container refuses the push) — recoverable without breaking
            // the belt.
            if (!sneaking && (slot == null || slot.Empty))
            {
                if (world.Side == EnumAppSide.Server)
                {
                    belt.ClaimItemsAt(blockSel.Position, byPlayer);
                }
                return true;
            }

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
                    AssetLocation shaftCode = new AssetLocation("vintagekinematics", "shaft-y");
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

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world?.BlockAccessor.GetBlockEntity(pos) is BEBelt)
            {
                base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
                return;
            }

            if (world?.Side == EnumAppSide.Server && (byPlayer == null || byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative))
            {
                Item beltItem = world.GetItem(new AssetLocation("vintagekinematics", "belt"));
                if (beltItem != null)
                {
                    world.SpawnItemEntity(new ItemStack(beltItem), pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            world?.BlockAccessor.RemoveBlockEntity(pos);
            world?.BlockAccessor.SetBlock(0, pos);
        }

        private static bool EnsureBeltRunEntities(IWorldAccessor world, BlockPos pos)
        {
            if (world?.Side != EnumAppSide.Server || pos == null) return false;
            Block block = world.BlockAccessor.GetBlock(pos);
            if (block is not BlockBelt) return false;

            string direction = block.Variant?["direction"];
            if (string.IsNullOrEmpty(direction)) return false;

            Vec3i fwd = HeadOffset(direction);
            BlockPos start = pos.Copy();
            int safety = BEBelt.MaxChainLength;
            while (safety-- > 0)
            {
                BlockPos behind = start.AddCopy(-fwd.X, 0, -fwd.Z);
                if (!IsSameDirectionBelt(world, behind, direction)) break;
                start = behind;
            }

            bool repaired = false;
            BlockPos cursor = start.Copy();
            safety = BEBelt.MaxChainLength;
            while (safety-- > 0 && IsSameDirectionBelt(world, cursor, direction))
            {
                if (world.BlockAccessor.GetBlockEntity(cursor) is not BEBelt belt || belt.Direction != direction)
                {
                    world.BlockAccessor.RemoveBlockEntity(cursor);
                    world.BlockAccessor.SpawnBlockEntity("Belt", cursor);
                    repaired = true;
                }
                world.BlockAccessor.MarkBlockDirty(cursor);
                cursor = cursor.AddCopy(fwd.X, 0, fwd.Z);
            }

            if (repaired && world.BlockAccessor.GetBlockEntity(start) is BEBelt startBelt)
            {
                startBelt.RepairChainSoon();
            }

            return repaired;
        }

        private static bool IsSameDirectionBelt(IWorldAccessor world, BlockPos pos, string direction)
        {
            return world?.BlockAccessor.GetBlock(pos) is BlockBelt block
                && block.Variant?["direction"] == direction;
        }
    }
}
