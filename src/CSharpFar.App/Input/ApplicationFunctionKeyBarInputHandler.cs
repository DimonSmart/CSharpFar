using CSharpFar.App.Commands;
using CSharpFar.App.Rendering;

namespace CSharpFar.App.Input;

internal sealed class ApplicationFunctionKeyBarInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationFunctionKeyBarInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationFunctionKeyInteraction interaction)
    {
        return ApplicationInputHandlingResult.FromHandled(
            _context.ExecuteRegisteredCommand(interaction.Action.CommandId, ApplicationPanelCommandInvocationFactory.Create(interaction.Frame)));
    }
}
