using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Datastructures;

namespace VintageKinematics.Storage.Persistence
{
    internal static class StorageAttributeCodec
    {
        public static byte[] Encode(ITreeAttribute attributes)
        {
            TreeAttribute normalized = NormalizeTree(attributes);
            return normalized.ToBytes();
        }

        public static bool TryDecode(byte[] bytes, out TreeAttribute attributes)
        {
            attributes = null;
            if (bytes == null || bytes.Length == 0
                || bytes.Length > StoragePersistenceConstants.MaxAttributeBytes)
            {
                return false;
            }

            try
            {
                using MemoryStream stream = new MemoryStream(bytes, writable: false);
                using BinaryReader reader = new BinaryReader(stream);
                TreeAttribute decoded = new TreeAttribute();
                decoded.FromBytes(reader);
                if (stream.Position != stream.Length) return false;
                attributes = decoded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static TreeAttribute NormalizeTree(ITreeAttribute source)
        {
            TreeAttribute normalized = new TreeAttribute();
            if (source == null) return normalized;

            IEnumerable<KeyValuePair<string, IAttribute>> ordered = source
                .OrderBy(pair => pair.Key, StringComparer.Ordinal);
            foreach (KeyValuePair<string, IAttribute> pair in ordered)
            {
                normalized[pair.Key] = NormalizeAttribute(pair.Value);
            }
            return normalized;
        }

        private static IAttribute NormalizeAttribute(IAttribute attribute)
        {
            if (attribute is ITreeAttribute tree) return NormalizeTree(tree);
            if (attribute is TreeArrayAttribute treeArray)
            {
                TreeAttribute[] source = treeArray.value ?? Array.Empty<TreeAttribute>();
                TreeAttribute[] normalized = new TreeAttribute[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    normalized[i] = NormalizeTree(source[i]);
                }
                return new TreeArrayAttribute(normalized);
            }
            return attribute?.Clone();
        }
    }
}
