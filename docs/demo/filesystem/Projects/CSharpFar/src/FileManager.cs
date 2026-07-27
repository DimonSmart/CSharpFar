namespace DemoFixture;

public sealed class FileManager
{
    public string CurrentPath { get; private set; } = "/";

    public void ChangeDirectory(string path)
    {
        CurrentPath = path;
    }
}
