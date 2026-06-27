namespace CSharpFar.Console.Input;

public interface IConsoleInputDiagnostics
{
    string InputBackendName { get; }

    bool MouseTrackingEnabled { get; }

    ModifierKeyTrackingSnapshot ModifierKeyTracking { get; }
}
