using CSharpFar.Core.Services;

namespace CSharpFar.Tests;

public sealed class PanelPathSemanticsTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData("C:")]
    [InlineData(@"\\server\share")]
    public void WindowsRoot_IsRoot(string path)
    {
        var semantics = new WindowsPanelPathSemantics();

        Assert.True(semantics.IsRoot(path));
        Assert.Null(semantics.GetParentPath(path));
    }

    [Theory]
    [InlineData(@"C:\Root\Sub1", @"C:\Root", "Sub1")]
    [InlineData(@"C:\Root\", @"C:\", "Root")]
    [InlineData("C:/Root/Sub1", @"C:\Root", "Sub1")]
    [InlineData(@"\\server\share\folder", @"\\server\share", "folder")]
    public void WindowsPathSemantics_ReturnParentAndFileName(
        string path,
        string expectedParent,
        string expectedName)
    {
        var semantics = new WindowsPanelPathSemantics();

        Assert.Equal(expectedParent, semantics.GetParentPath(path));
        Assert.Equal(expectedName, semantics.GetFileName(path));
    }

    [Fact]
    public void UnixRoot_IsRoot()
    {
        var semantics = new UnixPanelPathSemantics();

        Assert.True(semantics.IsRoot("/"));
        Assert.Null(semantics.GetParentPath("/"));
    }

    [Theory]
    [InlineData("/home/user/project", "/home/user", "project")]
    [InlineData("/home/user", "/home", "user")]
    [InlineData("/home", "/", "home")]
    [InlineData("/tmp/test/", "/tmp", "test")]
    public void UnixPathSemantics_ReturnParentAndFileName(
        string path,
        string expectedParent,
        string expectedName)
    {
        var semantics = new UnixPanelPathSemantics();

        Assert.Equal(expectedParent, semantics.GetParentPath(path));
        Assert.Equal(expectedName, semantics.GetFileName(path));
    }
}
