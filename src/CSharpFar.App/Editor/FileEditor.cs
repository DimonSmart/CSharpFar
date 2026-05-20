using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

/// <summary>
/// Full-screen text file editor backed by an editor session and document model.
/// </summary>
internal sealed class FileEditor
{
    private static string InternalClipboard = string.Empty;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly AppSettings.EditorSettings _settings;
    private readonly EditorFileService _fileService;
    private EditorFindDialogResult? _lastFind;
    private bool _markMode;

    public FileEditor(ScreenRenderer screen)
        : this(screen, null, null) { }

    public FileEditor(ScreenRenderer screen, ConsolePalette? palette)
        : this(screen, palette, null) { }

    public FileEditor(
        ScreenRenderer screen,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
        _settings = settings ?? new AppSettings.EditorSettings();
        _fileService = new EditorFileService(_settings);
    }

    public void Show(string filePath)
    {
        if (_fileService.RequiresSizeWarning(filePath) &&
            !new ConfirmDialog(_screen).Show(
                "Editor",
                $"File is larger than the editor warning limit ({_settings.FileSizeLimitBytes / 1024 / 1024} MB).",
                "Open anyway?"))
        {
            return;
        }

        EditorSession session;
        try
        {
            session = _fileService.Load(filePath);
        }
        catch (Exception ex)
        {
            new MessageDialog(_screen, _palette).Show("Editor", ex.Message);
            return;
        }

        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        try
        {
            session.RaiseOpened();
            RunLoop(session);
        }
        finally
        {
            session.RaiseClosed();
            _screen.Restore(saved);
        }
    }

