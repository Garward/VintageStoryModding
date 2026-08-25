using System;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace ResponsiveVS.Threading;

public sealed class MainThreadDispatcher
{
    private readonly ICoreClientAPI clientApi;
    private readonly ICoreServerAPI serverApi;

    private MainThreadDispatcher(ICoreClientAPI clientApi, ICoreServerAPI serverApi)
    {
        this.clientApi = clientApi;
        this.serverApi = serverApi;
    }

    public static MainThreadDispatcher ForClient(ICoreClientAPI api)
    {
        return new MainThreadDispatcher(api, null);
    }

    public static MainThreadDispatcher ForServer(ICoreServerAPI api)
    {
        return new MainThreadDispatcher(null, api);
    }

    public void RunClientMain(Action action, string code)
    {
        if (ThreadAssert.IsClientMainThread)
        {
            action();
            return;
        }

        clientApi.Event.EnqueueMainThreadTask(action, code);
    }

    public void RunServerMain(Action action, string code)
    {
        if (ThreadAssert.IsServerMainThread)
        {
            action();
            return;
        }

        serverApi.Event.EnqueueMainThreadTask(action, code);
    }
}
