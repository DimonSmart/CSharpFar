using System.Text;

namespace CSharpFar.Console.Ansi.Tests;

public sealed class UnixTerminalInputByteReaderTests
{
    [Fact]
    public void SinglePacket_ReturnsWithoutWaitingForQuietPeriod()
    {
        var source = new ScriptedSource("a");
        var reader = source.CreateReader();

        Assert.Equal((byte)'a', reader.ReadByte());
        Assert.Equal(1, source.ReadCount);
        Assert.Equal([-1, 0], source.PollTimeouts);
    }

    [Fact]
    public void SplitCsi_IsParsedAcrossNativeReads()
    {
        var source = new ScriptedSource("\x1b[", "13;5u");

        AnsiInputReadResult result = new AnsiInputParser().Read(source.CreateReader());

        Assert.Equal(Encoding.ASCII.GetBytes("\x1b[13;5u"), result.Bytes);
        Assert.DoesNotContain(100, source.PollTimeouts);
    }

    [Fact]
    public void SplitUtf8_IsDecodedAcrossNativeReads()
    {
        var source = new ScriptedSource([0xC3], [0xA9]);
        var reader = source.CreateReader();

        Assert.Equal('é', new AnsiInputParser().Read(reader).Key.KeyChar);
        Assert.DoesNotContain(100, source.PollTimeouts);
    }

    [Fact]
    public void ContinuousInput_IsDrainedInBoundedBatches()
    {
        var source = new ScriptedSource(Enumerable.Repeat("a", 30).ToArray());
        var reader = source.CreateReader();

        Assert.Equal((byte)'a', reader.ReadByte());
        Assert.Equal(8, source.ReadCount);
        Assert.DoesNotContain(100, source.PollTimeouts);
        Assert.All(source.PollTimeouts, timeout => Assert.True(timeout is -1 or 0));
    }

    [Fact]
    public void StandaloneEscape_RetainsParserTimeoutSemantics()
    {
        var source = new ScriptedSource("\x1b");
        var key = new AnsiInputParser().Read(source.CreateReader()).Key;

        Assert.Equal(ConsoleKey.Escape, key.Key);
        Assert.Contains(50, source.PollTimeouts);
    }

    [Fact]
    public void EmptyNonBlockingInput_ReturnsFalse()
    {
        var source = new ScriptedSource();
        Assert.False(source.CreateReader().TryReadByte(out _));
        Assert.Equal([0], source.PollTimeouts);
    }

    [Fact]
    public void EndOfStream_ThrowsRatherThanBusyLooping()
    {
        var reader = new UnixTerminalInputByteReader(_ => true, _ => 0);

        Assert.Throws<EndOfStreamException>(reader.ReadByte);
    }

    [Fact]
    public void InterruptedRead_RetriesWithoutDroppingInput()
    {
        var source = new ScriptedSource("z") { InterruptOnce = true };

        Assert.Equal((byte)'z', source.CreateReader().ReadByte());
        Assert.Equal(2, source.ReadCount);
    }

    [Fact]
    public void PollFailure_IsNotSwallowed()
    {
        var reader = new UnixTerminalInputByteReader(
            _ => throw new IOException("poll error"), _ => 0);

        Assert.Throws<IOException>(reader.ReadByte);
    }

    [Fact]
    public void ReadFailure_IsNotSwallowed()
    {
        var reader = new UnixTerminalInputByteReader(
            _ => true, _ => -5);

        Assert.Throws<InvalidOperationException>(reader.ReadByte);
    }

    private sealed class ScriptedSource
    {
        private readonly Queue<byte[]> _chunks;
        public List<int> PollTimeouts { get; } = [];
        public int ReadCount { get; private set; }
        public bool InterruptOnce { get; set; }

        public ScriptedSource(params string[] chunks)
            : this(chunks.Select(Encoding.UTF8.GetBytes).ToArray())
        {
        }

        public ScriptedSource(params byte[][] chunks) => _chunks = new(chunks);

        public UnixTerminalInputByteReader CreateReader() => new(Poll, Read);

        private bool Poll(int timeout)
        {
            PollTimeouts.Add(timeout);
            return _chunks.Count != 0;
        }

        private int Read(byte[] buffer)
        {
            ReadCount++;
            if (InterruptOnce)
            {
                InterruptOnce = false;
                return -4;
            }

            byte[] chunk = _chunks.Dequeue();
            chunk.CopyTo(buffer, 0);
            return chunk.Length;
        }
    }
}
