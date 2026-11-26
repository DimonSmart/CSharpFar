using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.Files;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Panels;

namespace CSharpFar.App.Commands;

internal sealed class CalculateDirectorySizesCommand(PanelCommandTargetResolver targetResolver, PanelDirectorySizeCoordinator? directorySizes = null) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.CalculateDirectorySizes;

    public bool CanExecute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        return directorySizes?.CanCalculateAll(target.State) == true;
    }

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!PanelCommandTargetResolver.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        directorySizes?.CalculateAll(target.Side, target.State);
        return ApplicationCommandResult.Rendered();
    }
}
