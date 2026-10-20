using CSharpFar.App;
using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Input;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed partial class ApplicationInputDispatcherTests
{
    [Fact]
    public void WorkspaceKeyboard_ModifierLifecycleRendersOnlyChangedPanelLayer()
    {
        var commandLine = new CommandLineState();
        ConsoleModifiers observed = default;
        int updates = 0;
        var router = KeyboardRouter(commandLine, setFunctionKeyLayer: modifiers =>
        {
            updates++;
            if (observed == modifiers)
                return false;
            observed = modifiers;
            return true;
        });
        var frame = Frame(commandLine);

        ApplicationInputHandlingResult first = Route(ConsoleModifiers.Control);
        Assert.True(first.Handled);
        Assert.True(first.ShouldRender);
        Assert.Equal(ApplicationRenderPart.FunctionKeyBar, first.RenderParts);
        Assert.False(first.ResumesHiddenInteraction);

        ApplicationInputHandlingResult same = Route(ConsoleModifiers.Control);
        Assert.False(same.ShouldRender);
        Assert.Equal(ApplicationRenderPart.None, same.RenderParts);

        ApplicationInputHandlingResult release = Route(default);
        Assert.True(release.ShouldRender);
        Assert.Equal(ApplicationRenderPart.FunctionKeyBar, release.RenderParts);
        Assert.Equal(3, updates);
        Assert.Equal(string.Empty, commandLine.Text);

        ApplicationInputHandlingResult Route(ConsoleModifiers modifiers) =>
            router.Handle(new UiRoutedInput<ApplicationUiFrame>(
                new ModifierKeyConsoleInputEvent(modifiers),
                frame,
                ApplicationTargetIds.WorkspaceKeyboard,
                UiInputRouteKind.KeyboardTarget));
    }

    [Fact]
    public void WorkspaceKeyboard_HiddenModifiersUpdateStateWithoutRenderingOrScrolling()
    {
        var commandLine = new CommandLineState();
        ConsoleModifiers observed = default;
        var router = KeyboardRouter(commandLine, setFunctionKeyLayer: modifiers =>
        {
            if (observed == modifiers)
                return false;
            observed = modifiers;
            return true;
        });
        var frame = Frame(commandLine) with { Mode = ApplicationWorkspaceMode.HiddenCommandLine };

        foreach (ConsoleModifiers modifier in new[]
            { ConsoleModifiers.Control, ConsoleModifiers.Alt, default(ConsoleModifiers) })
        {
            ApplicationInputHandlingResult result = router.Handle(new UiRoutedInput<ApplicationUiFrame>(
                new ModifierKeyConsoleInputEvent(modifier),
                frame,
                ApplicationTargetIds.WorkspaceKeyboard,
                UiInputRouteKind.KeyboardTarget));
            Assert.True(result.Handled);
            Assert.False(result.ShouldRender);
            Assert.Equal(ApplicationRenderPart.None, result.RenderParts);
            Assert.False(result.ResumesHiddenInteraction);
            Assert.Equal(modifier, observed);
        }

        Assert.Equal(string.Empty, commandLine.Text);
    }

    [Fact]
    public void WorkspaceKeyboard_UnroutedOrUnknownFrameModifiersNeverRequestRender()
    {
        var commandLine = new CommandLineState();
        int calls = 0;
        var router = KeyboardRouter(commandLine, setFunctionKeyLayer: _ =>
        {
            calls++;
            return true;
        });
        var frame = Frame(commandLine);

        var unrelated = router.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control),
            frame,
            ApplicationTargetIds.CommandLine,
            UiInputRouteKind.KeyboardTarget));
        Assert.False(unrelated.Handled);
        Assert.Equal(0, calls);

        var withoutFrame = router.Handle(new UiRoutedInput<ApplicationUiFrame>(
            new ModifierKeyConsoleInputEvent(ConsoleModifiers.Control),
            null!,
            ApplicationTargetIds.WorkspaceKeyboard,
            UiInputRouteKind.KeyboardTarget));
        Assert.True(withoutFrame.Handled);
        Assert.False(withoutFrame.ShouldRender);
        Assert.False(withoutFrame.ResumesHiddenInteraction);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void KeyboardRouting_RemovesCompatibilityAndLiveRoutingApis()
    {
        Assert.Null(typeof(KeyboardInputRouter).GetMethod("HandleModifier"));
        Assert.Null(typeof(KeyboardInputContext).GetProperty("IsPanelsMode"));
        Assert.Null(typeof(KeyboardInputContext).GetProperty("ActiveState"));
        Assert.Null(typeof(KeyboardInputContext).GetProperty("VisibleRows"));
        Assert.Null(typeof(KeyboardInputContext).GetProperty("VisibleRowsForSide"));
    }

}
