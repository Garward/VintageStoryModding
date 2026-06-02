using System;
using System.Linq;
using System.Reflection;
using Vintagestory.Server;

namespace ServerDoctor;

internal static class PacketInspector
{
    private static FieldInfo[] payloadFields;

    private static void EnsureFieldCache()
    {
        if (payloadFields != null) return;
        payloadFields = typeof(Packet_Server)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType.IsClass
                        && f.FieldType.Name.StartsWith("Packet_")
                        && f.FieldType != typeof(Packet_Server))
            .ToArray();
    }

    public static (string kind, string source, int sizeBytes) Inspect(Packet_Server packet)
    {
        if (packet == null) return ("<null>", "-", 0);

        EnsureFieldCache();

        string kind = "<empty>";
        object payload = null;
        for (int i = 0; i < payloadFields.Length; i++)
        {
            var v = payloadFields[i].GetValue(packet);
            if (v != null)
            {
                kind = payloadFields[i].FieldType.Name.StartsWith("Packet_")
                    ? payloadFields[i].FieldType.Name.Substring("Packet_".Length)
                    : payloadFields[i].FieldType.Name;
                payload = v;
                break;
            }
        }

        string source = "-";
        try { source = DescribeSource(kind, payload); } catch { source = "<src-err>"; }

        int size = 0;
        try
        {
            var stream = new CitoMemoryStream();
            ((IPacket)packet).SerializeTo(stream);
            size = stream.Position();
        }
        catch { size = 0; }

        return (kind, source, size);
    }

    private static string DescribeSource(string kind, object payload)
    {
        if (payload == null) return "-";
        var t = payload.GetType();

        switch (kind)
        {
            case "BlockEntityMessage":
            {
                int x = (int)t.GetField("X").GetValue(payload);
                int y = (int)t.GetField("Y").GetValue(payload);
                int z = (int)t.GetField("Z").GetValue(payload);
                int pid = (int)t.GetField("PacketId").GetValue(payload);
                return $"({x},{y},{z}) pid={pid}";
            }

            case "ServerSetBlock":
            case "BlockDamage":
            {
                int x = (int)t.GetField("X").GetValue(payload);
                int y = (int)t.GetField("Y").GetValue(payload);
                int z = (int)t.GetField("Z").GetValue(payload);
                return $"({x},{y},{z})";
            }

            case "BlockEntities":
            {
                var arr = t.GetField("BlockEntitites")?.GetValue(payload) as Array;
                if (arr == null || arr.Length == 0) return "(empty)";
                var first = arr.GetValue(0);
                var ft = first.GetType();
                int x = (int)ft.GetField("PosX").GetValue(first);
                int y = (int)ft.GetField("PosY").GetValue(first);
                int z = (int)ft.GetField("PosZ").GetValue(first);
                string cls = (string)ft.GetField("Classname")?.GetValue(first) ?? "?";
                return arr.Length == 1
                    ? $"({x},{y},{z}) {cls}"
                    : $"({x},{y},{z}) {cls} +{arr.Length - 1} more";
            }

            case "EntityPosition":
            case "EntityAttributes":
            case "EntityAttributeUpdate":
            case "Entity":
            case "EntitySpawn":
            case "EntityDespawn":
            case "EntityPacket":
            {
                long eid = (long)t.GetField("EntityId").GetValue(payload);
                return $"entity:{eid}";
            }

            case "Entities":
            {
                return "(bulk-entities)";
            }

            case "BulkEntityAttributes":
            {
                var fu = t.GetField("FullUpdates")?.GetValue(payload) as Array;
                var pu = t.GetField("PartialUpdates")?.GetValue(payload) as Array;
                int fc = fu?.Length ?? 0;
                int pc = pu?.Length ?? 0;
                return $"bulk full={fc} part={pc}";
            }

            case "ServerSetBlocks":
            case "ServerSetDecors":
            {
                return "(bulk-blocks)";
            }

            case "ServerMapChunk":
            {
                int cx = (int)t.GetField("ChunkX").GetValue(payload);
                int cz = (int)t.GetField("ChunkZ").GetValue(payload);
                return $"chunk({cx},{cz})";
            }

            case "ServerChunks":
            {
                var arr = t.GetField("Chunks")?.GetValue(payload) as Array;
                return arr == null ? "-" : $"chunks x{arr.Length}";
            }

            case "CustomPacket":
            {
                int chId = (int)(t.GetField("ChannelId")?.GetValue(payload) ?? 0);
                int mId = (int)(t.GetField("MessageId")?.GetValue(payload) ?? 0);
                return $"ch={chId} msg={mId}";
            }

            case "InventoryUpdate":
            case "InventoryDoubleUpdate":
            case "InventoryContents":
            case "NotifySlot":
            {
                return "(inv)";
            }

            default:
                return "-";
        }
    }
}
