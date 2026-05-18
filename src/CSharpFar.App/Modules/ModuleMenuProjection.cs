namespace CSharpFar.App.Modules;

public sealed record ModuleMenuProjection(
    Guid ActionId,
    string Text,
    char? HotKey);
