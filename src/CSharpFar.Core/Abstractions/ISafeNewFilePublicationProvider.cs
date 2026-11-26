namespace CSharpFar.Core.Abstractions;

/// <summary>
/// Optional capability for exclusive staging creation and publication without replacing
/// any existing entry. Implementations must enforce both conditions at the provider,
/// not by a client-side existence check followed by an overwriting operation.
/// A successful publication consumes the staging file.
/// </summary>
public interface ISafeNewFilePublicationProvider
{
    Task<Stream> OpenStagingWriteAsync(string stagingPath, CancellationToken cancellationToken = default);
    Task PublishNewFileAsync(string stagingPath, string destinationPath, CancellationToken cancellationToken = default);
}
