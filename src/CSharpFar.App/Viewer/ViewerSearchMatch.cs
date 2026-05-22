namespace CSharpFar.App.Viewer;

internal sealed record ViewerSearchMatch(
    long TopByteOffset,
    long LineStartOffset,
    int CharacterIndex,
    int CharacterLength,
    long ByteOffset,
    int ByteLength,
    string MatchedText,
    bool IsHex);
