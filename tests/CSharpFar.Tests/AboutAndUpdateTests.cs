using System.Net;
using CSharpFar.App.Commands;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Menu;
using CSharpFar.App.Updates;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class AboutAndUpdateTests
{
    [Theory]
    [InlineData("v1.0.68", 1, 0, 68)]
    [InlineData("1.0.68", 1, 0, 68)]
    [InlineData("v1.0.68+abcdef", 1, 0, 68)]
    [InlineData("1.0.68+abcdef", 1, 0, 68)]
    public void ReleaseVersionParser_ParsesSupportedVersions(
        string text,
        int major,
        int minor,
        int patch)
    {
        Assert.True(ReleaseVersionParser.TryParse(text, out ReleaseVersion version));
        Assert.Equal(new ReleaseVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("v1.0.68-beta")]
    [InlineData("1.0")]
    [InlineData("1.0.68+")]
    public void ReleaseVersionParser_RejectsUnsupportedVersions(string text) =>
        Assert.False(ReleaseVersionParser.TryParse(text, out _));

    [Fact]
    public void ReleaseVersionParser_NormalizesAssemblyVersionAndComparesNumerically()
    {
        Assert.True(
            ReleaseVersionParser.TryParse(
                new Version(1, 0, 68, 0),
                out ReleaseVersion assemblyVersion));
        Assert.Equal(new ReleaseVersion(1, 0, 68), assemblyVersion);

        Assert.True(ReleaseVersionParser.TryParse("v1.0.10", out ReleaseVersion newer));
        Assert.True(ReleaseVersionParser.TryParse("v1.0.9", out ReleaseVersion older));
        Assert.True(newer.CompareTo(older) > 0);
    }

    [Fact]
    public void ApplicationVersionProvider_FallsBackToAssemblyVersionForComparison()
    {
        ApplicationVersionInfo info = ApplicationVersionProvider.GetVersionInfo(
            "not-a-release-version",
            new Version(1, 0, 68, 0));

        Assert.Equal("not-a-release-version", info.DisplayVersion);
        Assert.Equal(new ReleaseVersion(1, 0, 68), info.ComparableVersion);
        Assert.Equal("1.0.68", info.AboutVersion);
    }

    [Fact]
    public async Task UpdateCheck_ReportsAvailableReleaseAndSendsRequiredHeaders()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "https://api.github.com/repos/DimonSmart/CSharpFar/releases/latest",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal("CSharpFar/1.0.68", request.Headers.UserAgent.ToString());
            Assert.Contains(
                request.Headers.Accept,
                value => value.MediaType == "application/vnd.github+json");
            Assert.Equal(
                UpdateCheckService.GitHubApiVersion,
                request.Headers.GetValues("X-GitHub-Api-Version").Single());

            return Task.FromResult(JsonResponse(
                """{"tag_name":"v1.0.69","html_url":"https://github.com/DimonSmart/CSharpFar/releases/tag/v1.0.69"}"""));
        });
        using var client = new HttpClient(handler);
        var service = new UpdateCheckService(client);

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new ReleaseVersion(1, 0, 69), result.LatestVersion);
        Assert.Equal(
            "https://github.com/DimonSmart/CSharpFar/releases/tag/v1.0.69",
            result.ReleaseUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("v1.0.68")]
    [InlineData("v1.0.67")]
    public async Task UpdateCheck_TreatsEqualOrOlderLatestAsUpToDate(string tag)
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(
                $"{{\"tag_name\":\"{tag}\",\"html_url\":\"https://github.com/DimonSmart/CSharpFar/releases/tag/{tag}\"}}"))));
        var service = new UpdateCheckService(client);

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UpdateCheck_NonSuccessResponseIsUnavailable(HttpStatusCode statusCode)
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode))));
        var service = new UpdateCheckService(client);

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData("""not json""")]
    [InlineData("""{}""")]
    [InlineData("""{"tag_name":"invalid","html_url":"https://github.com/DimonSmart/CSharpFar/releases/tag/invalid"}""")]
    [InlineData("""{"tag_name":"v1.0.69","html_url":"https://example.com/DimonSmart/CSharpFar/releases/tag/v1.0.69"}""")]
    public async Task UpdateCheck_InvalidReleaseDataIsUnavailable(string json)
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(JsonResponse(json))));
        var service = new UpdateCheckService(client);

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task UpdateCheck_HttpFailureIsUnavailable()
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"))));
        var service = new UpdateCheckService(client);

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task UpdateCheck_ServiceTimeoutIsUnavailable()
    {
        using var client = new HttpClient(new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }));
        var service = new UpdateCheckService(client, TimeSpan.FromMilliseconds(20));

        UpdateCheckResult result =
            await service.CheckAsync(new ReleaseVersion(1, 0, 68), CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task UpdateCheck_CallerCancellationIsPropagated()
    {
        using var client = new HttpClient(new StubHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }));
        var service = new UpdateCheckService(client, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CheckAsync(new ReleaseVersion(1, 0, 68), cancellation.Token));
    }

    [Fact]
    public void DefaultMenu_ContainsHelpAndAboutCommands()
    {
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

        Assert.Equal(
            ["File", "Commands", "Left", "Right", "Plugins", "Options", "Help"],
            menu.Items.Select(item => item.Text).ToArray());

        TopMenuItemDefinition help = menu.Items[^1];
        Assert.Equal('H', help.HotChar);
        Assert.Equal("Help", help.Children[0].Text);
        Assert.Equal(FunctionKeyCommandIds.Help, help.Children[0].CommandId);
        Assert.Equal(MenuItemKind.Separator, help.Children[1].Kind);
        Assert.Equal("About...", help.Children[2].Text);
        Assert.Equal(ApplicationCommandIds.About, help.Children[2].CommandId);
    }

    [Fact]
    public void DefaultCommandRegistry_ContainsAbout()
    {
        var registry = ApplicationCommandRegistry.CreateDefault();

        Assert.True(
            registry.TryGetCommand(ApplicationCommandIds.About, out IApplicationCommand command));
        Assert.IsType<AboutCommand>(command);
    }

    [Fact]
    public void AboutModel_TransitionsAndShowsReleaseActionOnlyWhenUpdateAvailable()
    {
        var model = new AboutDialogModel("1.0.68");

        Assert.Equal(AboutUpdateState.Checking, model.State);
        Assert.DoesNotContain(
            model.Snapshot().Buttons,
            button => button.Id == "open-release");

        model.Apply(new UpdateCheckResult(
            UpdateCheckStatus.UpToDate,
            new ReleaseVersion(1, 0, 68),
            new ReleaseVersion(1, 0, 68),
            null));
        Assert.Equal(AboutUpdateState.UpToDate, model.State);
        Assert.DoesNotContain(
            model.Snapshot().Buttons,
            button => button.Id == "open-release");

        var releaseUri =
            new Uri("https://github.com/DimonSmart/CSharpFar/releases/tag/v1.0.69");
        model.Apply(new UpdateCheckResult(
            UpdateCheckStatus.UpdateAvailable,
            new ReleaseVersion(1, 0, 68),
            new ReleaseVersion(1, 0, 69),
            releaseUri));
        Assert.Equal(AboutUpdateState.UpdateAvailable, model.State);
        Assert.Equal(releaseUri, model.ReleaseUri);
        Assert.Contains(
            model.Snapshot().Buttons,
            button => button.Id == "open-release");

        model.Apply(UpdateCheckResult.Unavailable(new ReleaseVersion(1, 0, 68)));
        Assert.Equal(AboutUpdateState.Unavailable, model.State);
        Assert.DoesNotContain(
            model.Snapshot().Buttons,
            button => button.Id == "open-release");
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public StubHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
            _send = send;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _send(request, cancellationToken);
    }
}
