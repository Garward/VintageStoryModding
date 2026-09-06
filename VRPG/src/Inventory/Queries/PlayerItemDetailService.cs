using System;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg.Players;
using VRPG.Network;

namespace VRPG.Inventory.Queries;

public sealed class PlayerItemDetailService
{
    private readonly GearComparisonService comparison;
    public PlayerItemDetailService(GearComparisonService comparison) => this.comparison = comparison;

    public PlayerItemDetailResult Detail(IPlayerItemStore store, RpgPlayerState state, long buildRevision,
        PlayerItemDetailRequest request)
    {
        var result = new PlayerItemDetailResult { ConnectionGeneration = request.ConnectionGeneration,
            QueryId = request.QueryId, EntryId = request.EntryId, RoleCode = request.RoleCode,
            StoreRevision = store.Revision, BuildRevision = buildRevision };
        result.RejectionCode = Validate(request);
        if (result.RejectionCode.Length > 0) return result;
        if (!store.TryGet(request.EntryId, out var detail) || detail == null)
        {
            result.RejectionCode = "entry-missing";
            return result;
        }
        if (detail.Prototype.Category == PlayerItemCategories.Gear)
            result.RejectionCode = comparison.Compare(store, state, request.EntryId, request.RoleCode, result);
        result.Actions = new[] { new PlayerItemActionAvailability { Action = PlayerItemAction.Equip,
            Enabled = false, ReasonCode = result.RejectionCode.Length > 0 ? result.RejectionCode : "unsupported-action" } };
        if (store.Revision != result.StoreRevision)
        {
            result.Comparison = Array.Empty<PlayerItemComparisonPacket>();
            result.RejectionCode = "stale-store";
        }
        return result;
    }

    public static string Validate(PlayerItemDetailRequest request)
    {
        if (request.ProtocolVersion != 1) return "protocol-version";
        if (request.ConnectionGeneration <= 0 || request.QueryId <= 0
            || !PlayerItemQueryService.Text(request.EntryId, 128, true)
            || !PlayerItemQueryService.Text(request.RoleCode, 128)) return "invalid-identity";
        return "";
    }
}
