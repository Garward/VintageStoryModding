using System;
using Vintagestory.API.Server;

namespace VintageKinematics.Storage.Recovery
{
    internal sealed class SaveGameStorageRecoveryStore : IStorageRecoveryStore
    {
        private readonly ISaveGame saveGame;

        public SaveGameStorageRecoveryStore(ISaveGame saveGame)
        {
            this.saveGame = saveGame ?? throw new ArgumentNullException(nameof(saveGame));
        }

        public byte[] Get(string key)
        {
            return saveGame.GetData(key);
        }

        public void Store(string key, byte[] data)
        {
            saveGame.StoreData(key, data);
        }
    }
}
