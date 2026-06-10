using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Files;

internal static class FileOperationOptionsFactory
{
    public static FileOperationOptions Create(AppSettingsAlias settings) =>
        new()
        {
            DefaultConflictDecision = ParseEnum(
                settings.FileOperations.ConflictDecision,
                ConflictDecisionMode.Ask),
            PreserveTimestamps = settings.FileOperations.PreserveTimestamps,
            PreserveAttributes = settings.FileOperations.PreserveAttributes,
            SecurityMode = ParseEnum(
                settings.FileOperations.SecurityMode,
                FileSecurityMode.Inherit),
            SymlinkMode = ParseEnum(
                settings.FileOperations.SymlinkMode,
                SymlinkCopyMode.CopyLink),
            UseRecycleBinForDelete = settings.FileOperations.UseRecycleBinForDelete,
        };

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }
}
