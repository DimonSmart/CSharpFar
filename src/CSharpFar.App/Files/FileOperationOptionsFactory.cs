using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Files;

internal static class FileOperationOptionsFactory
{
    public static FileOperationOptions Create(AppSettingsAlias settings)
    {
        CopyMode copyMode = ParseCopyMode(settings.FileOperations.CopyMode);
        ConflictDecisionMode conflictDecision = ParseConflictDecision(
            settings.FileOperations.ConflictDecision,
            ref copyMode);

        return new FileOperationOptions
        {
            CopyMode = copyMode,
            DefaultConflictDecision = conflictDecision,
            PreserveTimestamps = settings.FileOperations.PreserveTimestamps,
            PreserveAttributes = settings.FileOperations.PreserveAttributes,
            SecurityMode = ParseEnum(
                settings.FileOperations.SecurityMode,
                FileSecurityMode.Default),
            SymlinkMode = ParseEnum(
                settings.FileOperations.SymlinkMode,
                SymlinkCopyMode.CopyLink),
            UseRecycleBinForDelete = settings.FileOperations.UseRecycleBinForDelete,
        };
    }

    private static CopyMode ParseCopyMode(string? value) =>
        ParseEnum(value, CopyMode.Normal);

    private static ConflictDecisionMode ParseConflictDecision(string? value, ref CopyMode copyMode)
    {
        if (string.Equals(value, "Append", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "AppendAll", StringComparison.OrdinalIgnoreCase))
        {
            return ConflictDecisionMode.Ask;
        }

        if (string.Equals(value, "ResumeWithTailValidation", StringComparison.OrdinalIgnoreCase))
        {
            copyMode = CopyMode.Reliable;
            return ConflictDecisionMode.Ask;
        }

        return ParseEnum(value, ConflictDecisionMode.Ask);
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }
}
