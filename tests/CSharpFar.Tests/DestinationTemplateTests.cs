using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class DestinationTemplateTests
{
    private static readonly DateTime Modified = new(2026, 8, 12, 14, 35, 20);

    [Theory]
    [InlineData("photo.jpg", false, "{name}", "photo")]
    [InlineData("photo.jpg", false, "{ext}", ".jpg")]
    [InlineData("archive.tar.gz", false, "{name}", "archive.tar")]
    [InlineData("archive.tar.gz", false, "{ext}", ".gz")]
    [InlineData("readme", false, "{ext}", "")]
    [InlineData(".gitignore", false, "{name}{ext}", ".gitignore")]
    [InlineData("file.", false, "{name}{ext}", "file.")]
    [InlineData("Photos.2026", true, "{name}{ext}", "Photos.2026")]
    public void Evaluate_ExpandsNameAndExtension(string name, bool isDirectory, string template, string expected)
    {
        Assert.Equal(expected, DestinationTemplate.Parse(template).Evaluate(new DestinationTemplateContext(name, isDirectory, Modified)));
    }

    [Theory]
    [InlineData("{name}_OLD{ext}", "photo_OLD.jpg")]
    [InlineData("OLD_{name}{ext}", "OLD_photo.jpg")]
    [InlineData("{name}.backup{ext}", "photo.backup.jpg")]
    [InlineData("{name}{ext}.bak", "photo.jpg.bak")]
    [InlineData("{modified:yyyy}", "2026")]
    [InlineData("{modified:MM}", "08")]
    [InlineData("{modified:yyyy-MM-dd}", "2026-08-12")]
    [InlineData("{modified:yyyyMMdd-HHmmss}", "20260812-143520")]
    [InlineData("{{{name}}}", "{photo}")]
    public void Evaluate_ExpandsSupportedTokens(string template, string expected)
    {
        Assert.Equal(expected, DestinationTemplate.Parse(template).Evaluate(new DestinationTemplateContext("photo.jpg", false, Modified)));
    }

    [Theory]
    [InlineData("{name")]
    [InlineData("{name}}")]
    [InlineData("{unknown}")]
    [InlineData("{modified}")]
    [InlineData("{modified:}")]
    [InlineData("{modified:yyyy/MM/dd}")]
    [InlineData("{name}_*.txt")]
    public void ParseOrEvaluate_RejectsInvalidTemplate(string template)
    {
        Assert.Throws<IOException>(() => DestinationTemplate.Parse(template).Evaluate(new DestinationTemplateContext("photo.jpg", false, Modified)));
    }
}
