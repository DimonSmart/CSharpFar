namespace CSharpFar.App.Bootstrap;

public enum ApplicationRunMode
{
    Normal,
    Demo,
}

public sealed record ApplicationRunOptions(
    ApplicationRunMode Mode,
    string? DemoRootPath = null,
    bool DiagnosticsEnabled = false)
{
    public static ApplicationRunOptions Normal { get; } = new(ApplicationRunMode.Normal);
}
