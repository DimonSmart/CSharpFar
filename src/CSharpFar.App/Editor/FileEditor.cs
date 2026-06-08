using System.Text;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

/// <summary>
/// Full-screen text file editor backed by an editor session and document model.
/// </summary>
internal sealed class FileEditor
{
    private const int CustomCursorBlinkIntervalMs = 500;
    private const int CustomCursorInputPollMs = 25;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly AppSettings.EditorSettings _settings;
    private readonly EditorFileService _fileService;
    private readonly ITextClipboard _clipboard;
    private readonly EditorFileNameInsertionContext? _fileNameInsertionContext;
    private readonly IEditorSyntaxHighlighter _syntaxHighlighter;
    private EditorFindDialogResult? _lastFind;
    private ScrollBarDragState? _scrollbarDrag;
    private EditorPosition? _mouseSelectionAnchor;
    private bool _markMode;
    private bool _persistentSelection;
    private bool _customCursorVisible = true;

    public FileEditor(ScreenRenderer screen)
        : this(screen, null, null) { }

    public FileEditor(ScreenRenderer screen, ConsolePalette? palette)
        : this(screen, palette, null) { }

    public FileEditor(
        ScreenRenderer screen,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings)
        : this(screen, palette, settings, null)
    {
    }

    internal FileEditor(
        ScreenRenderer screen,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings,
        ITextClipboard? clipboard)
        : this(screen, palette, settings, clipboard, null)
    {
    }

    internal FileEditor(
        ScreenRenderer screen,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings,
        ITextClipboard? clipboard,
        EditorFileNameInsertionContext? fileNameInsertionContext)
        : this(screen, palette, settings, clipboard, fileNameInsertionContext, null)
    {
    }

