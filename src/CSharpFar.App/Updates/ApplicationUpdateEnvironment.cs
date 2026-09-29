namespace CSharpFar.App.Updates;

internal interface IApplicationUpdateEnvironment
{
    bool IsMacOS { get; }

    string? ProcessPath { get; }

    string? PathValue { get; }

    char PathSeparator { get; }

    bool FileExists(string path);

    bool IsExecutableFile(string path);
}

internal sealed class DefaultApplicationUpdateEnvironment : IApplicationUpdateEnvironment
{
    public static DefaultApplicationUpdateEnvironment Instance { get; } = new();

    private DefaultApplicationUpdateEnvironment()
    {
    }

    public bool IsMacOS => OperatingSystem.IsMacOS();

    public string? ProcessPath => Environment.ProcessPath;

    public string? PathValue => Environment.GetEnvironmentVariable("PATH");

    public char PathSeparator => Path.PathSeparator;

    public bool FileExists(string path) => File.Exists(path);

    public bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            return HasAnyExecuteBit(File.GetUnixFileMode(path));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool HasAnyExecuteBit(UnixFileMode mode) =>
        (mode &
            (UnixFileMode.UserExecute |
             UnixFileMode.GroupExecute |
             UnixFileMode.OtherExecute)) != 0;
}
