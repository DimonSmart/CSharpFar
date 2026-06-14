using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class TriStateCheckBoxLineTests
{
    [Fact]
    public void Space_TogglesIndeterminateToChecked()
    {
        var line = new TriStateCheckBoxLine("Read only", AttributeEditState.Indeterminate);

        bool handled = line.TryHandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.True(handled);
        Assert.Equal(AttributeEditState.Checked, line.Value);
    }

    [Fact]
    public void Space_TogglesCheckedToUnchecked()
    {
        var line = new TriStateCheckBoxLine("Read only", AttributeEditState.Checked);

        line.TryHandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.Equal(AttributeEditState.Unchecked, line.Value);
    }
}
