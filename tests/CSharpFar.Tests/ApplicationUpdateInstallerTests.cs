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

    [Fact]
    public async Task Availability_RejectsBuildOutput()
    {
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = Create(executor, processPath: "/tmp/csharpfar");

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(
            ApplicationUpdateAvailabilityStatus.CurrentProcessNotManagedBundle,
            availability.Status);
    }

    [Fact]
    public async Task Availability_RejectsNonStandardApplicationLocation()
    {
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = Create(
            executor,
            processPath: "/Users/test/CSharpFar.app/Contents/Resources/csharpfar");

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(
            ApplicationUpdateAvailabilityStatus.NonStandardAppLocation,
            availability.Status);
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
        var environment = CreateMacEnvironment();
        environment.Files.Clear();
        environment.ExecutableFiles.Clear();
        var installer = new MacOsHomebrewUpdateInstaller(executor, environment: environment);

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
        var environment = CreateMacEnvironment();
        environment.IsMacOS = false;
        var installer = new MacOsHomebrewUpdateInstaller(executor, environment: environment);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(ApplicationUpdateAvailabilityStatus.UnsupportedPlatform, availability.Status);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task Availability_PassesCallerTokenToCaskQuery()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = Create(executor);

        await installer.GetAvailabilityAsync(cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(executor.Calls).CancellationToken);
    }

    [Fact]
    public async Task Availability_PrefersExecutableHomebrewFromPathAndReturnsAbsolutePath()
    {
        string relativeDirectory = "fake-homebrew-bin";
        string expectedBrew = Path.GetFullPath(Path.Combine(relativeDirectory, "brew"));
        var environment = CreateMacEnvironment();
        environment.PathValue = relativeDirectory;
        environment.Files.Add(expectedBrew);
        environment.ExecutableFiles.Add(expectedBrew);
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = new MacOsHomebrewUpdateInstaller(executor, environment: environment);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(ApplicationUpdateAvailabilityStatus.Available, availability.Status);
        Assert.Equal(expectedBrew, availability.HomebrewExecutable);
        Assert.Equal(expectedBrew, Assert.Single(executor.Calls).Executable);
        Assert.True(Path.IsPathFullyQualified(availability.HomebrewExecutable!));
    }

    [Fact]
    public async Task Availability_SkipsNonExecutablePathCandidateAndUsesAppleSiliconFallback()
    {
        string directory = Path.GetFullPath("non-executable-homebrew-bin");
        string pathBrew = Path.Combine(directory, "brew");
        var environment = CreateMacEnvironment();
        environment.PathValue = directory;
        environment.Files.Add(pathBrew);
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = new MacOsHomebrewUpdateInstaller(executor, environment: environment);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(Brew, availability.HomebrewExecutable);
        Assert.Equal(Brew, Assert.Single(executor.Calls).Executable);
    }

    [Fact]
    public async Task Availability_ContinuesAfterPermissionFailureAndUsesFallback()
    {
        string directory = Path.GetFullPath("unreadable-homebrew-bin");
        string pathBrew = Path.Combine(directory, "brew");
        var environment = CreateMacEnvironment();
        environment.PathValue = directory;
        environment.Files.Add(pathBrew);
        environment.ExecutableCheckFailures.Add(pathBrew);
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = new MacOsHomebrewUpdateInstaller(executor, environment: environment);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(Brew, availability.HomebrewExecutable);
    }

    [Fact]
    public async Task Availability_UsesIntelFallbackWhenAppleSiliconCandidateIsNotExecutable()
    {
        const string intelBrew = "/usr/local/bin/brew";
        var environment = CreateMacEnvironment();
        environment.ExecutableFiles.Remove(Brew);
        environment.Files.Add(intelBrew);
        environment.ExecutableFiles.Add(intelBrew);
        var executor = new StubProcessExecutor((_, _, _) =>
            Result(0, "csharpfar-app 1.0.70"));
        var installer = new MacOsHomebrewUpdateInstaller(executor, environment: environment);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(intelBrew, availability.HomebrewExecutable);
        Assert.Equal(intelBrew, Assert.Single(executor.Calls).Executable);
    }

    [Fact]
    public async Task Availability_RejectsExistingFallbacksWithoutExecutePermission()
    {
        var environment = CreateMacEnvironment();
        environment.ExecutableFiles.Clear();
        environment.Files.Add("/usr/local/bin/brew");
        var executor = new StubProcessExecutor((_, _, _) =>
            throw new InvalidOperationException("should not run"));
        var installer = new MacOsHomebrewUpdateInstaller(executor, environment: environment);

        ApplicationUpdateAvailability availability =
            await installer.GetAvailabilityAsync(CancellationToken.None);

        Assert.Equal(ApplicationUpdateAvailabilityStatus.HomebrewUnavailable, availability.Status);
        Assert.Empty(executor.Calls);
    }

    [Theory]
    [InlineData(UnixFileMode.UserExecute)]
    [InlineData(UnixFileMode.GroupExecute)]
    [InlineData(UnixFileMode.OtherExecute)]
    public void HomebrewExecutablePermission_AnyExecuteBitIsEnough(UnixFileMode mode) =>
        Assert.True(DefaultApplicationUpdateEnvironment.HasAnyExecuteBit(mode));

    [Fact]
    public void HomebrewExecutablePermission_NoExecuteBitsIsRejected() =>
        Assert.False(DefaultApplicationUpdateEnvironment.HasAnyExecuteBit(
            UnixFileMode.UserRead | UnixFileMode.UserWrite));

    [Fact]
    public async Task Install_ReportsBrewUpdateFailure()
    {
        var executor = StandardExecutor(
            brewUpdate: Result(1, stderr: "update failed"));
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.HomebrewFailure, result.Status);
        Assert.DoesNotContain(
            executor.Calls,
            call => call.Arguments.Count > 0 && call.Arguments[0] == "info");
    }

    [Fact]
    public async Task Install_ReportsBrewInfoFailureWithoutStartingUpgrade()
    {
        var executor = StandardExecutor(
            brewInfo: Result(1, stderr: "info failed"));
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.HomebrewFailure, result.Status);
        Assert.DoesNotContain(executor.Calls, call => call.Arguments.Contains("upgrade"));
    }

    [Fact]
    public async Task Install_UsesCallerTokenUntilCancellationBoundaryAndNoneAfterIt()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = StandardExecutor();
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            cancellation.Token);

        Assert.Equal(ApplicationUpdateInstallStatus.Success, result.Status);
        Assert.Equal(
            cancellation.Token,
            Assert.Single(executor.Calls, call => call.Arguments.FirstOrDefault() == "list").CancellationToken);
        Assert.Equal(
            cancellation.Token,
            Assert.Single(executor.Calls, call => call.Arguments.FirstOrDefault() == "update").CancellationToken);
        Assert.Equal(
            cancellation.Token,
            Assert.Single(executor.Calls, call => call.Arguments.FirstOrDefault() == "info").CancellationToken);

        Assert.False(Assert.Single(
            executor.Calls,
            call => call.Arguments.FirstOrDefault() == "upgrade").CancellationToken.CanBeCanceled);
        Assert.False(Assert.Single(
            executor.Calls,
            call => call.Executable == MacOsHomebrewUpdateInstaller.ApplicationExecutablePath).CancellationToken.CanBeCanceled);
        Assert.All(
            executor.Calls.Where(call => call.Executable == MacOsHomebrewUpdateInstaller.XattrExecutable),
            call => Assert.False(call.CancellationToken.CanBeCanceled));
        Assert.False(Assert.Single(
            executor.Calls,
            call => call.Executable == MacOsHomebrewUpdateInstaller.OpenExecutable).CancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task Install_CancellationDuringBrewUpdateStopsBeforeInfoAndUpgrade()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new StubProcessExecutor((executable, arguments, token) =>
        {
            if (executable == Brew && arguments.FirstOrDefault() == "list")
                return Result(0, "csharpfar-app 1.0.70");

            if (executable == Brew && arguments.FirstOrDefault() == "update")
            {
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException("Unexpected process call.");
        });
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            cancellation.Token);

        Assert.Equal(ApplicationUpdateInstallStatus.Cancelled, result.Status);
        Assert.DoesNotContain(executor.Calls, call => call.Arguments.FirstOrDefault() == "info");
        Assert.DoesNotContain(executor.Calls, call => call.Arguments.FirstOrDefault() == "upgrade");
    }

    [Fact]
    public async Task Install_CancellationDuringBrewInfoStopsBeforeUpgrade()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new StubProcessExecutor((executable, arguments, token) =>
        {
            if (executable == Brew && arguments.FirstOrDefault() == "list")
                return Result(0, "csharpfar-app 1.0.70");
            if (executable == Brew && arguments.FirstOrDefault() == "update")
                return Result(0);

            if (executable == Brew && arguments.FirstOrDefault() == "info")
            {
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException("Unexpected process call.");
        });
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            cancellation.Token);

        Assert.Equal(ApplicationUpdateInstallStatus.Cancelled, result.Status);
        Assert.DoesNotContain(executor.Calls, call => call.Arguments.FirstOrDefault() == "upgrade");
    }

    [Fact]
    public async Task Install_FinalCancellationCheckRunsImmediatelyBeforeUpgrade()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new StubProcessExecutor((executable, arguments, _) =>
        {
            if (executable == Brew && arguments.FirstOrDefault() == "list")
                return Result(0, "csharpfar-app 1.0.70");
            if (executable == Brew && arguments.FirstOrDefault() == "update")
                return Result(0);
            if (executable == Brew && arguments.FirstOrDefault() == "info")
            {
                cancellation.Cancel();
                return Result(0, "{\"casks\":[{\"version\":\"1.0.71\"}]}");
            }

            throw new InvalidOperationException("Upgrade must not start.");
        });
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            cancellation.Token);

        Assert.Equal(ApplicationUpdateInstallStatus.Cancelled, result.Status);
        Assert.DoesNotContain(executor.Calls, call => call.Arguments.FirstOrDefault() == "upgrade");
    }

    [Fact]
    public async Task Install_IgnoresCallerCancellationAfterEnteringNonCancellablePhase()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = StandardExecutor();
        var installer = Create(executor);
        var progress = new InlineProgress<ApplicationUpdateProgress>(value =>
        {
            if (!value.CanCancel)
                cancellation.Cancel();
        });

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress,
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(ApplicationUpdateInstallStatus.Success, result.Status);
        Assert.False(Assert.Single(
            executor.Calls,
            call => call.Arguments.FirstOrDefault() == "upgrade").CancellationToken.CanBeCanceled);
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
    public async Task Install_AcceptsNewerCaskAndInstalledVersion()
    {
        var executor = StandardExecutor(
            caskVersion: "1.0.72",
            installedVersion: "1.0.72");
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.Success, result.Status);
    }

    [Fact]
    public async Task Install_ReportsUpgradeFailure()
    {
        var executor = StandardExecutor(
            upgrade: Result(1, stderr: "upgrade failed"));
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.HomebrewFailure, result.Status);
        Assert.DoesNotContain(
            executor.Calls,
            call => call.Executable == MacOsHomebrewUpdateInstaller.ApplicationExecutablePath);
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
    public async Task Install_FailsWhenInstalledBinaryIsMissing()
    {
        var executor = StandardExecutor(
            verification: Result(127, stderr: "missing"));
        var installer = Create(executor);

        ApplicationUpdateInstallResult result = await installer.InstallAsync(
            new ReleaseVersion(1, 0, 71),
            progress: null,
            CancellationToken.None);

        Assert.Equal(ApplicationUpdateInstallStatus.VerificationFailed, result.Status);
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
        Assert.Equal(
            "/usr/bin/xattr -dr com.apple.quarantine /Applications/CSharpFar.app",
            result.ManualCommand);
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
        string? processPath = StandardProcess) =>
        new(
            executor,
            environment: CreateMacEnvironment(processPath));

    private static StubUpdateEnvironment CreateMacEnvironment(
        string? processPath = StandardProcess)
    {
        var environment = new StubUpdateEnvironment
        {
            IsMacOS = true,
            ProcessPath = processPath,
        };
        environment.Files.Add(Brew);
        environment.ExecutableFiles.Add(Brew);
        return environment;
    }

    private static StubProcessExecutor StandardExecutor(
        string caskVersion = "1.0.71",
        string installedVersion = "1.0.71",
        string? infoJson = null,
        ProcessExecutionResult? brewUpdate = null,
        ProcessExecutionResult? brewInfo = null,
        ProcessExecutionResult? upgrade = null,
        ProcessExecutionResult? verification = null,
        ProcessExecutionResult? removeQuarantine = null,
        ProcessExecutionResult? quarantineCheck = null,
        ProcessExecutionResult? relaunch = null) =>
        new((executable, arguments, _) =>
        {
            if (executable == Brew && arguments.SequenceEqual(
                    ["list", "--cask", "--versions", MacOsHomebrewUpdateInstaller.CaskName]))
                return Result(0, "csharpfar-app 1.0.70");

            if (executable == Brew && arguments.SequenceEqual(["update"]))
                return brewUpdate ?? Result(0);

            if (executable == Brew && arguments.Count > 0 && arguments[0] == "info")
                return brewInfo ?? Result(
                    0,
                    infoJson ?? $"{{\"casks\":[{{\"version\":\"{caskVersion}\"}}]}}");

            if (executable == Brew && arguments.Count > 0 && arguments[0] == "upgrade")
                return upgrade ?? Result(0);

            if (executable == MacOsHomebrewUpdateInstaller.ApplicationExecutablePath)
                return verification ?? Result(0, $"CSharpFar {installedVersion}");

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
        IReadOnlyList<string> Arguments,
        CancellationToken CancellationToken);

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
            Calls.Add(new ProcessCall(executable, copiedArguments, cancellationToken));
            return Task.FromResult(handler(executable, copiedArguments, cancellationToken));
        }
    }

    private sealed class StubUpdateEnvironment : IApplicationUpdateEnvironment
    {
        public bool IsMacOS { get; set; }

        public string? ProcessPath { get; set; }

        public string? PathValue { get; set; }

        public char PathSeparator => Path.PathSeparator;

        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ExecutableFiles { get; } = new(StringComparer.Ordinal);

        public HashSet<string> ExecutableCheckFailures { get; } = new(StringComparer.Ordinal);

        public bool FileExists(string path) => Files.Contains(path);

        public bool IsExecutableFile(string path)
        {
            if (ExecutableCheckFailures.Contains(path))
                throw new UnauthorizedAccessException(path);

            return ExecutableFiles.Contains(path);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
