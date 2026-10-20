namespace CSharpFar.Testing;

/// <summary>Locates repository files from test output directories or the working directory.</summary>
public static class RepositoryPaths
{
    public static string FindRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CSharpFar.slnx")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "src", "CSharpFar.App")))
                    return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The CSharpFar repository root was not found.");
    }

    public static string FindFile(params string[] relativePath)
    {
        string file = Path.Combine([FindRoot(), .. relativePath]);
        if (!File.Exists(file))
            throw new FileNotFoundException($"Repository file was not found: {file}", file);
        return file;
    }
}
