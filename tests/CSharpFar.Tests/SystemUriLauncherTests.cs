using System.Diagnostics;
using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class SystemUriLauncherTests
{
    [Fact]
    public void OpenUsesSystemAssociationWithoutShellCommandConstruction()
    {
        ProcessStartInfo? captured = null;
        var launcher = new SystemUriLauncher(info =>
        {
            captured = info;
            return new Process();
        });

        launcher.Open(new Uri("https://example.com/docs?q=test#section"));

        Assert.NotNull(captured);
        Assert.Equal("https://example.com/docs?q=test#section", captured.FileName);
        Assert.True(captured.UseShellExecute);
        Assert.Empty(captured.ArgumentList);
    }

    [Fact]
    public void OpenRejectsRelativeUri()
    {
        var launcher = new SystemUriLauncher(_ => new Process());

        Assert.Throws<ArgumentException>(() => launcher.Open(new Uri("docs/readme.md", UriKind.Relative)));
    }
}
