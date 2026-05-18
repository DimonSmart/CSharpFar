using FarNet;

namespace CSharpFar.Tests.Fixtures.OfficialFarNetModule;

[ModuleTool(
    Name = "OfficialToolTitle",
    Options = ModuleToolOptions.Panels,
    Resources = true,
    Id = "6e2e88d2-42ba-4828-b086-0c00d4f887db")]
public sealed class OfficialMessageInputTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e)
    {
        string folder = Manager.GetFolderPath(SpecialFolder.LocalData, create: true);
        string? input = Far.Api.Input("Official value", null, "Official input", "official-seed");
        Far.Api.Message((input ?? "canceled") + "|" + Path.GetFileName(folder), "Official tool", MessageOptions.Ok);
    }
}

[ModuleTool(
    Name = "Official disk",
    Options = ModuleToolOptions.Disk,
    Id = "1ceadce7-02d7-4470-8227-7b0d1894a6f4")]
public sealed class OfficialDiskTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e) =>
        Far.Api.Message(e.IsLeft ? "left" : "right", "Official disk", MessageOptions.Ok);
}

[ModuleTool(
    Name = "Official panel",
    Options = ModuleToolOptions.Panels,
    Id = "f37012dd-6d46-4de0-8991-2d7c55bc0ac7")]
public sealed class OfficialPanelTool : ModuleTool
{
    public override void Invoke(object sender, ModuleToolEventArgs e)
    {
        var panel = new OfficialPanel(new OfficialExplorer())
        {
            Title = "Official panel",
            SortMode = PanelSortMode.Name,
            ViewMode = PanelViewMode.Descriptions,
        };

        panel.Open();
    }
}

[ModuleCommand(
    Name = "Official command",
    Prefix = "ofn",
    Id = "3e023d14-ac66-46dd-a515-931ac61f6640")]
public sealed class OfficialCommand : ModuleCommand
{
    public override void Invoke(object sender, ModuleCommandEventArgs e) =>
        Far.Api.Message(e.Command, "Official command", MessageOptions.Ok);
}

[ModuleCommand(
    Name = "Official panel command",
    Prefix = "ofnp",
    Id = "f78558e7-cd7b-4ec8-8bd3-74c0997469ec")]
public sealed class OfficialPanelCommand : ModuleCommand
{
    public override void Invoke(object sender, ModuleCommandEventArgs e)
    {
        _ = e;
        var panel = new OfficialPanel(new OfficialExplorer())
        {
            Title = "Official panel",
        };
        panel.Open();
    }
}

[ModuleCommand(
    Name = "Official command parameters",
    Prefix = "ofncp",
    Id = "16ab73db-0a5f-4cd6-a9b6-c7dc60a29245")]
public sealed class OfficialCommandParametersCommand : ModuleCommand
{
    public override void Invoke(object sender, ModuleCommandEventArgs e) =>
        InvokeSubcommand(e.Command, CreateSubcommand);

    private static Subcommand? CreateSubcommand(ReadOnlySpan<char> name, CommandParameters parameters)
    {
        if (!name.SequenceEqual("parse"))
            return null;

        string file = parameters.GetRequiredPath("file", ParameterOptions.None);
        bool flag = parameters.GetBool("flag");
        return new OfficialParsedSubcommand(file, flag);
    }

    private sealed class OfficialParsedSubcommand(string file, bool flag) : Subcommand
    {
        public override void Invoke() =>
            Far.Api.Message(Path.GetFileName(file) + "|" + flag, "Official command parameters", MessageOptions.Ok);
    }
}

public sealed class OfficialPanel(OfficialExplorer explorer) : Panel(explorer)
{
}

public sealed class OfficialExplorer() : Explorer(Guid.Parse("f87c3a82-60e3-40bf-865e-9a4b58b5c321"))
{
    public override IEnumerable<FarFile> GetFiles(GetFilesEventArgs args) =>
    [
        new SetFile
        {
            Name = "alpha.txt",
            Description = "Alpha description",
            Length = 5,
            LastWriteTime = new DateTime(2025, 1, 2, 3, 4, 5),
        },
    ];
}
