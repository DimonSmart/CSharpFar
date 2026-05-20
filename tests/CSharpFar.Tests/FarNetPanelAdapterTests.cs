using System.Globalization;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FarNetHost;
using CSharpFar.Module.Abstractions;
using CSharpFar.Tests.Fakes;
using FarNet;

namespace CSharpFar.Tests;

[Collection(FarNetTestCollection.Name)]
public sealed class FarNetPanelAdapterTests
{
    [Fact]
    public void ChildPanelRoot_ShowsParentEntryWhenRootParentEntriesAreHidden()
    {
        var adapter = CreateChildPanelAdapter();
        var registry = new FilePanelSourceRegistry([adapter]);
        var builder = new PanelViewBuilder(
            new FakeFileSystemService(),
            new PanelSortService(),
            sources: registry);

        var view = builder.Build(new PanelViewRequest
        {
            DirectoryPath = "/",
            Location = new PanelLocation(adapter.SourceId, "/"),
            Options = new AppSettings.PanelOptionsSettings
            {
                ShowParentDirectoryInRootFolders = false,
            },
            SortMode = SortMode.Name,
            SortDescending = false,
            SelectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        });

        var parentItem = Assert.Single(view.Items, item => item.IsParentDirectory);
        Assert.Equal("..", parentItem.Name);
        Assert.Equal("/..", parentItem.FullPath);
    }

    [Fact]
    public void OpenParentEntry_ReturnsParentPanel()
    {
        var adapter = CreateChildPanelAdapter();

        var result = adapter.OpenItem("/..");

        Assert.Equal(ModuleActionResultKind.OpenedPanel, result.Kind);
        Assert.NotNull(result.Panel);
        Assert.Equal("Parent panel", result.Panel.GetOpenPanelInfo().Title);
        var parentItems = result.Panel.EnumerateDirectory("/");
        var parentItem = Assert.Single(parentItems);
        Assert.Equal("parent.json", parentItem.Name);
    }

    private static FarNetPanelAdapter CreateChildPanelAdapter()
    {
        var parent = new Panel(new StaticExplorer("parent.json"))
        {
            Title = "Parent panel",
        };
        var child = new Panel(new StaticExplorer("child.json"))
        {
            Title = "Child panel",
        };

        var previousApi = Far.Api;
        Far.Api = new TestFarApi();
        try
        {
            child.OpenChild(parent);
        }
        finally
        {
            Far.Api = previousApi;
        }

        return new FarNetPanelAdapter(child);
    }

    private sealed class StaticExplorer(string fileName) : Explorer(Guid.NewGuid())
    {
        public override IEnumerable<FarFile> GetFiles(GetFilesEventArgs args) =>
        [
            new SetFile
            {
                Name = fileName,
                FullName = fileName,
                LastWriteTime = new DateTime(2026, 1, 2, 3, 4, 5),
            },
        ];
    }

    private sealed class TestFarApi : IFar, IFarNetPanelHost
    {
        public override Version FarVersion => new(0, 1, 0);
        public override Version FarNetVersion => new(10, 0, 30);
        public override string CurrentDirectory => "/";

        public void OpenPanel(Panel panel)
        {
        }

        public Panel? ConsumePendingPanel() => null;

        public override IModuleAction? GetModuleAction(Guid id) => null;
        public override int Message(MessageArgs args) => throw new FarNetUnsupportedApiException(nameof(Message));
        public override string? Input(string? prompt, string? history, string? title, string? text) => null;
        public override void ShowError(string? title, Exception exception) => throw exception;
        public override string GetFullPath(string path) => path;
        public override string TempName(string? prefix) => Path.GetTempFileName();
        public override IModuleManager GetModuleManager(string name) => throw new FarNetUnsupportedApiException(nameof(GetModuleManager));
        public override CultureInfo GetCurrentUICulture(bool update) => CultureInfo.CurrentUICulture;
        public override void ShowHelp(string path, string topic, HelpOptions options) =>
            throw new FarNetUnsupportedApiException(nameof(ShowHelp));
    }
}
