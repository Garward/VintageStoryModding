namespace VintageKinematics.Storage.Rendering
{
    /// <summary>Shape path and quarter-turn rotations selected from a face mask.</summary>
    public sealed class StorageConnectedShapeSelection
    {
        public string ShapePath { get; }
        public int RotateX { get; }
        public int RotateY { get; }
        public int RotateZ { get; }

        public StorageConnectedShapeSelection(
            string shapePath,
            int rotateX = 0,
            int rotateY = 0,
            int rotateZ = 0)
        {
            ShapePath = shapePath;
            RotateX = rotateX;
            RotateY = rotateY;
            RotateZ = rotateZ;
        }
    }
}
