using CSharpFar.Core.Highlighting;

namespace CSharpFar.Core.FileMasks;

public interface IFileMaskMatcher
{
    bool IsMatch(
        string maskExpression,
        string fileName,
        IReadOnlyDictionary<string, MaskGroup> groups);
}
