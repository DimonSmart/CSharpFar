namespace CSharpFar.Core.Abstractions;

/// <summary>
/// Explicit compatibility check for two instances with the same logical source ID.
/// Matching IDs alone never authorize replacing or reusing an existing live connection.
/// </summary>
public interface IReusableFilePanelSource
{
    bool CanReuse(IFilePanelSource candidate);
}
