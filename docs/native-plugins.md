# External native CSharpFar plugins

CSharpFar loads **trusted, user-installed in-process .NET extensions** automatically
in normal startup. The extension loader does not introduce a second registry:
every discovered plugin goes through the existing `ICSharpFarPlugin` /
`IPluginHost` registration pipeline, validation, command dispatch and teardown.

## Install an extension

The directory is `<config-directory>/extensions/<extension-name>/`. Each immediate
child folder may contain a `plugin.json` manifest, the entry DLL, its
`.deps.json`, and its private dependencies. A missing extensions directory is
valid; CSharpFar does not create or download plugins automatically.

```text
<config-directory>/
  extensions/
    hello-world/
      plugin.json
      HelloWorld.dll
      HelloWorld.deps.json
      SharedVersionedDependency.dll
  plugins/
    hello-world/
      counter.txt
```

The **installation** directory and **settings** directory are intentionally
different. The config directory is determined by `JsonSettingsStore` (normally
the platform's application-data folder under `CSharpFar`, or
`CSharpFar.config` beside the executable when `CSharpFar.portable` is present).
It is not the Homebrew/APT/executable installation directory, so an app update
does not replace manually installed extensions.

Copy the complete built extension folder into `extensions/` and restart
CSharpFar. Open the **Plugins** menu and select **Hello World...**, or enter
`hello` at the CSharpFar command line. The command increments
`plugins/hello-world/counter.txt`, shows a message using `IPluginUiService`,
and writes `disposed.marker` when the session shuts down.

External extensions are **not loaded in demo mode**.

## Manifest (API version 1)

```json
{
  "id": "hello-world",
  "name": "Hello World",
  "version": "1.0.0",
  "apiVersion": 1,
  "assembly": "HelloWorld.dll",
  "entryType": "HelloWorld.Plugin"
}
```

All six fields are mandatory. The ID is a safe, nonempty single path
component (ASCII letters/digits, dash, underscore, dot, excluding `.` and
`..`). IDs are unique **case-insensitively**, including built-in IDs.
`apiVersion` is the integer `1`; unsupported versions are rejected.
`assembly` must be a single DLL filename within the extension directory,
not an absolute or relative traversal path. `entryType` identifies a
public, non-abstract class with a public parameterless constructor implementing
`ICSharpFarPlugin`. No DLL scanning or additional entry points are performed.

First-party plugins initialize first. External folders load in deterministic
case-insensitive name order with an ordinal tie-breaker. A bad manifest, missing
dependency, rejected type, ID collision, constructor exception or initializer
failure only rejects that plugin. The startup UI shows one summary; run with
`--diagnostics` for more details. Resolve errors by checking the manifest,
DLL filenames, API version, deployment dependencies and .NET 10 target.

## Build a plugin against the binary SDK

The public contracts are distributed in two versioned .NET 10 NuGet packages:
`CSharpFar.Plugin.Abstractions` and its transitive dependency `CSharpFar.Core`.
No reference to `CSharpFar.App` or checkout of the application is needed by
third-party authors.

The package publishing feed is independent of this repository. Until the SDK is
available in a public NuGet feed, a reproducible local NuGet feed can be created
from the CSharpFar source release **once**, then shared as binary `.nupkg`
files to plugin authors:

```pwsh
./eng/pack-plugin-sdk.ps1
```

This creates `artifacts/plugin-sdk/CSharpFar.Core.1.0.0.nupkg` and
`CSharpFar.Plugin.Abstractions.1.0.0.nupkg`. To publish to NuGet.org, an
authorized maintainer can use `dotnet nuget push artifacts/plugin-sdk/*.nupkg
--source https://api.nuget.org/v3/index.json --api-key <key>`;
credentials must be supplied securely outside source control.

To create a plugin from an empty directory:

```sh
dotnet new classlib -n MyExtension -f net10.0
cd MyExtension
dotnet add package CSharpFar.Plugin.Abstractions --version 1.0.0 --source /path/to/plugin-sdk
```

A minimal `Plugin.cs` can be written as:

```csharp
using CSharpFar.Plugin.Abstractions;

namespace MyExtension;

public sealed class Plugin : ICSharpFarPlugin
{
    public ValueTask InitializeAsync(IPluginHost host, CancellationToken token)
    {
        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = "show",
            Title = "Show greeting",
            ExecuteAsync = (context, ct) =>
            {
                context.Ui.ShowMessage("MyExtension", "Hello");
                return ValueTask.FromResult(PluginCommandResult.Completed());
            }
        });
        host.RegisterMenuItem(new PluginMenuItemDescriptor
        {
            Id = "show-menu", Title = "Show greeting...",
            CommandId = "show", Placement = PluginMenuPlacement.Plugins
        });
        host.RegisterCommandPrefix("myext", "show");
        return ValueTask.CompletedTask;
    }
}
```

