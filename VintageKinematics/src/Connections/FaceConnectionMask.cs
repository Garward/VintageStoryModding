using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Connections
{
    /// <summary>Canonical six-face mask operations shared by connected block families.</summary>
    public static class FaceConnectionMask
    {
        public static readonly BlockFacing[] Faces =
        {
            BlockFacing.NORTH,
            BlockFacing.EAST,
            BlockFacing.SOUTH,
            BlockFacing.WEST,
            BlockFacing.UP,
            BlockFacing.DOWN
        };

        private const string OrderedCodes = "neswud";

        public static string Code(BlockFacing face)
        {
            if (face == BlockFacing.NORTH) return "n";
            if (face == BlockFacing.EAST) return "e";
            if (face == BlockFacing.SOUTH) return "s";
            if (face == BlockFacing.WEST) return "w";
            if (face == BlockFacing.UP) return "u";
            return "d";
        }

        public static string Opposite(string face)
        {
            return face switch
            {
                "n" => "s",
                "e" => "w",
                "s" => "n",
                "w" => "e",
                "u" => "d",
                "d" => "u",
                _ => null
            };
        }

        public static bool Contains(string mask, string face)
        {
            return !string.IsNullOrEmpty(mask)
                && !string.IsNullOrEmpty(face)
                && mask.Contains(face, StringComparison.Ordinal);
        }

        public static string Normalize(string mask)
        {
            if (string.IsNullOrEmpty(mask)) return null;
            HashSet<char> faces = new HashSet<char>();
            foreach (char face in mask)
            {
                if (OrderedCodes.Contains(face)) faces.Add(face);
            }
            return Sort(faces);
        }

        public static string Add(string mask, string face)
        {
            HashSet<char> faces = Parse(mask);
            if (!string.IsNullOrEmpty(face) && OrderedCodes.Contains(face[0]))
            {
                faces.Add(face[0]);
            }
            return Sort(faces);
        }

        public static string RotateY(string mask)
        {
            HashSet<char> rotated = new HashSet<char>();
            foreach (char face in Parse(mask))
            {
                rotated.Add(face switch
                {
                    'n' => 'e',
                    'e' => 's',
                    's' => 'w',
                    'w' => 'n',
                    _ => face
                });
            }
            return Sort(rotated);
        }

        public static string Sort(IEnumerable<string> faces)
        {
            HashSet<char> parsed = new HashSet<char>();
            foreach (string face in faces)
            {
                if (!string.IsNullOrEmpty(face) && OrderedCodes.Contains(face[0]))
                {
                    parsed.Add(face[0]);
                }
            }
            return Sort(parsed);
        }

        private static HashSet<char> Parse(string mask)
        {
            HashSet<char> faces = new HashSet<char>();
            if (string.IsNullOrEmpty(mask)) return faces;
            foreach (char face in mask)
            {
                if (OrderedCodes.Contains(face)) faces.Add(face);
            }
            return faces;
        }

        private static string Sort(HashSet<char> faces)
        {
            if (faces.Count == 0) return null;
            char[] result = new char[faces.Count];
            int index = 0;
            foreach (char face in OrderedCodes)
            {
                if (faces.Contains(face)) result[index++] = face;
            }
            return new string(result);
        }
    }
}
