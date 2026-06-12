namespace CSharpFar.Console;

public interface ITerminalScreenMode
{
    bool IsSupported { get; }
    bool IsApplicationScreenActive { get; }

    void EnterApplicationScreen();
    void LeaveApplicationScreen();
    void EnsureApplicationScreen();
    void EnsureMainScreen();
    void RestoreTerminal();
}
