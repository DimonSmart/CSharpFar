namespace CSharpFar.App.Updates;

internal enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Unavailable,
}

internal sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    ReleaseVersion? CurrentVersion,
    ReleaseVersion? LatestVersion,
    Uri? ReleaseUri)
{
    public static UpdateCheckResult Unavailable(ReleaseVersion? currentVersion) =>
        new(UpdateCheckStatus.Unavailable, currentVersion, null, null);
}
