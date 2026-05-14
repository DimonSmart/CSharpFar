namespace CSharpFar.Console;

public interface IConsoleOutputModeDriver
{
    void SetRenderingOutputMode(bool enabled);

    /// <summary>
    /// Re-applies the application's required console input mode.
    /// Call this after running a child process that may have changed the console input mode.
    /// </summary>
    void RestoreApplicationInputMode();
}
