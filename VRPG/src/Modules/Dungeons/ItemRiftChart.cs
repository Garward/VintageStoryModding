using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace VRPG.Modules.Dungeons;

public sealed class ItemRiftChart : Item
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (blockSel == null)
        {
            return;
        }

        if (api.Side != EnumAppSide.Server)
        {
            handling = EnumHandHandling.PreventDefaultAction;
            return;
        }

        if (TemporalRiftService.Current == null)
        {
            SendMessage(byEntity, "The chart finds no working rift mechanism.");
            handling = EnumHandHandling.PreventDefaultAction;
            return;
        }

        if (TemporalRiftService.Current.TryOpenRiftChart(byEntity, blockSel, slot, out string message))
        {
            SendMessage(byEntity, message);
        }
        else if (!string.IsNullOrWhiteSpace(message))
        {
            SendMessage(byEntity, message);
        }

        handling = EnumHandHandling.PreventDefaultAction;
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "heldhelp-vrpg-open-rift-chart",
                MouseButton = EnumMouseButton.Right
            }
        }.Append(base.GetHeldInteractionHelp(inSlot));
    }

    private void SendMessage(EntityAgent entity, string message)
    {
        if (entity is not EntityPlayer playerEntity)
        {
            return;
        }

        IPlayer player = entity.World.PlayerByUid(playerEntity.PlayerUID);
        if (player is IServerPlayer serverPlayer)
        {
            serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
        }
    }
}
