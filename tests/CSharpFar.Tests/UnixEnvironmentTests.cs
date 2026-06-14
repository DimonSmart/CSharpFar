using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class UnixEnvironmentTests
{
    [Fact]
    public void IsWsl_WhenDistroNameIsSet()
    {
        var environment = new UnixEnvironment(
            name => name == "WSL_DISTRO_NAME" ? "Ubuntu" : null,
            () => "Linux version");

        Assert.True(environment.IsWsl);
    }

    [Fact]
    public void IsWsl_WhenProcVersionContainsMicrosoft()
    {
        var environment = new UnixEnvironment(_ => null, () => "Linux version Microsoft");

        Assert.True(environment.IsWsl);
    }

    [Fact]
    public void IsWsl_ReturnsFalseForOrdinaryLinux()
    {
        var environment = new UnixEnvironment(_ => null, () => "Linux version generic");

        Assert.False(environment.IsWsl);
    }
}
