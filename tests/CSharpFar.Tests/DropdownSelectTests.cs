using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class DropdownSelectTests
{
    [Theory]
    [InlineData(ConsoleKey.Enter)]
    [InlineData(ConsoleKey.F4)]
    public void ClosedDropdown_OpensFromKey(ConsoleKey key)
    {
        var dropdown = new DropdownSelect<int>([1, 2], static value => value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        DropdownSelectFrame frame = dropdown.CalculateFrame(new ConsoleSize(20, 10), new Rect(0, 0, 10, 1));

        bool handled = dropdown.TryHandleKey(Key(key), frame, out bool selected, out bool valueChanged);

        Assert.True(handled);
        Assert.True(dropdown.IsOpen);
        Assert.False(selected);
        Assert.False(valueChanged);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
