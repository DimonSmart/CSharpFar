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
internal sealed partial class FileEditor
{
    private const int CustomCursorBlinkIntervalMs = 500;

    private readonly ModalDialogHost _modalDialogs;
    private readonly InteractiveSurfaceHost _surfaces;
    private readonly ConsolePalette _palette;
    private readonly AppSettings.EditorSettings _settings;
    private readonly EditorFileService _fileService;
    private readonly ITextClipboard _clipboard;
    private readonly EditorFileNameInsertionContext? _fileNameInsertionContext;
    private readonly IEditorSyntaxHighlighter _syntaxHighlighter;
    private readonly FunctionKeyBarController<ConsoleKeyInfo> _functionKeyBar = new();
    private EditorFindDialogResult? _lastFind;
    private bool _markMode;
    private bool _persistentSelection;

    public FileEditor(InteractiveSurfaceHost surfaces, ModalDialogHost modalDialogs)
        : this(surfaces, modalDialogs, null, null) { }

    public FileEditor(InteractiveSurfaceHost surfaces, ModalDialogHost modalDialogs, ConsolePalette? palette)
        : this(surfaces, modalDialogs, palette, null) { }

    public FileEditor(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings)
        : this(surfaces, modalDialogs, palette, settings, null)
    {
    }

    internal FileEditor(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings,
        ITextClipboard? clipboard)
        : this(surfaces, modalDialogs, palette, settings, clipboard, null)
    {
    }

    internal FileEditor(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings,
        ITextClipboard? clipboard,
        EditorFileNameInsertionContext? fileNameInsertionContext)
        : this(surfaces, modalDialogs, palette, settings, clipboard, fileNameInsertionContext, null)
    {
    }

    internal FileEditor(
        InteractiveSurfaceHost surfaces,
        ModalDialogHost modalDialogs,
        ConsolePalette? palette,
        AppSettings.EditorSettings? settings,
        ITextClipboard? clipboard,
        EditorFileNameInsertionContext? fileNameInsertionContext,
        IEditorSyntaxHighlighter? syntaxHighlighter)
    {
        _surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
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
            !new ConfirmDialog(_modalDialogs).Show(
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
            new MessageDialog(_modalDialogs).Show("Editor", ex.Message);
            return;
        }

        try
        {
            session.RaiseOpened();
            RunLoop(session);
        }
        finally
        {
            session.RaiseClosed();
        }
    }

    private void RunLoop(EditorSession session)
    {
        var layer = new FileEditorLayer(this, session);
        _surfaces.Run(
            layer,
            (packet, input) =>
            {
                layer.RestoreVisibleCursorPhase();
                return HandleSurfaceInput(session, input, packet.Frame);
            },
            getNextWakeUtc: layer.GetNextWakeUtc,
            handleWake: _ => layer.HandleWake());
    }

