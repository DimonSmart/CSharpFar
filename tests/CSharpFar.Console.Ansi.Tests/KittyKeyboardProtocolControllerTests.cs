using System.Text;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi.Tests;

public sealed class KittyKeyboardProtocolControllerTests
{
    [Fact]
    public void Resume_SupportedTerminal_ActivatesProductionProfile()
    {
        var input = Input(
            "\x1b[?0u",
            "\x1b[?1;2c",
            "\x1b[?27u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);

        controller.Resume();

        KeyboardProtocolSnapshot snapshot = controller.Snapshot;
        Assert.Equal("supported", snapshot.SupportStatus);
        Assert.Equal(27, snapshot.RequestedFlags);
        Assert.Equal(27, snapshot.ConfirmedFlags);
        Assert.True(snapshot.IsActive);
        Assert.Equal("kitty", snapshot.Protocol);
        Assert.Equal(27, snapshot.ActiveFlags);
        Assert.True(snapshot.ReportsKeyEventTypes);
        Assert.True(snapshot.CanTrackStandaloneModifiers);
        Assert.Equal("main", snapshot.ActiveScreen);
        Assert.Contains("\x1b[?u\x1b[c", controls);
        Assert.Contains("\x1b[>27u", controls);
    }

    [Fact]
    public void Resume_PdaWithoutKeyboardResponse_FallsBackAsUnsupported()
    {
        var input = Input("\x1b[?1;2c");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);

        controller.Resume();

        KeyboardProtocolSnapshot snapshot = controller.Snapshot;
        Assert.Equal("unsupported", snapshot.SupportStatus);
        Assert.False(snapshot.IsActive);
        Assert.Equal("legacy-vt", snapshot.Protocol);
        Assert.DoesNotContain("\x1b[>27u", controls);
    }

    [Fact]
    public void Resume_NoConclusiveResponse_FallsBackAsUnknown()
    {
        var input = Input();
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);

        controller.Resume();

