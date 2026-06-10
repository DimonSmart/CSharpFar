namespace CSharpFar.Console;

public interface IConsoleOutputModeDriver
{
    void SetRenderingOutputMode(bool enabled);

    void SetConsoleScrollbackEnabled(bool enabled);

    /// <summary>
    /// Re-applies the application's required console input mode.
    /// Call this after running a child process that may have changed the console input mode.
    /// </summary>
    void RestoreApplicationInputMode();

    /// <summary>
    /// Temporarily switches the console input mode for a current-console child process.
    /// Dispose the returned scope before resuming application input.
    /// </summary>
    IDisposable EnterChildProcessConsoleMode();
}
