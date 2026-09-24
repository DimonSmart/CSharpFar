using CSharpFar.App.Updates;

namespace CSharpFar.Tests;

public sealed class ApplicationUpdateInstallerTests
{
    private const string Brew = "/opt/homebrew/bin/brew";
    private const string StandardProcess =
        "/Applications/CSharpFar.app/Contents/Resources/csharpfar";

    [Fact]
    public async Task Availability_RequiresStandardHomebrewManagedBundle()
    {
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = Create(executor);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(ApplicationUpdateAvailabilityStatus.Available, availability.Status);
        Assert.Equal(Brew, availability.HomebrewExecutable);
        Assert.Equal(
            ["list", "--cask", "--versions", MacOsHomebrewUpdateInstaller.CaskName],
            Assert.Single(executor.Calls).Arguments);
    }

    [Theory]
    [InlineData("/tmp/csharpfar", ApplicationUpdateAvailabilityStatus.CurrentProcessNotManagedBundle)]
    [InlineData("/Users/test/CSharpFar.app/Contents/Resources/csharpfar", ApplicationUpdateAvailabilityStatus.NonStandardAppLocation)]
    public async Task Availability_RejectsOtherRunningCopies(
        string processPath,
        ApplicationUpdateAvailabilityStatus expected)
    {
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = Create(executor, processPath: processPath);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(expected, availability.Status);
    }

