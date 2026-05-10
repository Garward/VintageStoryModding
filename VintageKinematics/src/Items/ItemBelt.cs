using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.BlockEntities;
using VintageKinematics.Blocks;
using VintageKinematics.Network;

namespace VintageKinematics.Items
{
    /// <summary>
    /// Belt item — used to span a conveyor belt between two parallel pulley shafts.
    /// First right-click on a horizontal shaft (axis x or z) stores an anchor on the held stack.
    /// Second right-click on a parallel shaft validates geometry and places belt segments along
    /// the connecting line. The two shafts must share an axis, sit at the same Y, and the line
    /// between them must run perpendicular to that axis with all intermediate space empty.
    /// </summary>
    public class ItemBelt : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent || slot?.Itemstack == null || byEntity == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            // Suppress vanilla swing on every right-click of this item, regardless of target.
            handling = EnumHandHandling.PreventDefaultAction;

            if (api.Side != EnumAppSide.Server) return;

            IPlayer byPlayer = byEntity.World.PlayerByUid((byEntity as EntityPlayer)?.PlayerUID);
            if (blockSel == null)
            {
                ClearAnchor(slot);
                Notify(byPlayer, "Belt: cancelled — point at a shaft to start a span.");
                return;
            }

            Block targetBlock = byEntity.World.BlockAccessor.GetBlock(blockSel.Position);
            if (targetBlock is not BlockShaft)
            {
                ClearAnchor(slot);
                Notify(byPlayer, "Belt: cancelled — must click a shaft.");
                return;
            }

            string axis = targetBlock.Variant["axis"];
            if (axis != "x" && axis != "z")
            {
                ClearAnchor(slot);
                Notify(byPlayer, "Belt: pulley shafts must be horizontal (x or z axis).");
                return;
            }

            ITreeAttribute attr = slot.Itemstack.Attributes;
            bool hasAnchor = attr.HasAttribute("anchorX");

            if (!hasAnchor)
            {
                attr.SetInt("anchorX", blockSel.Position.X);
                attr.SetInt("anchorY", blockSel.Position.Y);
                attr.SetInt("anchorZ", blockSel.Position.Z);
                attr.SetString("anchorAxis", axis);
                slot.MarkDirty();
                Notify(byPlayer, $"Belt: first shaft set ({axis}). Click the parallel shaft to span.");
                return;
            }

            BlockPos anchor = new BlockPos(attr.GetInt("anchorX"), attr.GetInt("anchorY"), attr.GetInt("anchorZ"));
            string anchorAxis = attr.GetString("anchorAxis");
            BlockPos other = blockSel.Position;

            // Same shaft clicked twice → cancel.
            if (anchor.Equals(other))
            {
                ClearAnchor(slot);
                Notify(byPlayer, "Belt: cancelled.");
                return;
            }

            if (axis != anchorAxis)
            {
                ClearAnchor(slot);
                Notify(byPlayer, "Belt: shafts must share the same axis.");
                return;
            }

            if (anchor.Y != other.Y)
            {
                ClearAnchor(slot);
                Notify(byPlayer, "Belt: shafts must be at the same height.");
                return;
            }

            // Pulley axis = shaft axis. Belt travels perpendicular to that.
            // axis=x → belt along Z (n/s). Shafts must agree on X, differ on Z.
            // axis=z → belt along X (e/w). Shafts must agree on Z, differ on X.
            int dx = other.X - anchor.X;
            int dz = other.Z - anchor.Z;
            int travelDelta;
            Vec3i step;
            string direction;

            if (axis == "x")
            {
                if (dx != 0)
                {
                    ClearAnchor(slot);
                    Notify(byPlayer, "Belt: shafts must line up — same X, differ in Z.");
                    return;
                }
                travelDelta = Math.Abs(dz);
                step = new Vec3i(0, 0, dz > 0 ? 1 : -1);
                direction = dz > 0 ? "s" : "n";
            }
            else // axis == "z"
            {
                if (dz != 0)
                {
                    ClearAnchor(slot);
                    Notify(byPlayer, "Belt: shafts must line up — same Z, differ in X.");
                    return;
                }
                travelDelta = Math.Abs(dx);
                step = new Vec3i(dx > 0 ? 1 : -1, 0, 0);
                direction = dx > 0 ? "e" : "w";
            }

