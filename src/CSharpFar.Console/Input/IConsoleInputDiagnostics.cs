namespace CSharpFar.Console.Input;

public interface IConsoleInputDiagnostics
{
    string InputBackendName { get; }

    bool MouseTrackingEnabled { get; }

    KeyboardProtocolSnapshot KeyboardProtocol => KeyboardProtocolSnapshot.NotApplicable;

    ModifierKeyTrackingSnapshot ModifierKeyTracking { get; }
}
