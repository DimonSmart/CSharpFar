namespace CSharpFar.App.Bootstrap;

public enum ApplicationRunMode
{
    Normal,
    Demo,
}

public sealed record ApplicationRunOptions(
    ApplicationRunMode Mode,
    string? DemoRootPath = null)
{
    public static ApplicationRunOptions Normal { get; } = new(ApplicationRunMode.Normal);
}
