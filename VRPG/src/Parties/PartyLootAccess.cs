namespace VRPG.Parties;

public enum PartyLootAudience { Everyone, PartyOnly }
public sealed record PartyLootAccess(string DropperUid, PartyLootAudience Audience, string PartyId);
