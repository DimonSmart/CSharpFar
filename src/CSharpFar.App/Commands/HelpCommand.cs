using CSharpFar.App.State;
using CSharpFar.App.Viewer;
using CSharpFar.Ui;
using CSharpFar.App.FunctionKeys;
namespace CSharpFar.App.Commands;

internal sealed class HelpCommand(InteractiveSurfaceHost interactiveSurfaces, ApplicationState state) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Help;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        new HelpViewer(interactiveSurfaces, state.Palette).Show(HelpTopic.Main);
        return ApplicationCommandResult.Rendered();
    }
}
