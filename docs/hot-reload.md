# Hot reload: edit the proxy without disconnecting

`scripts/hot-reload.sh` runs HermesProxy from source under `dotnet watch`. When a `.cs` file is
saved, `dotnet watch` patches the running process instead of restarting it, so the client and
server connections, their encryption state and the session data all survive. The next packet goes
through the new code.

```sh
scripts/hot-reload.sh ~/path/to/installed/HermesProxy-folder
```

The argument is the folder of an installed HermesProxy. The proxy reads that folder's
`appsettings.json` and uses its `CSV/`, `AccountData/`, item cache, `Logs/` and `PacketsLog/`
(through `HERMES_WORKING_DIRECTORY`), so it behaves like the installed binary. Start it before the
game; a launcher that finds the BNet port already taken should use that proxy rather than start
its own. `dotnet watch` output is copied to `Logs/dotnet-watch.log`.

`dotnet watch` runs on the ASP.NET Core runtime, which a plain `dotnet-sdk` install may lack
(`pacman -S aspnet-runtime` on Arch).

## What applies live

| Change | Live? |
|---|---|
| Edit a method body (handler, codec, system, helper) | Yes |
| Add a method, a type, or a static field | Yes |
| Add `[HandlesCmsg]` / `[HandlesSmsg]` for an opcode that had no handler | Yes, see below |
| Change a method signature, a struct's layout, generic parameters, or a lambda's captures | No: a "rude edit", needs a restart |
| Edit a CSV or `appsettings.json` in the repo | Ignored (the install folder's copies are used) |

On a rude edit `dotnet watch` asks whether to restart. Restarting disconnects the game; answering
no leaves the old code running until the next restart.

`dotnet watch` restarts the app by killing the `dotnet run` process it starts it through, which
does not reach the proxy itself. Under `dotnet watch` (Debug, Linux) the proxy therefore checks every
second whether that parent is gone and shuts down normally when it is, instead of lingering as an
orphan next to its replacement.

The dispatch tables are built once by static initializers, which never re-run. In Debug builds
the generated `GeneratedCmsgDispatch` / `GeneratedSmsgDispatch` keep their table writable and
expose `Rebuild()`, and `HotReloadHandler` (a `MetadataUpdateHandler`) calls both after every
applied update. Each update is logged as `[HotReload] Applied update ...` in the proxy log. Release
builds are unchanged: the table stays `static readonly` and the handler is compiled out.

A fix only changes how later packets are translated. Anything the client already received wrong
stays wrong until it is sent again (the object leaves and re-enters view, `/reload`, or a relog).
