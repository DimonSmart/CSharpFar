namespace CSharpFar.Core.Comparison;

public static class NameComparisonDefaults
{
    public static NameComparisonMode Current => Resolve(OperatingSystem.IsWindows());

    internal static NameComparisonMode Resolve(bool isWindows) =>
        isWindows ? NameComparisonMode.CaseInsensitive : NameComparisonMode.CaseSensitive;
}
