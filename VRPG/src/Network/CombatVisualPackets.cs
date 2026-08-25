using System;
using ProtoBuf;

namespace VRPG.Network;

public enum CombatVisualKind : byte
{
    Impact = 0,
    Burst = 1,
    Ray = 2,
    Damage = 3,
    Heal = 4,
    Shield = 5,
    Break = 6,
    Counter = 7,
    Consume = 8,
    WindowOpen = 9,
    Mark = 10,
    Circle = 11
}

[Flags]
public enum CombatVisualFlags
{
    None = 0,
    Crit = 1,
    Threshold = 2,
    SynchronizeToCarrier = 4
}

[ProtoContract]
public sealed class CombatVisualEventPacket
{
    [ProtoMember(1)]
    public byte Kind { get; set; }

    [ProtoMember(2)]
    public string StyleCode { get; set; } = "";

    [ProtoMember(3)]
    public long SourceEntityId { get; set; }

    [ProtoMember(4)]
    public long TargetEntityId { get; set; }

    [ProtoMember(5)]
    public double X { get; set; }

    [ProtoMember(6)]
    public double Y { get; set; }

    [ProtoMember(7)]
    public double Z { get; set; }

    [ProtoMember(8)]
    public float Radius { get; set; }

    [ProtoMember(9)]
    public float Magnitude { get; set; }

    [ProtoMember(10)]
    public int Flags { get; set; }

    [ProtoMember(11)]
    public byte DamageType { get; set; }

    // Used only when StyleCode is empty (effects with no data definition, e.g. Evasive Step).
    [ProtoMember(12)]
    public int FallbackColorRgba { get; set; }

    /// <summary>
    /// Server-world elapsed time when the represented gameplay event resolved.
    /// Zero means an older or mixed-version sender and disables sync metrics.
    /// </summary>
    [ProtoMember(13)]
    public long ServerEventMs { get; set; }
}

public static class VisualDamageTypes
{
    public const byte Physical = 0;
    public const byte Fire = 1;
    public const byte Cold = 2;
    public const byte Lightning = 3;
    public const byte Rust = 4;
    public const byte Bleed = 5;
    public const byte Heal = 6;

    public static byte FromCode(string damageTypeCode)
    {
        string path = damageTypeCode ?? "";
        int colon = path.IndexOf(':');
        if (colon >= 0)
        {
            path = path.Substring(colon + 1);
        }

        return path.ToLowerInvariant() switch
        {
            "fire" => Fire,
            "cold" => Cold,
            "lightning" => Lightning,
            "rust" => Rust,
            "bleed" => Bleed,
            "heal" => Heal,
            _ => Physical
        };
    }

    public static int ColorRgba(byte id)
    {
        return id switch
        {
            Fire => unchecked((int)0xfff06a28),
            Cold => unchecked((int)0xff78c9f0),
            Lightning => unchecked((int)0xffffe66d),
            Rust => unchecked((int)0xffc96a1e),
            Bleed => unchecked((int)0xffb81c2e),
            Heal => unchecked((int)0xff54d16a),
            _ => unchecked((int)0xfff2ede4)
        };
    }
}
