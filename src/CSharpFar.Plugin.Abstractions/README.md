# CSharpFar.Plugin.Abstractions

Public interfaces for trusted, in-process CSharpFar native extensions, targeting .NET 10.

Install this package to implement `ICSharpFarPlugin`, register commands, menus,
command-line prefixes, file-open handlers and custom panel providers.
`CSharpFar.Core` is a transitive dependency.

Extensions run with the same OS privileges as CSharpFar. Only load trusted code.
Use the [native plugin guide](https://github.com/DimonSmart/CSharpFar/blob/main/docs/native-plugins.md)
for packaging, manifest format, lifecycle and binary compatibility.

Writable file-panel providers may implement the base `IFilePanelSource` contract
and appropriate capabilities. Transaction and safe-publication interfaces are
optional guarantees; see the native plugin guide for Best Effort semantics.
