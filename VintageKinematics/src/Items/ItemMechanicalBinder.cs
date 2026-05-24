using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;

namespace VintageKinematics.Items
{
    public class ItemMechanicalBinder : Item
    {
        private const string StartKey = "vkBinderStart";
        private const string EndKey = "vkBinderEnd";

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            if (slot?.Itemstack == null || byEntity == null || blockSel == null)
            {
                base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefaultAction;
            if (api.Side != EnumAppSide.Server) return;

            IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;
            if (!byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return;
            }

            SelectCorner(slot, byEntity, blockSel.Position, byPlayer);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent || slot?.Itemstack == null || byEntity == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefaultAction;
            if (api.Side != EnumAppSide.Server) return;

            IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;
            if (blockSel == null)
            {
                ClearSelection(slot);
                Notify(byPlayer, "Mechanical Binder selection cleared.");
                return;
            }

            if (!byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            {
                return;
            }

            ITreeAttribute attr = slot.Itemstack.Attributes;
            BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(byEntity.World, blockSel.Position);
            if (be is IContraptionController controller && HasCompleteSelection(attr))
            {
                BlockPos start = attr.GetBlockPos(StartKey);
                BlockPos end = attr.GetBlockPos(EndKey);
                controller.SetSelectionFromWorldBounds(start, end, byPlayer);
                return;
            }

            Notify(byPlayer, "Mechanical Binder: left click blocks to select corners, then right click a contraption controller.");
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-mechanicalbinder-select",
                    MouseButton = EnumMouseButton.Left
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-mechanicalbinder-assign",
                    MouseButton = EnumMouseButton.Right,
                }
            };
        }

        private static void SelectCorner(ItemSlot slot, EntityAgent byEntity, BlockPos pos, IPlayer byPlayer)
        {
            ITreeAttribute attr = slot.Itemstack.Attributes;
            if (!attr.HasAttribute(StartKey + "X") || HasCompleteSelection(attr) || byEntity.Controls?.Sneak == true)
            {
                attr.SetBlockPos(StartKey, pos);
                attr.RemoveAttribute(EndKey + "X");
                attr.RemoveAttribute(EndKey + "Y");
                attr.RemoveAttribute(EndKey + "Z");
                slot.MarkDirty();
                Notify(byPlayer, $"Mechanical Binder start: {FormatPos(pos)}");
                return;
            }

            attr.SetBlockPos(EndKey, pos);
            slot.MarkDirty();
            Notify(byPlayer, $"Mechanical Binder end: {FormatPos(pos)}. Right click a contraption controller to assign it.");
        }

        private static bool HasCompleteSelection(ITreeAttribute attr)
        {
            return attr.HasAttribute(StartKey + "X") && attr.HasAttribute(EndKey + "X");
        }

        private static void ClearSelection(ItemSlot slot)
        {
            ITreeAttribute attr = slot.Itemstack.Attributes;
            attr.RemoveAttribute(StartKey + "X");
            attr.RemoveAttribute(StartKey + "Y");
            attr.RemoveAttribute(StartKey + "Z");
            attr.RemoveAttribute(EndKey + "X");
            attr.RemoveAttribute(EndKey + "Y");
            attr.RemoveAttribute(EndKey + "Z");
            slot.MarkDirty();
        }

        private static string FormatPos(BlockPos pos)
        {
            return $"{pos.X}, {pos.Y}, {pos.Z}";
        }

        private static void Notify(IPlayer player, string message)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }
    }
}