    internal FileEditor(
        ScreenRenderer screen,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings,
        ITextClipboard? clipboard,
        EditorFileNameInsertionContext? fileNameInsertionContext,
        IEditorSyntaxHighlighter? syntaxHighlighter)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
        _settings = settings ?? new AppSettings.EditorSettings();
        _fileService = new EditorFileService(_settings);
        _clipboard = clipboard ?? TextCopyTextClipboard.Instance;
        _fileNameInsertionContext = fileNameInsertionContext;
        _syntaxHighlighter = syntaxHighlighter ?? new TextMateEditorSyntaxHighlighter();
    }

    public void Show(string filePath) => Show(filePath, newFileFormat: null);

    public void ShowWithNewFileFormat(string filePath, EditorDocumentFormat newFileFormat) =>
        Show(filePath, newFileFormat);

    private void Show(string filePath, EditorDocumentFormat? newFileFormat)
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
            session = _fileService.Load(filePath, newFileFormat);
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
        _scrollbarDrag = null;
        _mouseSelectionAnchor = null;
        while (true)
        {
            var size = _screen.GetSize();
            int contentHeight = Math.Max(1, size.Height - 3);
            int contentWidth = Math.Max(1, size.Width - 1);

            EnsureCursorVisible(session, contentHeight, contentWidth);
            _customCursorVisible = true;
            Draw(session, contentHeight, size, functionKeyModifiers);

            var input = ReadInput(session, contentHeight, size, functionKeyModifiers);
            if (input is ModifierKeyConsoleInputEvent modifierEvent)
            {
                functionKeyModifiers = modifierEvent.Modifiers;
                continue;
            }

            if (TryHandleMouseWheel(input, session))
                continue;

            if (input is MouseConsoleInputEvent scrollbarMouse &&
                TryHandleScrollbarMouse(scrollbarMouse, session, contentHeight, size))
            {
                continue;
            }

            if (input is MouseConsoleInputEvent mouse &&
                TryGetFunctionKeyBarKey(mouse, size, functionKeyModifiers, out var mouseKey))
            {
                input = new KeyConsoleInputEvent(mouseKey);
            }

            if (input is MouseConsoleInputEvent textMouse &&
                TryHandleTextMouse(textMouse, session, contentHeight, size))
            {
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            _mouseSelectionAnchor = null;
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
                _persistentSelection = false;
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

    private static bool TryHandleMouseWheel(ConsoleInputEvent input, EditorSession session)
    {
        if (input is not MouseConsoleInputEvent { Kind: MouseEventKind.Wheel } mouse)
            return false;

        const int wheelLines = 3;
        if (mouse.Button == MouseButton.WheelUp)
        {
            session.MoveUp(wheelLines);
            return true;
        }

        if (mouse.Button == MouseButton.WheelDown)
        {
            session.MoveDown(wheelLines);
            return true;
        }

        return false;
    }

    private bool TryHandleTextMouse(
        MouseConsoleInputEvent mouse,
        EditorSession session,
        int contentHeight,
        ConsoleSize size)
    {
        if (mouse.Button != MouseButton.Left &&
            _mouseSelectionAnchor is null)
        {
            return false;
        }

        switch (mouse.Kind)
        {
            case MouseEventKind.DoubleClick:
                if (!TryGetTextMousePosition(mouse, session, contentHeight, size, clampToContent: false, out var doubleClickPosition))
                    return false;

                session.SelectWordAt(doubleClickPosition);
                _mouseSelectionAnchor = null;
                _markMode = false;
                _persistentSelection = false;
                return true;

            case MouseEventKind.Down:
                if (!TryGetTextMousePosition(mouse, session, contentHeight, size, clampToContent: false, out var clickPosition))
                    return false;

                session.MoveTo(clickPosition);
                _mouseSelectionAnchor = clickPosition;
                _markMode = false;
                _persistentSelection = false;
                return true;

            case MouseEventKind.Click:
                if (!TryGetTextMousePosition(mouse, session, contentHeight, size, clampToContent: false, out var singleClickPosition))
                    return false;

                session.MoveTo(singleClickPosition);
                _mouseSelectionAnchor = null;
                _markMode = false;
                _persistentSelection = false;
                return true;

            case MouseEventKind.Move when _mouseSelectionAnchor is not null:
                if (!TryGetTextMousePosition(mouse, session, contentHeight, size, clampToContent: true, out var movePosition))
                    return false;

                session.SelectRange(_mouseSelectionAnchor.Value, movePosition);
                _markMode = false;
                _persistentSelection = false;
                return true;

            case MouseEventKind.Up when _mouseSelectionAnchor is not null:
                if (TryGetTextMousePosition(mouse, session, contentHeight, size, clampToContent: true, out var upPosition))
                    session.SelectRange(_mouseSelectionAnchor.Value, upPosition);

                _mouseSelectionAnchor = null;
                _markMode = false;
                _persistentSelection = false;
                return true;
        }

        return false;
    }

    private bool TryGetTextMousePosition(
        MouseConsoleInputEvent mouse,
        EditorSession session,
        int contentHeight,
        ConsoleSize size,
        bool clampToContent,
        out EditorPosition position)
    {
        position = default;

        int contentWidth = Math.Max(1, size.Width - 1);
        int minY = 1;
        int maxY = contentHeight;
        int textX = mouse.X;
        int textY = mouse.Y;

        if (clampToContent)
        {
            textX = Math.Clamp(textX, 0, contentWidth - 1);
            textY = Math.Clamp(textY, minY, maxY);
        }
        else if (textX < 0 || textX >= contentWidth || textY < minY || textY > maxY)
        {
            return false;
        }

        int lineIndex = Math.Clamp(
            session.Viewport.TopLine + textY - 1,
            0,
            session.Document.Buffer.LineCount - 1);
        string line = session.Document.Buffer.GetLine(lineIndex);
        int visualColumn = session.Viewport.LeftColumn + textX;
        position = new EditorPosition(lineIndex, LogicalColumnFromVisualColumn(line, visualColumn));
        return true;
    }

    private bool TryHandleScrollbarMouse(
        MouseConsoleInputEvent mouse,
        EditorSession session,
        int contentHeight,
        ConsoleSize size)
    {
        int topLine = session.Viewport.TopLine;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                mouse,
                new Rect(size.Width - 1, 1, 1, contentHeight),
                session.Document.Buffer.LineCount,
                contentHeight,
                ref topLine,
                ref _scrollbarDrag))
        {
            return false;
        }

        MoveViewportTo(session, topLine, contentHeight);
        return true;
    }

    private ConsoleInputEvent ReadInput(
        EditorSession session,
        int contentHeight,
        ConsoleSize size,
        ConsoleModifiers functionKeyModifiers)
    {
        if (!UsesCustomCursor(session))
            return _screen.ReadInput();

        long nextBlink = Environment.TickCount64 + CustomCursorBlinkIntervalMs;
        while (true)
        {
            if (_screen.TryReadInput(out var input))
            {
                _customCursorVisible = true;
                return input;
            }

            long now = Environment.TickCount64;
            if (now >= nextBlink)
            {
                _customCursorVisible = !_customCursorVisible;
                Draw(session, contentHeight, size, functionKeyModifiers);
                nextBlink = now + CustomCursorBlinkIntervalMs;
            }

            Thread.Sleep(CustomCursorInputPollMs);
        }
    }

    private bool HandleKey(EditorSession session, ConsoleKeyInfo key, int contentHeight)
    {
        bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        bool control = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool extendSelection = shift || _markMode;
        bool preserveSelection = _persistentSelection && !extendSelection;

        if (TryHandleNumberedBookmarkKey(session, key, control, shift, alt))
            return true;

        switch (key.Key)
        {
            case ConsoleKey.Backspace when control:
                session.DeleteWordLeft();
                return true;
            case ConsoleKey.Delete when control:
                session.DeleteWordRight();
                return true;
            case ConsoleKey.Delete when shift:
                CopySelectionToClipboard(session);
                session.CutSelection();
                _markMode = false;
                _persistentSelection = false;
                return true;
            case ConsoleKey.Insert when control:
                CopySelectionToClipboard(session);
                return true;
            case ConsoleKey.Insert when shift:
                PasteClipboardText(session);
                return true;
            case ConsoleKey.Backspace:
                session.DeleteBack();
                return true;
            case ConsoleKey.Delete:
                session.DeleteForward();
                return true;
            case ConsoleKey.Enter when control && shift:
                InsertPassivePanelFileName(session);
                return true;
            case ConsoleKey.Enter when shift:
                InsertActivePanelFileName(session);
                return true;
            case ConsoleKey.Enter:
                session.BreakLine();
                return true;
            case ConsoleKey.LeftArrow when control:
                session.MoveWordLeft(extendSelection, preserveSelection);
                return true;
            case ConsoleKey.RightArrow when control:
                session.MoveWordRight(extendSelection, preserveSelection);
                return true;
            case ConsoleKey.UpArrow when control:
                ScrollViewport(session, -1, contentHeight);
                return true;
            case ConsoleKey.DownArrow when control:
                ScrollViewport(session, 1, contentHeight);
                return true;
            case ConsoleKey.LeftArrow:
                session.MoveLeft(extendSelection, preserveSelection);
                return true;
            case ConsoleKey.RightArrow:
                session.MoveRight(extendSelection, preserveSelection);
                return true;
            case ConsoleKey.UpArrow:
                session.MoveUp(extendSelection: extendSelection, preserveSelection: preserveSelection);
                return true;
            case ConsoleKey.DownArrow:
                session.MoveDown(extendSelection: extendSelection, preserveSelection: preserveSelection);
                return true;
            case ConsoleKey.PageUp:
                session.MoveUp(contentHeight, extendSelection, preserveSelection);
                return true;
            case ConsoleKey.PageDown:
                session.MoveDown(contentHeight, extendSelection, preserveSelection);
                return true;
            case ConsoleKey.Home when control:
                session.MoveToDocumentStart(extendSelection, preserveSelection);
                return true;
            case ConsoleKey.Home:
                session.MoveToLineStart(extendSelection, preserveSelection);
                return true;
            case ConsoleKey.End when control:
                session.MoveToDocumentEnd(extendSelection, preserveSelection);
                return true;
            case ConsoleKey.End:
                session.MoveToLineEnd(extendSelection, preserveSelection);
                return true;
            case ConsoleKey.Z when control && shift:
                session.Redo();
                return true;
            case ConsoleKey.Z when control:
                session.Undo();
                return true;
            case ConsoleKey.Y when control:
                session.DeleteCurrentLine();
                return true;
            case ConsoleKey.A when control:
                session.SelectAll();
                _markMode = false;
                _persistentSelection = false;
                return true;
            case ConsoleKey.U when control:
                session.ClearSelection();
                _markMode = false;
                _persistentSelection = false;
                return true;
            case ConsoleKey.C when control:
                CopySelectionToClipboard(session);
                return true;
            case ConsoleKey.X when control:
                CopySelectionToClipboard(session);
                session.CutSelection();
                _persistentSelection = false;
                return true;
            case ConsoleKey.V when control:
                PasteClipboardText(session);
                _persistentSelection = false;
                return true;
            case ConsoleKey.F when control:
                session.InsertText(session.FilePath, "Insert edited file path");
                return true;
            case ConsoleKey.K when control:
                session.DeleteToLineEnd();
                return true;
            case ConsoleKey.T when control:
                session.DeleteWordRight();
                return true;
            case ConsoleKey.D when control:
                session.DeleteSelection();
                _markMode = false;
                _persistentSelection = false;
                return true;
            case ConsoleKey.P when control:
                if (!session.CopySelectionToCursor())
                    new MessageDialog(_screen, _palette).Show("Editor", "Select text to copy block.");
                _markMode = false;
                _persistentSelection = false;
                return true;
            case ConsoleKey.M when control:
                if (!session.MoveSelectionToCursor())
                    new MessageDialog(_screen, _palette).Show("Editor", "Select text outside the cursor to move block.");
                _markMode = false;
                _persistentSelection = false;
                return true;
            case ConsoleKey.B when control:
                session.ToggleBookmark();
                return true;
            case ConsoleKey.N when control:
                session.MoveToNextBookmark();
                return true;
            case ConsoleKey.D when alt:
                session.DeleteToLineEnd();
                return true;
            case ConsoleKey.H when alt:
                session.ToggleSyntaxHighlighting();
                return true;
            case ConsoleKey.L when alt:
                ShowSyntaxLanguageDialog(session);
                return true;
            case ConsoleKey.T when alt:
                ShowSyntaxThemeDialog(session);
                return true;
            case ConsoleKey.U when alt:
                session.ShiftSelectedLinesLeftOrCurrentLine();
                return true;
            case ConsoleKey.I when alt:
                session.ShiftSelectedLinesRightOrCurrentLine();
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
                CopySelectionToClipboard(session);
                _markMode = false;
                return true;
            case ConsoleKey.F6:
                CopySelectionToClipboard(session);
                session.CutSelection();
                _markMode = false;
                _persistentSelection = false;
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
                session.DeleteSelectionOrCurrentLine();
                return true;
        }

        return false;
    }

    private void CopySelectionToClipboard(EditorSession session)
    {
        string selectedText = session.CopySelection();
        if (selectedText.Length > 0)
            _clipboard.TrySetText(ClipboardTextForOperatingSystem(selectedText));
    }

    private void PasteClipboardText(EditorSession session)
    {
        if (_clipboard.TryGetText(out string clipboardText) && clipboardText.Length > 0)
            session.PasteText(clipboardText);
    }

    private bool TryHandleNumberedBookmarkKey(
        EditorSession session,
        ConsoleKeyInfo key,
        bool control,
        bool shift,
        bool alt)
    {
        if (!control || alt || !TryGetNumberKey(key.Key, out int slot))
            return false;

        if (shift)
        {
            session.SetNumberedBookmark(slot);
            return true;
        }

        session.MoveToNumberedBookmark(slot);
        return true;
    }

    private static bool TryGetNumberKey(ConsoleKey key, out int number)
    {
        number = key switch
        {
            ConsoleKey.D0 or ConsoleKey.NumPad0 => 0,
            ConsoleKey.D1 or ConsoleKey.NumPad1 => 1,
            ConsoleKey.D2 or ConsoleKey.NumPad2 => 2,
            ConsoleKey.D3 or ConsoleKey.NumPad3 => 3,
            ConsoleKey.D4 or ConsoleKey.NumPad4 => 4,
            ConsoleKey.D5 or ConsoleKey.NumPad5 => 5,
            ConsoleKey.D6 or ConsoleKey.NumPad6 => 6,
            ConsoleKey.D7 or ConsoleKey.NumPad7 => 7,
            ConsoleKey.D8 or ConsoleKey.NumPad8 => 8,
            ConsoleKey.D9 or ConsoleKey.NumPad9 => 9,
            _ => -1,
        };
        return number >= 0;
    }

    private void InsertActivePanelFileName(EditorSession session)
    {
        string text = _fileNameInsertionContext?.ActivePanelItemName
            ?? Path.GetFileName(session.FilePath);
        session.InsertText(text, "Insert active panel file name");
    }

    private void InsertPassivePanelFileName(EditorSession session)
    {
        string text = _fileNameInsertionContext?.PassivePanelItemName
            ?? Path.GetFileName(session.FilePath);
        session.InsertText(text, "Insert passive panel file name");
    }

    private static void ScrollViewport(EditorSession session, int delta, int contentHeight)
    {
        int maxTopLine = Math.Max(0, session.Document.Buffer.LineCount - contentHeight);
        MoveViewportTo(session, Math.Clamp(session.Viewport.TopLine + delta, 0, maxTopLine), contentHeight);
    }

    private static void MoveViewportTo(EditorSession session, int topLine, int contentHeight)
    {
        int maxTopLine = Math.Max(0, session.Document.Buffer.LineCount - contentHeight);
        topLine = Math.Clamp(topLine, 0, maxTopLine);
        session.Viewport.TopLine = topLine;
        if (session.Cursor.Line < topLine)
            session.MoveTo(new EditorPosition(topLine, session.Cursor.Column));
        else if (session.Cursor.Line >= topLine + contentHeight)
            session.MoveTo(new EditorPosition(topLine + contentHeight - 1, session.Cursor.Column));
    }

    private static string ClipboardTextForOperatingSystem(string text) =>
        OperatingSystem.IsWindows()
            ? text.ReplaceLineEndings("\r\n")
            : text;

    private void ToggleMarkMode(EditorSession session, EditorSelectionMode mode)
    {
        bool wasMarkingSameMode = _markMode && session.Selection?.Mode == mode;
        _markMode = !wasMarkingSameMode;
        if (_markMode)
        {
            _persistentSelection = false;
            session.SetSelectionMode(mode);
        }
        else
        {
            _persistentSelection = session.Selection is { IsEmpty: false };
        }
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

    private void ShowSyntaxLanguageDialog(EditorSession session)
    {
        string? language = new InputDialog(_screen, _palette).Show(
            "Syntax",
            "Language",
            allowEmpty: false,
            initialText: session.SyntaxLanguage);
        if (language is not null)
            session.SetSyntaxLanguage(language);
    }

    private void ShowSyntaxThemeDialog(EditorSession session)
    {
        string? theme = new InputDialog(_screen, _palette).Show(
            "Syntax",
            "Theme",
            allowEmpty: false,
            initialText: session.SyntaxTheme);
        if (theme is not null)
            session.SetSyntaxTheme(theme);
    }

    private void Draw(
        EditorSession session,
        int contentHeight,
        ConsoleSize size,
        ConsoleModifiers functionKeyModifiers)
    {
        using var frame = _screen.BeginFrame();
        DrawHeader(session, size);
        EditorSyntaxHighlightResult syntaxResult = ResolveSyntaxHighlighting(session, contentHeight);
        DrawContent(session, contentHeight, size, syntaxResult);
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

    private EditorSyntaxHighlightResult ResolveSyntaxHighlighting(EditorSession session, int contentHeight)
    {
        try
        {
            var result = _syntaxHighlighter.Highlight(new EditorSyntaxHighlightRequest
            {
                FilePath = session.FilePath,
                Buffer = session.Document.Buffer,
                DocumentRevision = session.Document.Revision,
                FirstLineIndex = session.Viewport.TopLine,
                LineCount = contentHeight,
                Settings = _settings,
                Cache = session.SyntaxHighlightCache,
                Palette = _palette,
                BaseStyle = EditorTextStyle(),
                IsEnabledForSession = session.SyntaxHighlightingEnabled,
                SessionLanguage = session.SyntaxLanguage,
                SessionTheme = session.SyntaxTheme,
            });
            session.SetSyntaxDiagnostics(result.Diagnostics);
            return result;
        }
        catch (Exception ex)
        {
            var result = EditorSyntaxHighlightResult.Disabled($"Syn:error {ex.Message}");
            session.SetSyntaxDiagnostics(result.Diagnostics);
            return result;
        }
    }

    private void DrawContent(
        EditorSession session,
        int contentHeight,
        ConsoleSize size,
        EditorSyntaxHighlightResult syntaxResult)
    {
        int textWidth = Math.Max(1, size.Width - 1);
        var syntaxSpansByLine = syntaxResult.Spans
            .GroupBy(span => span.LineIndex)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<EditorColorSpan>)group.ToArray());

        for (int row = 0; row < contentHeight; row++)
        {
            int lineIndex = session.Viewport.TopLine + row;
            if (lineIndex < session.Document.Buffer.LineCount)
            {
                syntaxSpansByLine.TryGetValue(lineIndex, out var lineSpans);
                DrawTextLine(session, lineIndex, row + 1, textWidth, lineSpans ?? []);
            }
            else
            {
                _screen.Write(0, row + 1, new string(' ', textWidth), EditorTextStyle());
            }
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
        string syntax = session.SyntaxDiagnostics.StatusText;
        int cursorColumn = CursorColumnStatus(session);
        string status =
            $" Ln {session.Cursor.Line + 1} Col {cursorColumn}  {code}  Undo:{(session.UndoHistory.CanUndo ? "Y" : "N")} Redo:{(session.UndoHistory.CanRedo ? "Y" : "N")}  {syntax}";
        _screen.Write(0, y, Fit(status, size.Width), PaletteStyles.CommandLine(_palette));
    }

    private void DrawKeyBar(ConsoleSize size, ConsoleModifiers modifiers)
    {
        var items = EditorCommandBindings.ForModifiers(modifiers)
            .Select(binding => new FunctionKeyBarItem(binding.KeyNumber, binding.Label))
            .ToArray();
        new FunctionKeyBarRenderer(_screen, _palette).Render(size.Height - 1, size.Width, items);
    }

    private static bool TryGetFunctionKeyBarKey(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        ConsoleModifiers modifiers,
        out ConsoleKeyInfo key)
    {
        key = default;

        if (!FunctionKeyBarRenderer.TryGetKeyNumberAt(mouse, size.Height - 1, size.Width, out int keyNumber))
            return false;

        var binding = EditorCommandBindings.ForModifiers(modifiers)
            .FirstOrDefault(candidate => candidate.KeyNumber == keyNumber);
        if (binding is null)
            return false;

        key = new ConsoleKeyInfo(
            '\0',
            binding.Key,
            shift: (binding.Modifiers & ConsoleModifiers.Shift) != 0,
            alt: (binding.Modifiers & ConsoleModifiers.Alt) != 0,
            control: (binding.Modifiers & ConsoleModifiers.Control) != 0);
        return true;
    }

    private void DrawTextLine(
        EditorSession session,
        int lineIndex,
        int screenY,
        int width,
        IReadOnlyList<EditorColorSpan> syntaxSpans)
    {
        string line = session.Document.Buffer.GetLine(lineIndex);
        for (int screenX = 0; screenX < width; screenX++)
        {
            int visualColumn = session.Viewport.LeftColumn + screenX;
            int logicalColumn = LogicalColumnFromVisualColumn(line, visualColumn);
            char ch = CharacterAtVisualColumn(line, visualColumn);
            bool selected = IsSelected(session.Selection, lineIndex, logicalColumn);
            bool cursorCell = IsCursorCell(session, lineIndex, line, logicalColumn);
            CellStyle style = SyntaxStyleAt(syntaxSpans, lineIndex, logicalColumn)
                ?? EditorTextStyle();
            _screen.WriteChar(
                screenX,
                screenY,
                ch,
                cursorCell || selected ? EditorSelectionStyle() : style);
        }
    }

    private static CellStyle? SyntaxStyleAt(
        IReadOnlyList<EditorColorSpan> syntaxSpans,
        int lineIndex,
        int logicalColumn)
    {
        foreach (var span in syntaxSpans)
        {
            if (span.Contains(lineIndex, logicalColumn))
                return span.Style;
        }

        return null;
    }

    private CellStyle EditorSelectionStyle() =>
        new(_palette.CommandLineBg, _palette.CommandLineFg);

    private CellStyle EditorTextStyle() =>
        new(_palette.CommandLineFg, _palette.PanelBackground);

    private void DrawCursor(EditorSession session, int contentHeight, ConsoleSize size)
    {
        string line = session.Document.Buffer.GetLine(session.Cursor.Line);
        if (session.Cursor.Column < line.Length &&
            EditorUnicode.DisplayCellWidthAt(line, session.Cursor.Column) > 1)
        {
            _screen.SetCursorVisible(false);
            return;
        }

        int screenRow = 1 + (session.Cursor.Line - session.Viewport.TopLine);
        int screenCol = VisualColumn(line, session.Cursor.Column)
            - session.Viewport.LeftColumn;
        if (screenRow >= 1 && screenRow <= contentHeight && screenCol >= 0 && screenCol < size.Width - 1)
        {
            _screen.SetCursorPosition(screenCol, screenRow);
            _screen.SetCursorVisible(true);
            return;
        }

        _screen.SetCursorVisible(false);
    }

    private bool IsCursorCell(
        EditorSession session,
        int lineIndex,
        string line,
        int logicalColumn)
    {
        if (!_customCursorVisible)
            return false;

        if (lineIndex != session.Cursor.Line ||
            session.Cursor.Column >= line.Length ||
            logicalColumn != session.Cursor.Column)
        {
            return false;
        }

        return EditorUnicode.DisplayCellWidthAt(line, session.Cursor.Column) > 1;
    }

    private static bool UsesCustomCursor(EditorSession session)
    {
        string line = session.Document.Buffer.GetLine(session.Cursor.Line);
        return session.Cursor.Column < line.Length &&
            EditorUnicode.DisplayCellWidthAt(line, session.Cursor.Column) > 1;
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
        int end = Math.Min(EditorUnicode.NormalizeScalarBoundary(line, logicalColumn), line.Length);
        for (int index = 0; index < end;)
        {
            visual += line[index] == '\t'
                ? tabSize - visual % tabSize
                : EditorUnicode.DisplayCellWidthAt(line, index);
            index = EditorUnicode.NextScalarColumn(line, index);
        }

        return visual + Math.Max(0, logicalColumn - line.Length);
    }

    private int LogicalColumnFromVisualColumn(string line, int targetVisualColumn)
    {
        int tabSize = EditorSettingsResolver.ResolveTabSize(_settings);
        int visual = 0;
        for (int logical = 0; logical < line.Length;)
        {
            int width = line[logical] == '\t'
                ? tabSize - visual % tabSize
                : EditorUnicode.DisplayCellWidthAt(line, logical);
            if (targetVisualColumn < visual + width)
                return logical;

            visual += width;
            logical = EditorUnicode.NextScalarColumn(line, logical);
        }

        return line.Length + Math.Max(0, targetVisualColumn - visual);
    }

    private char CharacterAtVisualColumn(string line, int targetVisualColumn)
    {
        int tabSize = EditorSettingsResolver.ResolveTabSize(_settings);
        int visual = 0;
        for (int logical = 0; logical < line.Length;)
        {
            char ch = line[logical];
            int width = ch == '\t'
                ? tabSize - visual % tabSize
                : EditorUnicode.DisplayCellWidthAt(line, logical);
            if (targetVisualColumn < visual + width)
            {
                if (ch == '\t')
                    return ' ';

                int cellOffset = targetVisualColumn - visual;
                int next = EditorUnicode.NextScalarColumn(line, logical);
                int charIndex = logical + cellOffset;
                return charIndex < next ? line[charIndex] : ' ';
            }

            visual += width;
            logical = EditorUnicode.NextScalarColumn(line, logical);
        }

        return ' ';
    }

    private static string CurrentCharacterStatus(EditorSession session)
    {
        string line = session.Document.Buffer.GetLine(session.Cursor.Line);
        if (session.Cursor.Column < line.Length)
        {
            if (EditorUnicode.TryGetScalarAt(line, session.Cursor.Column, out Rune scalar))
                return $"U+{scalar.Value:X4}/{scalar.Value}";

            char currentChar = line[session.Cursor.Column];
            return $"U+{(int)currentChar:X4}/{(int)currentChar}";
        }

        string? lineEnding = session.Document.Buffer.GetLineEnding(session.Cursor.Line);
        if (lineEnding is not null)
            return LineEndingStatus(lineEnding);

        return "EOF";
    }

    private static int CursorColumnStatus(EditorSession session)
    {
        string line = session.Document.Buffer.GetLine(session.Cursor.Line);
        int realColumn = Math.Min(session.Cursor.Column, line.Length);
        int virtualColumn = Math.Max(0, session.Cursor.Column - line.Length);
        return EditorUnicode.ScalarColumnFromUtf16Column(line, realColumn) + virtualColumn + 1;
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
