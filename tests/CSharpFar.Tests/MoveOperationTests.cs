using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 8: F6 move/rename — FileOperationService.MoveAsync.
/// </summary>
public class MoveOperationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _src;
    private readonly string _dst;

    public MoveOperationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CSharpFarMoveTest_{Guid.NewGuid():N}");
        _src = Path.Combine(_tempRoot, "src");
        _dst = Path.Combine(_tempRoot, "dst");
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_dst);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private FileOperationService Svc() => new();

    // ── Rename (single source, plain name) ────────────────────────────────────

    [Fact]
    public async Task MoveAsync_RenamesFile_WhenSingleSourceAndPlainName()
    {
        string srcFile = Write(_src, "old.txt", "data");

        await Svc().MoveAsync([srcFile], "new.txt");

        Assert.False(File.Exists(srcFile));
        Assert.True(File.Exists(Path.Combine(_src, "new.txt")));
        Assert.Equal("data", File.ReadAllText(Path.Combine(_src, "new.txt")));
    }

    [Fact]
    public async Task MoveAsync_RenamesFile_WhenSingleSourceUsesWildcardPattern()
    {
        string srcFile = Write(_src, "old.txt", "data");

        await Svc().MoveAsync([srcFile], "*.bak");

        Assert.False(File.Exists(srcFile));
        Assert.Equal("data", File.ReadAllText(Path.Combine(_src, "old.bak")));
    }

    [Fact]
    public async Task MoveAsync_RejectsWildcardCollisionsBeforeMutation()
    {
        string first = Write(_src, "same.txt", "first");
        string second = Write(_src, "same.csv", "second");

        await Assert.ThrowsAsync<IOException>(() => Svc().MoveAsync([first, second], Path.Combine(_dst, "*.bak")));

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task MoveAsync_TransformsEachSelectedRootNameWithWildcardDestination()
    {
        string first = Write(_src, "foo.txt", "first");
        string second = Write(_src, "bar.csv", "second");

        await Svc().MoveAsync([first, second], Path.Combine(_dst, "*.bak"));

        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Equal("first", File.ReadAllText(Path.Combine(_dst, "foo.bak")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_dst, "bar.bak")));
    }

    [Fact]
    public async Task CopyAsync_TemplateRoutesEachSelectedRootAndCreatesDirectories()
    {
        string first = Write(_src, "foo.txt", "first");
        string second = Write(_src, "bar.csv", "second");
        File.SetLastWriteTime(first, new DateTime(2026, 8, 12, 14, 35, 20));
        File.SetLastWriteTime(second, new DateTime(2026, 8, 13, 14, 35, 20));

        await Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [first, second],
            Destination = Path.Combine(_dst, "{modified:yyyy-MM-dd}", "{name}_OLD{ext}"),
            UseDestinationTemplate = true,
            Options = new FileOperationOptions(),
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.Equal("first", File.ReadAllText(Path.Combine(_dst, "2026-08-12", "foo_OLD.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_dst, "2026-08-13", "bar_OLD.csv")));
    }

    [Fact]
    public async Task CopyAsync_TemplateIntermediateDirectoryRespectsLocalPlatformFileNameRules()
    {
        string first = Write(_src, "first.txt", "first");
        string second = Write(_src, "second.txt", "second");
        File.SetLastWriteTime(first, new DateTime(2026, 8, 12, 14, 35, 20));
        File.SetLastWriteTime(second, new DateTime(2026, 8, 12, 14, 35, 20));

        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [first, second],
            Destination = Path.Combine(_dst, "{modified:HH:mm}", "{name}{ext}"),
            UseDestinationTemplate = true,
            Options = new FileOperationOptions(),
        };

        if (OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<IOException>(() => Svc().ExecuteAsync(request, progress: null, conflictResolver: new OverwriteResolver()));

            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.Empty(Directory.EnumerateFileSystemEntries(_dst));
        }
        else
        {
            await Svc().ExecuteAsync(request, progress: null, conflictResolver: new OverwriteResolver());

            Assert.Equal("first", File.ReadAllText(Path.Combine(_dst, "14:35", "first.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(_dst, "14:35", "second.txt")));
        }
    }

    [Fact]
    public async Task MoveAsync_TemplateIntermediateDirectoryRespectsLocalPlatformFileNameRules()
    {
        string first = Write(_src, "first.txt", "first");
        string second = Write(_src, "second.txt", "second");
        File.SetLastWriteTime(first, new DateTime(2026, 8, 12, 14, 35, 20));
        File.SetLastWriteTime(second, new DateTime(2026, 8, 12, 14, 35, 20));

        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [first, second],
            Destination = Path.Combine(_dst, "{modified:HH:mm}", "{name}{ext}"),
            UseDestinationTemplate = true,
            Options = new FileOperationOptions(),
        };

        if (OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<IOException>(() => Svc().ExecuteAsync(request, progress: null, conflictResolver: new OverwriteResolver()));

            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.Empty(Directory.EnumerateFileSystemEntries(_dst));
        }
        else
        {
            await Svc().ExecuteAsync(request, progress: null, conflictResolver: new OverwriteResolver());

            Assert.False(File.Exists(first));
            Assert.False(File.Exists(second));
            Assert.Equal("first", File.ReadAllText(Path.Combine(_dst, "14:35", "first.txt")));
            Assert.Equal("second", File.ReadAllText(Path.Combine(_dst, "14:35", "second.txt")));
        }
    }

    [Fact]
    public async Task MoveAsync_TemplateRenamesSingleFileInItsCurrentParentDirectory()
    {
        string source = Write(_src, "report.txt", "report");

        await Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [source],
            Destination = "{name}_OLD{ext}",
            UseDestinationTemplate = true,
            Options = new FileOperationOptions(),
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.False(File.Exists(source));
        Assert.Equal("report", File.ReadAllText(Path.Combine(_src, "report_OLD.txt")));
    }

    [Fact]
    public async Task MoveAsync_TemplateCollisionRejectsWholeOperationBeforeMutation()
    {
        string first = Write(_src, "same.txt", "first");
        string second = Write(_src, "same.csv", "second");

        await Assert.ThrowsAsync<IOException>(() => Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [first, second],
            Destination = Path.Combine(_dst, "same.txt"),
            UseDestinationTemplate = true,
            Options = new FileOperationOptions(),
        }, progress: null, conflictResolver: new OverwriteResolver()));

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task CopyAsync_TemplateAppliesToDirectoryRootOnly()
    {
        string directory = Path.Combine(_src, "Vacation");
        Directory.CreateDirectory(directory);
        Write(directory, "IMG001.jpg", "photo");
        Directory.SetLastWriteTime(directory, new DateTime(2026, 8, 12, 14, 35, 20));

        await Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [directory],
            Destination = Path.Combine(_dst, "{modified:yyyy}", "{name}"),
            UseDestinationTemplate = true,
            Options = new FileOperationOptions(),
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.Equal("photo", File.ReadAllText(Path.Combine(_dst, "2026", "Vacation", "IMG001.jpg")));
    }

    [Fact]
    public async Task CopyAsync_BracesRemainLiteralWhenTemplateModeIsOff()
    {
        string source = Write(_src, "photo.jpg", "photo");
        string literalDestination = Path.Combine(_dst, "{name}_OLD{ext}");
        Directory.CreateDirectory(literalDestination);

        await Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [source],
            Destination = literalDestination,
            Options = new FileOperationOptions(),
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.Equal("photo", File.ReadAllText(Path.Combine(literalDestination, "photo.jpg")));
    }

    [Fact]
    public async Task MoveAsync_WithWildcardDestination_RespectsFileMask()
    {
        string text = Write(_src, "foo.txt", "text");
        string csv = Write(_src, "bar.csv", "csv");

        await Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [text, csv],
            Destination = Path.Combine(_dst, "*.bak"),
            Options = new FileOperationOptions { FileMask = "*.txt" },
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.False(File.Exists(text));
        Assert.True(File.Exists(csv));
        Assert.True(File.Exists(Path.Combine(_dst, "foo.bak")));
    }

    [Fact]
    public async Task CopyAsync_WithOrdinaryDestination_PreservesRenameAllConflictSemantics()
    {
        string firstDirectory = Path.Combine(_src, "A");
        string secondDirectory = Path.Combine(_src, "B");
        Directory.CreateDirectory(firstDirectory);
        Directory.CreateDirectory(secondDirectory);
        string first = Write(firstDirectory, "foo.txt", "first");
        string second = Write(secondDirectory, "foo.txt", "second");

        await Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [first, second],
            Destination = _dst,
            Options = new FileOperationOptions { DefaultConflictDecision = ConflictDecisionMode.RenameAll },
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.Equal("first", File.ReadAllText(Path.Combine(_dst, "foo.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_dst, "foo (2).txt")));
    }

    [Fact]
    public async Task MoveAsync_FallbackMovesEmptyDirectoryToResolvedWildcardTarget()
    {
        string empty = Path.Combine(_src, "Empty");
        Directory.CreateDirectory(empty);
        var service = new FileOperationService(FileOperationServiceDependencies.Default with { ForceMoveFallback = static (_, _) => true });

        await service.MoveAsync([empty], Path.Combine(_dst, "*_old"));

        Assert.False(Directory.Exists(empty));
        Assert.True(Directory.Exists(Path.Combine(_dst, "Empty_old")));
    }

    [Fact]
    public async Task MoveAsync_FallbackDoesNotDeleteFilteredDirectorySource()
    {
        string directory = Path.Combine(_src, "Mixed");
        Directory.CreateDirectory(directory);
        Write(directory, "A.txt", "A");
        Write(directory, "B.csv", "B");
        var service = new FileOperationService(FileOperationServiceDependencies.Default with { ForceMoveFallback = static (_, _) => true });

        await service.ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [directory],
            Destination = Path.Combine(_dst, "*_old"),
            Options = new FileOperationOptions { FileMask = "*.txt" },
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.False(File.Exists(Path.Combine(directory, "A.txt")));
        Assert.True(File.Exists(Path.Combine(directory, "B.csv")));
        Assert.True(File.Exists(Path.Combine(_dst, "Mixed_old", "A.txt")));
    }

    [Fact]
    public async Task MoveAsync_AppliesFileMaskBeforeWildcardCollisionValidation()
    {
        string text = Write(_src, "foo.txt", "text");
        string csv = Write(_src, "foo.csv", "csv");

        await Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [text, csv],
            Destination = Path.Combine(_dst, "*.bak"),
            Options = new FileOperationOptions { FileMask = "*.txt" },
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.False(File.Exists(text));
        Assert.True(File.Exists(csv));
        Assert.Equal("text", File.ReadAllText(Path.Combine(_dst, "foo.bak")));
    }

    [Fact]
    public async Task MoveAsync_FallbackKeepsSkippedFilteredFileInSource()
    {
        string directory = Path.Combine(_src, "Source");
        Directory.CreateDirectory(directory);
        string first = Write(directory, "A.txt", "A");
        string second = Write(directory, "B.txt", "source B");
        string target = Path.Combine(_dst, "Source_old");
        Directory.CreateDirectory(target);
        Write(target, "B.txt", "destination B");
        var service = new FileOperationService(FileOperationServiceDependencies.Default with { ForceMoveFallback = static (_, _) => true });

        await service.ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [directory],
            Destination = Path.Combine(_dst, "*_old"),
            Options = new FileOperationOptions { FileMask = "*.txt" },
        }, progress: null, conflictResolver: new DelegateResolver(conflict =>
            Path.GetFileName(conflict.DestinationPath) == "B.txt"
                ? FileOperationConflictDecision.FromMode(ConflictDecisionMode.Skip)
                : FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite)));

        Assert.False(File.Exists(first));
        Assert.Equal("A", File.ReadAllText(Path.Combine(target, "A.txt")));
        Assert.True(File.Exists(second));
        Assert.Equal("destination B", File.ReadAllText(Path.Combine(target, "B.txt")));
    }

    [Fact]
    public async Task MoveAsync_DirectoryWithoutMatchingFilesDoesNotCreateDestinationOrWildcardCollision()
    {
        string directory = Path.Combine(_src, "foo");
        Directory.CreateDirectory(directory);
        string csv = Write(directory, "A.csv", "csv");
        string text = Write(_src, "foo.txt", "text");

        await Svc().ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [directory, text],
            Destination = Path.Combine(_dst, "*.bak"),
            Options = new FileOperationOptions { FileMask = "*.txt" },
        }, progress: null, conflictResolver: new OverwriteResolver());

        Assert.Equal("csv", File.ReadAllText(csv));
        Assert.False(Directory.Exists(Path.Combine(_dst, "foo.bak")));
        Assert.Equal("text", File.ReadAllText(Path.Combine(_dst, "foo.bak")));
    }

    [Fact]
    public async Task MoveAsync_FallbackPreservesConflictRenamedWildcardTarget()
    {
        string source = Write(_src, "foo.txt", "new");
        Write(_dst, "foo.bak", "existing");
        var resolver = new DelegateResolver(_ => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Rename));
        var service = new FileOperationService(FileOperationServiceDependencies.Default with { ForceMoveFallback = static (_, _) => true });

        await service.ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [source],
            Destination = Path.Combine(_dst, "*.bak"),
            Options = new FileOperationOptions(),
        }, progress: null, conflictResolver: resolver);

        Assert.False(File.Exists(source));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_dst, "foo.bak")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_dst, "foo (2).bak")));
        Assert.Equal(1, resolver.CallCount);
    }

    [Fact]
    public async Task MoveAsync_RejectsWildcardOutsideDestinationName()
    {
        string source = Write(_src, "foo.txt", "data");

        IOException error = await Assert.ThrowsAsync<IOException>(() =>
            Svc().MoveAsync([source], Path.Combine(_dst, "*", "foo.txt")));

        Assert.Equal("Wildcards are allowed only in the destination file name.", error.Message);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task MoveAsync_RenamesDirectory_WhenSingleSourceAndPlainName()
    {
        string srcDir = Path.Combine(_src, "OldName");
        Directory.CreateDirectory(srcDir);
        Write(srcDir, "file.txt", "x");

        await Svc().MoveAsync([srcDir], "NewName");

        Assert.False(Directory.Exists(srcDir));
        string renamed = Path.Combine(_src, "NewName");
        Assert.True(Directory.Exists(renamed));
        Assert.True(File.Exists(Path.Combine(renamed, "file.txt")));
    }

    [Fact]
    public async Task MoveAsync_RenameToSameNameIsNoOp()
    {
        string srcFile = Write(_src, "same.txt", "data");

        await Svc().MoveAsync([srcFile], "same.txt", onConflict: _ => ConflictChoice.Overwrite);

        Assert.True(File.Exists(srcFile));
        Assert.Equal("data", File.ReadAllText(srcFile));
    }

    // ── Move (path destination) ───────────────────────────────────────────────

    [Fact]
    public async Task MoveAsync_MovesFileToDirectory()
    {
        string srcFile = Write(_src, "file.txt", "hello");

        await Svc().MoveAsync([srcFile], _dst);

        Assert.False(File.Exists(srcFile));
        Assert.True(File.Exists(Path.Combine(_dst, "file.txt")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_dst, "file.txt")));
    }

    [Fact]
    public async Task MoveAsync_MovesDirectoryToDestination()
    {
        string srcDir = Path.Combine(_src, "SubDir");
        Directory.CreateDirectory(srcDir);
        Write(srcDir, "inner.txt", "y");

        await Svc().MoveAsync([srcDir], _dst);

        Assert.False(Directory.Exists(srcDir));
        Assert.True(File.Exists(Path.Combine(_dst, "SubDir", "inner.txt")));
    }

    [Fact]
    public async Task MoveAsync_ThrowsWhenDirectoryDestinationIsInsideSource()
    {
        string srcDir = Path.Combine(_src, "SubDir");
        string child = Path.Combine(srcDir, "Child");
        Directory.CreateDirectory(child);

        await Assert.ThrowsAsync<IOException>(() =>
            Svc().MoveAsync([srcDir], child));
    }

    [Fact]
    public async Task MoveAsync_MovesMultipleFilesToDirectory()
    {
        string f1 = Write(_src, "a.txt", "A");
        string f2 = Write(_src, "b.txt", "B");

        await Svc().MoveAsync([f1, f2], _dst);

        Assert.True(File.Exists(Path.Combine(_dst, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_dst, "b.txt")));
        Assert.False(File.Exists(f1));
        Assert.False(File.Exists(f2));
    }

    // ── Multiple sources always treated as move ───────────────────────────────

    [Fact]
    public async Task MoveAsync_MultipleSourcesWithPlainNameIsMoveNotRename()
    {
        // "name" has no path separators, but with 2 sources it's a directory destination
        string f1 = Write(_src, "a.txt", "A");
        string f2 = Write(_src, "b.txt", "B");
        string dir = Path.Combine(_src, "name");
        Directory.CreateDirectory(dir);

        await Svc().MoveAsync([f1, f2], dir);

        Assert.True(File.Exists(Path.Combine(dir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "b.txt")));
    }

    // ── Conflict handling ─────────────────────────────────────────────────────

    [Fact]
    public async Task MoveAsync_CallsOnConflictWhenDestExists()
    {
        string srcFile = Write(_src, "dup.txt", "new");
        Write(_dst, "dup.txt", "old");

        bool called = false;
        await Svc().MoveAsync([srcFile], _dst, onConflict: _ => { called = true; return ConflictChoice.Skip; });

        Assert.True(called);
    }

    [Fact]
    public async Task MoveAsync_OverwritesWhenChoiceIsOverwrite()
    {
        string srcFile = Write(_src, "dup.txt", "new content");
        Write(_dst, "dup.txt", "old content");

        await Svc().MoveAsync([srcFile], _dst, onConflict: _ => ConflictChoice.Overwrite);

        Assert.False(File.Exists(srcFile));
        Assert.Equal("new content", File.ReadAllText(Path.Combine(_dst, "dup.txt")));
    }

    [Fact]
    public async Task MoveAsync_DoesNotOverwriteDirectoryWithFile()
    {
        string srcFile = Write(_src, "dup.txt", "new content");
        string destDir = Path.Combine(_dst, "dup.txt");
        Directory.CreateDirectory(destDir);
        Write(destDir, "inner.txt", "keep");

        bool conflictCalled = false;
        await Assert.ThrowsAsync<IOException>(() =>
            Svc().MoveAsync([srcFile], _dst, onConflict: _ =>
            {
                conflictCalled = true;
                return ConflictChoice.Overwrite;
            }));

        Assert.False(conflictCalled);
        Assert.True(File.Exists(srcFile));
        Assert.True(Directory.Exists(destDir));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(destDir, "inner.txt")));
    }

    [Fact]
    public async Task MoveAsync_DoesNotOverwriteFileWithDirectory()
    {
        string srcDir = Path.Combine(_src, "dup");
        Directory.CreateDirectory(srcDir);
        Write(srcDir, "inner.txt", "new content");
        string destFile = Write(_dst, "dup", "old content");

        bool conflictCalled = false;
        await Assert.ThrowsAsync<IOException>(() =>
            Svc().MoveAsync([srcDir], _dst, onConflict: _ =>
            {
                conflictCalled = true;
                return ConflictChoice.Overwrite;
            }));

        Assert.False(conflictCalled);
        Assert.True(Directory.Exists(srcDir));
        Assert.True(File.Exists(destFile));
        Assert.Equal("old content", File.ReadAllText(destFile));
    }

    [Fact]
    public async Task MoveAsync_SkipsWhenChoiceIsSkip()
    {
        string srcFile = Write(_src, "dup.txt", "new content");
        Write(_dst, "dup.txt", "old content");

        await Svc().MoveAsync([srcFile], _dst, onConflict: _ => ConflictChoice.Skip);

        Assert.True(File.Exists(srcFile));  // source not moved
        Assert.Equal("old content", File.ReadAllText(Path.Combine(_dst, "dup.txt")));
    }

    [Fact]
    public async Task MoveAsync_ThrowsOperationCancelledWhenChoiceIsCancel()
    {
        string srcFile = Write(_src, "dup.txt", "new");
        Write(_dst, "dup.txt", "old");

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Svc().MoveAsync([srcFile], _dst, onConflict: _ => ConflictChoice.Cancel));
    }

    // ── CancellationToken ─────────────────────────────────────────────────────

    [Fact]
    public async Task MoveAsync_RespectsExternalCancellation()
    {
        string f1 = Write(_src, "a.txt", "");
        string f2 = Write(_src, "b.txt", "");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Svc().MoveAsync([f1, f2], _dst, cancellationToken: cts.Token));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string Write(string dir, string name, string content)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class OverwriteResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict _) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private sealed class DelegateResolver(Func<FileOperationConflict, FileOperationConflictDecision> resolve) : IFileOperationConflictResolver
    {
        public int CallCount { get; private set; }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            CallCount++;
            return resolve(conflict);
        }
    }
}
