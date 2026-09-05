using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VRPG.Inventory.Persistence;

public static class PlayerItemArchiveSlotCodec
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("VRPGINV1");
    private const int HashLength = 32;
    private const int MaximumPayloadBytes = 256 * 1024 * 1024;

    public static byte[] Encode(long generation, byte[] payload)
    {
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0 || payload.Length > MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payload));

        byte[] hash = SHA256.HashData(payload);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(PlayerItemPersistenceSchema.SlotFormatVersion);
        writer.Write(generation);
        writer.Write(payload.Length);
        writer.Write(hash);
        writer.Write(payload);
        writer.Flush();
        return stream.ToArray();
    }

    public static bool TryDecode(
        byte[]? encoded,
        out long generation,
        out byte[] payload,
        out string error)
    {
        generation = 0;
        payload = Array.Empty<byte>();
        error = "";
        if (encoded == null || encoded.Length == 0)
        {
            error = "The snapshot slot is empty.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(encoded, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                throw new InvalidDataException("The snapshot slot has an invalid signature.");
            int format = reader.ReadInt32();
            if (format != PlayerItemPersistenceSchema.SlotFormatVersion)
                throw new InvalidDataException($"Unsupported snapshot slot format {format}.");
            generation = reader.ReadInt64();
            int length = reader.ReadInt32();
            byte[] expectedHash = reader.ReadBytes(HashLength);
            if (generation <= 0 || length <= 0 || length > MaximumPayloadBytes
                || expectedHash.Length != HashLength || stream.Length - stream.Position != length)
            {
                throw new InvalidDataException("The snapshot slot header is invalid.");
            }
            payload = reader.ReadBytes(length);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(payload)))
                throw new InvalidDataException("The snapshot slot checksum does not match.");
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or ArgumentException)
        {
            error = exception.Message;
            generation = 0;
            payload = Array.Empty<byte>();
            return false;
        }
    }
}
