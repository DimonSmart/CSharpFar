using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TriStateCheckBoxLineTests
{
    [Fact]
    public void Space_TogglesIndeterminateToChecked()
    {
        var line = new TriStateCheckBoxLine("Read only", CheckState.Indeterminate);

        bool handled = line.TryHandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.True(handled);
        Assert.Equal(CheckState.Checked, line.Value);
    }

    [Fact]
    public void Space_TogglesCheckedToUnchecked()
    {
        var line = new TriStateCheckBoxLine("Read only", CheckState.Checked);

        line.TryHandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.Equal(CheckState.Unchecked, line.Value);
    }

    [Fact]
    public void DisabledLine_DoesNotHandleInputOrChangeValue()
    {
        var line = new TriStateCheckBoxLine("Read only", CheckState.Indeterminate, enabled: false);

        bool handled = line.TryHandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.False(handled);
        Assert.Equal(CheckState.Indeterminate, line.Value);
    }
}
