using ProtoBuf;

namespace VRPG.Network;

[ProtoContract]
public sealed class HubOptionsPacket
{
    [ProtoMember(1)]
    public bool ShowCooldownNotifications { get; set; }

    [ProtoMember(2)]
    public bool ShowResourceNotifications { get; set; }

    // Client-local presentation state. The client overlays this after receiving the server profile.
    [ProtoMember(3)]
    public bool SkillHotbarLocked { get; set; } = true;

    // Client-local presentation state. Hidden slots retain their server loadout assignments.
    [ProtoMember(4)]
    public int SkillHotbarSlots { get; set; } = 4;
}

[ProtoContract]
public sealed class UpdateHubOptionsPacket
{
    [ProtoMember(1)]
    public bool ShowCooldownNotifications { get; set; }

    [ProtoMember(2)]
    public bool ShowResourceNotifications { get; set; }
}
