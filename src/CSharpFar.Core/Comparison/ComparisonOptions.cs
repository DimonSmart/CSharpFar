namespace CSharpFar.Core.Comparison;

public sealed record ComparisonOptions
{
    public CompareMode Mode { get; init; } = CompareMode.FolderStructure;
    public CompareMethod Method { get; init; } = CompareMethod.Fast;
    public FileSetMatchMode FileSetMatchMode { get; init; } = FileSetMatchMode.FileName;
    public bool IncludeSubfolders { get; init; } = true;
    public int? MaxDepth { get; init; }
    public string IncludeMasks { get; init; } = "*";
    public string ExcludeMasks { get; init; } = string.Empty;
    public NameComparisonMode NameComparison { get; init; } = NameComparisonMode.SystemDefault;
    public bool SelectedItemsOnly { get; init; }

    public bool IsNameComparisonCaseSensitive =>
        NameComparison switch
        {
            NameComparisonMode.CaseSensitive => true,
            NameComparisonMode.CaseInsensitive => false,
            _ => !OperatingSystem.IsWindows(),
        };

    public TimeSpan TimestampToleranceValue =>
        TimestampTolerance switch
        {
            TimestampTolerance.TwoSeconds => TimeSpan.FromSeconds(2),
            TimestampTolerance.OneHour => TimeSpan.FromHours(1),
            _ => TimeSpan.Zero,
        };

    public TimestampTolerance TimestampTolerance { get; init; } = TimestampTolerance.Exact;
}
