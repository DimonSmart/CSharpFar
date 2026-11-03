# DimonSmart Terminal

A family of .NET 10 terminal packages. Choose an explicit backend for the platform where your application runs. The UI package is backend-independent.

| Package | Contents |
| --- | --- |
| `DimonSmart.Terminal` | Core terminal abstractions (`CSharpFar.Console.dll`) |
| `DimonSmart.Terminal.Ansi` | Linux/macOS ANSI backend (`CSharpFar.Console.Ansi.dll`) |
| `DimonSmart.Terminal.Windows` | Windows console backend (`CSharpFar.Console.Windows.dll`) |
| `DimonSmart.Terminal.Ui` | Dialogs, forms, tables, menus, rendering (`CSharpFar.Ui.dll`) |

For Windows applications using the UI:

```xml
<PackageReference Include="DimonSmart.Terminal.Ui" Version="0.1.0-beta.1" />
<PackageReference Include="DimonSmart.Terminal.Windows" Version="0.1.0-beta.1" />
```

For Linux and macOS applications using the UI:

```xml
<PackageReference Include="DimonSmart.Terminal.Ui" Version="0.1.0-beta.1" />
<PackageReference Include="DimonSmart.Terminal.Ansi" Version="0.1.0-beta.1" />
```

The backend packages bring `DimonSmart.Terminal` transitively. The UI package depends on core and `TextCopy`, but never brings a backend. The host creates a suitable console driver explicitly. Applications targeting all platforms can reference both backend packages, selecting the platform-specific driver at runtime.

Use a single matching version for all four packages. The source build uses `eng/TerminalPackages.props` for package version coordination; the examples above reflect its current version and do not imply that all packages are already available on nuget.org.

All packages are MIT licensed.
