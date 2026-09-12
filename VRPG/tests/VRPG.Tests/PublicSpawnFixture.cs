using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Server;
using Vintagestory.Server.Systems;

namespace VRPG.Tests;

/// <summary>Real public wrapper bodies and ordinary publication queue, with downstream world/transport probes.</summary>
internal sealed class PublicSpawnFixture : IDisposable
{
    private readonly Harmony probes = new("vrpg.tests.public-spawn." + Guid.NewGuid());
    private static PublicSpawnFixture? current;
    public readonly ServerMain Server = (ServerMain)RuntimeHelpers.GetUninitializedObject(typeof(ServerMain));
    public int Initializations, PrioritySends;

    public PublicSpawnFixture()
    {
        Server.EntityTypesByCode = new();
        Server.EntityTypesByCode[new("game:item")] = new();
        Server.EntitySpawnSendQueue = new();
        Server.Clients = new();
        Server.ServerUdpNetwork = (ServerUdpNetwork)RuntimeHelpers.GetUninitializedObject(typeof(ServerUdpNetwork));
        Server.ServerUdpNetwork.physicsManager = (PhysicsManager)RuntimeHelpers.GetUninitializedObject(typeof(PhysicsManager));
        current = this;
        probes.Patch(AccessTools.Method(typeof(ServerMain), "SpawnEntity_internal"),
            prefix: new HarmonyMethod(typeof(PublicSpawnFixture), nameof(Initialize)) { priority = Priority.Last });
        probes.Patch(AccessTools.Method(typeof(PhysicsManager), "SendPrioritySpawn"),
            prefix: new HarmonyMethod(typeof(PublicSpawnFixture), nameof(SendPriority)));
    }

    private static bool Initialize(Entity entity)
    {
        current!.Initializations++;
        entity.EntityId = 71;
        return false;
    }
    private static bool SendPriority() { current!.PrioritySends++; return false; }
    public void Dispose() { probes.UnpatchAll(probes.Id); current = null; }
}
