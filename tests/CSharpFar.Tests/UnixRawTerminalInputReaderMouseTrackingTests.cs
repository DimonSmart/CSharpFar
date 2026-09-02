using CSharpFar.Console.Ansi;

namespace CSharpFar.Tests;

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

    private static UnixRawTerminalInputReader CreateReader(
        RecordingTerminalInputMode terminalMode,
        List<string> controls) =>
        new(
            new StreamAnsiInputByteReader(new MemoryStream(), null),
            () => new CSharpFar.Console.Models.ConsoleSize(80, 25),
            () => { },
            controls.Add,
            terminalMode);

    private sealed class RecordingTerminalInputMode : ITerminalInputMode
    {
        public int EnableCount { get; private set; }
        public int RestoreCount { get; private set; }

        public void EnableRawMode() => EnableCount++;

        public void RestoreOriginalMode() => RestoreCount++;

        public void Dispose()
        {
        }
    }
}
