# Composing native CSharpFar plugins

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
commands, menu items, prefixes and providers through the supplied `IPluginHost`;
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
