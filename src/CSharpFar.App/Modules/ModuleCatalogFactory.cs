using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;

namespace CSharpFar.App.Modules;

internal static class ModuleCatalogFactory
{
    public static NativeModuleCatalog Create(
        SftpModule? sftpModule,
        FtpModule? ftpModule,
        ModuleStartupInfo startupInfo)
    {
        var catalog = new NativeModuleCatalog();

        if (sftpModule is not null)
        {
            sftpModule.Initialize(startupInfo);
            catalog.AddMenuAction(
                new ModuleMenuProjection(SftpModuleIds.MenuActionId, "SFTP...", 'S'),
                sftpModule.OpenFromMenu);
            catalog.AddDiskMenuAction(
                new ModuleMenuProjection(SftpModuleIds.DiskActionId, "SFTP", 'S'),
                sftpModule.OpenFromDiskMenu);
            catalog.AddCommandPrefix("sftp", (commandLine, side) => sftpModule.OpenFromCommandLine(side, commandLine));
        }

        if (ftpModule is not null)
        {
            ftpModule.Initialize(startupInfo);
            catalog.AddMenuAction(
                new ModuleMenuProjection(FtpModuleIds.MenuActionId, "FTP/FTPS...", 'F'),
                ftpModule.OpenFromMenu);
            catalog.AddDiskMenuAction(
                new ModuleMenuProjection(FtpModuleIds.DiskActionId, "FTP/FTPS", 'F'),
                ftpModule.OpenFromDiskMenu);
            catalog.AddCommandPrefix("ftp", (commandLine, side) => ftpModule.OpenFromCommandLine(side, commandLine));
            catalog.AddCommandPrefix("ftps", (commandLine, side) => ftpModule.OpenFromCommandLine(side, commandLine));
        }

        return catalog;
    }
}
