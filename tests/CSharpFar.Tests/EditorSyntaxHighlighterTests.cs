using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class EditorSyntaxHighlighterTests
{
    [Theory]
    [InlineData("sample.cs", "csharp", "source.cs")]
    [InlineData("sample.json", "json", "source.json")]
    [InlineData("sample.xml", "xml", "text.xml")]
    [InlineData("sample.md", "markdown", "text.html.markdown")]
    [InlineData("sample.ps1", "powershell", "source.powershell")]
    [InlineData("sample.yml", "yaml", "source.yaml")]
    [InlineData("Dockerfile", "dockerfile", "source.dockerfile")]
    public void LanguageSelector_SelectsMinimumLanguages(string fileName, string expectedId, string expectedScope)
    {
        var registry = new TextMateGrammarRegistry(new AppSettings.EditorSettings(), "Dark+");
        var selector = new TextMateLanguageSelector(registry.Options);

        var language = selector.SelectLanguage(fileName, "auto");

        Assert.NotNull(language);
        Assert.Equal(expectedId, language.Id);
        Assert.Equal(expectedScope, language.ScopeName);
    }

    [Theory]
    [InlineData("sample.cs", "public sealed class Demo { string Name = \"x\"; }")]
    [InlineData("sample.json", "{\"name\":\"demo\",\"enabled\":true}")]
    [InlineData("sample.xml", "<root name=\"demo\"><item /></root>")]
    [InlineData("sample.md", "# Title\n\n`code`")]
    [InlineData("sample.ps1", "$name = \"demo\"\nWrite-Host $name")]
    [InlineData("sample.yml", "name: demo\nenabled: true")]
    public void TextMateHighlighter_ProducesSpansForMinimumLanguages(string fileName, string text)
    {
        var session = CreateSession(fileName, text);
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session);

        Assert.False(result.Diagnostics.IsFallback);
        Assert.NotEmpty(result.Spans);
        Assert.Contains(result.Spans, span => !span.Style.Equals(EditorBaseStyle));
    }

    [Fact]
    public void TextMateHighlighter_UnknownFileTypeFallsBackToPlainText()
    {
        var session = CreateSession("sample.unknown", "plain text");
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session);

        Assert.Empty(result.Spans);
        Assert.True(result.Diagnostics.IsFallback);
        Assert.Equal("Syn:plain", result.Diagnostics.StatusText);
    }

    [Fact]
    public void TextMateHighlighter_GlobalSettingDisablesHighlighting()
    {
        var settings = new AppSettings.EditorSettings { SyntaxHighlightingEnabled = false };
        var session = CreateSession("sample.cs", "public class Demo {}", settings);
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session, settings);

        Assert.Empty(result.Spans);
        Assert.False(result.Diagnostics.IsEnabled);
        Assert.Equal("Syn:off", result.Diagnostics.StatusText);
    }

    [Fact]
    public void TextMateHighlighter_SessionToggleDisablesHighlighting()
    {
        var session = CreateSession("sample.cs", "public class Demo {}", new AppSettings.EditorSettings());
        session.ToggleSyntaxHighlighting();
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session);

        Assert.Empty(result.Spans);
        Assert.False(result.Diagnostics.IsEnabled);
        Assert.Equal("Syn:off", result.Diagnostics.StatusText);
    }

    [Fact]
    public void TextMateHighlighter_ExplicitLanguageOverridesFileExtension()
    {
        var session = CreateSession("sample.txt", "{\"name\":\"demo\"}", new AppSettings.EditorSettings());
        session.SetSyntaxLanguage("json");
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session);

        Assert.False(result.Diagnostics.IsFallback);
        Assert.Equal("JSON", result.Diagnostics.SelectedGrammar);
        Assert.NotEmpty(result.Spans);
    }

    [Fact]
    public void TextMateHighlighter_InvalidThemeFallsBackToDarkPlus()
    {
        var settings = new AppSettings.EditorSettings { SyntaxTheme = "MissingTheme" };
        var session = CreateSession("sample.cs", "public class Demo {}", settings);
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session, settings);

        Assert.False(result.Diagnostics.IsFallback);
        Assert.Equal("Dark+", result.Diagnostics.SelectedTheme);
        Assert.Contains("MissingTheme", result.Diagnostics.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_SetSyntaxThemeInvalidatesSyntaxCache()
    {
        var session = CreateSession("sample.cs", "public class Demo {}", new AppSettings.EditorSettings());
        var highlighter = new TextMateEditorSyntaxHighlighter();
        HighlightVisibleLines(highlighter, session);
        Assert.True(session.SyntaxHighlightCache.FirstInvalidLine > 0);

        session.SetSyntaxTheme("Light+");

        Assert.Equal(0, session.SyntaxHighlightCache.FirstInvalidLine);
    }

    [Fact]
    public void TextMateHighlighter_LongLineFallsBackToPlainLine()
    {
        var settings = new AppSettings.EditorSettings { SyntaxMaxLineLength = 5 };
        var session = CreateSession("sample.cs", "public", settings);
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session, settings);

        Assert.Empty(result.Spans);
        Assert.Equal(0, session.SyntaxHighlightCache.TokenizedLineCount);
    }

    [Fact]
    public void TextMateHighlighter_CursorOnlyMovementUsesCachedLines()
    {
        var session = CreateSession("sample.cs", "public class Demo {}", new AppSettings.EditorSettings());
        var highlighter = new TextMateEditorSyntaxHighlighter();

        HighlightVisibleLines(highlighter, session);
        int tokenizedAfterFirstDraw = session.SyntaxHighlightCache.TokenizedLineCount;

        session.MoveRight();
        HighlightVisibleLines(highlighter, session);

        Assert.Equal(tokenizedAfterFirstDraw, session.SyntaxHighlightCache.TokenizedLineCount);
    }

    [Fact]
    public void TextMateHighlighter_EditInvalidatesChangedLine()
    {
        var session = CreateSession("sample.cs", "public class Demo {}", new AppSettings.EditorSettings());
        var highlighter = new TextMateEditorSyntaxHighlighter();

        HighlightVisibleLines(highlighter, session);
        int tokenizedAfterFirstDraw = session.SyntaxHighlightCache.TokenizedLineCount;

        session.InsertText("// ");
        HighlightVisibleLines(highlighter, session);

        Assert.True(session.SyntaxHighlightCache.TokenizedLineCount > tokenizedAfterFirstDraw);
    }

    [Fact]
    public void TextMateHighlighter_MultilineCommentStatePropagatesToFollowingLine()
    {
        var session = CreateSession("sample.cs", "/* comment\nstill comment\n*/ public", new AppSettings.EditorSettings());
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session);

        Assert.Contains(result.Spans, span => span.LineIndex == 1);
    }

    [Fact]
    public void TextMateHighlighter_CSharpMappingMatchesFarLikeConsoleColors()
    {
        var session = CreateSession(
            "sample.cs",
            "public interface IConsoleOutputModeDriver\n{\n    void SetRenderingOutputMode(bool enabled);\n    /// <summary>",
            new AppSettings.EditorSettings());
        var highlighter = new TextMateEditorSyntaxHighlighter();

        var result = HighlightVisibleLines(highlighter, session);

        Assert.False(result.Diagnostics.IsFallback);

        var openBrace = SpanAt(result.Spans, lineIndex: 1, column: 0);
        Assert.NotNull(openBrace);
        Assert.Equal(ConsoleColor.Yellow, openBrace.Value.Style.Foreground);
        Assert.Equal(ConsoleColor.DarkBlue, openBrace.Value.Style.Background);

        var comment = SpanAt(result.Spans, lineIndex: 3, column: 4);
        Assert.NotNull(comment);
        Assert.Equal(ConsoleColor.DarkCyan, comment.Value.Style.Foreground);
        Assert.Equal(ConsoleColor.DarkBlue, comment.Value.Style.Background);
    }

    private static EditorSyntaxHighlightResult HighlightVisibleLines(
        IEditorSyntaxHighlighter highlighter,
        EditorSession session,
        AppSettings.EditorSettings? settings = null)
    {
        settings ??= new AppSettings.EditorSettings();
        return highlighter.Highlight(new EditorSyntaxHighlightRequest
        {
            FilePath = session.FilePath,
            Buffer = session.Document.Buffer,
            DocumentRevision = session.Document.Revision,
            FirstLineIndex = session.Viewport.TopLine,
            LineCount = session.Document.Buffer.LineCount,
            Settings = settings,
            Cache = session.SyntaxHighlightCache,
            Palette = PaletteRegistry.Default,
            BaseStyle = EditorBaseStyle,
            IsEnabledForSession = session.SyntaxHighlightingEnabled,
            SessionLanguage = session.SyntaxLanguage,
            SessionTheme = session.SyntaxTheme,
        });
    }

    private static CellStyle EditorBaseStyle => new(ConsoleColor.White, ConsoleColor.DarkBlue);

    private static EditorSession CreateSession(
        string fileName,
        string text,
        AppSettings.EditorSettings? settings = null)
    {
        settings ??= new AppSettings.EditorSettings();
        var format = new EditorDocumentFormat(Encoding.UTF8, false, EditorLineEnding.Lf, "UTF-8");
        var document = new EditorDocument(EditorTextBuffer.FromText(text), format);
        document.MarkClean();
        return new EditorSession(fileName, document, settings, readOnly: false);
    }

    private static EditorColorSpan? SpanAt(IReadOnlyList<EditorColorSpan> spans, int lineIndex, int column)
    {
        foreach (var span in spans)
        {
            if (span.Contains(lineIndex, column))
                return span;
        }

        return null;
    }
}
