namespace CSharpFar.FileSystem;

/// <summary>Provider-neutral destination collision checking for transfer plans.</summary>
internal static class FileOperationPlanPreflight
{
    public static void ValidateDistinctTargets(
        IEnumerable<string> paths,
        Func<string, string> normalize,
        StringComparer comparer,
        Action<string> validate)
    {
        var targets = new HashSet<string>(comparer);
        foreach (string path in paths)
        {
            validate(path);
            if (!targets.Add(normalize(path)))
                throw new IOException($"Multiple source items resolve to the same destination: {path}");
        }
    }
}
