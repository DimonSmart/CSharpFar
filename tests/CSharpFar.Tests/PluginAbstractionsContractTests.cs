using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Tests;

public sealed class PluginAbstractionsContractTests
{
    [Fact]
    public void PluginApplicationContext_ExposesRunMode()
    {
        var property = typeof(IPluginApplicationContext).GetProperty(nameof(IPluginApplicationContext.RunMode));

        Assert.NotNull(property);
        Assert.Equal(typeof(PluginRunMode), property.PropertyType);
    }

    [Fact]
    public void PluginRunMode_DefinesNormalAndDemo()
    {
        Assert.Contains(PluginRunMode.Normal, Enum.GetValues<PluginRunMode>());
        Assert.Contains(PluginRunMode.Demo, Enum.GetValues<PluginRunMode>());
    }
}
