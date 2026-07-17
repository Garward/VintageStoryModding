using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class OpenLibraryPacket
{
    [ProtoMember(1)]
    public LibraryEntryPacket[] Entries { get; set; } = System.Array.Empty<LibraryEntryPacket>();
}

[ProtoContract]
public sealed class LibraryEntryPacket
{
    [ProtoMember(1)]
    public string Code { get; set; } = "";

    [ProtoMember(2)]
    public string Name { get; set; } = "";

    [ProtoMember(3)]
    public string Category { get; set; } = "";

    [ProtoMember(4)]
    public string Summary { get; set; } = "";

    [ProtoMember(5)]
    public string Source { get; set; } = "";

    [ProtoMember(6)]
    public string[] Tags { get; set; } = System.Array.Empty<string>();

    [ProtoMember(7)]
    public LibraryFieldPacket[] Fields { get; set; } = System.Array.Empty<LibraryFieldPacket>();
}

[ProtoContract]
public sealed class LibraryFieldPacket
{
    [ProtoMember(1)]
    public string Label { get; set; } = "";

    [ProtoMember(2)]
    public string Value { get; set; } = "";
}
