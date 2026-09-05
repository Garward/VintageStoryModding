using System;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Players;

/// <summary>Keeps player progression inside the current savegame.</summary>
public sealed class SaveGameRpgPlayerStateStorage : IRpgPlayerStateStorage
{
    public const string StorageKey = "vrpg:rpg-player-states:v1";

    private readonly ISaveGame saveGame;

    public SaveGameRpgPlayerStateStorage(ISaveGame saveGame)
    {
        this.saveGame = saveGame ?? throw new ArgumentNullException(nameof(saveGame));
    }

    public byte[]? Read() => saveGame.GetData(StorageKey);

    public void Write(byte[] data) => saveGame.StoreData(StorageKey, data);
}
