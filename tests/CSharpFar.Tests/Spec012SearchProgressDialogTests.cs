using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec012SearchProgressDialogTests
{
    [Fact]
    public void GoTo_FixesSelectedResultAndRequestsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        SearchResultItem selected = Result(@"C:\root\found.txt");
        var session = new SearchProgressSession(cancellation);

        bool accepted = session.TryGoTo(selected);

        Assert.True(accepted);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Same(selected, session.GoToResult);
        Assert.False(session.CanGoTo);
        Assert.False(session.CanStop);
    }

    [Fact]
    public void GoTo_CannotBeReplacedByAnotherResult()
    {
        using var cancellation = new CancellationTokenSource();
        SearchResultItem first = Result(@"C:\root\first.txt");
        SearchResultItem second = Result(@"C:\root\second.txt");
        var session = new SearchProgressSession(cancellation);

        session.TryGoTo(first);
        bool accepted = session.TryGoTo(second);

        Assert.False(accepted);
        Assert.Same(first, session.GoToResult);
    }

    [Fact]
    public void GoTo_CannotBeReplacedByStop()
    {
        using var cancellation = new CancellationTokenSource();
        SearchResultItem selected = Result(@"C:\root\found.txt");
        var session = new SearchProgressSession(cancellation);

        session.TryGoTo(selected);
        bool accepted = session.TryStop();

        Assert.False(accepted);
        Assert.Same(selected, session.GoToResult);
    }

    [Fact]
    public void Stop_CannotBeReplacedByGoTo()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new SearchProgressSession(cancellation);

        session.TryStop();
        bool accepted = session.TryGoTo(Result(@"C:\root\found.txt"));

        Assert.False(accepted);
        Assert.Null(session.GoToResult);
    }

    [Fact]
    public void BuildResult_ForGoToRetainsSelectedResult()
    {
        using var cancellation = new CancellationTokenSource();
        SearchResultItem selected = Result(@"C:\root\found.txt");
        var session = new SearchProgressSession(cancellation);
        session.TryGoTo(selected);

        SearchRunResult result = session.BuildResult([selected], cancelled: false);

        Assert.Same(selected, result.GoToResult);
        Assert.True(result.Cancelled);
        Assert.False(result.DiscardResults);
    }

    [Fact]
    public void BuildResult_ForStopDiscardsResults()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new SearchProgressSession(cancellation);
        session.TryStop();

        SearchRunResult result = session.BuildResult([Result(@"C:\root\found.txt")], cancelled: false);

        Assert.Null(result.GoToResult);
        Assert.True(result.Cancelled);
        Assert.True(result.DiscardResults);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void Show_DoesNotConsumeInputAfterSearchCompletes()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);

        var modals = ModalTestHost.Create(screen);
        var result = new SearchProgressDialog(modals, new EmptySearchService(), new DialogService(modals, new FormFieldFactory(TextFieldHistoryTestProvider.Create())))
            .Show(Request(@"C:\root", "*.txt"));

        Assert.False(result.Cancelled);
        Assert.Empty(result.Results);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        Assert.Equal(ConsoleKey.F10, screen.ReadKey().Key);
    }

    private static SearchRequest Request(string rootPath, string fileMaskExpression) =>
        new()
        {
            RootPath = rootPath,
            FileMaskExpression = fileMaskExpression,
            Scope = SearchScope.CurrentDirectoryRecursive,
            MaxDegreeOfParallelism = 1,
        };

    private static SearchResultItem Result(string fullPath) =>
        new()
        {
            FullPath = fullPath,
            Name = Path.GetFileName(fullPath),
            Kind = SearchResultItemKind.File,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        };

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private sealed class EmptySearchService : ISearchService
    {
        public async IAsyncEnumerable<SearchResultItem> SearchAsync(
            SearchRequest request,
            IProgress<SearchProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
