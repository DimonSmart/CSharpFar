# Azure Blob extension: SDK feedback

This report distinguishes public-contract analysis from observed runtime behavior. The extension uses only CSharpFar.Plugin.Abstractions and CSharpFar.Core; the repository location is not a runtime dependency. The binary package script targets SDK 2.0.0 from v1.0.129.

| Area | Classification | Observation and reproduction | Workaround / recommendation |
|---|---|---|---|
| UI | Limitation | IPluginUiService.Input has no masked-input parameter. Inspect the public signature. | Environment variable references and Azure CLI. Add a secret-input contract with no history. |
| UI | Developer Experience | The public UI facade offers simple menu/input/choice calls, not editable multi-field forms. Reproduce by creating a saved connection. | Sequential prompts; consider a generic form descriptor. |
| UI threading | Limitation | Public modal dialogs must execute on the input thread; no public modal UI dispatch/progress contract is exposed. | Synchronous account-level container selection; actual panel enumeration uses IAsyncFilePanelSource. Provide UI-safe continuation/progress. |
| File provider | Limitation | Flat namespace prefixes cannot guarantee atomic directory moves or recoverable replacement. ISafeNewFilePublicationProvider allows staging without replacement; IRecoverableEntryRenameProvider is deliberately not implemented. | Conditional uploads and ETag deletion; no overwrite; surface partial or unknown outcomes. |
| Settings | Supported | IPluginSettingsService.GetSettingsDirectory supplies a plugin-owned directory separate from deployment. | Versioned metadata JSON; never store connection secrets. |
| Credentials | Limitation | The public credential service does not itself specify an OS-backed secure persistence guarantee. | Do not assume protected persistence; use environment variables or Azure CLI. |
| Dependencies / lifecycle | Supported by documented contract | The public loader isolates private dependencies, shares the two contract assemblies and retires ITemporaryFilePanelSource sessions. | Ship a complete private closure and no duplicate host contract DLLs. |
| Diagnostics | Limitation | Network exception details may contain credentials, particularly SAS URLs. | Suppress SDK request logging and expose sanitized status/error messages; improve typed diagnostic reporting. |
| Testing | Developer Experience | The existing published-host self-test expects exactly two specific sample plugins and their messages, rather than arbitrary third-party extensions. | Use normal startup in a PTY or manual installation; consider a generic manifest/registration smoke mode. |
| NuGet / compatibility | Developer Experience | SDK packages are distributed as release assets, not assumed available on NuGet.org. | Download pinned assets and verify published SHA-256 before restore. |
| Installation | Supported by documented contract | Extension folder, separate settings and Options → Plugins are documented. | Manual installation; verify the full package and update procedure against the published host. |

Runtime CI and live-host results are tracked in GitHub Actions. No claim of real Azure account validation or screenshots is made until those checks have actually executed.