            int segmentCount = travelDelta - 1;
            if (segmentCount < 1)
            {
                ClearAnchor(slot);
                Notify(byPlayer, "Belt: shafts are too close — need at least one block of gap.");
                return;
            }
            if (segmentCount > BEBelt.MaxChainLength)
            {
                ClearAnchor(slot);
                Notify(byPlayer, $"Belt: span too long ({segmentCount} > {BEBelt.MaxChainLength}).");
                return;
            }
            if (slot.Itemstack.StackSize < segmentCount)
            {
                ClearAnchor(slot);
                Notify(byPlayer, $"Belt: not enough belts ({slot.Itemstack.StackSize}/{segmentCount}).");
                return;
            }

            AssetLocation beltCode = new AssetLocation("vintagekinematics", "belt-" + direction);
            Block beltBlock = byEntity.World.GetBlock(beltCode);
            if (beltBlock == null)
            {
                ClearAnchor(slot);
                Notify(byPlayer, "Belt: internal error (belt block not found).");
                return;
            }

            // Verify path is clear before placing anything.
            BlockPos cursor = anchor.AddCopy(step.X, 0, step.Z);
            for (int i = 0; i < segmentCount; i++)
            {
                Block existing = byEntity.World.BlockAccessor.GetBlock(cursor);
                if (existing != null && existing.Id != 0)
                {
                    ClearAnchor(slot);
                    Notify(byPlayer, $"Belt: path blocked at {cursor}.");
                    return;
                }
                cursor = cursor.AddCopy(step.X, 0, step.Z);
            }

            // Replace the two shafts at the endpoints with belt blocks (preserving the line) —
            // these become the visible pulley ends. Then fill the middles.
            var placed = new System.Collections.Generic.List<BlockPos>();
            byEntity.World.BlockAccessor.SetBlock(beltBlock.Id, anchor);
            placed.Add(anchor.Copy());
            byEntity.World.BlockAccessor.SetBlock(beltBlock.Id, other);
            placed.Add(other.Copy());

            cursor = anchor.AddCopy(step.X, 0, step.Z);
            for (int i = 0; i < segmentCount; i++)
            {
                byEntity.World.BlockAccessor.SetBlock(beltBlock.Id, cursor);
                placed.Add(cursor.Copy());
                cursor = cursor.AddCopy(step.X, 0, step.Z);
            }

            api.Event.RegisterCallback(_ =>
            {
                foreach (BlockPos pos in placed)
                {
                    byEntity.World.BlockAccessor.MarkBlockDirty(pos);
                }

                api.ModLoader.GetModSystem<KineticNetworkManager>()?.OnPlaced(anchor);
            }, 50);

            // Consume one belt item per middle segment (endpoints reuse the existing shaft).
            slot.Itemstack.StackSize -= segmentCount;
            if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
            ClearAnchor(slot);
            slot.MarkDirty();
            Notify(byPlayer, $"Belt: span placed ({segmentCount} middle, 2 ends).");
        }

        private static void ClearAnchor(ItemSlot slot)
        {
            if (slot?.Itemstack?.Attributes == null) return;
            slot.Itemstack.Attributes.RemoveAttribute("anchorX");
            slot.Itemstack.Attributes.RemoveAttribute("anchorY");
            slot.Itemstack.Attributes.RemoveAttribute("anchorZ");
            slot.Itemstack.Attributes.RemoveAttribute("anchorAxis");
        }

        private static void Notify(IPlayer player, string message)
        {
            if (player is IServerPlayer sp)
            {
                sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-belt-link",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}
