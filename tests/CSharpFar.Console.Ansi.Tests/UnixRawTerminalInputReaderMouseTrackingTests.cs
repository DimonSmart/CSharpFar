using CSharpFar.Console.Ansi;

namespace CSharpFar.Console.Ansi.Tests;

public sealed class UnixRawTerminalInputReaderMouseTrackingTests
{
    private const string EnableMouseTracking = "\u001b[?1003h\u001b[?1006h";
    private const string DisableMouseTracking = "\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l";

    [Fact]
    public void SetMouseTrackingEnabled_TogglesMouseWithoutLeavingRawMode()
    {
        var terminalMode = new RecordingTerminalInputMode();
        var controls = new List<string>();
        using var reader = CreateReader(terminalMode, controls);

        Assert.True(reader.MouseTrackingEnabled);
        Assert.Equal(1, terminalMode.EnableCount);
        Assert.Equal(0, terminalMode.RestoreCount);

        reader.SetMouseTrackingEnabled(false);

        Assert.False(reader.MouseTrackingEnabled);
        Assert.Equal(1, terminalMode.EnableCount);
        Assert.Equal(0, terminalMode.RestoreCount);
        Assert.Equal(DisableMouseTracking, controls[^1]);

        reader.SetMouseTrackingEnabled(true);

        Assert.True(reader.MouseTrackingEnabled);
        Assert.Equal(1, terminalMode.EnableCount);
        Assert.Equal(0, terminalMode.RestoreCount);
        Assert.Equal(EnableMouseTracking, controls[^1]);
    }

    [Fact]
    public void RestoreInputMode_RespectsMouseTrackingRequest()
    {
        var terminalMode = new RecordingTerminalInputMode();
        var controls = new List<string>();
        using var reader = CreateReader(terminalMode, controls);

        reader.SetMouseTrackingEnabled(false);
        int controlCountAfterDisable = controls.Count;

        reader.SuspendInputMode();
        reader.RestoreInputMode();

        Assert.False(reader.MouseTrackingEnabled);
        Assert.Equal(2, terminalMode.EnableCount);
        Assert.Equal(1, terminalMode.RestoreCount);
        Assert.Equal(controlCountAfterDisable, controls.Count);

        reader.SetMouseTrackingEnabled(true);

        Assert.True(reader.MouseTrackingEnabled);
        Assert.Equal(EnableMouseTracking, controls[^1]);
    }

    [Fact]
    public void SuspendInputMode_DisableWriteFailureStillRestoresAndCanRecover()
    {
        var terminalMode = new RecordingTerminalInputMode();
        var controls = new List<string>();
        bool failDisable = true;
        using var reader = CreateReader(terminalMode, sequence =>
        {
            controls.Add(sequence);
            if (sequence == DisableMouseTracking && failDisable)
            {
                failDisable = false;
                throw new IOException("disable failed");
            }
        });

        Assert.Throws<IOException>(reader.SuspendInputMode);

        Assert.False(reader.MouseTrackingEnabled);
        Assert.Equal(1, terminalMode.RestoreCount);
        Assert.Equal(DisableMouseTracking, controls[^1]);

        reader.RestoreInputMode();

        Assert.True(reader.MouseTrackingEnabled);
        Assert.Equal(2, terminalMode.EnableCount);
        Assert.Equal(EnableMouseTracking, controls[^1]);
    }

    [Fact]
    public void EnableFailure_AttemptsComprehensiveDisableBeforeRestoringMode()
    {
        var terminalMode = new RecordingTerminalInputMode();
        var controls = new List<string>();

        Assert.Throws<IOException>(() => CreateReader(terminalMode, sequence =>
        {
            controls.Add(sequence);
            if (sequence == EnableMouseTracking)
                throw new IOException("enable failed");
        }));

        Assert.Equal([EnableMouseTracking, DisableMouseTracking], controls);
        Assert.Equal(1, terminalMode.RestoreCount);
        Assert.Equal(1, terminalMode.DisposeCount);
    }

    private static UnixRawTerminalInputReader CreateReader(
        RecordingTerminalInputMode terminalMode,
        List<string> controls) => CreateReader(terminalMode, controls.Add);

    private static UnixRawTerminalInputReader CreateReader(
        RecordingTerminalInputMode terminalMode,
        Action<string> writeControl) =>
        new(
            new StreamAnsiInputByteReader(new MemoryStream(), null),
            () => new CSharpFar.Console.Models.ConsoleSize(80, 25),
            () => { },
            writeControl,
            terminalMode);

    private sealed class RecordingTerminalInputMode : ITerminalInputMode
    {
        public int EnableCount { get; private set; }
        public int RestoreCount { get; private set; }
        public int DisposeCount { get; private set; }

        public void EnableRawMode() => EnableCount++;

        public void RestoreOriginalMode() => RestoreCount++;

        public void Dispose() => DisposeCount++;
    }
}
