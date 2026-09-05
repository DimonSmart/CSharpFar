using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class FormControlApiArchitectureTests
{
    [Fact]
    public void Ui_Project_DoesNotReferenceApplicationAssemblies()
    {
        string projectPath = FindRepositoryFile("src", "CSharpFar.Ui", "CSharpFar.Ui.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] projectReferences = project.Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => include is not null)
            .Cast<string>()
            .ToArray();

        Assert.DoesNotContain(projectReferences, reference =>
            reference.Contains("CSharpFar.Core", StringComparison.Ordinal) ||
            reference.Contains("CSharpFar.App", StringComparison.Ordinal) ||
            reference.Contains("CSharpFar.Platform", StringComparison.Ordinal) ||
            reference.Contains("CSharpFar.Module", StringComparison.Ordinal));

        string[] assemblyReferences = typeof(FormControls).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();
        Assert.DoesNotContain(assemblyReferences, reference =>
            reference.StartsWith("CSharpFar.Core", StringComparison.Ordinal) ||
            reference.StartsWith("CSharpFar.App", StringComparison.Ordinal) ||
            reference.StartsWith("CSharpFar.Platform", StringComparison.Ordinal) ||
            reference.StartsWith("CSharpFar.Module", StringComparison.Ordinal));
    }

    [Fact]
    public void Ui_DoesNotGrantApplicationFriendAccess()
    {
        string[] friends = typeof(FormControls).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0])
            .ToArray();

        Assert.DoesNotContain("CSharpFar.App", friends);
    }

    [Fact]
    public void ImplementationTypes_AreNotPublic()
    {
        Type[] implementationTypes =
        [
            typeof(CheckBoxLine),
            typeof(TriStateCheckBoxLine),
            typeof(ChoiceModel<>),
            typeof(ChoiceSelection<>),
            typeof(DropdownSelect<>),
            typeof(DropdownInputResultKind),
            typeof(DropdownInputResult),
            typeof(DropdownSelectPopupFrame),
            typeof(DropdownSelectStateSnapshot),
            typeof(DropdownSelectFrame),
            typeof(DialogButtonBar),
            typeof(DialogButtonBarLayout),
            typeof(DialogButtonBarStyle),
            typeof(DialogButtonBarState),
            typeof(DialogButtonBarInputResult),
        ];

        Assert.All(implementationTypes, type => Assert.False(type.IsPublic, $"{type} must remain internal."));
    }

    [Fact]
    public void StandardFormRows_DoNotHavePublicInstanceConstructors()
    {
        Type[] rowTypes =
        [
            typeof(CheckBoxRow),
            typeof(TriStateCheckBoxRow),
            typeof(ChoiceFormRow<>),
            typeof(CompactChoiceFormRow<>),
            typeof(MultiLineChoiceFormRow<>),
            typeof(DropdownSelectFormRow<>),
            typeof(ButtonRow),
            typeof(CheckBoxColumnsRow),
            typeof(TriStateCheckBoxColumnsRow),
        ];

        Assert.All(rowTypes, type => Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)));
    }

    [Fact]
    public void FormControlFactories_ExposeOnlySemanticPublicSignatures()
    {
        Type[] implementationTypes =
        [
            typeof(CheckBoxLine), typeof(TriStateCheckBoxLine), typeof(ChoiceModel<>), typeof(ChoiceSelection<>),
            typeof(DropdownSelect<>), typeof(DropdownInputResult), typeof(DropdownSelectFrame),
            typeof(DialogButtonBar), typeof(DialogButtonBarStyle),
        ];
        string[] rawParameterNames = ["columnGap", "labelWidth", "startIndex", "endIndex"];

        MethodInfo[] methods = typeof(FormControls).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.All(methods, method =>
        {
            Assert.DoesNotContain(method.GetParameters(), parameter => rawParameterNames.Contains(parameter.Name, StringComparer.Ordinal));
            Assert.False(ContainsImplementationType(method.ReturnType, implementationTypes), $"{method} returns an implementation type.");
            Assert.DoesNotContain(method.GetParameters(), parameter =>
                ContainsImplementationType(parameter.ParameterType, implementationTypes));
        });
    }

    private static bool ContainsImplementationType(Type type, IReadOnlyList<Type> implementationTypes)
    {
        Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if (implementationTypes.Contains(definition))
            return true;

        return type.IsGenericType && type.GetGenericArguments().Any(argument => ContainsImplementationType(argument, implementationTypes));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("The repository UI project was not found.");
    }
}
