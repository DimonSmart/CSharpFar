using System.ComponentModel;
using System.Reflection;

namespace CSharpFar.Architecture.Tests;

public sealed class UiPublicBoundaryTests
{
    private static readonly Assembly UiAssembly = typeof(ConsolePalette).Assembly;

    [Fact]
    public void ExportedSurface_ExcludesProductAndFormRuntimeTypes()
    {
        string[] exported = UiAssembly.GetExportedTypes().Select(type => type.Name).ToArray();

        Assert.DoesNotContain("ModuleUiServices", exported);
        Assert.DoesNotContain("FarDialogStyles", exported);
        Assert.DoesNotContain("WarningDialogStyles", exported);
        Assert.DoesNotContain(exported, name => name.StartsWith("FormGrid", StringComparison.Ordinal));
        Assert.DoesNotContain(exported, name => name is "IFormCompositeSnapshot" or "FormCompositeFrameContext" or
            "FormCompositeFrame" or "FormCompositeOverlayFrame" or "FormCompositeTarget");
    }

    [Fact]
    public void ConsolePalette_ContainsNoCSharpFarProductRoles()
    {
        string[] forbiddenPrefixes = ["Panel", "NormalFile", "Directory", "FileUsage", "CursorActive", "FooterActive", "CommandLine", "Help"];
        string[] properties = typeof(ConsolePalette).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(properties, name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
        Assert.DoesNotContain("FarClassic", typeof(PaletteRegistry).GetProperties(BindingFlags.Static | BindingFlags.Public).Select(property => property.Name));
    }

    [Fact]
    public void PaletteRegistry_ExposesOnlyBuiltInPaletteObjects()
    {
        string[] properties = typeof(PaletteRegistry)
            .GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        MethodInfo[] methods = typeof(PaletteRegistry)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.Equal(new[] { "All", "Default" }, properties);
        Assert.Empty(methods);
    }

    [Fact]
    public void FormControlIdentityOverloads_AreDiscoverableAdvancedApi()
    {
        MethodInfo[] methods = typeof(FormControls)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);

        Assert.Contains(methods, method =>
            method.Name == nameof(FormControls.CheckBox) &&
            method.GetParameters() is { Length: >= 2 } parameters &&
            parameters[0].ParameterType == typeof(string) &&
            parameters[1].ParameterType == typeof(string));
        Assert.DoesNotContain(methods, method =>
            method.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never);
    }

    [Fact]
    public void AdvancedReusableContracts_RemainExported()
    {
        string[] required = ["UiCompositionHost", "UiLayer`1", "UiLayerInputPolicy", "IUiSurface", "IUiCanvas", "ScreenRenderer",
            "UiRenderContext", "UiInteractionFrame", "UiInputRouteContext", "UiInputResult", "ModalDialogHost", "ScrollableViewport",
            "ScrollableListState`1", "ScrollState", "MenuLayoutService"];
        string[] exported = UiAssembly.GetExportedTypes().Select(type => type.Name).ToArray();

        foreach (string typeName in required)
            Assert.Contains(typeName, exported);
    }
}
