using System.Text.Json;
using CSharpFar.App.Diagnostics;

namespace CSharpFar.App.Updates;

internal sealed class MacOsHomebrewUpdateInstaller : IApplicationUpdateInstaller
{
    internal const string CaskName = "dimonsmart/csharpfar/csharpfar-app";
    internal const string ApplicationPath = "/Applications/CSharpFar.app";
    internal const string ApplicationExecutablePath =
        "/Applications/CSharpFar.app/Contents/Resources/csharpfar";
    internal const string XattrExecutable = "/usr/bin/xattr";
    internal const string OpenExecutable = "/usr/bin/open";
    internal const string QuarantineAttribute = "com.apple.quarantine";

    private readonly IProcessExecutor _processExecutor;
    private readonly IDiagnosticLog _diagnostics;
    private readonly Func<bool> _isMacOs;
    private readonly Func<string?> _homebrewLocator;
    private readonly Func<string?> _processPath;

    public MacOsHomebrewUpdateInstaller(
        IProcessExecutor processExecutor,
        IDiagnosticLog? diagnostics = null,
        Func<bool>? isMacOs = null,
        Func<string?>? homebrewLocator = null,
        Func<string?>? processPath = null)
    {
        _processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
        _diagnostics = diagnostics ?? DisabledDiagnosticLog.Instance;
        _isMacOs = isMacOs ?? OperatingSystem.IsMacOS;
        _homebrewLocator = homebrewLocator ?? FindHomebrewExecutable;
        _processPath = processPath ?? (() => Environment.ProcessPath);
    }

    public async Task<ApplicationUpdateAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        if (!_isMacOs())
            return Availability(ApplicationUpdateAvailabilityStatus.UnsupportedPlatform);

        string? brew = _homebrewLocator();
        if (string.IsNullOrWhiteSpace(brew))
            return Availability(ApplicationUpdateAvailabilityStatus.HomebrewUnavailable);

        Log($"Detected Homebrew executable: {DiagnosticSanitizer.RedactHomePath(brew)}");

        ProcessExecutionResult caskQuery;
        try
        {
            caskQuery = await _processExecutor.ExecuteAsync(
                brew,
                ["list", "--cask", "--versions", CaskName],
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogException(ex, "Cask query failed");
            return Availability(ApplicationUpdateAvailabilityStatus.PackageQueryFailed);
        }

        LogProcessResult("Cask query", caskQuery);
        if (caskQuery.ExitCode != 0)
        {
            return Availability(
                LooksLikeCaskNotInstalled(caskQuery)
                    ? ApplicationUpdateAvailabilityStatus.CaskNotInstalled
                    : ApplicationUpdateAvailabilityStatus.PackageQueryFailed);
        }

        Log("Detected installation method: Homebrew Cask");

        string currentProcess = NormalizePath(_processPath());
        string standardProcess = NormalizePath(ApplicationExecutablePath);
        if (string.Equals(currentProcess, standardProcess, StringComparison.Ordinal))
        {
            Log("Current process path classification: standard Homebrew application bundle");
            Log("Updater availability=Available");
            return ApplicationUpdateAvailability.Available(brew);
        }

        if (currentProcess.EndsWith(
                "/CSharpFar.app/Contents/Resources/csharpfar",
                StringComparison.Ordinal))
        {
            Log($"Current process path classification: non-standard app location ({DiagnosticSanitizer.RedactHomePath(currentProcess)})");
            return Availability(ApplicationUpdateAvailabilityStatus.NonStandardAppLocation);
        }

        Log($"Current process path classification: not Homebrew-managed bundle ({DiagnosticSanitizer.RedactHomePath(currentProcess)})");
        return Availability(ApplicationUpdateAvailabilityStatus.CurrentProcessNotManagedBundle);
    }

