using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRPG.Parties;

public enum PartyAction { Create, Invite, Accept, Decline, Leave, Remove, TransferLeader, Disband }
public enum PartyHealth { Unavailable, Healthy }
public sealed record PartyCommand(
    [property: JsonProperty(Required = Required.Always)] string RequestId,
    [property: JsonProperty(Required = Required.Always)] long ExpectedRevision,
    [property: JsonProperty(Required = Required.Always)] PartyAction Action,
    [property: JsonProperty(Required = Required.Always)] string TargetUid = "",
    [property: JsonProperty(Required = Required.Always)] string InvitationId = "")
{
    // Preserve archive extensions without exposing a mutable authority collection to command callers.
    [JsonExtensionData] private IDictionary<string, JToken>? ExtensionData { get; set; }
}
public sealed record PartyMemberView(string Uid, bool IsOnline);
public sealed record PartyInvitationView(string InvitationId, string PartyId, string LeaderUid, DateTimeOffset ExpiresAt);
public sealed record PartyView(PartyHealth Health, long Revision, string? PartyId, string? LeaderUid,
    IReadOnlyList<PartyMemberView> Members, IReadOnlyList<PartyInvitationView> IncomingInvitations);
public sealed record PartyCommandResult(bool Success, string Code, PartyView View);
