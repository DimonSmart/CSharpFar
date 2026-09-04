using CSharpFar.Console;
using CSharpFar.Console.Ansi;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Demo;

internal static class Program
{
    public static int Main()
    {
        if (global::System.Console.IsInputRedirected || global::System.Console.IsOutputRedirected)
        {
            global::System.Console.Error.WriteLine("CSharpFar UI Demo requires an interactive terminal.");
            return 2;
        }

        IDisposable? driverLifetime = null;
        ITerminalScreenMode? terminal = null;
        try
        {
            IConsoleDriver driver;
            if (OperatingSystem.IsLinux()) driver = (AnsiTerminalConsoleDriver)(driverLifetime = AnsiTerminalConsoleDriver.CreateLinux());
            else if (OperatingSystem.IsMacOS()) driver = (AnsiTerminalConsoleDriver)(driverLifetime = AnsiTerminalConsoleDriver.CreateMacOs());
            else if (OperatingSystem.IsWindows()) driver = (SystemConsoleDriver)(driverLifetime = new SystemConsoleDriver());
            else throw new PlatformNotSupportedException("The demo supports Windows, Linux, and macOS terminals.");

            terminal = (ITerminalScreenMode)driver;
            terminal.EnterApplicationScreen();
            driver.SetCursorVisible(false);

            var screen = new ScreenRenderer(driver);
            var host = new UiCompositionHost(screen);
            var app = new DemoApplication(screen, host, new DemoRepository());
            host.SetRootSurface(app);
            using var menuRegistration = host.RegisterPersistentOverlay(app.Menu);
            host.Render();
            while (!app.ShouldExit)
            {
                var input = host.ReadInput();
                UiInputResult result = host.DispatchInput(input);
                bool commandExecuted = app.ExecutePendingCommand();
                if (result.Invalidate || commandExecuted) host.Render();
            }
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine($"CSharpFar UI Demo stopped: {ex.Message}");
            return 1;
        }
        finally
        {
            try { terminal?.RestoreTerminal(); }
            catch { /* Never hide the original failure during terminal recovery. */ }
            driverLifetime?.Dispose();
        }
    }
}
