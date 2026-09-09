using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRPG.Inventory.Bridges;

public enum PlayerItemBridgeOperation { Pickup, Reward, AdminGrant, HeldItem, Use, Drop, ExternalTransfer, HomePlacement }
public enum PlayerItemBridgeStage { New, Prepared, EffectPending, ExternalRecorded, CanonicalCommitted, Completed, RolledBack, ReconcileRequired, Refused }

public sealed record PlayerItemBridgeRequest(string OperationId, string PlayerUid,
    PlayerItemBridgeOperation Operation, string AdapterCode, string EntryId, long Quantity,
    string Source, string Destination, long ExpectedRevision)
{
    [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }

    /// <summary>Extensions participate in identity structurally: property order is irrelevant,
    /// array order/types are significant, and null/empty extension maps both mean no extensions.</summary>
    public bool HasSameIdentity(PlayerItemBridgeRequest? other) => other != null
        && (this with { ExtensionData = null }) == (other with { ExtensionData = null })
        && JToken.DeepEquals(JObject.FromObject(ExtensionData ?? new Dictionary<string, JToken>()),
            JObject.FromObject(other.ExtensionData ?? new Dictionary<string, JToken>()));
}

/// <summary>Known means every requested unit is accounted for by the external system.</summary>
public sealed record PlayerItemExternalResult(bool Known, long Consumed, long Returned, string Evidence)
{
    [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public sealed class PlayerItemBridgeReceipt
{
    public int Version { get; set; } = 1;
    public PlayerItemBridgeRequest Request { get; set; } = new("", "", PlayerItemBridgeOperation.Drop, "", "", 0, "", "", 0);
    public PlayerItemBridgeStage Stage { get; set; }
    public string ReservationId { get; set; } = "";
    public PlayerItemAmount[] Amounts { get; set; } = Array.Empty<PlayerItemAmount>();
    public PlayerItemExternalResult? ExternalResult { get; set; }
    public PlayerItemPrototype? Claim { get; set; }
    public string Error { get; set; } = "";
    [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }

    public PlayerItemBridgeReceipt Clone() => JsonConvert.DeserializeObject<PlayerItemBridgeReceipt>(JsonConvert.SerializeObject(this))!;
}