    private ModalDialogLoopResult<bool> HandleSurfaceInput(
        EditorSession session,
        FileEditorInput input,
        FileEditorFrame frame)
    {
        switch (input.Kind)
        {
            case FileEditorInputKind.None:
            case FileEditorInputKind.ModifierChanged:
                return ModalDialogLoopResult<bool>.Continue;
            case FileEditorInputKind.MouseWheel:
                if (input.ScrollLines < 0)
                    session.MoveUp(-input.ScrollLines);
                else if (input.ScrollLines > 0)
                    session.MoveDown(input.ScrollLines);
                return ModalDialogLoopResult<bool>.Continue;
            case FileEditorInputKind.ScrollbarToLine:
                MoveViewportTo(session, input.TopLine, frame.ContentHeight);
                return ModalDialogLoopResult<bool>.Continue;
            case FileEditorInputKind.TextMouseDown:
                if (input.Position is { } downPosition)
                    session.MoveTo(downPosition);
                _markMode = false;
                _persistentSelection = false;
                return ModalDialogLoopResult<bool>.Continue;
            case FileEditorInputKind.TextMouseDoubleClick:
                if (input.Position is { } doubleClickPosition)
                    session.SelectWordAt(doubleClickPosition);
                _markMode = false;
                _persistentSelection = false;
                return ModalDialogLoopResult<bool>.Continue;
            case FileEditorInputKind.TextMouseDrag:
                if (input.Anchor is { } dragAnchor && input.Position is { } dragPosition)
                    session.SelectRange(dragAnchor, dragPosition);
                _markMode = false;
                _persistentSelection = false;
                return ModalDialogLoopResult<bool>.Continue;
            case FileEditorInputKind.TextMouseUp:
                if (input.Anchor is { } upAnchor && input.Position is { } upPosition)
                    session.SelectRange(upAnchor, upPosition);
                _markMode = false;
                _persistentSelection = false;
                return ModalDialogLoopResult<bool>.Continue;
            case FileEditorInputKind.Keyboard:
                if (input.Key is not { } key)
                    return ModalDialogLoopResult<bool>.Continue;

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
                    return ModalDialogLoopResult<bool>.Continue;
                }

                if (HandleKey(session, key, frame.ContentHeight))
                    return ModalDialogLoopResult<bool>.Continue;

                if (key.Key is ConsoleKey.Escape or ConsoleKey.F10 && TryExit(session))
                    return ModalDialogLoopResult<bool>.Complete(true);

                return ModalDialogLoopResult<bool>.Continue;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
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
                    new MessageDialog(_modalDialogs).Show("Editor", "Select text to copy block.");
                _markMode = false;
                _persistentSelection = false;
                return true;
            case ConsoleKey.M when control:
                if (!session.MoveSelectionToCursor())
                    new MessageDialog(_modalDialogs).Show("Editor", "Select text outside the cursor to move block.");
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

        var choice = new SaveChangesDialog(_modalDialogs).Show(Path.GetFileName(session.FilePath));
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
            new MessageDialog(_modalDialogs).Show("Save Error", ex.Message);
            return false;
        }
    }

    private void ShowFormatDialog(EditorSession session)
    {
        var selected = new EditorFormatDialog(_modalDialogs).Show(session.Document.Format);
        if (selected is not null)
            session.Document.SetFormat(selected);
    }

    private void ShowFindDialog(EditorSession session)
    {
        var result = new EditorFindDialog(_modalDialogs, _palette).Show(_lastFind);
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
            new MessageDialog(_modalDialogs).Show("Find", "Text not found.");
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
        string? pattern = new InputDialog(_modalDialogs).Show("Replace", "Find", allowEmpty: false);
        if (pattern is null)
            return;

        string? replacement = new InputDialog(_modalDialogs).Show("Replace", "With", allowEmpty: true);
        if (replacement is null)
            return;

        int count;
        try
        {
            count = session.ReplaceAll(new EditorSearchOptions(pattern), replacement);
        }
        catch (ArgumentException ex)
        {
            new MessageDialog(_modalDialogs).Show("Replace", ex.Message);
            return;
        }

        if (count == 0)
            new MessageDialog(_modalDialogs).Show("Replace", "Text not found.");
    }

    private void ShowSyntaxLanguageDialog(EditorSession session)
    {
        string? language = new InputDialog(_modalDialogs).Show(
            "Syntax",
            "Language",
            allowEmpty: false,
            initialText: session.SyntaxLanguage);
        if (language is not null)
            session.SetSyntaxLanguage(language);
    }

    private void ShowSyntaxThemeDialog(EditorSession session)
    {
        string? theme = new InputDialog(_modalDialogs).Show(
            "Syntax",
            "Theme",
            allowEmpty: false,
            initialText: session.SyntaxTheme);
        if (theme is not null)
            session.SetSyntaxTheme(theme);
    }

    private FileEditorFrame RenderFrame(
        EditorSession session,
        ConsoleModifiers functionKeyModifiers,
        UiRenderContext context,
        bool customCursorVisible)
    {
        int contentHeight = Math.Max(1, context.Size.Height - 3);
        int contentWidth = Math.Max(1, context.Size.Width - 1);
        EditorViewport viewport = CalculateEffectiveViewport(session, contentHeight, contentWidth);
        Rect headerBounds = context.Size.Height > 0
            ? new Rect(0, 0, context.Size.Width, 1)
            : new Rect(0, 0, 0, 0);
        Rect contentBounds = contentHeight > 0
            ? new Rect(0, 1, contentWidth, contentHeight)
            : new Rect(0, 0, 0, 0);
        Rect statusBounds = context.Size.Height > 1
            ? new Rect(0, contentHeight + 1, context.Size.Width, 1)
            : new Rect(0, 0, 0, 0);
        Rect functionKeyBarBounds = context.Size.Height > 0
            ? new Rect(0, context.Size.Height - 1, context.Size.Width, 1)
            : new Rect(0, 0, 0, 0);
        EditorSyntaxHighlightResult syntaxResult = ResolveSyntaxHighlighting(session, contentHeight, viewport.TopLine);
        IReadOnlyList<FunctionKeyBarAction<ConsoleKeyInfo>> functionKeyActions =
            CreateEditorFunctionKeyBarActions(functionKeyModifiers);
        Rect? scrollbarBounds = contentHeight > 0 && context.Size.Width > 0
            ? new Rect(context.Size.Width - 1, 1, 1, contentHeight)
            : null;
        ScrollState? scrollState = scrollbarBounds is not null
            ? new ScrollState
            {
                TotalItems = session.Document.Buffer.LineCount,
                ViewportItems = contentHeight,
                FirstVisibleIndex = viewport.TopLine,
            }
            : null;
        UiCursorPlacement cursor = BuildCursorPlacement(session, contentHeight, context.Size, viewport);
        bool usesCustomCursor = UsesCustomCursor(session);
        var frame = new FileEditorFrame(
            session,
            context.Viewport,
            context.Size,
            headerBounds,
            contentBounds,
            statusBounds,
            functionKeyBarBounds,
            contentHeight,
            contentWidth,
            viewport.TopLine,
            viewport.LeftColumn,
            Math.Min(session.Document.Buffer.LineCount, viewport.TopLine + contentHeight),
            scrollbarBounds,
            scrollState,
            VerticalScrollbarFrame: null,
            functionKeyActions,
            cursor,
            usesCustomCursor,
            customCursorVisible,
            syntaxResult.Diagnostics,
            syntaxResult);

        Draw(context.Canvas, frame);
        return frame;
    }

    private void Draw(IUiCanvas canvas, FileEditorFrame frame)
    {
        DrawHeader(canvas, frame.Session, frame.Size);
        DrawContent(canvas, frame);
        DrawStatus(canvas, frame.Session, frame.StatusBarBounds.Y, frame.Size);
        DrawKeyBar(canvas, frame.FunctionKeyBarBounds, frame.FunctionKeyActions);
    }

    private void DrawHeader(IUiCanvas canvas, EditorSession session, ConsoleSize size)
    {
        string dirty = session.Document.IsDirty ? "*" : " ";
        string readOnly = session.ReadOnly ? " RO" : string.Empty;
        string left = $"{dirty} {session.FilePath}{readOnly}";
        string right = $" {session.Document.Format.EncodingDisplayName} {session.Document.Format.BomDisplayName} {session.Document.Format.LineEndingDisplayName} ";
        int leftWidth = Math.Max(0, size.Width - right.Length);
        canvas.Write(0, 0, Fit(left, leftWidth) + right, PaletteStyles.PathHeaderActive(_palette));
    }

    private EditorSyntaxHighlightResult ResolveSyntaxHighlighting(EditorSession session, int contentHeight, int topLine)
    {
        try
        {
            var result = _syntaxHighlighter.Highlight(new EditorSyntaxHighlightRequest
            {
                FilePath = session.FilePath,
                Buffer = session.Document.Buffer,
                DocumentRevision = session.Document.Revision,
                FirstLineIndex = topLine,
                LineCount = contentHeight,
                Settings = _settings,
                Cache = session.SyntaxHighlightCache,
                Palette = _palette,
                BaseStyle = EditorTextStyle(),
                IsEnabledForSession = session.SyntaxHighlightingEnabled,
                SessionLanguage = session.SyntaxLanguage,
                SessionTheme = session.SyntaxTheme,
            });
            return result;
        }
        catch (Exception ex)
        {
            var result = EditorSyntaxHighlightResult.Disabled($"Syn:error {ex.Message}");
            return result;
        }
    }

    private void DrawContent(IUiCanvas canvas, FileEditorFrame frame)
    {
        EditorSession session = frame.Session;
        int textWidth = frame.ContentBounds.Width;
        var syntaxSpansByLine = frame.SyntaxResult.Spans
            .GroupBy(span => span.LineIndex)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<EditorColorSpan>)group.ToArray());

        for (int row = 0; row < frame.ContentHeight; row++)
        {
            int lineIndex = frame.TopLine + row;
            if (lineIndex < session.Document.Buffer.LineCount)
            {
                syntaxSpansByLine.TryGetValue(lineIndex, out var lineSpans);
                DrawTextLine(canvas, frame, lineIndex, frame.ContentBounds.Y + row, textWidth, lineSpans ?? []);
            }
            else
            {
                canvas.Write(frame.ContentBounds.X, frame.ContentBounds.Y + row, new string(' ', textWidth), EditorTextStyle());
            }
        }

        if (frame.ScrollBarBounds is { } bounds && frame.VerticalScrollState is { } scrollState)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                canvas,
                bounds,
                scrollState,
                new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false },
                PaletteStyles.DialogBorder(_palette));
        }
    }

    private void DrawStatus(IUiCanvas canvas, EditorSession session, int y, ConsoleSize size)
    {
        string code = CurrentCharacterStatus(session);
        string syntax = session.SyntaxDiagnostics.StatusText;
        int cursorColumn = CursorColumnStatus(session);
        string status =
            $" Ln {session.Cursor.Line + 1} Col {cursorColumn}  {code}  Undo:{(session.UndoHistory.CanUndo ? "Y" : "N")} Redo:{(session.UndoHistory.CanRedo ? "Y" : "N")}  {syntax}";
        canvas.Write(0, y, Fit(status, size.Width), PaletteStyles.CommandLine(_palette));
    }

    private void DrawKeyBar(
        IUiCanvas canvas,
        Rect bounds,
        IReadOnlyList<FunctionKeyBarAction<ConsoleKeyInfo>> actions)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        _functionKeyBar.Render(canvas, bounds.Y, bounds.Width, actions);
    }

    private bool TryGetFunctionKeyAction(
        MouseConsoleInputEvent mouse,
        FileEditorFrame frame,
        out ConsoleKeyInfo key)
    {
        Rect bounds = frame.FunctionKeyBarBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            key = default;
            return false;
        }

        return _functionKeyBar.TryGetAction(
            mouse,
            bounds.Y,
            bounds.Width,
            frame.FunctionKeyActions,
            out key);
    }

    private static IReadOnlyList<FunctionKeyBarAction<ConsoleKeyInfo>> CreateEditorFunctionKeyBarActions(
        ConsoleModifiers modifiers) =>
        EditorCommandBindings.ForModifiers(modifiers)
            .Select(binding => new FunctionKeyBarAction<ConsoleKeyInfo>(
                binding.KeyNumber,
                binding.Label,
                ToConsoleKeyInfo(binding)))
            .ToArray();

    private static ConsoleKeyInfo ToConsoleKeyInfo(EditorCommandBinding binding) =>
        new(
            '\0',
            binding.Key,
            shift: (binding.Modifiers & ConsoleModifiers.Shift) != 0,
            alt: (binding.Modifiers & ConsoleModifiers.Alt) != 0,
            control: (binding.Modifiers & ConsoleModifiers.Control) != 0);

    private void DrawTextLine(
        IUiCanvas canvas,
        FileEditorFrame frame,
        int lineIndex,
        int screenY,
        int width,
        IReadOnlyList<EditorColorSpan> syntaxSpans)
    {
        EditorSession session = frame.Session;
        string line = session.Document.Buffer.GetLine(lineIndex);
        for (int screenX = 0; screenX < width; screenX++)
        {
            int visualColumn = frame.LeftColumn + screenX;
            int logicalColumn = LogicalColumnFromVisualColumn(line, visualColumn);
            char ch = CharacterAtVisualColumn(line, visualColumn);
            bool selected = IsSelected(session.Selection, lineIndex, logicalColumn);
            bool cursorCell = IsCursorCell(frame, lineIndex, line, logicalColumn);
            CellStyle style = SyntaxStyleAt(syntaxSpans, lineIndex, logicalColumn)
                ?? EditorTextStyle();
            canvas.WriteChar(
                frame.ContentBounds.X + screenX,
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

    private UiCursorPlacement BuildCursorPlacement(
        EditorSession session,
        int contentHeight,
        ConsoleSize size,
        EditorViewport viewport)
    {
        string line = session.Document.Buffer.GetLine(session.Cursor.Line);
        if (session.Cursor.Column < line.Length &&
            EditorUnicode.DisplayCellWidthAt(line, session.Cursor.Column) > 1)
        {
            return new UiCursorPlacement(0, 0, Visible: false);
        }

        int screenRow = 1 + (session.Cursor.Line - viewport.TopLine);
        int screenCol = VisualColumn(line, session.Cursor.Column) - viewport.LeftColumn;
        bool visible = screenRow >= 1 && screenRow <= contentHeight && screenCol >= 0 && screenCol < size.Width - 1;
        return new UiCursorPlacement(Math.Max(0, screenCol), Math.Max(0, screenRow), visible);
    }

    private bool IsCursorCell(
        FileEditorFrame frame,
        int lineIndex,
        string line,
        int logicalColumn)
    {
        if (!frame.CustomCursorVisible)
            return false;

        EditorSession session = frame.Session;
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

    private EditorViewport CalculateEffectiveViewport(EditorSession session, int contentHeight, int contentWidth)
    {
        int topLine = session.Viewport.TopLine;
        if (session.Cursor.Line < topLine)
            topLine = session.Cursor.Line;
        else if (session.Cursor.Line >= topLine + contentHeight)
            topLine = session.Cursor.Line - contentHeight + 1;

        int visualColumn = VisualColumn(session.Document.Buffer.GetLine(session.Cursor.Line), session.Cursor.Column);
        int leftColumn = session.Viewport.LeftColumn;
        if (visualColumn < leftColumn)
            leftColumn = visualColumn;
        else if (visualColumn >= leftColumn + contentWidth)
            leftColumn = visualColumn - contentWidth + 1;

        return new EditorViewport { TopLine = topLine, LeftColumn = leftColumn };
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

    private bool TryGetTextMousePosition(
        MouseConsoleInputEvent mouse,
        FileEditorFrame frame,
        bool clampToContent,
        out EditorPosition position)
    {
        position = default;

        Rect content = frame.ContentBounds;
        if (content.Width <= 0 || content.Height <= 0)
            return false;

        int textX = mouse.X;
        int textY = mouse.Y;
        if (clampToContent)
        {
            textX = Math.Clamp(textX, content.X, content.X + content.Width - 1);
            textY = Math.Clamp(textY, content.Y, content.Y + content.Height - 1);
        }
        else if (!content.Contains(textX, textY))
        {
            return false;
        }

        int lineIndex = Math.Clamp(
            frame.TopLine + textY - content.Y,
            0,
            frame.Session.Document.Buffer.LineCount - 1);
        string line = frame.Session.Document.Buffer.GetLine(lineIndex);
        int visualColumn = frame.LeftColumn + textX - content.X;
        position = new EditorPosition(lineIndex, LogicalColumnFromVisualColumn(line, visualColumn));
        return true;
    }

}
