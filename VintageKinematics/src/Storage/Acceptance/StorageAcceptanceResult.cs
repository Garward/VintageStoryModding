namespace VintageKinematics.Storage.Acceptance
{
    public readonly struct StorageAcceptanceResult
    {
        public readonly bool Accepted;
        public readonly string MessageLangCode;

        private StorageAcceptanceResult(bool accepted, string messageLangCode)
        {
            Accepted = accepted;
            MessageLangCode = messageLangCode;
        }

        public static StorageAcceptanceResult Allow()
        {
            return new StorageAcceptanceResult(true, null);
        }

        public static StorageAcceptanceResult Reject(string messageLangCode)
        {
            return new StorageAcceptanceResult(false, messageLangCode);
        }
    }
}
