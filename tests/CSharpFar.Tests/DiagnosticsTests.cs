using System.Net;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.App.Diagnostics;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Menu;
using CSharpFar.App.Updates;
using CSharpFar.Console;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void RunOptionsParser_SupportsDiagnosticsIndependentlyAndWithDemo()
    {
        Assert.True(ApplicationRunOptionsParser.TryParse([], out ApplicationRunOptions normal, out _));
        Assert.False(normal.DiagnosticsEnabled);

        Assert.True(ApplicationRunOptionsParser.TryParse(
            ["--diagnostics"],
            out ApplicationRunOptions diagnostics,
            out _));
        Assert.Equal(ApplicationRunMode.Normal, diagnostics.Mode);
        Assert.True(diagnostics.DiagnosticsEnabled);

        Assert.True(ApplicationRunOptionsParser.TryParse(
            ["--demo", "fixture", "--diagnostics"],
            out ApplicationRunOptions demoThenDiagnostics,
            out _));
        Assert.Equal(ApplicationRunMode.Demo, demoThenDiagnostics.Mode);
        Assert.Equal("fixture", demoThenDiagnostics.DemoRootPath);
        Assert.True(demoThenDiagnostics.DiagnosticsEnabled);

        Assert.True(ApplicationRunOptionsParser.TryParse(
            ["--diagnostics", "--demo", "fixture"],
            out ApplicationRunOptions diagnosticsThenDemo,
            out _));
        Assert.Equal(demoThenDiagnostics, diagnosticsThenDemo);
    }

    [Theory]
    [InlineData("--diagnostics", "--diagnostics")]
    [InlineData("--unknown", null)]
    public void RunOptionsParser_RejectsDuplicateOrUnknownArguments(string first, string? second)
    {
        string[] arguments = second is null ? [first] : [first, second];

        Assert.False(ApplicationRunOptionsParser.TryParse(arguments, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ApplicationComposition_ReceivesDiagnosticsInNormalMode()
    {
        var driver = new FakeConsoleDriver();
        var fileSystem = new FakeFileSystemService();
        const string root = @"C:\Root";
        fileSystem.AddDirectory(root);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = root;
        settings.Panels.RightStartDirectory = root;

        ApplicationServices services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(driver),
            fileSystem,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            enableBuiltInNetworkModules: false,
            runOptions: new ApplicationRunOptions(
                ApplicationRunMode.Normal,
                DiagnosticsEnabled: true));

        Assert.True(services.CommandContext.DiagnosticLog.IsEnabled);
        Assert.Contains(
            services.CommandContext.DiagnosticLog.GetSnapshot(),
            entry => entry.Category == DiagnosticCategory.Application &&
                     entry.Message.Contains("Diagnostics enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnosticLog_DisabledImplementationDoesNotAccumulate()
    {
        IDiagnosticLog log = DisabledDiagnosticLog.Instance;

        log.Write(DiagnosticCategory.Application, "event");
        log.WriteException(DiagnosticCategory.Application, new InvalidOperationException("boom"));

        Assert.False(log.IsEnabled);
        Assert.Empty(log.GetSnapshot());
    }

    [Fact]
    public void DiagnosticLog_RingBufferKeepsNewestThousandInOrder()
    {
        var start = new DateTimeOffset(2026, 9, 21, 20, 0, 0, TimeSpan.FromHours(2));
        int tick = 0;
        var log = new InMemoryDiagnosticLog(
            clock: () => start.AddMilliseconds(Interlocked.Increment(ref tick)));

        for (int index = 0; index < 1005; index++)
            log.Write(DiagnosticCategory.Application, $"event {index}");

        IReadOnlyList<DiagnosticEntry> snapshot = log.GetSnapshot();
        Assert.Equal(1000, snapshot.Count);
        Assert.Equal("event 5", snapshot[0].Message);
        Assert.Equal("event 1004", snapshot[^1].Message);
        Assert.True(snapshot.Zip(snapshot.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));

        log.Clear();
        Assert.Empty(log.GetSnapshot());
    }

    [Fact]
    public void DiagnosticLog_IsSafeForConcurrentWriters()
    {
        var log = new InMemoryDiagnosticLog();

        Parallel.For(0, 4000, index =>
            log.Write(DiagnosticCategory.Application, $"event {index}"));

        Assert.Equal(InMemoryDiagnosticLog.DefaultCapacity, log.GetSnapshot().Count);
    }

    [Fact]
    public void DiagnosticLog_CapturesExceptionAsSanitizedDataOnly()
    {
        var log = new InMemoryDiagnosticLog();
        var exception = new InvalidOperationException(
            "failed https://user:password@example.com/api?q=secret#fragment token=abc",
            new ArgumentException("password=inner-secret"));

        log.WriteException(DiagnosticCategory.UpdateCheck, exception, "request failed");

        DiagnosticEntry entry = Assert.Single(log.GetSnapshot());
        Assert.NotNull(entry.ExceptionInfo);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.ExceptionInfo!.Type);
        Assert.DoesNotContain("password@example.com", entry.ExceptionInfo.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("q=secret", entry.ExceptionInfo.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", entry.ExceptionInfo.Message, StringComparison.Ordinal);
        Assert.Single(entry.ExceptionInfo.InnerExceptions);
        Assert.DoesNotContain("inner-secret", entry.ExceptionInfo.InnerExceptions[0].Message, StringComparison.Ordinal);

        Assert.DoesNotContain(
            typeof(DiagnosticEntry).GetProperties(),
            property => typeof(Exception).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(
            typeof(DiagnosticExceptionInfo).GetProperties(),
            property => typeof(Exception).IsAssignableFrom(property.PropertyType));
    }

    [Fact]
    public void DiagnosticSanitizer_RemovesUrlSecretsAndKnownCredentialPatterns()
    {
        string sanitized = DiagnosticSanitizer.SanitizeText(
            "GET https://user:password@example.com/api?q=secret#fragment\n" +
            "Authorization: Bearer authorization-secret\n" +
            "api_key=key-secret token=token-secret password=password-secret");

        Assert.Contains("https://example.com/api", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("user:", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("q=secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("fragment", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("authorization-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("key-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secret", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("password-secret", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticSanitizer_RedactsHomePrefix()
    {
        string home = Path.Combine(Path.GetTempPath(), "diagnostic-home");
        string path = Path.Combine(home, "src", "CSharpFar");

        string redacted = DiagnosticSanitizer.RedactHomePath(path, home);

        Assert.StartsWith("~", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(home, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportBuilder_ProducesSingleSafeOnReportAndOffGuidance()
    {
        var generated = new DateTimeOffset(2026, 9, 21, 20, 21, 3, 412, TimeSpan.FromHours(2));
        var log = new InMemoryDiagnosticLog(clock: () => generated);
        log.Write(DiagnosticCategory.Application, "Diagnostics enabled.");
        var version = new ApplicationVersionInfo(
            "1.0.68+local",
            new ReleaseVersion(1, 0, 68),
            "1.0.68+local",
            new Version(1, 0, 68, 0));
        var builder = new DiagnosticReportBuilder(() => generated);

        string on = builder.Build(true, log.GetSnapshot(), version, terminal: null);
        string off = builder.Build(false, [], version, terminal: null);

        Assert.Contains("CSharpFar Diagnostic Report", on, StringComparison.Ordinal);
        Assert.Contains("Generated: 2026-09-21 20:21:03.412 +02:00", on, StringComparison.Ordinal);
        Assert.Contains("Diagnostics mode: ON", on, StringComparison.Ordinal);
        Assert.Contains("Version: 1.0.68+local", on, StringComparison.Ordinal);
        Assert.Contains("Informational version: 1.0.68+local", on, StringComparison.Ordinal);
        Assert.Contains("Assembly version: 1.0.68.0", on, StringComparison.Ordinal);
        Assert.Contains("Comparable version: 1.0.68", on, StringComparison.Ordinal);
        Assert.Contains("[Application] Diagnostics enabled.", on, StringComparison.Ordinal);
        Assert.Contains("OS architecture:", on, StringComparison.Ordinal);
        Assert.Contains("Process architecture:", on, StringComparison.Ordinal);
        Assert.Contains("Runtime:", on, StringComparison.Ordinal);

        Assert.Contains("Diagnostics mode: OFF", off, StringComparison.Ordinal);
        Assert.Contains("Runtime event tracing is disabled.", off, StringComparison.Ordinal);
        Assert.Contains("csharpfar --diagnostics", off, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsCommand_IsRegisteredAndHelpMenuKeepsTerminalDiagnosticsSeparate()
    {
        var registry = ApplicationCommandRegistry.CreateDefault();
        Assert.True(registry.TryGetCommand(
            ApplicationCommandIds.Diagnostics,
            out IApplicationCommand command));
        Assert.IsType<DiagnosticsCommand>(command);

        var panel = new FilePanelState { CurrentDirectory = Path.GetTempPath() };
        MenuBarDefinition menu = new DefaultMenuDefinitionProvider().BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = PanelSide.Left,
            LeftPanel = panel,
            RightPanel = panel,
            LeftViewMode = PanelViewMode.Full,
            RightViewMode = PanelViewMode.Full,
            Settings = new AppSettings(),
            CanSaveSettings = true,
        });

        TopMenuItemDefinition help = menu.Items.Single(item => item.Id == "Help");
        Assert.Contains(
            help.Children,
            item => item.Text == "Diagnostics..." &&
                    item.CommandId == ApplicationCommandIds.Diagnostics);

        TopMenuItemDefinition options = menu.Items.Single(item => item.Id == "Options");
        Assert.Contains(options.Children, item => item.Text == "Terminal diagnostics");
    }

    [Fact]
    public void ClipboardFailure_IsContained()
    {
        Assert.False(DiagnosticsDialog.TryCopyReport(new FalseClipboard(), "report"));
        Assert.False(DiagnosticsDialog.TryCopyReport(new ThrowingClipboard(), "report"));
    }

    [Fact]
    public async Task UpdateCheck_DiagnosticsDescribeSuccessfulResult()
    {
        var log = new InMemoryDiagnosticLog();
        using var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{"tag_name":"v1.0.69","html_url":"https://github.com/DimonSmart/CSharpFar/releases/tag/v1.0.69"}"""))));
        var service = new UpdateCheckService(client, diagnostics: log);

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        string events = EventText(log);
        Assert.Contains("Request started.", events, StringComparison.Ordinal);
        Assert.Contains("HTTP 200", events, StringComparison.Ordinal);
        Assert.Contains("Latest release tag=v1.0.69", events, StringComparison.Ordinal);
        Assert.Contains("Result=UpdateAvailable", events, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateCheck_DiagnosticsDescribeHttpFailureAndRateLimit()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "60");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
        response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", "123456");
        var log = new InMemoryDiagnosticLog();
        using var client = new HttpClient(new StubHandler((_, _) => Task.FromResult(response)));
        var service = new UpdateCheckService(client, diagnostics: log);

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
        string events = EventText(log);
        Assert.Contains("HTTP 403", events, StringComparison.Ordinal);
        Assert.Contains("X-RateLimit-Remaining=0", events, StringComparison.Ordinal);
        Assert.Contains("Reason=non-success HTTP status", events, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateCheck_DiagnosticsDistinguishTimeoutFromCallerCancellation()
    {
        var timeoutLog = new InMemoryDiagnosticLog();
        using var timeoutClient = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var timeoutService = new UpdateCheckService(
            timeoutClient,
            TimeSpan.FromMilliseconds(20),
            timeoutLog);

        UpdateCheckResult timeoutResult =
            await timeoutService.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Unavailable, timeoutResult.Status);
        Assert.Contains("timeout", EventText(timeoutLog), StringComparison.OrdinalIgnoreCase);

        var cancellationLog = new InMemoryDiagnosticLog();
        using var cancellationClient = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var cancellationService = new UpdateCheckService(
            cancellationClient,
            TimeSpan.FromSeconds(5),
            cancellationLog);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancellationService.CheckAsync(new ReleaseVersion(1, 0, 68), cancellation.Token));
        Assert.Contains(
            "cancelled by caller",
            EventText(cancellationLog),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{}""", "missing tag_name")]
    [InlineData("""{"tag_name":"invalid","html_url":"https://github.com/DimonSmart/CSharpFar/releases/tag/invalid"}""", "invalid release version")]
    [InlineData("""{"tag_name":"v1.0.69","html_url":"https://example.com/release"}""", "invalid release URL")]
    public async Task UpdateCheck_DiagnosticsDescribeInvalidReleaseData(
        string json,
        string expectedReason)
    {
        var log = new InMemoryDiagnosticLog();
        using var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(json))));
        var service = new UpdateCheckService(client, diagnostics: log);

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
        Assert.Contains(expectedReason, EventText(log), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateCheck_DiagnosticsCaptureHttpRequestExceptionAndInvalidJson()
    {
        var httpLog = new InMemoryDiagnosticLog();
        using var httpClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("offline token=secret-value"))));
        var httpService = new UpdateCheckService(httpClient, diagnostics: httpLog);

        _ = await httpService.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        DiagnosticEntry exceptionEntry = Assert.Single(
            httpLog.GetSnapshot(),
            entry => entry.ExceptionInfo is not null);
        Assert.Contains("HttpRequestException", exceptionEntry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", exceptionEntry.ExceptionInfo!.Message, StringComparison.Ordinal);

        var jsonLog = new InMemoryDiagnosticLog();
        using var jsonClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse("not json"))));
        var jsonService = new UpdateCheckService(jsonClient, diagnostics: jsonLog);

        _ = await jsonService.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Contains("invalid JSON", EventText(jsonLog), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateCheck_DiagnosticsExplainUnavailableCurrentVersion()
    {
        var log = new InMemoryDiagnosticLog();
        var service = new UpdateCheckService(diagnostics: log);

        UpdateCheckResult result = await service.CheckAsync(null, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
        Assert.Contains(
            "current comparable version unavailable",
            EventText(log),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string EventText(IDiagnosticLog log) =>
        string.Join("\n", log.GetSnapshot().Select(entry => entry.Message));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class FalseClipboard : ITextClipboard
    {
        public bool TrySetText(string text) => false;

        public bool TryGetText(out string text)
        {
            text = string.Empty;
            return false;
        }
    }

    private sealed class ThrowingClipboard : ITextClipboard
    {
        public bool TrySetText(string text) => throw new InvalidOperationException("clipboard failed");

        public bool TryGetText(out string text)
        {
            text = string.Empty;
            throw new InvalidOperationException("clipboard failed");
        }
    }
}
