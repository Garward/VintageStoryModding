using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Local block-space transform for a machine item-stack display renderer.
    /// Coordinates are relative to the controller block entity position.
    /// </summary>
    public class ItemStackDisplayTransform
    {
        public Vec3f Translation { get; set; } = new Vec3f(0.5f, 0.5f, 0.5f);
        public Vec3f Rotation { get; set; } = new Vec3f();
        public Vec3f Scale { get; set; } = new Vec3f(0.5f, 0.5f, 0.5f);
        public double RenderOrder { get; set; } = 0.5;
        public int RenderRange { get; set; } = 24;
    }
}
