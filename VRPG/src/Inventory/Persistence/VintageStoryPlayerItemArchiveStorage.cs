using System;
using Vintagestory.API.Server;

namespace VRPG.Inventory.Persistence;

public sealed class VintageStoryPlayerItemArchiveStorage : IPlayerItemArchiveStorage
{
    private readonly ISaveGame saveGame;

    public VintageStoryPlayerItemArchiveStorage(ISaveGame saveGame)
    {
        this.saveGame = saveGame ?? throw new ArgumentNullException(nameof(saveGame));
    }

    public byte[]? Read(string key) => saveGame.GetData(key);

    public void Write(string key, byte[] data) => saveGame.StoreData(key, data);
}
