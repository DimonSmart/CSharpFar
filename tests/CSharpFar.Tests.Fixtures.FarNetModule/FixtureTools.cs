using CSharpFar.Tests.Fixtures.FarNetDependency;
using FarNet;

namespace CSharpFar.Tests.Fixtures.FarNetModule;

public sealed class FixtureHost : ModuleHost
{
    internal static FixtureHost? Instance { get; private set; }

    public FixtureHost()
    {
        Instance = this;
    }
}

[ModuleTool(
    Name = "Ask user",
    Options = ModuleToolOptions.Panels,
    Id = "e7ee66f9-a88f-4e69-bb20-1864c7015cbd")]
public sealed class MessageInputTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e)
    {
        string? input = Far.Api.Input("Value", null, "Ask user", "seed");
        Far.Api.Message(input ?? "canceled", "Ask user", MessageOptions.Ok);
    }
}

[ModuleTool(
    Name = "Disk tool",
    Options = ModuleToolOptions.Disk,
    Id = "2e2ee555-7153-4c4a-a73b-79b38b42c5d4")]
public sealed class DiskTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e) =>
        Far.Api.Message(e.IsLeft ? "left" : "right", "Disk tool", MessageOptions.Ok);
}

[ModuleTool(
    Name = "Unsupported",
    Options = ModuleToolOptions.Panels,
    Id = "ace47cd5-15b9-4316-a0e6-e0fcbbca8dfd")]
public sealed class UnsupportedApiTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e) =>
        Far.Api.CreateListMenu();
}

[ModuleTool(
    Name = "Help tool",
    Options = ModuleToolOptions.Panels,
    Id = "d8b970d9-85c0-4c8c-aec0-8f02705a8d6d")]
public sealed class HelpTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e) =>
        ShowHelpTopic("menu");
}

[ModuleTool(
    Name = "Full path",
    Options = ModuleToolOptions.Panels,
    Id = "72537710-df2a-4c79-9737-5697f74b8765")]
public sealed class FullPathTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e) =>
        Far.Api.Message(Far.Api.GetFullPath("relative.json"), "Full path", MessageOptions.Ok);
}

[ModuleTool(
    Name = "Host dependent",
    Options = ModuleToolOptions.Panels,
    Id = "540d3106-79a8-4871-b915-df8d548d42c3")]
public sealed class HostDependentTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e) =>
        Far.Api.Message(FixtureHost.Instance is null ? "missing" : "present", "Host dependent", MessageOptions.Ok);
}

[ModuleTool(
    Name = "Config only",
    Options = ModuleToolOptions.Config,
    Id = "cb9d77ba-6c79-46e0-9374-df1ba4c68fa1")]
public sealed class ConfigOnlyTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e)
    {
    }
}

[ModuleTool(
    Name = "Invalid id",
    Options = ModuleToolOptions.Panels,
    Id = "not-a-guid")]
public sealed class InvalidGuidTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e)
    {
    }
}

[ModuleTool(
    Name = "Duplicate one",
    Options = ModuleToolOptions.Panels,
    Id = "986c5308-7fa0-4bb6-bdcd-393317fe24ba")]
public sealed class DuplicateToolOne : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e)
    {
    }
}

[ModuleTool(
    Name = "Duplicate two",
    Options = ModuleToolOptions.Panels,
    Id = "986c5308-7fa0-4bb6-bdcd-393317fe24ba")]
public sealed class DuplicateToolTwo : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e)
    {
    }
}

[ModuleTool(
    Name = "Missing dependency",
    Options = ModuleToolOptions.Panels,
    Id = "27c9a86b-ed08-44d5-ad02-6436ded6aa71")]
public sealed class MissingDependencyTool : ModuleTool
{
    public MissingDependencyMarker Marker { get; } = new();

    public override void Invoke(object sender, ModuleToolEventArgs e)
    {
    }
}