        KeyboardProtocolSnapshot snapshot = controller.Snapshot;
        Assert.Equal("unknown", snapshot.SupportStatus);
        Assert.False(snapshot.IsActive);
        Assert.Equal("negotiation timeout", snapshot.FallbackReason);
    }

    [Fact]
    public void Resume_PartialFlags_RestoresPreviousKeyboardState()
    {
        var input = Input(
            "\x1b[?0u",
            "\x1b[?1;2c",
            "\x1b[?9u",
            "\x1b[?9u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);

        controller.Resume();

        KeyboardProtocolSnapshot snapshot = controller.Snapshot;
        Assert.Equal("supported", snapshot.SupportStatus);
        Assert.Equal(9, snapshot.ConfirmedFlags);
        Assert.False(snapshot.IsActive);
        Assert.Equal("legacy-vt", snapshot.Protocol);
        Assert.Contains("\x1b[<u", controls);
        Assert.Contains("not confirmed", snapshot.FallbackReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_UnrelatedInputDuringNegotiation_IsReplayed()
    {
        var input = Input(
            "x",
            "\x1b[?0u",
            "\x1b[?1;2c",
            "\x1b[?27u");
        using var controller = new KittyKeyboardProtocolController(input, _ => { });

        controller.Resume();

        Assert.Equal((byte)'x', input.ReadByte());
    }

    [Fact]
    public void ScreenChange_PopsBeforeSwitchAndPushesAfterSwitch()
    {
        var input = Input(
            "\x1b[?0u",
            "\x1b[?1;2c",
            "\x1b[?27u",
            "\x1b[?27u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);
        controller.Resume();

        controls.Clear();
        controller.PrepareForScreenChange();
        controls.Add("ENTER_ALT_SCREEN");
        controller.CompleteScreenChange(alternate: true);

        int pop = controls.IndexOf("\x1b[<u");
        int screen = controls.IndexOf("ENTER_ALT_SCREEN");
        int push = controls.IndexOf("\x1b[>27u");
        Assert.True(pop >= 0);
        Assert.True(pop < screen);
        Assert.True(screen < push);
        Assert.True(controller.Snapshot.IsActive);
        Assert.Equal("alternate", controller.Snapshot.ActiveScreen);
    }

    [Fact]
    public void SuspendedScreenChange_DoesNotActivateNewScreenUntilResume()
    {
        var input = Input(
            "\x1b[?0u",
            "\x1b[?1;2c",
            "\x1b[?27u",
            "\x1b[?27u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);
        controller.Resume();

        controller.Suspend();
        controls.Clear();
        controller.CompleteScreenChange(alternate: true);

        Assert.DoesNotContain("\x1b[>27u", controls);
        Assert.False(controller.Snapshot.IsActive);

        controller.Resume();

        Assert.True(controller.Snapshot.IsActive);
        Assert.Equal("alternate", controller.Snapshot.ActiveScreen);
    }

    [Fact]
    public void RepeatedSuspendAndScreenPreparation_DoNotOverPop()
    {
        var input = Input(
            "\x1b[?0u",
            "\x1b[?1;2c",
            "\x1b[?27u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);
        controller.Resume();

        controls.Clear();
        controller.Suspend();
        controller.Suspend();
        controller.PrepareForScreenChange();

        Assert.Single(controls, value => value == "\x1b[<u");
    }


    [Fact]
    public void Resume_EventReportingNotConfirmed_FallsBackToBasicKitty()
    {
        var input = Input(
            "\x1b[?0u",
            "\x1b[?1;2c",
            "\x1b[?25u",
            "\x1b[?25u");
        var writes = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, writes.Add);

        controller.Resume();

        Assert.Equal(27, controller.Snapshot.RequestedFlags);
        Assert.Equal(25, controller.Snapshot.ConfirmedFlags);
        Assert.Equal(25, controller.Snapshot.ActiveFlags);
        Assert.True(controller.Snapshot.IsActive);
        Assert.False(controller.Snapshot.CanTrackStandaloneModifiers);
        Assert.Contains("not confirmed", controller.Snapshot.FallbackReason);
        Assert.Equal(1, writes.Count(w => w == "\x1b[>27u"));
        Assert.Equal(1, writes.Count(w => w == "\x1b[>25u"));
        Assert.Equal(1, writes.Count(w => w == "\x1b[<u"));
        Assert.True(writes.IndexOf("\x1b[<u") < writes.IndexOf("\x1b[>25u"));
    }

    [Fact]
    public void Resume_AdditionalConfirmedFlags_AcceptsFullProfile()
    {
        var input = Input("\x1b[?0u", "\x1b[?1;2c", "\x1b[?31u");
        using var controller = new KittyKeyboardProtocolController(input, _ => { });

        controller.Resume();

        Assert.Equal(31, controller.Snapshot.ConfirmedFlags);
        Assert.Equal(27, controller.Snapshot.ActiveFlags);
        Assert.True(controller.Snapshot.CanTrackStandaloneModifiers);
    }

    [Fact]
    public void Resume_PushWriteFailure_DoesNotPopOrAttemptFallback()
    {
        var input = Input("\x1b[?0u", "\x1b[?1;2c");
        var writes = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, sequence =>
        {
            writes.Add(sequence);
            if (sequence == "\x1b[>27u")
                throw new IOException("write failed");
        });

        controller.Resume();
        controller.Resume();

        Assert.False(controller.Snapshot.IsActive);
        Assert.DoesNotContain("\x1b[<u", writes);
        Assert.DoesNotContain("\x1b[>25u", writes);
        Assert.Equal(1, writes.Count(w => w == "\x1b[>27u"));
        Assert.Contains("stack state unknown", controller.Snapshot.FallbackReason);
    }

    [Fact]
    public void Resume_RepeatAndDispose_MaintainOneStackLevel()
    {
        var input = Input("\x1b[?0u", "\x1b[?1;2c", "\x1b[?27u");
        var writes = new List<string>();
        var controller = new KittyKeyboardProtocolController(input, writes.Add);
        controller.Resume();
        controller.Resume();

        Assert.Single(writes, w => w == "\x1b[>27u");

        controller.Dispose();
        controller.Dispose();

        Assert.Single(writes, w => w == "\x1b[<u");
    }

    private static ReplayAnsiInputByteReader Input(params string[] packets)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(string.Concat(packets));
        return new ReplayAnsiInputByteReader(
            new StreamAnsiInputByteReader(new MemoryStream(bytes), null));
    }
}
