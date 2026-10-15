namespace CSharpFar.Core.Abstractions;

/// <summary>Identifies a panel source whose connection is not saved and is released after its last use.</summary>
public interface ITemporaryFilePanelSource
{
    bool IsTemporary { get; }
}
