using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class UnixPermissionFormatterTests
{
    [Theory]
    [InlineData(0x1a4, "0644", "rw-r--r--")]
    [InlineData(0x1a6, "0646", "rw-r--rw-")]
    [InlineData(0x1ed, "0755", "rwxr-xr-x")]
    [InlineData(0x1ff, "0777", "rwxrwxrwx")]
    [InlineData(0x9ed, "4755", "rwsr-xr-x")]
    [InlineData(0x9a4, "4644", "rwSr--r--")]
    [InlineData(0x5ed, "2755", "rwxr-sr-x")]
    [InlineData(0x5a4, "2644", "rw-r-Sr--")]
    [InlineData(0x3ff, "1777", "rwxrwxrwt")]
    [InlineData(0x3b6, "1666", "rw-rw-rwT")]
    public void Formatter_ReturnsExpectedOctalAndSymbolicStrings(int value, string octal, string symbolic)
    {
        var permissions = (UnixPermissionBits)value;

        Assert.Equal(octal, UnixPermissionFormatter.ToOctalString(permissions));
        Assert.Equal(symbolic, UnixPermissionFormatter.ToSymbolicString(permissions));
        Assert.Equal(9, UnixPermissionFormatter.ToSymbolicString(permissions).Length);
    }

    [Fact]
    public void ToDisplayString_SeparatesOctalAndSymbolicWithTwoSpaces()
    {
        var permissions = (UnixPermissionBits)0x1a4;

        Assert.Equal("0644  rw-r--r--", UnixPermissionFormatter.ToDisplayString(permissions));
    }
}
