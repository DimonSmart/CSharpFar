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
    public void AuditedAccidentalApis_AreNotPublic()
    {
        string[] exported = UiAssembly.GetExportedTypes().Select(type => type.Name).ToArray();
        Assert.DoesNotContain("ScrollStateCalculator", exported);

        PropertyInfo? id = typeof(TextField).GetProperty(
            "Id",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(id);
        Assert.False(id!.GetMethod!.IsPublic);

        MethodInfo[] publicTextMethods = typeof(FormFieldFactory)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(FormFieldFactory.Text))
            .ToArray();
        Assert.DoesNotContain(publicTextMethods, method =>
            method.GetParameters() is { Length: > 0 } parameters &&
            parameters[0].ParameterType == typeof(string));
    }

    [Fact]
    public void ConsumerBootstrap_ExposesSimpleAndAdvancedConstructors()
    {
        ConstructorInfo[] fieldConstructors = typeof(FormFieldFactory).GetConstructors();
        Assert.Contains(fieldConstructors, constructor => HasParameters(constructor));
        Assert.Contains(fieldConstructors, constructor => HasParameters(constructor, typeof(ITextFieldHistoryProvider)));

        ConstructorInfo[] dialogConstructors = typeof(DialogService).GetConstructors();
        Assert.Contains(dialogConstructors, constructor => HasParameters(constructor, typeof(UiCompositionHost), typeof(FormFieldFactory)));
        Assert.Contains(dialogConstructors, constructor => HasParameters(constructor, typeof(ModalDialogHost), typeof(FormFieldFactory)));
        Assert.DoesNotContain(dialogConstructors, constructor => HasParameters(constructor, typeof(UiCompositionHost)));
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
    public void SingleLineTextEditing_UsesReusablePublicStateName()
    {
        string[] exported = UiAssembly.GetExportedTypes().Select(type => type.Name).ToArray();

        Assert.Contains(nameof(SingleLineTextEditState), exported);
        Assert.DoesNotContain("CommandLineState", exported);
    }

    [Fact]
    public void StandardDialogConcreteTypes_HaveIntentionalVisibility()
    {
        string[] exported = UiAssembly.GetExportedTypes().Select(type => type.Name).ToArray();

        Assert.DoesNotContain("ConfirmDialog", exported);
        Assert.Contains(nameof(MessageDialog), exported);
        Assert.Contains(nameof(ChoiceDialog), exported);
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

    private static bool HasParameters(ConstructorInfo constructor, params Type[] parameterTypes) =>
        constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes);
}
