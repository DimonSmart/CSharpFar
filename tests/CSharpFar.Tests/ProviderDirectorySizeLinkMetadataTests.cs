using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;

namespace CSharpFar.Tests;

public sealed class ProviderDirectorySizeLinkMetadataTests
{
    [Theory]
    [InlineData(false, false, FileAttributes.Normal)]
    [InlineData(true, false, FileAttributes.Directory)]
    [InlineData(false, true, FileAttributes.Normal | FileAttributes.ReparsePoint)]
    [InlineData(true, true, FileAttributes.Directory | FileAttributes.ReparsePoint)]
    public void Sftp_PreservesSymbolicLinkMetadata(
        bool isDirectory,
        bool isSymbolicLink,
        FileAttributes expected)
    {
        Assert.Equal(expected, SftpFilePanelSource.ToPanelAttributes(isDirectory, isSymbolicLink));
    }

    [Theory]
    [InlineData(false, false, FileAttributes.Normal)]
    [InlineData(true, false, FileAttributes.Directory)]
    [InlineData(false, true, FileAttributes.Normal | FileAttributes.ReparsePoint)]
    [InlineData(true, true, FileAttributes.Directory | FileAttributes.ReparsePoint)]
    public void Ftp_PreservesLinkMetadata(
        bool isDirectory,
        bool isSymbolicLink,
        FileAttributes expected)
    {
        Assert.Equal(expected, FtpFilePanelSource.ToPanelAttributes(isDirectory, isSymbolicLink));
    }
}
