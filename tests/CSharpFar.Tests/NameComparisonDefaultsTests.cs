using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class NameComparisonDefaultsTests
{
    [Theory]
    [InlineData(true, NameComparisonMode.CaseInsensitive)]
    [InlineData(false, NameComparisonMode.CaseSensitive)]
    public void Resolve_ReturnsExpectedConcreteMode(bool isWindows, NameComparisonMode expected)
    {
        Assert.Equal(expected, NameComparisonDefaults.Resolve(isWindows));
    }

    [Fact]
    public void ComparisonOptions_DefaultUsesPlatformDefault()
    {
        var options = new ComparisonOptions();

        Assert.Equal(NameComparisonDefaults.Current, options.NameComparison);
        Assert.Equal(OperatingSystem.IsWindows(), !options.IsNameComparisonCaseSensitive);
    }

    [Fact]
    public void CompareSettings_DefaultUsesPlatformDefault()
    {
        var settings = new AppSettings.CompareSettings();

        Assert.Equal(NameComparisonDefaults.Current.ToString(), settings.NameComparison);
    }
}
