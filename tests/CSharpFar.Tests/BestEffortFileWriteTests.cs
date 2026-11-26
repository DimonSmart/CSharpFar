using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class BestEffortFileWriteTests
{
    [Fact]
    public async Task BasicProviderCreatesAndReplacesWithoutOptionalInterfaces()
    {
        var source = new BasicSource();
        Assert.Null(await SafeFileWriteService.WriteDocumentAsync(source, "/target", Encoding.UTF8.GetBytes("first")));
        Assert.False(source.LastOverwrite);
        Assert.Equal("first", source.ReadText("/target"));
        Assert.Null(await SafeFileWriteService.WriteDocumentAsync(source, "/target", Encoding.UTF8.GetBytes("second")));
        Assert.True(source.LastOverwrite);
        Assert.Equal("second", source.ReadText("/target"));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("size")]
    public async Task FailureNeverReportsSuccessOrDeletesExistingDestination(string fault)
    {
        var source = new BasicSource { Fault = fault };
        source.SeedFile("/target", "original");
        var error = await Assert.ThrowsAsync<IOException>(() =>
            SafeFileWriteService.WriteDocumentAsync(source, "/target", Encoding.UTF8.GetBytes("data")));
        Assert.Contains("previous destination may be damaged", error.Message);
        Assert.NotNull(source.GetItem("/target"));
        Assert.Equal(0, source.DeleteCalls);
    }

    [Fact]
    public async Task LostCloseReplyIsUnknownEvenIfBytesReachedDestination()
    {
        var source = new BasicSource { Fault = "close" };
        await Assert.ThrowsAsync<UnknownFilePublicationException>(() =>
            SafeFileWriteService.WriteDocumentAsync(source, "/target", Encoding.UTF8.GetBytes("data")));
        Assert.Equal("data", source.ReadText("/target"));
        Assert.Equal(0, source.DeleteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationRetainsUnownedPartialDestination(bool keep)
    {
        using var cts = new CancellationTokenSource();
        var source = new BasicSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SafeFileWriteService.WriteProviderAsync(source, "/target", null, 4, async stream =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("part"));
                cts.Cancel();
                return 4;
            }, keep, cts.Token));
        Assert.Equal("part", source.ReadText("/target"));
        Assert.Equal(0, source.DeleteCalls);
    }

    [Fact]
    public async Task ReadOnlyProviderIsRejectedBeforeOpeningStream()
    {
        var source = new BasicSource { ReadOnly = true };
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            SafeFileWriteService.WriteDocumentAsync(source, "/target", Encoding.UTF8.GetBytes("data")));
        Assert.Equal(0, source.OpenCalls);
    }

    [Fact]
    public async Task ExpectedByteCountMismatchIsFailure()
    {
        var source = new BasicSource();
        await Assert.ThrowsAsync<IOException>(() =>
            SafeFileWriteService.WriteProviderAsync(source, "/target", null, 8, async stream =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("data"));
                return 4;
            }, false, CancellationToken.None));
        Assert.Equal(0, source.DeleteCalls);
    }

    private sealed class BasicSource : MemoryFilePanelView, IFilePanelSource
    {
        public BasicSource() : base(new PanelSourceId("basic-write-test")) { }
        public string Fault { get; init; } = "";
        public bool ReadOnly { get; init; }
        public bool LastOverwrite { get; private set; }
        public int OpenCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        PanelProviderCapabilities IFilePanelSource.Capabilities => ReadOnly
            ? base.Capabilities & ~PanelProviderCapabilities.OpenWrite
            : base.Capabilities;

        public new Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastOverwrite = overwrite;
            OpenCalls++;
            if (Fault == "open" || (!overwrite && GetItem(path) is not null))
                throw new IOException("open rejected");
            return Task.FromResult<Stream>(new FaultStream(Fault, bytes =>
                SeedFile(path, Fault == "size" ? "x" : Encoding.UTF8.GetString(bytes))));
        }

        public new Task DeleteAsync(string path, bool recursive, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return base.DeleteAsync(path, recursive, cancellationToken);
        }
    }

    private sealed class FaultStream(string fault, Action<byte[]> publish) : MemoryStream
    {
        private bool _closed;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (fault == "write") throw new IOException("write failed");
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (fault == "flush") throw new IOException("flush failed");
            return base.FlushAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                publish(ToArray());
                if (fault == "close") throw new IOException("reply lost");
            }
            base.Dispose(disposing);
        }
    }
}
