using System.Reflection;
using System.Runtime.CompilerServices;
using CSharpFar.Console.Ansi;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class ReusableFriendAccessArchitectureTests
{
    [Fact]
    public void AnsiBackend_DoesNotGrantProductFriendAccess()
    {
        Assert.DoesNotContain(GetFriends(typeof(AnsiTerminalConsoleDriver).Assembly), friend =>
            string.Equals(friend, "csharpfar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ui_DoesNotGrantPerfHarnessFriendAccess()
    {
        Assert.DoesNotContain(GetFriends(typeof(FormControls).Assembly), friend =>
            string.Equals(friend, "CSharpFar.EditorPerfHarness", StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetFriends(Assembly assembly) =>
        assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0])
            .ToArray();
}
