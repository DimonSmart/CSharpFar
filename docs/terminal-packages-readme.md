# DimonSmart Terminal

`DimonSmart.Terminal` is a reusable .NET terminal runtime with platform backends for Windows, Linux, and macOS. `DimonSmart.Terminal.Ui` adds dialogs, forms, lists, tables, menus, rendering, scrolling, and input handling.

Install the UI package for a full terminal UI application:

```xml
<PackageReference Include="DimonSmart.Terminal.Ui" Version="0.1.0-beta.1" />
```

The terminal runtime is included transitively. Application startup selects the Windows backend on Windows and the ANSI backend on Linux and macOS.

Create reusable UI services from your composition host:

```csharp
var fields = new FormFieldFactory();
var dialogs = new DialogService(uiHost, fields);
```

The packages are MIT licensed. CSharpFar is an application that uses this terminal stack.
