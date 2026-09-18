# DimonSmart Terminal

`DimonSmart.Terminal` is a reusable .NET 10 terminal runtime with production backends for Windows, Linux, and macOS. `DimonSmart.Terminal.Ui` adds dialogs, forms, lists, tables, menus, rendering, scrolling, composition, and input handling.

## Installation

For a terminal UI application, reference the UI package:

```xml
<PackageReference Include="DimonSmart.Terminal.Ui" Version="0.1.0-beta.1" />
```

`DimonSmart.Terminal.Ui` depends on `DimonSmart.Terminal`, so the terminal runtime and production backends are available transitively. Reference `DimonSmart.Terminal` directly only when you need the terminal runtime without the UI layer.

The NuGet package names intentionally differ from the implementation assembly and namespace names. Consumer code uses the `CSharpFar.Console`, `CSharpFar.Console.Ansi`, and `CSharpFar.Ui` namespaces.

## Minimal application startup

The following startup uses the production backend for the current operating system, creates a renderer/composition host, creates the ordinary form/dialog services, and restores the terminal when the application finishes:

```csharp
using CSharpFar.Console;
using CSharpFar.Console.Ansi;
using CSharpFar.Ui;

IDisposable? driverLifetime = null;
ITerminalScreenMode? terminal = null;

try
{
    IConsoleDriver driver;
    if (OperatingSystem.IsWindows())
        driver = (SystemConsoleDriver)(driverLifetime = new SystemConsoleDriver());
    else if (OperatingSystem.IsLinux())
        driver = (AnsiTerminalConsoleDriver)(driverLifetime = AnsiTerminalConsoleDriver.CreateLinux());
    else if (OperatingSystem.IsMacOS())
        driver = (AnsiTerminalConsoleDriver)(driverLifetime = AnsiTerminalConsoleDriver.CreateMacOs());
    else
        throw new PlatformNotSupportedException();

    terminal = (ITerminalScreenMode)driver;
    terminal.EnterApplicationScreen();
    driver.SetCursorVisible(false);

    var renderer = new ScreenRenderer(driver);
    var uiHost = new UiCompositionHost(renderer);
    uiHost.SetRootSurface(new ScreenRendererSurface(renderer, _ => { }));

    var fields = new FormFieldFactory();
    var dialogs = new DialogService(uiHost, fields);

    uiHost.Render();
    dialogs.Message("Hello", "DimonSmart.Terminal.Ui is running.");
}
finally
{
    try
    {
        terminal?.RestoreTerminal();
    }
    finally
    {
        driverLifetime?.Dispose();
    }
}
```

A real application normally replaces the empty `ScreenRendererSurface` with its own root surface. The repository's `CSharpFar.Ui.Demo` sample shows a complete application lifecycle and is also compiled in package-consumer mode during package verification.

## Basic UI example

Standard dialogs are built from semantic form controls rather than frame or routing implementation details:

```csharp
TextField name = fields.Text(new TextFieldOptions(""));
LabeledTextInputRow nameRow = FormControls.Text("Name", name);

bool? accepted = dialogs.Form(
    new FormDialogOptions("Profile", PreferredWidth: 48, PreferredHeight: 9),
    rows: () => [nameRow],
    footer: () => [FormControls.OkCancel()],
    submit: () => FormSubmit.Success(true));

if (accepted == true)
{
    string value = name.Text;
    // Use value.
}
```

Low-level composition, rendering, routing, modal-hosting, scrolling, and popup APIs are also intentionally available for custom UI surfaces that cannot be expressed with the standard form API.

## Supported platforms

- Windows
- Linux
- macOS
- Target framework: `net10.0`

## Stability

The `0.x` releases are beta releases. The public API is still allowed to change before the stable `1.0` contract. Do not treat the first beta API as a compatibility promise for 1.0.

The packages are MIT licensed. CSharpFar itself is an application that consumes the same reusable terminal stack.
