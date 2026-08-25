using System;
using System.Threading;
using ResponsiveVS.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ResponsiveVS.Diagnostics;

public static class ResponsiveDiagnostics
{
    private static ICoreAPI api;
    private static long nextEventId;

    public static ResponsiveDiagnosticsLevel Level { get; private set; } = ResponsiveDiagnosticsLevel.Off;

    public static bool BasicEnabled => Level >= ResponsiveDiagnosticsLevel.Basic;
    public static bool VerboseEnabled => Level >= ResponsiveDiagnosticsLevel.Verbose;
    public static bool TraceEnabled => Level >= ResponsiveDiagnosticsLevel.Trace;

    public static void Initialize(ICoreAPI coreApi)
    {
        api = coreApi;
        Level = ResponsiveVSConfigSystem.Config.DiagnosticsLevel;
    }

    public static void RegisterCommand(ICoreAPI coreApi)
    {
        coreApi.ChatCommands
            .Create("rvsdiag")
            .WithDescription("Toggle ResponsiveVS diagnostics")
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(coreApi.ChatCommands.Parsers.OptionalWord("state"))
            .HandleWith(args =>
            {
                string state = ((string)args[0] ?? "status").ToLowerInvariant();

                if (state == "on" || state == "basic" || state == "1" || state == "true" || state == "enable" || state == "enabled")
                {
                    SetLevel(ResponsiveDiagnosticsLevel.Basic);
                    return TextCommandResult.Success("ResponsiveVS diagnostics set to Basic.");
                }

                if (state == "verbose" || state == "2")
                {
                    SetLevel(ResponsiveDiagnosticsLevel.Verbose);
                    return TextCommandResult.Success("ResponsiveVS diagnostics set to Verbose.");
                }

                if (state == "trace" || state == "3")
                {
                    SetLevel(ResponsiveDiagnosticsLevel.Trace);
                    return TextCommandResult.Success("ResponsiveVS diagnostics set to Trace.");
                }

                if (state == "off" || state == "0" || state == "false" || state == "disable" || state == "disabled")
                {
                    SetLevel(ResponsiveDiagnosticsLevel.Off);
                    PerfCounters.Reset();
                    return TextCommandResult.Success("ResponsiveVS diagnostics disabled.");
                }

                if (state == "counters" || state == "perf")
                {
                    return TextCommandResult.Success(PerfCounters.Summary());
                }

                return TextCommandResult.Success("ResponsiveVS diagnostics are " + Level + ".");
            });
    }

    public static void SetLevel(ResponsiveDiagnosticsLevel level)
    {
        Level = level;
    }

    public static long NextEventId()
    {
        return Interlocked.Increment(ref nextEventId);
    }

    public static void Basic(string message, params object[] args)
    {
        if (BasicEnabled) Log(message, args);
    }

    public static void Verbose(string message, params object[] args)
    {
        if (VerboseEnabled) Log(message, args);
    }

    public static void Trace(string message, params object[] args)
    {
        if (TraceEnabled) Log(message, args);
    }

    public static void Warning(string message, params object[] args)
    {
        api?.Logger.Warning("[responsivevs] " + message, args);
    }

    private static void Log(string message, params object[] args)
    {
        api?.Logger.Notification("[RVS] " + message, args);
    }
}
