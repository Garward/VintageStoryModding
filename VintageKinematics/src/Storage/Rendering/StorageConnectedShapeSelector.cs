using VintageKinematics.Connections;

namespace VintageKinematics.Storage.Rendering
{
    /// <summary>Pure face-mask to exact storage-model selection.</summary>
    public static class StorageConnectedShapeSelector
    {
        private const string Root = "vintagekinematics:shapes/block/storage/";

        public static StorageConnectedShapeSelection SelectCell(string rawMask)
        {
            string mask = FaceConnectionMask.Normalize(rawMask) ?? string.Empty;
            return Shape("storagecell-mask-" + (mask.Length == 0 ? "isolated" : mask) + ".json");
        }

        public static StorageConnectedShapeSelection SelectElbow(string elbowName)
        {
            foreach (StorageConcaveElbow elbow in StorageConcaveElbow.All)
            {
                if (elbow.Name == elbowName)
                {
                    return Shape("storagecell-elbow-" + elbowName + ".json");
                }
            }
            return null;
        }

        public static StorageConnectedShapeSelection SelectController(string side)
        {
            int rotateY = HorizontalRotation(side);
            return Shape("storagecontroller-north.json", rotateY: rotateY);
        }

        public static StorageConnectedShapeSelection SelectPort(string port, string side)
        {
            string filename = port switch
            {
                "beltoutput" => "storageport-belt-output-north.json",
                "kineticinput" => "storageport-kinetic-input-north.json",
                _ => "storageport-belt-input-north.json"
            };
            return Shape(filename, rotateY: HorizontalRotation(side));
        }

        private static int HorizontalRotation(string side)
        {
            return side switch
            {
                "e" or "east" => 90,
                "s" or "south" => 180,
                "w" or "west" => 270,
                _ => 0
            };
        }

        private static StorageConnectedShapeSelection Shape(
            string filename,
            int rotateX = 0,
            int rotateY = 0,
            int rotateZ = 0)
        {
            return new StorageConnectedShapeSelection(
                Root + filename,
                rotateX,
                rotateY,
                rotateZ);
        }
    }
}
