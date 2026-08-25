namespace VintageKinematics.Storage.Recovery
{
    internal interface IStorageRecoveryStore
    {
        byte[] Get(string key);
        void Store(string key, byte[] data);
    }
}
