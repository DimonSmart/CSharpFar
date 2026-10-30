using CSharpFar.App.History;
using CSharpFar.Core.Models;

if (args.Length != 2)
    throw new ArgumentException("Usage: HistorySmoke <directory> <writer-prefix|verify>");

string directory = args[0];
var commands = new JsonHistoryStore(Path.Combine(directory, "history.json"));
var fields = new JsonSingleLineTextHistoryStore(directory);
if (args[1] == "verify")
{
    var all = commands.GetCommandHistory();
    var values = fields.Load("Shared");
    if (all.Count != 50 || values.Count != 50 ||
        all.Select(item => item.Command).Distinct(StringComparer.Ordinal).Count() != 50 ||
        values.Distinct(StringComparer.Ordinal).Count() != 50)
        throw new InvalidDataException($"Expected 50 independent writes: commands={all.Count}, values={values.Count}.");
    Console.WriteLine("Cross-process history synchronization succeeded.");
    return;
}

for (int i = 0; i < 25; i++)
{
    string value = $"{args[1]}-{i}";
    commands.AddCommand(new CommandHistoryItem { Command = value, WorkingDirectory = directory });
    if (fields.Promote("Shared", value) is null)
        throw new IOException("Field-history persistence failed.");
    Thread.Sleep(2);
}
