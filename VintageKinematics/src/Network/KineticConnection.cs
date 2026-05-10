namespace VintageKinematics.Network
{
    public struct KineticConnection
    {
        public float Ratio;
        public int Direction;
        // Radians added to the neighbor's render phase relative to current. Used to interleave
        // meshing cog teeth so adjacent cogs aren't rendered tooth-on-tooth ("kissing").
        public float PhaseOffset;

        public KineticConnection(float ratio, int direction)
        {
            Ratio = ratio;
            Direction = direction;
            PhaseOffset = 0f;
        }

        public KineticConnection(float ratio, int direction, float phaseOffset)
        {
            Ratio = ratio;
            Direction = direction;
            PhaseOffset = phaseOffset;
        }
    }
}
