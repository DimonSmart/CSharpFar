using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec012SearchProgressDialogTests
{
    [Fact]
    public void Show_RendersFindFileFarStyleDialog()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(KeyChar('S', ConsoleKey.S));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SearchProgressDialog(screen, new BlockingSearchService(Result(@"C:\root\found.cs")))
            .Show(Request(@"C:\root", "*.cs"));

        Assert.True(result.Cancelled);
        Assert.True(result.DiscardResults);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Find file: *.cs", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Files:", StringComparison.Ordinal) && r.Background == ConsoleColor.Gray);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("{ Go to }", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("[ Stop ]", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Search has been interrupted", StringComparison.Ordinal) && r.Background == ConsoleColor.DarkRed);
    }

    [Fact]
    public void Show_RendersResultsBeforeSearchCompletes()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        EnqueueNoOpKeys(driver, 8);
        driver.EnqueueKey(KeyChar('S', ConsoleKey.S));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SearchProgressDialog(screen, new BlockingSearchService(Result(@"C:\root\found.txt")))
            .Show(Request(@"C:\root", "*.txt"));

        Assert.True(result.Cancelled);
        Assert.True(result.DiscardResults);
        Assert.Empty(result.Results);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("found.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_GoToReturnsSelectedResultAndCancelsSearch()
    {
        var item = Result(@"C:\root\found.txt");
        var service = new BlockingSearchService(item);
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        EnqueueNoOpKeys(driver, 8);
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SearchProgressDialog(screen, service).Show(Request(@"C:\root", "*.txt"));

        Assert.True(result.Cancelled);
        Assert.Same(item, result.GoToResult);
        Assert.True(service.CancellationObserved);
    }

    [Fact]
    public void Show_StopConfirmationNoContinuesSearch()
    {
        var item = Result(@"C:\root\found.txt");
        var service = new BlockingSearchService(item);
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.NoName));
        driver.EnqueueKey(KeyChar('S', ConsoleKey.S));
        driver.EnqueueKey(KeyChar('N', ConsoleKey.N));
        driver.EnqueueKey(KeyChar('G', ConsoleKey.G));

        var result = new SearchProgressDialog(screen, service).Show(Request(@"C:\root", "*.txt"));

        Assert.True(result.Cancelled);
        Assert.False(result.DiscardResults);
        Assert.Same(item, result.GoToResult);
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

    private static ConsoleKeyInfo KeyChar(char keyChar, ConsoleKey key) =>
        new(keyChar, key, shift: false, alt: false, control: false);

    private static void EnqueueNoOpKeys(FakeConsoleDriver driver, int count)
    {
        for (int i = 0; i < count; i++)
            driver.EnqueueKey(Key(ConsoleKey.NoName));
    }

    private sealed class BlockingSearchService : ISearchService
    {
        private readonly SearchResultItem _item;

        public BlockingSearchService(SearchResultItem item) => _item = item;

        public bool CancellationObserved { get; private set; }

        public async IAsyncEnumerable<SearchResultItem> SearchAsync(
            SearchRequest request,
            IProgress<SearchProgress>? progress,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            progress?.Report(new SearchProgress
            {
                CurrentPath = request.RootPath,
                ScannedFiles = 1,
                MatchedItems = 1,
            });

            yield return _item;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
