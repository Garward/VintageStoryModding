namespace VintageKinematics.Storage.Acceptance
{
    public static class StorageRejectionCodes
    {
        public const string InvalidStack = "vintagekinematics:storage-reject-invalid-stack";
        public const string InvalidQuantity = "vintagekinematics:storage-reject-invalid-quantity";
        public const string MissingCode = "vintagekinematics:storage-reject-missing-code";
        public const string Transitioning = "vintagekinematics:storage-reject-transitioning";
        public const string Temperature = "vintagekinematics:storage-reject-temperature";
        public const string NestedStack = "vintagekinematics:storage-reject-nested-stack";
        public const string Backpack = "vintagekinematics:storage-reject-backpack";
        public const string LiquidContainer = "vintagekinematics:storage-reject-liquid-container";
        public const string Blacklisted = "vintagekinematics:storage-reject-blacklisted";
        public const string InspectionFailed = "vintagekinematics:storage-reject-inspection-failed";
    }
}
