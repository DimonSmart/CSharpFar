using CSharpFar.Module.Abstractions;

namespace CSharpFar.FarNetHost;

public interface IFarNetPanelOperations
{
    ModuleActionResult OpenItem(string sourcePath);
    bool TryGetEditableText(string sourcePath, out string text, out string? error);
    ModuleActionResult SetEditedText(string sourcePath, string text);
    ModuleActionResult PressKey(string? sourcePath, int virtualKeyCode, bool shift, bool control, bool alt);
}
