namespace CSharpFar.Core.Abstractions;

/// <summary>
/// Optional identity for providers exposing a single physical mutable container.
/// Two panel sessions may have different SourceIds while targeting the same file.
/// This is not a substitute for OS-level identity checks and external-write locks.
/// </summary>
public interface IFileProviderPhysicalIdentity
{
    string PhysicalIdentity { get; }
}
