# CSharpFar.Core

Public models and file-panel contracts used by the CSharpFar native plugin SDK.

This package is normally installed automatically through
`CSharpFar.Plugin.Abstractions`. It does not include the CSharpFar application.

See the [CSharpFar native plugin guide](https://github.com/DimonSmart/CSharpFar/blob/main/docs/native-plugins.md)
for SDK compatibility requirements and examples.

## Asynchronous file sources

New providers can implement `IAsyncFilePanelSource` to expose asynchronous directory and metadata operations. The inherited stream and mutation methods must also return promptly. Use `FilePanelSourceAsync.AsAsync()` to access a legacy `IFilePanelSource` through bounded worker execution without changing the registered provider or its optional interfaces. Cancellation of a started operation is cooperative; keep its source lease until the task actually completes and handle any returned stream. Path and capability methods must not perform I/O.
