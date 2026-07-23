using CSharpFar.App;
using CSharpFar.App.Input;
using CSharpFar.App.Rendering;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class ApplicationInputDispatcherTestExtensions
{
    public static ApplicationRuntimeRenderRequest Handle(
        this ApplicationInputDispatcher dispatcher,
        UiRoutedInput<ApplicationUiFrame> routed) =>
        dispatcher.Handle(new ApplicationUiInputPacket(routed));
}
