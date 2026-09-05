namespace VRPG.Inventory.Persistence;

/// <summary>Minimal byte-store boundary implemented by the Vintage Story world save.</summary>
public interface IPlayerItemArchiveStorage
{
    byte[]? Read(string key);
    void Write(string key, byte[] data);
}