Create `plugin.json` as above, replacing `id`, `name`, `assembly` with
`MyExtension.dll`, and `entryType` with `MyExtension.Plugin`. Publish using
`dotnet publish -c Release -o ./extension`, copy `plugin.json` into that
directory, and distribute the folder. The published output includes
`.deps.json` and required private libraries. See the complete independent
[sample](../samples/CSharpFar.Plugin.HelloWorld/README.md).

## Dependency and lifetime rules

Each extension has an `AssemblyLoadContext` and uses
`AssemblyDependencyResolver` for private managed/native libraries. Public
shared contract assemblies `CSharpFar.Plugin.Abstractions` and `CSharpFar.Core`
are resolved from the host's default load context. Extension-private packages
are never installed in a global CSharpFar plugin-dependency directory.
The loader does not support arbitrary dependencies on internal
`CSharpFar.App` assemblies.

Use asynchronous `InitializeAsync` for registration and defer modal dialogs
until a command executes on the UI thread. All registrations are atomic per
plugin. Implement `IAsyncDisposable` or `IDisposable` if the plugin itself
owns disposable resources; the runtime disposes these after owned providers.

**Security:** native extensions run with the full privileges of the CSharpFar
process. `AssemblyLoadContext` is dependency isolation, **not** security
isolation; `IPluginHost` does not sandbox untrusted code. Install only manually
obtained, trusted plugins. No automatic downloading, marketplace, plugin
manager, hot reload, unload or out-of-process execution is provided.

## Explicit composition from application code

Pass explicit `(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)` entries in
`additionalPlugins` to `ApplicationFactory.CreateAsync(..., cancellationToken: token)`.
The factory composes built-ins (SFTP, FTP/FTPS, Processes and Ports) before supplied
plugins, in enumeration order. The sequence is enumerated once per attempt.
There is no automatic discovery or DLL loader.

Use `Create` only if every `InitializeAsync` completes synchronously. An unfinished
initializer makes `Create` throw immediately and aborts host-side publication.
Its cancellation token is signalled, but an arbitrary in-process plugin cannot be
forcibly stopped. Late completion is observed and staged host resources are
cleaned when the plugin actually exits.

`CreateAsync` awaits initializers **sequentially**. A plugin that throws or
cancels itself is omitted, and later independent plugins continue. A cancelled
factory `cancellationToken` instead aborts the entire startup. Register all
commands, menu items, prefixes, providers and file-open handlers through the supplied `IPluginHost`;
the batch commits atomically only after initialization and validation.

A modal `host.Ui.ShowMessage`, `Input` or `ShowMenu` call during initialization
is only valid on the terminal-owning thread. Do not call modal UI after a pending
`await` in `InitializeAsync`: a continuation might execute elsewhere, in which
case the host reports a local plugin failure without touching the terminal.
Move interactive prompts to commands invoked once `Application.Run()` starts.
Asynchronous command results continue through the regular UI action queue.

Failed plugin names and brief sanitized reasons are displayed in one startup
notification; opt-in `--diagnostics` also records plugin events in the existing
diagnostics view. Plugin providers belong to the plugin runtime until shutdown,
while sources opened by providers belong to the `FilePanelSourceRegistry`.
Temporary demo plugin settings are removed on session shutdown. Call
`Application.Run()` for the usual owned-session lifecycle.

## Open files and container panels

`IPluginHost.RegisterFileOpenHandler` registers a handler during initialization.
`CanOpen` should be quick and should claim only files the plugin can confidently
handle. The async `OpenAsync` receives the original `FilePanelItem`,
`PanelLocation` and `IFilePanelSource` and can return a new `PluginPanelOpenRequest`.
Two competing handlers produce an explicit ambiguity error; exceptions and
cancellation do not silently run the system file association.

A container provider may implement `IFileOperationBatchProvider` in addition to
`IFilePanelSource`. For multi-file F5 copies, `FileOperationService` writes to
its staged `IFileOperationBatch` view and commits once. Existing FTP, SFTP,
and local providers do not have to change. See
[archive infrastructure](archive-provider-infrastructure.md) for the
contracts, ownership and remaining limitations.
