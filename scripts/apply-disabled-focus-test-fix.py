from pathlib import Path

path = Path("tests/CSharpFar.Tests/UiLayerTargetRoutingTests.cs")
text = path.read_text(encoding="utf-8")
old = '''    public void FocusRequest_ForDisabledTargetIsRejected()
    {
        var enabled = new UiTargetId("enabled");
        var disabled = new UiTargetId("disabled");
        var layer = Layer(FocusFrame([new(enabled, 0), new(disabled, 1, IsEnabled: false)], enabled));
        layer.Result = (_, _) => UiInputResult.RequestFocus(disabled);
        var host = Host(layer);
        host.Render();

        Assert.Throws<InvalidOperationException>(() => host.DispatchInput(Key(ConsoleKey.A)));
        Assert.Equal(enabled, layer.FocusState.FocusedTarget);
    }
'''
new = '''    public void FocusRequest_ForDisabledTargetIsIgnoredOnNextCommit()
    {
        var enabled = new UiTargetId("enabled");
        var disabled = new UiTargetId("disabled");
        var layer = Layer(FocusFrame([new(enabled, 0), new(disabled, 1, IsEnabled: false)], enabled));
        layer.Result = (_, _) => UiInputResult.RequestFocus(disabled);
        var host = Host(layer);
        host.Render();

        host.DispatchInput(Key(ConsoleKey.A));
        Assert.Equal(enabled, layer.FocusState.FocusedTarget);

        host.Render();
        Assert.Equal(enabled, layer.FocusState.FocusedTarget);
    }
'''
if text.count(old) != 1:
    raise RuntimeError(f"Expected one disabled-focus test block, found {text.count(old)}")
path.write_text(text.replace(old, new), encoding="utf-8", newline="")
