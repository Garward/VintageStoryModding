namespace VintageKinematics.Api
{
    public interface IExternalWorkProgressProvider
    {
        string ExternalProgressProviderCode { get; }
        float ExternalWorkProgress { get; }
        float ExternalWorkProgressMax { get; }
        bool CanProgressExternalWork();
    }
}
