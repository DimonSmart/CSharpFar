using System.Text;
using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class ViewerPresentationSessionBehaviorTests
{
    [Fact]
    public void RawModeDoesNotReadPresentationContext()
    {
        var reader = new CountingReader(Encoding.UTF8.GetBytes("first\nsecond\nthird\n"));
        var cache = new BlockCache(reader, blockSize: 16, capacity: 4);
        var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
        reader.Reset();
        var source = new ScannedLine(6, 13, "second");
        var session = new ViewerPresentationSession();

        PresentedLine result = session.Present(
            ViewerPresentationMode.Raw,
            "notes.md",
            scanner,
            [source],
            80).Single();

        Assert.Equal(source.Text, result.Text);
        Assert.Equal(0, reader.ReadCalls);
    }

    [Fact]
    public void AutoWithoutMatchingProviderDoesNotReadPresentationContext()
    {
        var reader = new CountingReader(Encoding.UTF8.GetBytes("first\nsecond\nthird\n"));
        var cache = new BlockCache(reader, blockSize: 16, capacity: 4);
        var scanner = LineScanner.CreateAsync(cache, reader).GetAwaiter().GetResult();
        reader.Reset();
        var source = new ScannedLine(6, 13, "second");
        var session = new ViewerPresentationSession();

        PresentedLine result = session.Present(
            ViewerPresentationMode.Auto,
            "notes.txt",
            scanner,
            [source],
            80).Single();

        Assert.Equal(source.Text, result.Text);
        Assert.Equal(0, reader.ReadCalls);
    }

    private sealed class CountingReader(byte[] content) : IFileByteReader
    {
        public long Length => content.LongLength;
        public int ReadCalls { get; private set; }

        public Task<int> ReadAsync(
            long offset,
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            if (offset < 0 || offset >= content.LongLength)
                return Task.FromResult(0);

            int count = (int)Math.Min(buffer.Length, content.LongLength - offset);
            content.AsMemory((int)offset, count).CopyTo(buffer);
            return Task.FromResult(count);
        }

        public void Reset() => ReadCalls = 0;
    }
}
