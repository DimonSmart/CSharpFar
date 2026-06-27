using System.Diagnostics.CodeAnalysis;

namespace CSharpFar.Console.Input;

public sealed record ModifierKeyTrackingSnapshot(
    string BackendName,
    bool IsPlatformSupported,
    bool IsEnabled,
    bool CanTrackShiftOnly,
    string Status,
    string? FailureReason,
    IReadOnlyList<ModifierKeyDeviceSnapshot> Devices);

public sealed record ModifierKeyDeviceSnapshot(
    string Path,
    string? Name,
    bool IsReadable,
    bool HasShiftCapability,
    string? Error);

public static class ModifierKeyTrackingStatus
{
    public const string Enabled = "Enabled";
    public const string PlatformNotSupported = "PlatformNotSupported";
    public const string NoInputDeviceDirectory = "NoInputDeviceDirectory";
    public const string NoReadableDevices = "NoReadableDevices";
    public const string NoShiftCapableDevices = "NoShiftCapableDevices";
    public const string PermissionDenied = "PermissionDenied";
    public const string DisabledByConfiguration = "DisabledByConfiguration";
    public const string Failed = "Failed";
}

public interface IModifierKeyTracker : IDisposable
{
    string BackendName { get; }

    ModifierKeyTrackingSnapshot GetSnapshot();

    bool TryCreateInputEvent([NotNullWhen(true)] out ModifierKeyConsoleInputEvent? inputEvent);

    void ObserveConsoleInput(ConsoleInputEvent inputEvent);

    void Suspend();

    void Resume();
}
