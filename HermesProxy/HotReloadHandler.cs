#if DEBUG
using System;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading;
using Framework.Logging;
using HermesProxy.World.Dispatch;

[assembly: MetadataUpdateHandler(typeof(HermesProxy.HotReloadHandler))]

namespace HermesProxy;

/// <summary>
/// Runs after <c>dotnet watch</c> applies a hot-reload edit to the live proxy (scripts/hot-reload.sh).
/// </summary>
/// <remarks>
/// An edited method body takes effect on its own. A handler newly added with <c>[HandlesCmsg]</c> or
/// <c>[HandlesSmsg]</c> does not: the dispatch tables are built once by their static initializers,
/// which never run again. Rebuilding them here picks up the regenerated BuildTable, so a new handler
/// starts receiving packets without dropping the connected client.
/// </remarks>
internal static class HotReloadHandler
{
    // Called by the runtime through MetadataUpdateHandlerAttribute after every applied update.
    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        GeneratedCmsgDispatch.Rebuild();
        GeneratedSmsgDispatch.Rebuild();
        Log.Print(LogType.Server,
            $"[HotReload] Applied update ({updatedTypes?.Length ?? 0} types changed), dispatch tables rebuilt.");
    }

    private static Timer? _watcherCheck;

    /// <summary>
    /// Under dotnet watch on Linux, stops the proxy (SIGTERM, a normal shutdown) once the process
    /// that started it is gone.
    /// </summary>
    /// <remarks>
    /// dotnet watch runs the app through <c>dotnet run</c> and restarts it by killing that
    /// process (exit 137). The kill does not reach the app, which lived on as an orphan with its
    /// binary replaced. Re-parenting is what tells it apart, so the parent is checked every
    /// second. PR_SET_PDEATHSIG would not do: it fires when the parent's spawning *thread*
    /// exits, and in a .NET parent that can be a thread pool thread retiring mid-session.
    /// </remarks>
    internal static void ExitWithWatcher()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("DOTNET_WATCH") != "1")
            return;

        int parent = getppid();
        _watcherCheck = new Timer(_ =>
        {
            if (getppid() != parent)
            {
                _watcherCheck?.Dispose();
                kill(Environment.ProcessId, SigTerm);
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private const int SigTerm = 15;

    [DllImport("libc")]
    private static extern int getppid();

    [DllImport("libc")]
    private static extern int kill(int pid, int sig);
}
#endif
