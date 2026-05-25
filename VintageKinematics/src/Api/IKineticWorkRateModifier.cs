namespace VintageKinematics.Api
{
    public interface IKineticWorkRateModifier
    {
        float ModifyKineticWorkRPM(float rpm, float minRPM);
    }
}
