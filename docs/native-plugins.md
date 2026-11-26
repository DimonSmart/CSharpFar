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

## Manifest (API version 2)

```json
{
  "id": "hello-world",
  "name": "Hello World",
  "version": "1.0.0",
  "apiVersion": 2,
  "assembly": "HelloWorld.dll",
  "entryType": "HelloWorld.Plugin"
}
```

All six fields are mandatory. The ID is a safe, nonempty single path
component (ASCII letters/digits, dash, underscore, dot, excluding `.` and
`..`). IDs are unique **case-insensitively**, including built-in IDs.
`apiVersion` is the integer `2`; unsupported versions are rejected.
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

The SDK package version is independent of the CSharpFar application version.
`eng/PluginSdk.props` is the single source for the SDK NuGet version and stable
assembly version. Releases include downloadable `CSharpFar.Core` and
`CSharpFar.Plugin.Abstractions` packages, along with
`CSharpFar.Plugin.Templates` for `dotnet new`. Until SDK packages are
published to a public NuGet feed, the binary `.nupkg` files can be shared
from GitHub Releases or generated locally using:

```pwsh
./eng/pack-plugin-sdk.ps1
```

This creates `artifacts/plugin-sdk/CSharpFar.Core.2.0.0.nupkg`,
`CSharpFar.Plugin.Abstractions.2.0.0.nupkg`, and
`CSharpFar.Plugin.Templates.2.0.0.nupkg`. Public NuGet.org publishing is
not automated: a maintainer must publish explicitly, with credentials stored
outside source control. Do not attempt to push a package version that already
exists in the feed.

To generate a working plugin from the versioned template package:

```sh
dotnet new install artifacts/plugin-sdk/CSharpFar.Plugin.Templates.2.0.0.nupkg
dotnet new csharpfar-plugin -n MyExtension --PluginId my-extension --PluginTitle "My Extension"
dotnet restore MyExtension.csproj --source /path/to/plugin-sdk
dotnet publish MyExtension.csproj -c Release -o ./extension
```

The generated project automatically copies `plugin.json` into the published
folder. For a plain class library without the template:

```sh
dotnet new classlib -n MyExtension -f net10.0
cd MyExtension
dotnet add package CSharpFar.Plugin.Abstractions --version 2.0.0 --source /path/to/plugin-sdk
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

## SDK compatibility policy

Manifest API version 2 requires the SDK 2.x assemblies (assembly version 2.0.0.0). SDK 1.x binaries are deliberately rejected rather than silently loading incompatible contracts. Rebuild against the new packages and set apiVersion to 2 only after migrating: use FilePanelState.SelectedLocations and PanelViewRequest.SelectedLocations, and FileOperationRequest.Sources and Destination as provider-qualified PanelLocation values. Providers may override path comparison and composition; the default comparison is ordinal. Do not reinterpret remote paths using System.IO.Path.

The CI check `eng/testing/verify-plugin-sdk-compatibility.ps1` builds the
initial SDK contract from the pinned Git commit
`d36124662dc578fcb7110a3f35bbec249f9e2246` (SDK 2; SDK 1 baseline remains `6d9873abc0c855d93325d5227b34fe6f8eabadb8`) and compares both assemblies
with `Microsoft.DotNet.ApiCompat.Tool`. This is an API compatibility check,
not a guarantee about every runtime behavior. Keep the old baseline intact for
binary-compatible releases; do not suppress breaking changes by silently moving
the baseline. An intentionally incompatible SDK needs a reviewed major API
migration and independent compatibility tests.

Build and release pipelines load the binary-only sample extensions. The release
pipeline additionally executes external DLLs against the exact self-contained,
single-file executable it packages. The template package is tested by generating
and publishing a separate project in CI.

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
The factory composes built-ins (SFTP, FTP/FTPS, Processes and Ports,
Network Traffic, Archives) before supplied plugins, in enumeration order.
The sequence is enumerated once per attempt. Normal application startup separately
discovers user-installed extensions and passes them through this same factory.

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
its staged `IFileOperationBatch` view and commits once. A base writable provider does not need this interface: declare OpenWrite and
the supported operations (e.g. CopyTo), implement OpenWriteAsync with the
overwrite flag, and surface write, flush and close failures. The host selects
transactional writes first, then safe publication where applicable, then Best
Effort. ISafeNewFilePublicationProvider is optional and provides exclusive staging
and guaranteed no-overwrite rename. Best Effort does not promise atomicity,
exclusive create or rollback; partial data with unknown ownership is not removed.
Do not advertise an unsupported operation. Existing SDK 2.x providers need no
new mandatory interface or manifest version. See
[archive infrastructure](archive-provider-infrastructure.md) for the
contracts, ownership and remaining limitations.

## Managing installed extensions

Use **Options → Plugins…** to view external packages (including broken
manifests), see installation paths and errors, and enable or disable by folder.
**Enter** toggles, **F4** shows details, and **Esc** closes. Changes are saved
immediately in `<config>/extensions-state.json` but take effect only after
restarting CSharpFar. A plugin already loaded into the current process remains
active until restart. Built-in plugins are not affected.

If an installed extension prevents normal startup, launch
`csharpfar --no-extensions`. This skips all external DLLs for that session
without changing your saved choices, so you can open Options → Plugins… and
disable the broken package before restarting normally. The `disabled` array
stores **installation folder names**, regardless of manifest IDs or whether a
folder currently exists.

## Plugin UI and conflicting file handlers

The neutral `IPluginUiService` facade supports `ShowMessage`, `Input`,
`ShowMenu`, `Confirm`, `ShowChoice` and `ShowHelp`. Confirmation and
choice dialogs are synchronous terminal modal operations; they **must** be
requested from the input/UI thread, not after a background `await`.
For example:

```csharp
bool accepted = context.Ui.Confirm("Delete", "Delete item?", itemName);
int? button = context.Ui.ShowChoice("Options", "Continue?", ["Continue", "Cancel"]);
context.Ui.ShowHelp("Plugin help", ["First action", "Second action"]);
```

When two or more registered file handlers claim an item, CSharpFar shows a
chooser instead of aborting with an ambiguity error. The chooser includes a
normal-open option and Escape cancels without launching any handler.
A plugin's `CanOpen` should remain inexpensive, deterministic and free of
modal UI: it is called before presenting the chooser.

## Asynchronous file panel providers

New file-panel providers should implement the optional `IAsyncFilePanelSource`
interface in CSharpFar.Core. Implement `EnumerateDirectoryAsync` and
`GetItemAsync`, and ensure the inherited asynchronous stream/mutation methods
return promptly rather than performing synchronous network I/O before returning.
Honor cancellation where supported; do not report a completed write as cancelled
or dispose a stream while its operation is still running. Keep path operations
and capabilities free of I/O.

Existing `IFilePanelSource` binaries remain compatible: the host uses a bounded
legacy adapter without replacing the provider's registered identity or optional
interfaces. No manifest API version change is needed for this additive contract.
See [asynchronous file panel sources](architecture/async-file-panel-sources.md)
for scheduling, lifetime and cancellation details.
