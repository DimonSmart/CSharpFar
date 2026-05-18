namespace CSharpFar.FarNetHost;

public sealed record FarNetModuleMenuItem(
    Guid ActionId,
    string Text,
    char? HotKey);