    public async Task<ApplicationUpdateInstallResult> InstallAsync(
        ReleaseVersion expectedVersion,
        IProgress<ApplicationUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        Log($"Update install requested. Expected version={expectedVersion}");

        try
        {
            ApplicationUpdateAvailability availability =
                await GetAvailabilityAsync(cancellationToken);
            if (!availability.IsAvailable || string.IsNullOrWhiteSpace(availability.HomebrewExecutable))
            {
                return new ApplicationUpdateInstallResult(
                    ApplicationUpdateInstallStatus.Unavailable,
                    "In-app update is not available for this installation.");
            }

            string brew = availability.HomebrewExecutable;

            Report(progress, "Updating Homebrew metadata...", canCancel: true);
            cancellationToken.ThrowIfCancellationRequested();
            Log("brew update started");
            ProcessExecutionResult update = await _processExecutor.ExecuteAsync(
                brew,
                ["update"],
                CancellationToken.None);
            LogProcessResult("brew update completed", update);
            if (update.ExitCode != 0)
                return HomebrewFailure("brew update", update);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, "Checking Homebrew package...", canCancel: true);
            ProcessExecutionResult info = await _processExecutor.ExecuteAsync(
                brew,
                ["info", "--cask", "--json=v2", CaskName],
                CancellationToken.None);
            LogProcessResult("brew info completed", info);
            if (info.ExitCode != 0)
                return HomebrewFailure("brew info", info);

            if (!TryReadCaskVersion(info.StandardOutput, out ReleaseVersion availableVersion))
            {
                Log("Unable to parse available Cask version");
                return new ApplicationUpdateInstallResult(
                    ApplicationUpdateInstallStatus.HomebrewFailure,
                    "Unable to read the Homebrew package version.");
            }

            Log($"Available Cask version={availableVersion}");
            if (availableVersion.CompareTo(expectedVersion) < 0)
            {
                return new ApplicationUpdateInstallResult(
                    ApplicationUpdateInstallStatus.PackageNotAvailableYet,
                    $"CSharpFar {expectedVersion} has been released, but the Homebrew package is not available yet. Please try again shortly.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, $"Installing CSharpFar {expectedVersion}...", canCancel: false);
            Log("brew upgrade started");
            ProcessExecutionResult upgrade = await _processExecutor.ExecuteAsync(
                brew,
                ["upgrade", "--cask", "--no-quit", "--appdir=/Applications", CaskName],
                CancellationToken.None);
            LogProcessResult("brew upgrade completed", upgrade);
            if (upgrade.ExitCode != 0 && !LooksLikeAlreadyUpToDate(upgrade))
                return HomebrewFailure("brew upgrade", upgrade);

            Report(progress, "Verifying installation...", canCancel: false);
            ProcessExecutionResult verification = await _processExecutor.ExecuteAsync(
                ApplicationExecutablePath,
                ["--version"],
                CancellationToken.None);
            LogProcessResult("Installed binary verification", verification);
            if (verification.ExitCode != 0 ||
                !TryReadApplicationVersion(verification.StandardOutput, out ReleaseVersion installedVersion))
            {
                return new ApplicationUpdateInstallResult(
                    ApplicationUpdateInstallStatus.VerificationFailed,
                    "CSharpFar was installed, but the installed version could not be verified.");
            }

            Log($"Installed version={installedVersion}");
            if (installedVersion.CompareTo(expectedVersion) < 0)
            {
                return new ApplicationUpdateInstallResult(
                    ApplicationUpdateInstallStatus.VerificationFailed,
                    $"The installed CSharpFar version is {installedVersion}, but {expectedVersion} was expected.");
            }

            Report(progress, "Removing macOS quarantine...", canCancel: false);
            ProcessExecutionResult removeQuarantine = await _processExecutor.ExecuteAsync(
                XattrExecutable,
                ["-dr", QuarantineAttribute, ApplicationPath],
                CancellationToken.None);
            LogProcessResult("Quarantine removal", removeQuarantine);

            ProcessExecutionResult quarantineCheck = await _processExecutor.ExecuteAsync(
                XattrExecutable,
                ["-r", "-l", ApplicationPath],
                CancellationToken.None);
            LogProcessResult("Quarantine verification", quarantineCheck);
            bool quarantineRemains =
                quarantineCheck.StandardOutput.Contains(QuarantineAttribute, StringComparison.Ordinal);
            if (quarantineCheck.ExitCode != 0 || quarantineRemains)
            {
                Log("Quarantine removal result: failed");
                return new ApplicationUpdateInstallResult(
                    ApplicationUpdateInstallStatus.QuarantineFailed,
                    "CSharpFar was updated, but macOS quarantine could not be removed. Please remove it manually and start CSharpFar again.",
                    $"xattr -dr {QuarantineAttribute} {ApplicationPath}");
            }

            Log("Quarantine removal result: success");

            Report(progress, "Restarting CSharpFar...", canCancel: false);
            ProcessExecutionResult relaunch = await _processExecutor.ExecuteAsync(
                OpenExecutable,
                ["-n", ApplicationPath],
                CancellationToken.None);
            LogProcessResult("Relaunch result", relaunch);
            if (relaunch.ExitCode != 0)
            {
                return new ApplicationUpdateInstallResult(
                    ApplicationUpdateInstallStatus.RelaunchFailed,
                    "CSharpFar was updated successfully, but could not be restarted automatically. Please start CSharpFar again.");
            }

            return new ApplicationUpdateInstallResult(
                ApplicationUpdateInstallStatus.Success,
                $"CSharpFar {installedVersion} was updated successfully.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log("Update cancelled before package modification");
            return new ApplicationUpdateInstallResult(
                ApplicationUpdateInstallStatus.Cancelled,
                "Update cancelled.");
        }
        catch (Exception ex)
        {
            LogException(ex, "Unexpected application update failure");
            return new ApplicationUpdateInstallResult(
                ApplicationUpdateInstallStatus.HomebrewFailure,
                "Unable to update CSharpFar.");
        }
    }

    private ApplicationUpdateAvailability Availability(ApplicationUpdateAvailabilityStatus status)
    {
        Log($"Updater availability={status}");
        return new ApplicationUpdateAvailability(status);
    }

    private ApplicationUpdateInstallResult HomebrewFailure(
        string stage,
        ProcessExecutionResult result)
    {
        Log($"Homebrew failure. Stage={stage} ExitCode={result.ExitCode} Error={ShortError(result.StandardError)}");
        return new ApplicationUpdateInstallResult(
            ApplicationUpdateInstallStatus.HomebrewFailure,
            "Unable to update CSharpFar.");
    }

    private static bool TryReadCaskVersion(string json, out ReleaseVersion version)
    {
        version = default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("casks", out JsonElement casks) ||
                casks.ValueKind != JsonValueKind.Array ||
                casks.GetArrayLength() == 0)
            {
                return false;
            }

            JsonElement first = casks[0];
            return first.TryGetProperty("version", out JsonElement versionElement) &&
                   versionElement.ValueKind == JsonValueKind.String &&
                   ReleaseVersionParser.TryParse(versionElement.GetString(), out version);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadApplicationVersion(string output, out ReleaseVersion version)
    {
        version = default;
        bool found = false;
        foreach (string token in output.Split(
                     [' ', '\t', '\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ReleaseVersionParser.TryParse(token, out ReleaseVersion parsed))
                continue;

            version = parsed;
            found = true;
        }

        return found;
    }

    private static bool LooksLikeCaskNotInstalled(ProcessExecutionResult result)
    {
        string text = result.StandardOutput + "\n" + result.StandardError;
        return text.Contains("not installed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("no such keg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAlreadyUpToDate(ProcessExecutionResult result)
    {
        string text = result.StandardOutput + "\n" + result.StandardError;
        return text.Contains("already up-to-date", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("already installed and up-to-date", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindHomebrewExecutable()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string directory in path.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(directory, "brew");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        if (File.Exists("/opt/homebrew/bin/brew"))
            return "/opt/homebrew/bin/brew";
        if (File.Exists("/usr/local/bin/brew"))
            return "/usr/local/bin/brew";
        return null;
    }

    private static string NormalizePath(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('\\', '/').TrimEnd('/');

    private static void Report(
        IProgress<ApplicationUpdateProgress>? progress,
        string message,
        bool canCancel) =>
        progress?.Report(new ApplicationUpdateProgress(message, canCancel));

    private void Log(string message)
    {
        if (_diagnostics.IsEnabled)
            _diagnostics.Write(DiagnosticCategory.ApplicationUpdate, message);
    }

    private void LogException(Exception exception, string context)
    {
        if (_diagnostics.IsEnabled)
            _diagnostics.WriteException(DiagnosticCategory.ApplicationUpdate, exception, context);
    }

    private void LogProcessResult(string stage, ProcessExecutionResult result)
    {
        if (!_diagnostics.IsEnabled)
            return;

        _diagnostics.Write(
            DiagnosticCategory.ApplicationUpdate,
            $"{stage}. ExitCode={result.ExitCode} Error={ShortError(result.StandardError)}");
    }

    private static string ShortError(string value)
    {
        string sanitized = DiagnosticSanitizer.SanitizeText(value);
        string firstLine = sanitized.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return firstLine.Length <= 240 ? firstLine : firstLine[..240];
    }
}
