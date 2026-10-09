#if DEBUG
using System;
using System.Reflection.Metadata;
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
}
#endif
