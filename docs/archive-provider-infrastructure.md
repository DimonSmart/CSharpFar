# Container / archive provider infrastructure (preparation)

The infrastructure is **format-neutral**: ZIP, RAR, 7z and TAR adapters are not yet implemented. The existing `IFilePanelSource` remains the common boundary.

## Enter and nested navigation

A plugin can register an `IFileOpenHandler` during `InitializeAsync`:

```csharp
host.RegisterFileOpenHandler(new MyContainerOpener());
```

The opener implements `Id`, `CanOpen(FilePanelItem)` and
`OpenAsync(FileOpenContext, CancellationToken)`. It reads the original item
through `context.Source` and `context.Location`; to enter a container, it returns
`PluginFileOpenResult.OpenPanel(new PluginPanelOpenRequest { Source = ..., InitialDirectory = "/", Title = "..." })`.

On Enter, zero matching handlers use the usual launcher/viewer. Multiple
matches fail explicitly. A handler can return `NotHandled` to request the
ordinary fallback, `Cancelled`, or `Failed(message)`. Async completion
is posted to the existing UI queue. Nested panels save the previous location,
selected item, cursor, scroll offset, selection, and source display. On moving
above the nested root, the old source is restored. A usage lease pins any
temporary source referenced in the navigation stack.

## Provider batch writes

A format implementation may additionally implement `IFileOperationBatchProvider`:

```csharp
public ValueTask<IFileOperationBatch> BeginBatchAsync(
    FileOperationPlan plan, CancellationToken cancellationToken = default);
```

The returned `IFileOperationBatch` is a staged `IFilePanelSource` view with
`CommitAsync`, `AbortAsync` and `DisposeAsync`. The copy service builds the
plan first; then all source-to-destination copies and nested directory
operations use one batch and one final commit. Other providers continue using
the original per-file operations. The batch must preserve committed data on
failed writes, failed commit, or abort; stream disposal must complete before
commit. Staged overwrites do not first delete the committed file.

`EditorFileService.SaveAsync` follows the same commit and final-disposal
semantics, and only marks a document clean after success.

## Tests and current boundaries

The test-only `InMemoryBatchFilePanelSource` simulates a container without
depending on any archive codec, supports subdirectories and mutable files,
and allows controlled failures and cancellation.

- Source-to-source copying is no longer restricted by whether either source
  is local. Cross-provider moving remains unsupported.
- Non-seekable viewer streams are spooled to temporary disk, not held as a
  full byte array. The current initial remote spool and the synchronous modal
  editor still require UI-thread responsiveness work for genuinely slow
  providers. `SaveAsync` is ready, but the F2 modal route remains synchronous.
- The normal file-operation API does not promise transactions; only the
  optional batch contract provides atomic publication.
- Archive codecs, encrypted containers, archive UI actions, DLL loading, and
  shared remote-container storage are outside this stage.
