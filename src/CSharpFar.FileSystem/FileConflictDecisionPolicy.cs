using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>Operation-scoped decisions, shared by local and provider operations.</summary>
internal sealed class FileConflictDecisionPolicy
{
    private ConflictDecisionMode? _remembered;

    public ConflictDecisionMode Current(ConflictDecisionMode configured) => _remembered ?? configured;

    public FileOperationConflictDecision Resolve(
        FileOperationOptions options,
        IFileOperationConflictResolver resolver,
        Func<FileOperationConflict> conflict,
        bool requiresExplicitDirectoryDecision = false)
    {
        ConflictDecisionMode configured = Current(options.DefaultConflictDecision);
        if (requiresExplicitDirectoryDecision &&
            configured is ConflictDecisionMode.Overwrite or ConflictDecisionMode.OverwriteAll)
            configured = ConflictDecisionMode.Ask;

        FileOperationConflictDecision decision = configured == ConflictDecisionMode.Ask
            ? resolver.Resolve(conflict())
            : FileOperationConflictDecision.FromMode(configured);

        return decision with { Mode = Remember(decision.Mode) };
    }

    private ConflictDecisionMode Remember(ConflictDecisionMode decision)
    {
        switch (decision)
        {
            case ConflictDecisionMode.OverwriteAll:
                _remembered = ConflictDecisionMode.Overwrite;
                return ConflictDecisionMode.Overwrite;
            case ConflictDecisionMode.SkipAll:
                _remembered = ConflictDecisionMode.Skip;
                return ConflictDecisionMode.Skip;
            case ConflictDecisionMode.RenameAll:
                _remembered = ConflictDecisionMode.Rename;
                return ConflictDecisionMode.Rename;
            default:
                return decision;
        }
    }
}
