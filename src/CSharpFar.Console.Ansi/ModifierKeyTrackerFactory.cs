using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

internal static class ModifierKeyTrackerFactory
{
    public static ModifierKeyTrackingSnapshot UnsupportedSnapshot { get; } =
        new(
            "none",
            IsPlatformSupported: false,
            IsEnabled: false,
            CanTrackShiftOnly: false,
            Status: ModifierKeyTrackingStatus.PlatformNotSupported,
            FailureReason: null,
            Devices: []);

    public static IModifierKeyTracker? TryCreateForCurrentPlatform()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        try
        {
            return LinuxEvdevModifierKeyTracker.Create();
        }
        catch (Exception ex)
        {
            return LinuxEvdevModifierKeyTracker.CreateFailed(ex.Message);
        }
    }
}
