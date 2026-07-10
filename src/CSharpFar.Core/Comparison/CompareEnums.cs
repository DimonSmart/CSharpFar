namespace CSharpFar.Core.Comparison;

public enum CompareMode
{
    FolderStructure,
    FileSet,
}

public enum CompareMethod
{
    Fast,
    Content,
    Text,
}

public enum FileSetMatchMode
{
    FileName,
    FileNameAndSize,
    FileNameAndContentHash,
}

public enum CompareStatus
{
    Equal,
    Different,
    LeftOnly,
    RightOnly,
    Ambiguous,
    Duplicate,
    Error,
}

public enum NameComparisonMode
{
    SystemDefault,
    CaseSensitive,
    CaseInsensitive,
}

public enum TimestampTolerance
{
    Exact,
    TwoSeconds,
    OneHour,
}
