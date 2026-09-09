using System;
using System.IO;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG.Inventory.Persistence;

public enum PlayerItemCustodyPresence { Absent, History, Uncertain }

/// <summary>Feature requests never stand in for historical ownership evidence.</summary>
public sealed record PlayerItemCustodyBootstrap(PlayerItemCustodyPresence Presence, bool OperationsRequested)
{
    public bool RequiresProtection => OperationsRequested || Presence != PlayerItemCustodyPresence.Absent;

    public static PlayerItemCustodyBootstrap Inspect(WorldScopedPlayerItemArchiveStorage storage, bool operationsRequested) =>
        new(storage.ProbePresence(), operationsRequested);

    public static PlayerItemPersistenceModule? CreateModule(ICoreServerAPI api, bool operationsRequested)
    {
        try
        {
            // GetOrCreateDataPath has a write side effect, even for a conclusively empty world.
            var storage = new WorldScopedPlayerItemArchiveStorage(Path.Combine(GamePaths.DataPath, "VRPG/player-items"),
                api.WorldManager.SaveGame.SavegameIdentifier, new VintageStoryPlayerItemArchiveStorage(api.WorldManager.SaveGame));
            return Inspect(storage, operationsRequested).RequiresProtection
                ? new PlayerItemPersistenceModule(operationsRequested, storage) : null;
        }
        catch (Exception error)
        {
            // An invalid world/path must not become an ordinary modloader exception with
            // no guard installed. Refuse at the same loaded-world boundary as module failures.
            return new PlayerItemPersistenceModule(operationsRequested, "Custody storage could not be located: " + error.Message);
        }
    }
}
