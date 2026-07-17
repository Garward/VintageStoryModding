using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class TalentEditorOpenRequestPacket { }

[ProtoContract]
public sealed class TalentEditorSnapshotPacket
{
    [ProtoMember(1)] public TalentTreeSnapshotPacket Tree { get; set; } = new TalentTreeSnapshotPacket();
    [ProtoMember(2)] public string[] TemplateCodes { get; set; } = System.Array.Empty<string>();
    [ProtoMember(3)] public string[] TemplateNames { get; set; } = System.Array.Empty<string>();
    [ProtoMember(4)] public TalentEditorStatPacket[] Stats { get; set; } = System.Array.Empty<TalentEditorStatPacket>();
    [ProtoMember(5)] public string SelectedTemplateCode { get; set; } = "";
    [ProtoMember(6)] public bool Dirty { get; set; }
    [ProtoMember(7)] public string Feedback { get; set; } = "";
    [ProtoMember(8)] public bool FeedbackError { get; set; }
    [ProtoMember(9)] public string[] SavedTreeCodes { get; set; } = System.Array.Empty<string>();
    [ProtoMember(10)] public string[] SavedTreeNames { get; set; } = System.Array.Empty<string>();
    [ProtoMember(11)] public string SelectedSavedTreeCode { get; set; } = "";
    [ProtoMember(12)] public string ActiveTreeCode { get; set; } = "";
    [ProtoMember(13)] public int GraphResetRevision { get; set; }
}

[ProtoContract]
public sealed class TalentEditorStatPacket
{
    [ProtoMember(1)] public string Code { get; set; } = "";
    [ProtoMember(2)] public string Name { get; set; } = "";
    [ProtoMember(3)] public string Category { get; set; } = "";
}

[ProtoContract]
public sealed class TalentEditorSelectTemplatePacket
{
    [ProtoMember(1)] public string TemplateCode { get; set; } = "";
}

[ProtoContract]
public sealed class TalentEditorOpenSavedTreePacket
{
    [ProtoMember(1)] public string TreeCode { get; set; } = "";
}

[ProtoContract]
public sealed class TalentEditorSaveAsPacket
{
    [ProtoMember(1)] public string Name { get; set; } = "";
}

[ProtoContract]
public sealed class TalentEditorDeleteSavedTreePacket
{
    [ProtoMember(1)] public string TreeCode { get; set; } = "";
}

[ProtoContract]
public sealed class TalentEditorAddModifierPacket
{
    [ProtoMember(1)] public string NodeCode { get; set; } = "";
    [ProtoMember(2)] public string StatCode { get; set; } = "";
    [ProtoMember(3)] public string Operation { get; set; } = "add";
    [ProtoMember(4)] public float Amount { get; set; }
}

[ProtoContract]
public sealed class TalentEditorRenameNodePacket
{
    [ProtoMember(1)] public string NodeCode { get; set; } = "";
    [ProtoMember(2)] public string Name { get; set; } = "";
}

[ProtoContract]
public sealed class TalentEditorRenameTreePacket
{
    [ProtoMember(1)] public string Name { get; set; } = "";
}

[ProtoContract]
public sealed class TalentEditorSavePacket { }
