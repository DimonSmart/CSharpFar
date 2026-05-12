using CSharpFar.App.FunctionKeys;

namespace CSharpFar.Tests;

public sealed class FunctionKeyBindingProviderTests
{
    [Fact]
    public void GetBindings_ReturnsExpectedCoreBindings()
    {
        var bindings = new DefaultFunctionKeyBindingProvider().GetBindings();

        AssertBinding(bindings, FunctionKeyCommandIds.Help,
            FunctionKeyLayer.Plain, ConsoleKey.F1, "Help");
        AssertBinding(bindings, FunctionKeyCommandIds.Quit,
            FunctionKeyLayer.Plain, ConsoleKey.F10, "Quit");
        AssertBinding(bindings, FunctionKeyCommandIds.Search,
            FunctionKeyLayer.Alt, ConsoleKey.F7, "Search");
        AssertBinding(bindings, FunctionKeyCommandIds.CommandHistory,
            FunctionKeyLayer.Alt, ConsoleKey.F8, "History");
        AssertBinding(bindings, FunctionKeyCommandIds.ToggleLeftPanel,
            FunctionKeyLayer.Control, ConsoleKey.F1, "LeftPn");
        AssertBinding(bindings, FunctionKeyCommandIds.ToggleRightPanel,
            FunctionKeyLayer.Control, ConsoleKey.F2, "RightPn");
        AssertBinding(bindings, FunctionKeyCommandIds.SortByName,
            FunctionKeyLayer.Control, ConsoleKey.F3, "SortNm");
        AssertBinding(bindings, FunctionKeyCommandIds.SortBySize,
            FunctionKeyLayer.Control, ConsoleKey.F6, "SortSz");
    }

    [Fact]
    public void GetBindings_MarksUnavailableRunnablePlainCommands()
    {
        var bindings = new DefaultFunctionKeyBindingProvider().GetBindings();
        string[] runnableWhenUnavailable =
        [
            FunctionKeyCommandIds.Edit,
            FunctionKeyCommandIds.Copy,
            FunctionKeyCommandIds.RenameOrMove,
            FunctionKeyCommandIds.CreateFolder,
            FunctionKeyCommandIds.Delete,
        ];

        foreach (string commandId in runnableWhenUnavailable)
            Assert.True(Binding(bindings, commandId).RunsWhenUnavailable);

        Assert.False(Binding(bindings, FunctionKeyCommandIds.View).RunsWhenUnavailable);
    }

    [Fact]
    public void GetBindings_DoesNotDuplicateChords()
    {
        var bindings = new DefaultFunctionKeyBindingProvider().GetBindings();

        var duplicateChords = bindings
            .GroupBy(binding => (binding.Layer, binding.Key))
            .Where(group => group.Count() > 1)
            .ToArray();

        Assert.Empty(duplicateChords);
    }

    private static void AssertBinding(
        IReadOnlyList<FunctionKeyBinding> bindings,
        string commandId,
        FunctionKeyLayer layer,
        ConsoleKey key,
        string label,
        bool runsWhenUnavailable = false)
    {
        var binding = Binding(bindings, commandId);

        Assert.Equal(layer, binding.Layer);
        Assert.Equal(key, binding.Key);
        Assert.Equal(label, binding.Label);
        Assert.Equal(runsWhenUnavailable, binding.RunsWhenUnavailable);
    }

    private static FunctionKeyBinding Binding(
        IReadOnlyList<FunctionKeyBinding> bindings,
        string commandId) =>
        bindings.Single(binding => binding.CommandId == commandId);
}
