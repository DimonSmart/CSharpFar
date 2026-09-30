namespace CSharpFar.Core.Comparison;

public static class FileComparerFactory
{
    public static IFileComparer Create(ComparisonOptions options, IComparisonFileSystem? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        fileSystem ??= new LocalComparisonFileSystem();

        return options.Method switch
        {
            CompareMethod.Fast => new FastFileComparer(options.TimestampToleranceValue),
            CompareMethod.Content => new ByteContentFileComparer(fileSystem),
            CompareMethod.Text => new TextFileComparer(options, fileSystem),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options), options.Method, "Unsupported comparison method."),
        };
    }
}
