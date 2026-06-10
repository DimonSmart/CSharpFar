using CSharpFar.Core.Models;
using CSharpFar.FarNetHost;
using CSharpFar.Module.Abstractions;
using CSharpFar.Module.Ftp;
using CSharpFar.Module.Sftp;

namespace CSharpFar.App.Modules;

internal static class ModuleCatalogFactory
{
    public static NativeModuleCatalog Create(
        SftpModule? sftpModule,
        FtpModule? ftpModule,
        FarNetModuleHost? farNetModuleHost,
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

        if (farNetModuleHost is not null)
        {
            foreach (var item in farNetModuleHost.MenuItems)
            {
                catalog.AddMenuAction(
                    ToModuleMenuProjection(item),
                    _ => NativeModuleCatalog.FromFarNet(farNetModuleHost.OpenFromMenu(item.ActionId)));
            }

            foreach (var item in farNetModuleHost.DiskMenuItems)
            {
                catalog.AddDiskMenuAction(
                    ToModuleMenuProjection(item),
                    side => NativeModuleCatalog.FromFarNet(
                        farNetModuleHost.OpenFromDiskMenu(item.ActionId, side == PanelSide.Left)));
            }

            foreach (string prefix in farNetModuleHost.CommandPrefixes)
            {
                catalog.AddCommandPrefix(
                    prefix,
                    (commandLine, _) => NativeModuleCatalog.FromFarNet(
                        farNetModuleHost.OpenFromCommandLine(commandLine)));
            }
        }

        return catalog;
    }

    private static ModuleMenuProjection ToModuleMenuProjection(FarNetModuleMenuItem item) =>
        new(item.ActionId, item.Text, item.HotKey);
}
