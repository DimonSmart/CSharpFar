using CSharpFar.App.FunctionKeys;

namespace CSharpFar.Tests;

public sealed class FunctionKeyBindingProviderTests
{
    private enum FunctionKeyCommandCategory
    {
        Global,
        FixedSide,
        PanelScoped,
        CommandLineHistory,
    }

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
        AssertBinding(bindings, FunctionKeyCommandIds.SortByName,
            FunctionKeyLayer.Control, ConsoleKey.F3, "SortNm");
        AssertBinding(bindings, FunctionKeyCommandIds.SortBySize,
            FunctionKeyLayer.Control, ConsoleKey.F6, "SortSz");
        AssertBinding(bindings, FunctionKeyCommandIds.Rename,
            FunctionKeyLayer.Shift, ConsoleKey.F6, "Rename", runsWhenUnavailable: true);
        Assert.DoesNotContain(bindings, binding => binding.Layer == FunctionKeyLayer.Control && binding.Key == ConsoleKey.F1);
        Assert.DoesNotContain(bindings, binding => binding.Layer == FunctionKeyLayer.Control && binding.Key == ConsoleKey.F2);
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
            FunctionKeyCommandIds.Rename,
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

    [Fact]
    public void GetBindings_EveryCommandHasExplicitExecutionCategory()
    {
        var bindings = new DefaultFunctionKeyBindingProvider().GetBindings();
        var categories = new Dictionary<string, FunctionKeyCommandCategory>
        {
            [FunctionKeyCommandIds.Help] = FunctionKeyCommandCategory.Global,
            [FunctionKeyCommandIds.UserMenu] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.View] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.Edit] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.OpenCreateFile] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.Copy] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.RenameOrMove] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.Rename] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.CreateFolder] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.Delete] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.TopMenu] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.Quit] = FunctionKeyCommandCategory.Global,
            [FunctionKeyCommandIds.LeftVolume] = FunctionKeyCommandCategory.FixedSide,
            [FunctionKeyCommandIds.RightVolume] = FunctionKeyCommandCategory.FixedSide,
            [FunctionKeyCommandIds.Search] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.CommandHistory] = FunctionKeyCommandCategory.CommandLineHistory,
            [FunctionKeyCommandIds.FileHistory] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.DirectoryHistory] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.SortByName] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.SortByExtension] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.SortByLastWriteTime] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.SortBySize] = FunctionKeyCommandCategory.PanelScoped,
            [FunctionKeyCommandIds.Attributes] = FunctionKeyCommandCategory.PanelScoped,
        };

        Assert.All(bindings, binding => Assert.True(
            categories.ContainsKey(binding.CommandId),
            $"Missing function-key execution category for {binding.CommandId}."));
        Assert.Empty(categories.Keys.Except(bindings.Select(binding => binding.CommandId)));

        string[] panelScoped =
        [
            FunctionKeyCommandIds.UserMenu,
            FunctionKeyCommandIds.View,
            FunctionKeyCommandIds.Edit,
            FunctionKeyCommandIds.OpenCreateFile,
            FunctionKeyCommandIds.Copy,
            FunctionKeyCommandIds.RenameOrMove,
            FunctionKeyCommandIds.Rename,
            FunctionKeyCommandIds.CreateFolder,
            FunctionKeyCommandIds.Delete,
            FunctionKeyCommandIds.TopMenu,
            FunctionKeyCommandIds.Search,
            FunctionKeyCommandIds.FileHistory,
            FunctionKeyCommandIds.DirectoryHistory,
            FunctionKeyCommandIds.SortByName,
            FunctionKeyCommandIds.SortByExtension,
            FunctionKeyCommandIds.SortByLastWriteTime,
            FunctionKeyCommandIds.SortBySize,
            FunctionKeyCommandIds.Attributes,
        ];
        Assert.All(panelScoped, commandId =>
            Assert.Equal(FunctionKeyCommandCategory.PanelScoped, categories[commandId]));
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
