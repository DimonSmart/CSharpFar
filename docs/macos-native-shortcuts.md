# Native macOS text shortcuts

The **CSharpFar.app** Edit menu supports Command+C (copy), Command+V (paste),
Command+X (cut), Command+A (select all), Command+Z (editor undo), and
Command+Shift+Z (editor redo). Undo/Redo are not supported in one-line fields.

Existing Ctrl shortcuts remain supported; Ctrl+X still navigates command
history, whereas Command+X cuts selected command-line text. The Far
function-key commands are unchanged.

When SwiftTerm has a text selection, Command+C copies terminal text;
otherwise CSharpFar copies its selected text. Copy/Cut without selection
do nothing. Password fields cannot be copied or cut.

While an external command owns the foreground PTY, CSharpFar-specific
Command shortcuts are not forwarded. SwiftTerm selection copy and
terminal paste still work.

**CLI mode:** External terminals decide which Command shortcuts are passed to
the application, so these native key equivalents are not guaranteed there.
