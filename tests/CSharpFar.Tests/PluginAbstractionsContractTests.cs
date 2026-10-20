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
}