    private void RunLoop(EditorSession session)
    {
        var functionKeyModifiers = default(ConsoleModifiers);
        while (true)
        {
            var size = _screen.GetSize();
            int contentHeight = Math.Max(1, size.Height - 3);
            int contentWidth = Math.Max(1, size.Width - 1);

            EnsureCursorVisible(session, contentHeight, contentWidth);
            Draw(session, contentHeight, size, functionKeyModifiers);

            var input = _screen.ReadInput();
            if (input is ModifierKeyConsoleInputEvent modifierEvent)
            {
                functionKeyModifiers = modifierEvent.Modifiers;
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            functionKeyModifiers = key.Modifiers;
            session.RaiseInput(key);

            bool printable = key.KeyChar >= ' ' &&
                (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
            if (printable)
            {
                string text = key.KeyChar == '\t' && _settings.ExpandTabs
                    ? new string(' ', EditorSettingsResolver.ResolveTabSize(_settings))
                    : key.KeyChar.ToString();
                session.InsertText(text);
                continue;
            }

            if (HandleKey(session, key, contentHeight))
                continue;

            if (key.Key is ConsoleKey.Escape or ConsoleKey.F10)
            {
                if (TryExit(session))
                    return;
            }
        }
    }

    private bool HandleKey(EditorSession session, ConsoleKeyInfo key, int contentHeight)
    {
        bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        bool control = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool extendSelection = shift || _markMode;

        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                session.DeleteBack();
                return true;
            case ConsoleKey.Delete:
                session.DeleteForward();
                return true;
            case ConsoleKey.Enter:
                session.BreakLine();
                return true;
            case ConsoleKey.LeftArrow when control:
                session.MoveWordLeft(extendSelection);
                return true;
            case ConsoleKey.RightArrow when control:
                session.MoveWordRight(extendSelection);
                return true;
            case ConsoleKey.LeftArrow:
                session.MoveLeft(extendSelection);
                return true;
            case ConsoleKey.RightArrow:
                session.MoveRight(extendSelection);
                return true;
            case ConsoleKey.UpArrow:
                session.MoveUp(extendSelection: extendSelection);
                return true;
            case ConsoleKey.DownArrow:
                session.MoveDown(extendSelection: extendSelection);
                return true;
            case ConsoleKey.PageUp:
                session.MoveUp(contentHeight, extendSelection);
                return true;
            case ConsoleKey.PageDown:
                session.MoveDown(contentHeight, extendSelection);
                return true;
            case ConsoleKey.Home when control:
                session.MoveToDocumentStart(extendSelection);
                return true;
            case ConsoleKey.Home:
                session.MoveToLineStart(extendSelection);
                return true;
            case ConsoleKey.End when control:
                session.MoveToDocumentEnd(extendSelection);
                return true;
            case ConsoleKey.End:
                session.MoveToLineEnd(extendSelection);
                return true;
            case ConsoleKey.Z when control:
                session.Undo();
                return true;
            case ConsoleKey.Y when control:
                session.Redo();
                return true;
            case ConsoleKey.A when control:
                session.SelectAll();
                _markMode = false;
                return true;
            case ConsoleKey.C when control:
                InternalClipboard = session.CopySelection();
                return true;
            case ConsoleKey.X when control:
                InternalClipboard = session.CopySelection();
                session.CutSelection();
                return true;
            case ConsoleKey.V when control:
                session.PasteText(InternalClipboard);
                return true;
            case ConsoleKey.B when control:
                session.ToggleBookmark();
                return true;
            case ConsoleKey.N when control:
                session.MoveToNextBookmark();
                return true;
            case ConsoleKey.P when control:
                session.MoveToPreviousBookmark();
                return true;
            case ConsoleKey.F2 when shift:
                ShowFormatDialog(session);
                return true;
            case ConsoleKey.F2:
                SaveFile(session);
                return true;
            case ConsoleKey.F3:
                ToggleMarkMode(session, EditorSelectionMode.Linear);
                return true;
            case ConsoleKey.F4:
                ToggleMarkMode(session, EditorSelectionMode.Rectangular);
                return true;
            case ConsoleKey.F5:
                InternalClipboard = session.CopySelection();
                _markMode = false;
                return true;
            case ConsoleKey.F6:
                InternalClipboard = session.CopySelection();
                session.CutSelection();
                _markMode = false;
                return true;
            case ConsoleKey.F7 when control:
                ShowReplaceDialog(session);
                return true;
            case ConsoleKey.F7 when shift:
                RepeatFind(session, searchBackward: false);
                return true;
            case ConsoleKey.F7 when alt:
                RepeatFind(session, searchBackward: true);
                return true;
            case ConsoleKey.F7:
                ShowFindDialog(session);
                return true;
            case ConsoleKey.F8:
                session.DeleteForward();
                return true;
        }

        return false;
    }

    private void ToggleMarkMode(EditorSession session, EditorSelectionMode mode)
    {
        _markMode = !_markMode || session.Selection?.Mode != mode;
        if (_markMode)
            session.SetSelectionMode(mode);
    }

    private bool TryExit(EditorSession session)
    {
        if (!session.Document.IsDirty)
            return true;

        var choice = new SaveChangesDialog(_screen, _palette).Show(Path.GetFileName(session.FilePath));
        return choice switch
        {
            SaveChangesChoice.Save => SaveFile(session),
            SaveChangesChoice.Discard => true,
            _ => false,
        };
    }

    private bool SaveFile(EditorSession session)
    {
        try
        {
            _fileService.Save(session);
            return true;
        }
        catch (Exception ex)
        {
            new MessageDialog(_screen, _palette).Show("Save Error", ex.Message);
            return false;
        }
    }

    private void ShowFormatDialog(EditorSession session)
    {
        var selected = new EditorFormatDialog(_screen, _palette).Show(
            session.Document.Format,
            () => Draw(session, Math.Max(1, _screen.GetSize().Height - 3), _screen.GetSize(), default));
        if (selected is not null)
            session.Document.SetFormat(selected);
    }

    private void ShowFindDialog(EditorSession session)
    {
        var result = new EditorFindDialog(_screen, _palette).Show(_lastFind);
        if (result is null)
            return;

        _lastFind = result;
        FindAndSelect(session, result, searchBackward: false);
    }

    private void RepeatFind(EditorSession session, bool searchBackward)
    {
        if (_lastFind is null)
        {
            ShowFindDialog(session);
            return;
        }

        FindAndSelect(session, _lastFind, searchBackward);
    }

    private void FindAndSelect(
        EditorSession session,
        EditorFindDialogResult request,
        bool searchBackward)
    {
        MoveToFindStart(session, searchBackward);
        var match = session.Find(new EditorSearchOptions(
            request.Pattern,
            SearchBackward: searchBackward,
            CaseSensitive: request.CaseSensitive,
            WholeWords: request.WholeWords,
            UseRegex: false));
        if (match is null)
        {
            new MessageDialog(_screen, _palette).Show("Find", "Text not found.");
            return;
        }

        session.SelectRange(match.Value.Start, match.Value.End);
    }

    private static void MoveToFindStart(EditorSession session, bool searchBackward)
    {
        if (session.Selection is { IsEmpty: false, Mode: EditorSelectionMode.Linear } selection)
        {
            var (start, end) = selection.OrderedRange;
            session.MoveTo(searchBackward ? start : end);
            return;
        }

        if (!searchBackward)
            session.MoveRight();
    }

    private void ShowReplaceDialog(EditorSession session)
    {
        string? pattern = new InputDialog(_screen, _palette).Show("Replace", "Find", allowEmpty: false);
        if (pattern is null)
            return;

        string? replacement = new InputDialog(_screen, _palette).Show("Replace", "With", allowEmpty: true);
        if (replacement is null)
            return;

        int count;
        try
        {
            count = session.ReplaceAll(new EditorSearchOptions(pattern), replacement);
        }
        catch (ArgumentException ex)
        {
            new MessageDialog(_screen, _palette).Show("Replace", ex.Message);
            return;
        }

        if (count == 0)
            new MessageDialog(_screen, _palette).Show("Replace", "Text not found.");
    }

    private void Draw(
        EditorSession session,
        int contentHeight,
        ConsoleSize size,
        ConsoleModifiers functionKeyModifiers)
    {
        using var frame = _screen.BeginFrame();
        DrawHeader(session, size);
        DrawContent(session, contentHeight, size);
        DrawStatus(session, contentHeight + 1, size);
        DrawKeyBar(size, functionKeyModifiers);
        DrawCursor(session, contentHeight, size);
        session.RaiseRedraw(session.Viewport.TopLine, contentHeight);
    }

    private void DrawHeader(EditorSession session, ConsoleSize size)
    {
        string dirty = session.Document.IsDirty ? "*" : " ";
        string readOnly = session.ReadOnly ? " RO" : string.Empty;
        string left = $"{dirty} {session.FilePath}{readOnly}";
        string right = $" {session.Document.Format.EncodingDisplayName} {session.Document.Format.BomDisplayName} {session.Document.Format.LineEndingDisplayName} ";
        int leftWidth = Math.Max(0, size.Width - right.Length);
        _screen.Write(0, 0, Fit(left, leftWidth) + right, PaletteStyles.PathHeaderActive(_palette));
    }

    private void DrawContent(EditorSession session, int contentHeight, ConsoleSize size)
    {
        int textWidth = Math.Max(1, size.Width - 1);
        for (int row = 0; row < contentHeight; row++)
        {
            int lineIndex = session.Viewport.TopLine + row;
            if (lineIndex < session.Document.Buffer.LineCount)
                DrawTextLine(session, lineIndex, row + 1, textWidth);
            else
                _screen.Write(0, row + 1, new string(' ', textWidth), PaletteStyles.CommandLine(_palette));
        }

        new ScrollBarRenderer().RenderVerticalScrollbar(
            _screen,
            new Rect(size.Width - 1, 1, 1, contentHeight),
            new ScrollState
            {
                TotalItems = session.Document.Buffer.LineCount,
                ViewportItems = contentHeight,
                FirstVisibleIndex = session.Viewport.TopLine,
            },
            new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
            PaletteStyles.DialogBorder(_palette));
    }

    private void DrawStatus(EditorSession session, int y, ConsoleSize size)
    {
        string code = CurrentCharacterStatus(session);
        string status =
            $" Ln {session.Cursor.Line + 1} Col {session.Cursor.Column + 1}  {code}  Undo:{(session.UndoHistory.CanUndo ? "Y" : "N")} Redo:{(session.UndoHistory.CanRedo ? "Y" : "N")}";
        _screen.Write(0, y, Fit(status, size.Width), PaletteStyles.CommandLine(_palette));
    }

    private void DrawKeyBar(ConsoleSize size, ConsoleModifiers modifiers)
    {
        var items = EditorCommandBindings.ForModifiers(modifiers)
            .Select(binding => new FunctionKeyBarItem(binding.KeyNumber, binding.Label))
            .ToArray();
        new FunctionKeyBarRenderer(_screen, _palette).Render(size.Height - 1, size.Width, items);
    }

    private void DrawTextLine(EditorSession session, int lineIndex, int screenY, int width)
    {
        string line = session.Document.Buffer.GetLine(lineIndex);
        for (int screenX = 0; screenX < width; screenX++)
        {
            int visualColumn = session.Viewport.LeftColumn + screenX;
            int logicalColumn = LogicalColumnFromVisualColumn(line, visualColumn);
            char ch = CharacterAtVisualColumn(line, visualColumn);
            bool selected = IsSelected(session.Selection, lineIndex, logicalColumn);
            _screen.WriteChar(
                screenX,
                screenY,
                ch,
                selected ? EditorSelectionStyle() : PaletteStyles.CommandLine(_palette));
        }
    }

    private CellStyle EditorSelectionStyle() =>
        new(_palette.CommandLineBg, _palette.CommandLineFg);

    private void DrawCursor(EditorSession session, int contentHeight, ConsoleSize size)
    {
        int screenRow = 1 + (session.Cursor.Line - session.Viewport.TopLine);
        int screenCol = VisualColumn(session.Document.Buffer.GetLine(session.Cursor.Line), session.Cursor.Column)
            - session.Viewport.LeftColumn;
        if (screenRow >= 1 && screenRow <= contentHeight && screenCol >= 0 && screenCol < size.Width - 1)
        {
            _screen.SetCursorPosition(screenCol, screenRow);
            _screen.SetCursorVisible(true);
            return;
        }

        _screen.SetCursorVisible(false);
    }

    private void EnsureCursorVisible(EditorSession session, int contentHeight, int contentWidth)
    {
        if (session.Cursor.Line < session.Viewport.TopLine)
            session.Viewport.TopLine = session.Cursor.Line;
        else if (session.Cursor.Line >= session.Viewport.TopLine + contentHeight)
            session.Viewport.TopLine = session.Cursor.Line - contentHeight + 1;

        int visualColumn = VisualColumn(session.Document.Buffer.GetLine(session.Cursor.Line), session.Cursor.Column);
        if (visualColumn < session.Viewport.LeftColumn)
            session.Viewport.LeftColumn = visualColumn;
        else if (visualColumn >= session.Viewport.LeftColumn + contentWidth)
            session.Viewport.LeftColumn = visualColumn - contentWidth + 1;
    }

    private string FormatLine(string line, int scrollLeft, int width)
    {
        string expanded = ExpandTabs(line);
        if (scrollLeft >= expanded.Length)
            return new string(' ', width);
        string visible = expanded[scrollLeft..];
        return Fit(visible, width);
    }

    private string ExpandTabs(string line)
    {
        int tabSize = EditorSettingsResolver.ResolveTabSize(_settings);
        var builder = new System.Text.StringBuilder(line.Length);
        int column = 0;
        foreach (char ch in line)
        {
            if (ch == '\t')
            {
                int spaces = tabSize - column % tabSize;
                builder.Append(' ', spaces);
                column += spaces;
            }
            else
            {
                builder.Append(ch);
                column++;
            }
        }

        return builder.ToString();
    }

    private int VisualColumn(string line, int logicalColumn)
    {
        int tabSize = EditorSettingsResolver.ResolveTabSize(_settings);
        int visual = 0;
        int end = Math.Min(logicalColumn, line.Length);
        for (int index = 0; index < end; index++)
        {
            visual += line[index] == '\t'
                ? tabSize - visual % tabSize
                : 1;
        }

        return visual + Math.Max(0, logicalColumn - line.Length);
    }

    private int LogicalColumnFromVisualColumn(string line, int targetVisualColumn)
    {
        int tabSize = EditorSettingsResolver.ResolveTabSize(_settings);
        int visual = 0;
        for (int logical = 0; logical < line.Length; logical++)
        {
            int width = line[logical] == '\t'
                ? tabSize - visual % tabSize
                : 1;
            if (targetVisualColumn < visual + width)
                return logical;

            visual += width;
        }

        return line.Length + Math.Max(0, targetVisualColumn - visual);
    }

    private char CharacterAtVisualColumn(string line, int targetVisualColumn)
    {
        int tabSize = EditorSettingsResolver.ResolveTabSize(_settings);
        int visual = 0;
        foreach (char ch in line)
        {
            int width = ch == '\t'
                ? tabSize - visual % tabSize
                : 1;
            if (targetVisualColumn < visual + width)
                return ch == '\t' ? ' ' : ch;

            visual += width;
        }

        return ' ';
    }

    private static string CurrentCharacterStatus(EditorSession session)
    {
        string line = session.Document.Buffer.GetLine(session.Cursor.Line);
        if (session.Cursor.Column < line.Length)
        {
            char currentChar = line[session.Cursor.Column];
            return $"U+{(int)currentChar:X4}/{(int)currentChar}";
        }

        string? lineEnding = session.Document.Buffer.GetLineEnding(session.Cursor.Line);
        if (lineEnding is not null)
            return LineEndingStatus(lineEnding);

        return "EOF";
    }

    private static string LineEndingStatus(string lineEnding) =>
        lineEnding switch
        {
            "\r\n" => "CRLF",
            "\n" => "LF",
            "\r" => "CR",
            _ => "EOL",
        };

    private static bool IsSelected(EditorSelection? selection, int lineIndex, int logicalColumn)
    {
        if (selection is null || selection.IsEmpty)
            return false;

        if (selection.Mode == EditorSelectionMode.Rectangular)
        {
            int startLine = Math.Min(selection.Anchor.Line, selection.Active.Line);
            int endLine = Math.Max(selection.Anchor.Line, selection.Active.Line);
            int startColumn = Math.Min(selection.Anchor.Column, selection.Active.Column);
            int endColumn = Math.Max(selection.Anchor.Column, selection.Active.Column);
            return lineIndex >= startLine &&
                lineIndex <= endLine &&
                logicalColumn >= startColumn &&
                logicalColumn < endColumn;
        }

        var (start, end) = selection.OrderedRange;
        if (lineIndex < start.Line || lineIndex > end.Line)
            return false;
        if (start.Line == end.Line)
            return logicalColumn >= start.Column && logicalColumn < end.Column;
        if (lineIndex == start.Line)
            return logicalColumn >= start.Column;
        if (lineIndex == end.Line)
            return logicalColumn < end.Column;
        return true;
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
