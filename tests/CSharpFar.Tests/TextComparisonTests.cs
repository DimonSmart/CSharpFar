using System.Text;
using CSharpFar.Core.Comparison;

namespace CSharpFar.Tests;

public sealed class TextComparisonTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "CSharpFarTextCompareTests", Guid.NewGuid().ToString("N"));

    public TextComparisonTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TextComparer_NormalizesLineEndingsAndWhitespace()
    {
        FileEntry left = WriteBytes("left.txt", Encoding.UTF8.GetBytes("a\r\nfoo\t  bar\r"));
        FileEntry right = WriteBytes("right.txt", Encoding.UTF8.GetBytes("a\nfoo bar\n"));

        Assert.True(Comparer().Compare(left, right).Equal);
    }

    [Fact]
    public void TextComparer_ExactAndIgnoreAllModesAreIndependent()
    {
        FileEntry left = WriteBytes("left.txt", Encoding.UTF8.GetBytes("int \t x\r\n"));
        FileEntry right = WriteBytes("right.txt", Encoding.UTF8.GetBytes("intx\n"));
        var whitespaceOnly = Comparer(new ComparisonOptions
        {
            Method = CompareMethod.Text,
            TextWhitespace = TextWhitespaceMode.IgnoreAll,
            TextLineEndings = TextLineEndingComparison.Normalize,
        });
        var exactEol = Comparer(new ComparisonOptions
        {
            Method = CompareMethod.Text,
            TextWhitespace = TextWhitespaceMode.IgnoreAll,
            TextLineEndings = TextLineEndingComparison.Exact,
        });

        Assert.True(whitespaceOnly.Compare(left, right).Equal);
        Assert.False(exactEol.Compare(left, right).Equal);
    }

    [Fact]
    public void TextComparer_NormalizeWhitespacePreservesPresence()
    {
        FileEntry left = WriteBytes("left.txt", Encoding.UTF8.GetBytes("a \nint x"));
        FileEntry right = WriteBytes("right.txt", Encoding.UTF8.GetBytes("a\nintx"));

        Assert.False(Comparer().Compare(left, right).Equal);
    }

    [Fact]
    public void TextComparer_BomModesAndCrossEncodingWork()
    {
        FileEntry utf8Bom = WriteBytes("utf8-bom.txt", WithBom(new UTF8Encoding(true), "Привет 😀"));
        FileEntry utf16Bom = WriteBytes(
            "utf16-bom.txt",
            WithBom(new UnicodeEncoding(bigEndian: true, byteOrderMark: true), "Привет 😀"));
        FileEntry utf8NoBom = WriteBytes("utf8.txt", Encoding.UTF8.GetBytes("Привет 😀"));

        Assert.True(Comparer(new ComparisonOptions
        {
            Method = CompareMethod.Text,
            TextBom = TextBomComparison.Exact,
        }).Compare(utf8Bom, utf16Bom).Equal);

        Assert.False(Comparer(new ComparisonOptions
        {
            Method = CompareMethod.Text,
            TextBom = TextBomComparison.Exact,
        }).Compare(utf8Bom, utf8NoBom).Equal);

        Assert.True(Comparer().Compare(utf8Bom, utf8NoBom).Equal);
    }

    [Fact]
    public void TextComparer_RealFeffContentIsNotBomMetadata()
    {
        FileEntry left = WriteBytes("left.txt", Encoding.UTF8.GetBytes("a\uFEFFb"));
        FileEntry right = WriteBytes("right.txt", Encoding.UTF8.GetBytes("ab"));

        Assert.False(Comparer().Compare(left, right).Equal);
    }

    [Fact]
    public void TextComparer_Utf8SplitAtDetectionBoundaryStillUsesUtf8()
    {
        FileEntry left = WriteBytes("utf8-boundary.txt", Encoding.UTF8.GetBytes("aaaяZ"));
        FileEntry right = WriteBytes(
            "utf16-boundary.txt",
            WithBom(new UnicodeEncoding(false, true), "aaaяZ"));
        var comparer = new TextFileComparer(
            new ComparisonOptions { Method = CompareMethod.Text },
            new LocalComparisonFileSystem(),
            byteBufferSize: 1,
            detectionPrefixSize: 4);

        Assert.True(comparer.Compare(left, right).Equal);
    }

    [Fact]
    public void TextComparer_BinaryFallsBackToStrictBytes()
    {
        FileEntry left = WriteBytes("left.bin", [0, 1, 2, 3]);
        FileEntry same = WriteBytes("same.bin", [0, 1, 2, 3]);
        FileEntry different = WriteBytes("different.bin", [0, 1, 2, 4]);
        TextFileComparer comparer = Comparer(new ComparisonOptions
        {
            Method = CompareMethod.Text,
            TextWhitespace = TextWhitespaceMode.IgnoreAll,
        });

        Assert.True(comparer.Compare(left, same).Equal);
        Assert.False(comparer.Compare(left, different).Equal);
    }

    [Fact]
    public void TextComparer_StateSurvivesOneByteChunks()
    {
        FileEntry left = WriteBytes("left.txt", Encoding.UTF8.GetBytes("я\r\nfoo\t   bar😀"));
        FileEntry right = WriteBytes("right.txt", Encoding.UTF8.GetBytes("я\nfoo bar😀"));
        var comparer = new TextFileComparer(
            new ComparisonOptions { Method = CompareMethod.Text },
            new LocalComparisonFileSystem(),
            byteBufferSize: 1,
            detectionPrefixSize: 128);

        Assert.True(comparer.Compare(left, right).Equal);
    }

    [Fact]
    public void TextHasher_IsConsistentWithComparerAndBinaryStaysRaw()
    {
        FileEntry left = WriteBytes("left.txt", WithBom(new UTF8Encoding(true), "a\r\nfoo\tbar"));
        FileEntry right = WriteBytes("right.txt", WithBom(new UnicodeEncoding(false, true), "a\nfoo bar"));
        FileEntry binary = WriteBytes("binary.bin", [0, 1, 2, 3]);
        var options = new ComparisonOptions
        {
            Method = CompareMethod.Text,
            TextBom = TextBomComparison.Exact,
        };
        var hasher = new TextContentHasher();

        TextContentHashResult leftHash = hasher.ComputeSha256(left, options);
        TextContentHashResult rightHash = hasher.ComputeSha256(right, options);
        TextContentHashResult binaryHash = hasher.ComputeSha256(binary, options);

        Assert.Equal(leftHash.Digest, rightHash.Digest);
        Assert.True(new TextFileComparer(options).Compare(left, right).Equal);
        Assert.True(binaryHash.IsBinary);
        Assert.Equal(new FileContentHasher().ComputeSha256(binary), binaryHash.Digest);
    }

    [Fact]
    public void TextHasher_BomExactChangesHashButIgnoreDoesNot()
    {
        FileEntry withBom = WriteBytes("bom.txt", WithBom(new UTF8Encoding(true), "abc"));
        FileEntry withoutBom = WriteBytes("plain.txt", Encoding.UTF8.GetBytes("abc"));
        var hasher = new TextContentHasher();

        var ignore = new ComparisonOptions { Method = CompareMethod.Text, TextBom = TextBomComparison.Ignore };
        var exact = new ComparisonOptions { Method = CompareMethod.Text, TextBom = TextBomComparison.Exact };

        Assert.Equal(hasher.ComputeSha256(withBom, ignore).Digest, hasher.ComputeSha256(withoutBom, ignore).Digest);
        Assert.NotEqual(hasher.ComputeSha256(withBom, exact).Digest, hasher.ComputeSha256(withoutBom, exact).Digest);
    }

    [Fact]
    public void Engines_RespectTextAndFileSetMatchSemantics()
    {
        WriteBytes("folder-left/same.txt", Encoding.UTF8.GetBytes("a\r\nb"));
        WriteBytes("folder-right/same.txt", Encoding.UTF8.GetBytes("a\nb"));
        var text = new ComparisonOptions { Method = CompareMethod.Text };

        CompareResult folder = new FolderStructureCompareEngine().Compare(
            new FolderScanRequest { RootPath = Path.Combine(_root, "folder-left") },
            new FolderScanRequest { RootPath = Path.Combine(_root, "folder-right") },
            text);
        Assert.Equal(CompareStatus.Equal, Assert.Single(folder.Rows).Status);

        WriteBytes("hash-left/same.txt", WithBom(new UTF8Encoding(true), "foo\r\nbar"));
        WriteBytes("hash-right/same.txt", Encoding.UTF8.GetBytes("foo\nbar"));
        CompareResult hashed = new FileSetCompareEngine().Compare(
            new FolderScanRequest { RootPath = Path.Combine(_root, "hash-left") },
            new FolderScanRequest { RootPath = Path.Combine(_root, "hash-right") },
            text with { FileSetMatchMode = FileSetMatchMode.FileNameAndContentHash });
        Assert.Equal(CompareStatus.Equal, Assert.Single(hashed.Rows).Status);

        CompareResult sized = new FileSetCompareEngine().Compare(
            new FolderScanRequest { RootPath = Path.Combine(_root, "folder-left") },
            new FolderScanRequest { RootPath = Path.Combine(_root, "folder-right") },
            text with { FileSetMatchMode = FileSetMatchMode.FileNameAndSize });
        Assert.Equal(2, sized.Rows.Count);
    }

    [Fact]
    public void FileComparerFactory_RejectsUnknownMethod()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FileComparerFactory.Create(new ComparisonOptions { Method = (CompareMethod)999 }));
    }

    private TextFileComparer Comparer(ComparisonOptions? options = null) =>
        new(options ?? new ComparisonOptions { Method = CompareMethod.Text });

    private FileEntry WriteBytes(string relativePath, byte[] bytes)
    {
        string path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        var info = new FileInfo(path);
        return new FileEntry
        {
            FullPath = path,
            RelativePath = info.Name,
            FileName = info.Name,
            Size = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc,
        };
    }

    private static byte[] WithBom(Encoding encoding, string text)
    {
        byte[] preamble = encoding.GetPreamble();
        byte[] content = encoding.GetBytes(text);
        byte[] result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }
}
