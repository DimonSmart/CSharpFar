namespace CSharpFar.App.Updates;

internal enum ApplicationUpdateAvailabilityStatus
{
    Available,
    UnsupportedPlatform,
    HomebrewUnavailable,
    CaskNotInstalled,
    CurrentProcessNotManagedBundle,
    NonStandardAppLocation,
    PackageQueryFailed,
}

internal sealed record ApplicationUpdateAvailability(
    ApplicationUpdateAvailabilityStatus Status,
    string? HomebrewExecutable = null)
{
    public bool IsAvailable => Status == ApplicationUpdateAvailabilityStatus.Available;

    public static ApplicationUpdateAvailability Available(string homebrewExecutable) =>
        new(ApplicationUpdateAvailabilityStatus.Available, homebrewExecutable);
}

internal enum ApplicationUpdateInstallStatus
{
    Success,
    Cancelled,
    Unavailable,
    PackageNotAvailableYet,
    HomebrewFailure,
    VerificationFailed,
    QuarantineFailed,
    RelaunchFailed,
}

internal sealed record ApplicationUpdateInstallResult(
    ApplicationUpdateInstallStatus Status,
    string Message,
    string? ManualCommand = null)
{
    public bool ShouldExitCurrentProcess => Status == ApplicationUpdateInstallStatus.Success;
}

internal readonly record struct ApplicationUpdateProgress(
    string Message,
    bool CanCancel);

internal interface IApplicationUpdateInstaller
{
    Task<ApplicationUpdateAvailability> GetAvailabilityAsync(CancellationToken cancellationToken);

    Task<ApplicationUpdateInstallResult> InstallAsync(
        ReleaseVersion expectedVersion,
        IProgress<ApplicationUpdateProgress>? progress,
        CancellationToken cancellationToken);
}

internal sealed class UnsupportedApplicationUpdateInstaller : IApplicationUpdateInstaller
{
    public static UnsupportedApplicationUpdateInstaller Instance { get; } = new();

    private UnsupportedApplicationUpdateInstaller()
    {
    }

    public Task<ApplicationUpdateAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ApplicationUpdateAvailability(
            ApplicationUpdateAvailabilityStatus.UnsupportedPlatform));

    public Task<ApplicationUpdateInstallResult> InstallAsync(
        ReleaseVersion expectedVersion,
        IProgress<ApplicationUpdateProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ApplicationUpdateInstallResult(
            ApplicationUpdateInstallStatus.Unavailable,
            "In-app update is not available for this installation."));
}