    [Fact]
    public async Task Availability_ReportsMissingCask()
    {
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(1, stderr: "Error: Cask 'csharpfar-app' is not installed."));
        var installer = Create(executor);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(ApplicationUpdateAvailabilityStatus.CaskNotInstalled, availability.Status);
    }

    [Fact]
    public async Task Availability_ReportsMissingHomebrewWithoutRunningProcesses()
    {
        var executor = new StubProcessExecutor((_, _, _) =>
            throw new InvalidOperationException("should not run"));
        var installer = Create(executor, homebrew: null);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(ApplicationUpdateAvailabilityStatus.HomebrewUnavailable, availability.Status);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task Availability_IsUnsupportedOutsideMacOs()
    {
        var executor = new StubProcessExecutor((_, _, _) =>
            throw new InvalidOperationException("should not run"));
        var installer = new MacOsHomebrewUpdateInstaller(
            executor,
            isMacOs: () => false,
            homebrewLocator: () => Brew,
            processPath: () => StandardProcess);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(ApplicationUpdateAvailabilityStatus.UnsupportedPlatform, availability.Status);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task Install_StopsWhenHomebrewMetadataLagsRelease()
    {
        var executor = StandardExecutor(caskVersion: "1.0.70");
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.PackageNotAvailableYet, result.Status);
        Assert.DoesNotContain(executor.Calls, call => call.Arguments.Contains("upgrade"));
    }

    [Fact]
    public async Task Install_UsesTargetedNoQuitUpgradeAndRelaunches()
    {
        var executor = StandardExecutor();
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.Success, result.Status);
        Assert.True(result.ShouldExitCurrentProcess);

        ProcessCall upgrade = Assert.Single(
            executor.Calls,
            call => call.Arguments.Count > 0 && call.Arguments[0] == "upgrade");
        Assert.Equal(Brew, upgrade.Executable);
        Assert.Equal(
            ["upgrade", "--cask", "--no-quit", "--appdir=/Applications", MacOsHomebrewUpdateInstaller.CaskName],
            upgrade.Arguments);

        Assert.Contains(
            executor.Calls,
            call => call.Executable == MacOsHomebrewUpdateInstaller.OpenExecutable &&
                    call.Arguments.SequenceEqual(["-n", MacOsHomebrewUpdateInstaller.ApplicationPath]));
    }

    [Fact]
    public async Task Install_AcceptsAlreadyUpToDateUpgradeMessage()
    {
        var executor = StandardExecutor(
            upgrade: Result(1, stderr: "CSharpFar is already installed and up-to-date."));
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.Success, result.Status);
    }

    [Fact]
    public async Task Install_FailsWhenInstalledBinaryIsOlderThanExpected()
    {
        var executor = StandardExecutor(installedVersion: "1.0.70");
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.VerificationFailed, result.Status);
        Assert.DoesNotContain(
            executor.Calls,
            call => call.Executable == MacOsHomebrewUpdateInstaller.XattrExecutable);
    }

    [Fact]
    public async Task Install_TreatsMissingQuarantineAsSuccess()
    {
        var executor = StandardExecutor(
            removeQuarantine: Result(1, stderr: "No such xattr"),
            quarantineCheck: Result(0, string.Empty));
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.Success, result.Status);
    }

    [Fact]
    public async Task Install_DoesNotRestartWhenQuarantineRemains()
    {
        var executor = StandardExecutor(
            quarantineCheck: Result(0, "com.apple.quarantine: 0081;..."));
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.QuarantineFailed, result.Status);
        Assert.Contains("xattr -dr", result.ManualCommand);
        Assert.DoesNotContain(
            executor.Calls,
            call => call.Executable == MacOsHomebrewUpdateInstaller.OpenExecutable);
    }

    [Fact]
    public async Task Install_ReportsRelaunchFailureWithoutRollback()
    {
        var executor = StandardExecutor(relaunch: Result(1, stderr: "open failed"));
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.RelaunchFailed, result.Status);
        Assert.False(result.ShouldExitCurrentProcess);
    }

    [Fact]
    public async Task Install_RejectsMalformedBrewInfoJson()
    {
        var executor = StandardExecutor(infoJson: "{not-json");
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.HomebrewFailure, result.Status);
    }

    private static MacOsHomebrewUpdateInstaller Create(
        IProcessExecutor executor,
        string? processPath = StandardProcess,
        string? homebrew = Brew) =>
        new(
            executor,
            isMacOs: () => true,
            homebrewLocator: () => homebrew,
            processPath: () => processPath);

    private static StubProcessExecutor StandardExecutor(
        string caskVersion = "1.0.71",
        string installedVersion = "1.0.71",
        string? infoJson = null,
        ProcessExecutionResult? upgrade = null,
        ProcessExecutionResult? removeQuarantine = null,
        ProcessExecutionResult? quarantineCheck = null,
        ProcessExecutionResult? relaunch = null) =>
        new((executable, arguments, _) =>
        {
            if (executable == Brew && arguments.SequenceEqual(
                    ["list", "--cask", "--versions", MacOsHomebrewUpdateInstaller.CaskName]))
                return Result(0, "csharpfar-app 1.0.70");

            if (executable == Brew && arguments.SequenceEqual(["update"]))
                return Result(0);

            if (executable == Brew && arguments.Count > 0 && arguments[0] == "info")
                return Result(0, infoJson ?? $"{{\"casks\":[{{\"version\":\"{caskVersion}\"}}]}}");

            if (executable == Brew && arguments.Count > 0 && arguments[0] == "upgrade")
                return upgrade ?? Result(0);

            if (executable == MacOsHomebrewUpdateInstaller.ApplicationExecutablePath)
                return Result(0, $"CSharpFar {installedVersion}");

            if (executable == MacOsHomebrewUpdateInstaller.XattrExecutable &&
                arguments.Count > 0 && arguments[0] == "-dr")
                return removeQuarantine ?? Result(0);

            if (executable == MacOsHomebrewUpdateInstaller.XattrExecutable)
                return quarantineCheck ?? Result(0);

            if (executable == MacOsHomebrewUpdateInstaller.OpenExecutable)
                return relaunch ?? Result(0);

            throw new InvalidOperationException(
                $"Unexpected process: {executable} {string.Join(' ', arguments)}");
        });

    private static ProcessExecutionResult Result(
        int exitCode,
        string stdout = "",
        string stderr = "") =>
        new(exitCode, stdout, stderr);

    private sealed record ProcessCall(
        string Executable,
        IReadOnlyList<string> Arguments);

    private sealed class StubProcessExecutor(
        Func<string, IReadOnlyList<string>, CancellationToken, ProcessExecutionResult> handler)
        : IProcessExecutor
    {
        public List<ProcessCall> Calls { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var copiedArguments = arguments.ToArray();
            Calls.Add(new ProcessCall(executable, copiedArguments));
            return Task.FromResult(handler(executable, copiedArguments, cancellationToken));
        }
    }
}
