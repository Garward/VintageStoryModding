namespace VRPG.Inventory.Persistence;

/// <summary>Minimal byte-store boundary implemented by the Vintage Story world save.</summary>
public interface IPlayerItemArchiveStorage
{
    byte[]? Read(string key);
    void Write(string key, byte[] data);
}

/// <summary>Writes return only after the persisted directory entry and bytes are durable.</summary>
public interface IDurablePlayerItemArchiveStorage : IPlayerItemArchiveStorage
{
    bool IsCrashDurable { get; }
}
