# CSharpFar Azure Blob Storage

An external CSharpFar API v2 file-panel extension for Azure Blob Storage. The source is in the CSharpFar repository, but the host does not reference the plugin and does not load it as a built-in module. Azure SDK dependencies are private to the extension.

## Installation

Download the Azure Blob plugin ZIP from a CSharpFar release containing the extension (or a successful Azure Blob CI artifact). Extract the entire `azure-blob` folder into `<CSharpFar config>/extensions/` and restart CSharpFar 1.0.129 or newer with API v2. Do not copy DLLs into the application installation directory. No SDK is needed to install the extension.

Open Plugins → Azure Blob Storage, the disk-selection menu, or type `azure:` in the command line. Select a saved connection, Quick connect or New connection. Settings live in `<config>/plugins/azure-blob/connections.json`; they contain only connection metadata and environment variable names. The normal application config folder is the platform application-data directory; portable mode uses `CSharpFar.config` beside the executable.

## Authentication

| Method | Configuration |
|---|---|
| Connection string | Set an environment variable, e.g. AZURE_STORAGE_CONNECTION, and enter its **name** in the connection editor. |
| SAS token | Enter a storage endpoint without a query and the environment variable name containing the SAS token. |
| SAS URL | Store the account or container SAS URL in an environment variable; enter only that variable name. |
| Microsoft Entra ID | Run `az login` before starting CSharpFar; enter the storage endpoint. Azure CLI must be accessible to the application process. |
| Azurite | Choose Azurite with an empty credential reference for UseDevelopmentStorage=true; for a custom emulator endpoint, use an environment variable containing its connection string. |

For example, on Windows PowerShell, set `$env:AZURE_STORAGE_CONNECTION = '<connection string>'` in a private shell session, then launch CSharpFar from that shell. Do not paste secrets into the application's input fields or command line. Azure CLI authorization needs a suitable Storage Blob Data role. You can choose a container after connecting or enter its name directly when listing containers is not permitted. Quick connect never writes a saved profile.

## Operations

| Operation | Support |
|---|---|
| Navigation and refresh | Hierarchical listing; case-sensitive names |
| F3 viewer and F5 download | Streaming |
| F5 upload | New blobs only; exclusive staging and no-overwrite publication |
| F7 | Empty virtual directories |
| F8 | File and prefix deletion; never the container root |
| Rename / F6 | Copy then conditional delete; not atomic; host restrictions apply |
| Read-only mode | Mutations rejected |
| Overwrite, F4, batch transactions | Not supported |

Directory markers are zero-byte blobs named `.csharpfar-directory` within the prefix with metadata `csharpfar_directory=azure-blob-v1`. Other zero-byte files are ordinary files. A directory rename or delete can be partial; refresh before retrying. If publication is uncertain, inspect the staging and target names before retrying; existing destination blobs are never intentionally replaced. Flat namespace directories cannot be reserved atomically, so a concurrent writer can create sibling blobs while a directory operation is in progress. Unrepresentable names (leading slash, repeated separators, dot components, literal backslashes, non-marker trailing slash, file/directory name collisions) are rejected rather than normalized into a different object. No forced snapshot/version deletion.

## Build

Normal repository build: `dotnet build CSharpFar.slnx -c Release`.

Independent package build against published SDK and NuGet dependencies: from repository root run `pwsh ./eng/testing/build-azure-blob-extension.ps1`. The resulting ZIP and SHA-256 are under `artifacts/azure-blob/`. The two shared SDK contract DLLs are deliberately absent from the installation package.

To run the provider integration tests against an isolated Azurite instance, set `CSHARPFAR_AZURE_TEST_CONNECTION=UseDevelopmentStorage=true` and run `dotnet test tests/CSharpFar.Plugin.AzureBlob.Tests`. An explicitly designated non-production Azure account can also be used; tests create and remove uniquely named disposable containers. Never supply a production account.

## Updates and removal

Close CSharpFar, replace the entire extension folder (do not merge DLLs from older versions), and restart. The settings directory is unaffected. Options → Plugins can disable or re-enable the extension, with restart required. Use `--no-extensions` to start without any external extensions. To uninstall, remove the installation folder while CSharpFar is closed; optionally remove the separate settings directory.

See [SDK feedback](sdk-feedback.md) for verified API constraints and outstanding validation.
