namespace VRPG.Parties.Persistence;

public enum PartyRepositoryLoadStatus { Absent, Loaded, Unavailable }
public enum PartyRepositorySaveStatus { Committed, Failed, Uncertain }
public sealed record PartyRepositoryLoadResult(PartyRepositoryLoadStatus Status, PartyArchive? Archive);

/// <summary>
/// SyncRoot is shared by all same-world instances, including synchronous item commits. Implementations
/// enforce exclusive world ownership for their lifetime, return independent archive snapshots and accept
/// only the next generation. Invalidate CommittedGeneration before writes and on failures; publish a new
/// stamp only after durable validation. Disposal/ownership loss must make the stamp unavailable.
/// </summary>
public interface IPartyRepository
{
    object SyncRoot { get; }
    /// <summary>Cached validated generation while exclusively owned; null requires explicit reconciliation. Performs no disk I/O.</summary>
    long? CommittedGeneration { get; }
    PartyRepositoryLoadResult Load();
    PartyRepositorySaveStatus Save(PartyArchive archive, long expectedGeneration);
}
