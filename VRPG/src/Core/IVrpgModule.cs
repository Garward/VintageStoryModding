using System;
using Vintagestory.API.Server;

namespace VRPG.Core;

public interface IVrpgModule : IDisposable
{
    string Code { get; }

    void StartServerSide(ICoreServerAPI api);
}
