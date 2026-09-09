using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRPG.Parties.Persistence;

public sealed class PartyArchive
{
    [JsonProperty(Required = Required.Always)] public long Generation { get; set; }
    [JsonProperty(Required = Required.Always)] public List<PartyRecord> Parties { get; set; } = new();
    [JsonProperty(Required = Required.Always)] public List<PartyInvitationRecord> Invitations { get; set; } = new();
    [JsonProperty(Required = Required.Always)] public List<PartyReplayRecord> Replay { get; set; } = new();
    [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public sealed class PartyRecord
{
    [JsonProperty(Required = Required.Always)] public string PartyId { get; set; } = "";
    [JsonProperty(Required = Required.Always)] public string LeaderUid { get; set; } = "";
    [JsonProperty(Required = Required.Always)] public List<string> Members { get; set; } = new();
    [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public sealed class PartyInvitationRecord
{
    [JsonProperty(Required = Required.Always)] public string InvitationId { get; set; } = "";
    [JsonProperty(Required = Required.Always)] public string PartyId { get; set; } = "";
    [JsonProperty(Required = Required.Always)] public string LeaderUid { get; set; } = "";
    [JsonProperty(Required = Required.Always)] public string RecipientUid { get; set; } = "";
    [JsonProperty(Required = Required.Always)] public DateTimeOffset ExpiresAt { get; set; }
    [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }
}

public sealed class PartyReplayRecord
{
    [JsonProperty(Required = Required.Always)] public string ActorUid { get; set; } = "";
    [JsonProperty(Required = Required.Always)] public PartyCommand Command { get; set; } = null!;
    [JsonExtensionData] public IDictionary<string, JToken>? ExtensionData { get; set; }
}
