# Controls registry

This registry records reusable UI controls and control-like surfaces in CSharpFar.
Before adding a new control, check this file and extend an existing control when its
responsibility already matches the requested behavior.

## Single-line text input

Owner: `src/CSharpFar.Ui/SingleLineTextInput.cs`

Purpose: reusable one-line editable text field backed by `CommandLineState`.

Functions:
- printable character insertion;
- Backspace/Delete;
- Left/Right/Home/End cursor movement;
- Ctrl+A select all;
- selected text rendering;
- masked rendering for password-like fields;
- cursor X calculation and visible text calculation.

Optional features:
- `SingleLineTextHistoryState` adds duplicate-free recency history;
- history dropdown arrow on the right edge;
- prefix-matching dropdown;
- Up/Down selection, Enter accept, Escape close;
- mouse click accepts a dropdown suggestion;
- mouse click/drag on the dropdown scrollbar scrolls suggestions;
- mouse click outside an open dropdown closes it;
- single-border dropdown with right-border scrollbar.

Known reuse points:
- `InputDialog`;
- `CreateFolderDialog`;
- `SearchDialog`;
- `FileOperationDialog`;
- `EditorFindDialog`;
- `FarNetInputDialog`;
- FTP and SFTP connection dialogs.

Current adoption:
- regular text fields in these dialogs are history-enabled by default;
- FTP/SFTP password fields intentionally stay masked text fields without history;
- histories are process-local and keyed by logical field name.

## Command line

Owner: `src/CSharpFar.App/Application.cs`, `src/CSharpFar.App/Rendering/CommandLineRenderer.cs`

Purpose: main Far-like command entry line.

Functions:
- command text editing through `CommandLineState`;
- command execution;
- Far-like shortcuts from spec 0027;
- Ctrl+A selects all command text when the command line has text; with an empty
  command line, it keeps the panel select-all behavior;
- hidden-panel command-line mode uses the same text state and Ctrl+A selects the
  command text;
- hidden-panel history browsing;
- visible-panel command history completion.

Related controls:
- `CommandHistoryCompletionRenderer` renders the current command completion popup.
- `IHistoryStore` stores command, file, and directory history.

## Command history completion popup

Owner: `src/CSharpFar.App/Rendering/CommandHistoryCompletionRenderer.cs`

Purpose: non-blocking command history suggestions above the command line.

Functions:
- prefix suggestions from command history;
- selected row highlight;
- up to 8 content rows;
- single border, no shadow;
- right-border scrollbar.

Prefer `SingleLineTextHistoryState` for new dialog fields. Keep this renderer for the
main command line unless the command-line behavior is intentionally refactored.

## Panel quick search input

Owner: `src/CSharpFar.App/Rendering/PanelQuickSearchRenderer.cs`, `Application.PanelQuickSearchState`

Purpose: transient active-panel filename prefix navigation started by Alt+letter.

Functions:
- framed `Search` overlay;
- prefix text display;
- first visible item matching in current panel order;
- Backspace, Escape, and close-then-continue handling for other keys.

This is navigation state, not a general text input field.

## Popup and frame rendering

Owners:
- `src/CSharpFar.Ui/PopupRenderer.cs`
- `src/CSharpFar.Ui/DialogFrameRenderer.cs`
- `src/CSharpFar.Ui/PopupRenderOptions.cs`

Purpose: shared framed popup/dialog rendering.

Functions:
- single or double border;
- optional title;
- optional shadow;
- optional right-border vertical scrollbar;
- content callback with calculated inner bounds.

Use these for new framed surfaces instead of drawing boxes directly.

## Scrollbar

Owners:
- `src/CSharpFar.Ui/ScrollBarRenderer.cs`
- `src/CSharpFar.Ui/ScrollBarMouseHandler.cs`
- `src/CSharpFar.Core/Models/ScrollState.cs`
- `src/CSharpFar.Core/Models/ScrollStateCalculator.cs`

Purpose: shared vertical scrollbar and scroll math.

Functions:
- right-border scrollbar rendering;
- thumb position from `TotalItems`, `ViewportItems`, and `FirstVisibleIndex`;
- selection visibility math;
- mouse interaction helpers.

## Dialog button bar

Owner: `src/CSharpFar.Ui/DialogButtonBar.cs`

Purpose: reusable dialog action buttons.

Functions:
- button layout and rendering;
- keyboard focus movement;
- Enter activation;
- mouse activation.

## List dialogs

Owners:
- `HistoryDialog`;
- `DirectoryHistoryDialog`;
- `FileHistoryDialog`;
- `DriveDialog`;
- `UserMenuDialog`;
- `EncodingSelectionDialog`.

Purpose: modal selection from a scrollable list.

Functions:
- cursor selection;
- Enter accept and Escape cancel;
- right-border scrollbar where implemented;
- mouse row selection in most list dialogs.

When adding another modal chooser, first check whether one of these can be generalized
without mixing unrelated domain behavior.

## Form dialogs

Owners:
- `SearchDialog`;
- `FileOperationDialog`;
- `SettingsDialog`;
- plugin connection dialogs.

Purpose: modal multi-control forms.

Functions:
- focus row management;
- text fields through `SingleLineTextInput`;
- checkboxes/radio-like option rows;
- buttons through `DialogButtonBar`;
- body scrolling in short consoles for core dialogs.

## Built-in text editor

Owner: `src/CSharpFar.App/Editor/FileEditor.cs`, `src/CSharpFar.App/Editor/EditorSession.cs`

Purpose: full-screen F4 text file editor.

Functions:
- document-backed text editing through `EditorSession`;
- printable character insertion, Backspace/Delete, Enter;
- cursor movement by character, word, line, page, and document;
- Shift or mark-mode selection;
- Ctrl+A select all;
- internal copy, cut, paste;
- find, repeat find, replace all;
- format selection and save.

## Panels

Owners:
- `PanelRenderer`;
- `BriefTwoColumnsPanelRenderer`;
- `PanelStatusRenderer`;
- `PanelTitleRenderer`;
- `PanelController`;
- `FilePanelState`.

Purpose: main file list surfaces.

Functions:
- full and brief view modes;
- cursor movement and scroll offset;
- selection;
- sorting;
- panel title/status rendering;
- automatic right-border scrollbar.

## Menus and dropdowns

Owners:
- `TopMenuController`;
- `MenuBarRenderer`;
- `DropdownMenuRenderer`;
- `MenuLayoutService`;
- `MenuHitTester`.

Purpose: top menu bar and dropdown command lists.

Functions:
- keyboard navigation;
- command execution requests;
- overflow scrolling;
- right-border scrollbar;
- mouse hit testing.

## Message, confirm, warning, and progress dialogs

Owners:
- `MessageDialog`;
- `ConfirmDialog`;
- `WarningDialogStyles`;
- `ProgressDialog`;
- `OperationCancelDialog`;
- `SearchProgressDialog`.

Purpose: modal status, confirmation, and progress surfaces.

Functions:
- framed message display;
- multi-line message wrapping;
- keyboard scrolling for messages taller than the dialog viewport;
- right-border scrollbar for scrollable messages;
- focused action buttons where needed;
- operation progress metrics;
- cancel/pause/resume interactions where implemented.
