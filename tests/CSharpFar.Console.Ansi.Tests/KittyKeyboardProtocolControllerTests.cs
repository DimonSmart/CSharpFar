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
            "\x1b[?25u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);

        controller.Resume();

        KeyboardProtocolSnapshot snapshot = controller.Snapshot;
        Assert.Equal("supported", snapshot.SupportStatus);
        Assert.Equal(25, snapshot.RequestedFlags);
        Assert.Equal(25, snapshot.ConfirmedFlags);
        Assert.True(snapshot.IsActive);
        Assert.Equal("kitty", snapshot.Protocol);
        Assert.Equal("main", snapshot.ActiveScreen);
        Assert.Contains("\x1b[?u\x1b[c", controls);
        Assert.Contains("\x1b[>25u", controls);
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
        Assert.DoesNotContain("\x1b[>25u", controls);
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
            "\x1b[?25u");
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
            "\x1b[?25u",
            "\x1b[?25u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);
        controller.Resume();

        controls.Clear();
        controller.PrepareForScreenChange();
        controls.Add("ENTER_ALT_SCREEN");
        controller.CompleteScreenChange(alternate: true);

        int pop = controls.IndexOf("\x1b[<u");
        int screen = controls.IndexOf("ENTER_ALT_SCREEN");
        int push = controls.IndexOf("\x1b[>25u");
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
            "\x1b[?25u",
            "\x1b[?25u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);
        controller.Resume();

        controller.Suspend();
        controls.Clear();
        controller.CompleteScreenChange(alternate: true);

        Assert.DoesNotContain("\x1b[>25u", controls);
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
            "\x1b[?25u");
        var controls = new List<string>();
        using var controller = new KittyKeyboardProtocolController(input, controls.Add);
        controller.Resume();

        controls.Clear();
        controller.Suspend();
        controller.Suspend();
        controller.PrepareForScreenChange();

        Assert.Single(controls, value => value == "\x1b[<u");
    }

    private static ReplayAnsiInputByteReader Input(params string[] packets)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(string.Concat(packets));
        return new ReplayAnsiInputByteReader(
            new StreamAnsiInputByteReader(new MemoryStream(bytes), null));
    }
}
