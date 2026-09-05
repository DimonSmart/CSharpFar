namespace CSharpFar.Ui.Tests;

public sealed class UiThemeTests
{
    [Fact]
    public void TemporaryThemeScope_RestoresThemeAfterException()
    {
        var temporary = new ConsolePalette { Name = "Temporary" };
        UiTheme.ResetForTests();
        UiTheme.Initialize(PaletteRegistry.Default);
        try
        {
            void ThrowDuringTemporaryScope()
            {
                using (UiTheme.UseTemporary(temporary))
                {
                    Assert.Same(temporary, UiTheme.Current);
                    throw new InvalidOperationException("render failed");
                }
            }

            Assert.Throws<InvalidOperationException>((Action)ThrowDuringTemporaryScope);
            Assert.Same(PaletteRegistry.Default, UiTheme.Current);
        }
        finally
        {
            UiTheme.ResetForTests();
        }
    }

    [Fact]
    public void TemporaryThemeScope_RestoresNestedThemesInOrder()
    {
        var temporary = new ConsolePalette { Name = "Temporary" };
        UiTheme.ResetForTests();
        UiTheme.Initialize(PaletteRegistry.Default);
        try
        {
            using (UiTheme.UseTemporary(temporary))
            {
                Assert.Same(temporary, UiTheme.Current);
                using (UiTheme.UseTemporary(PaletteRegistry.Default))
                    Assert.Same(PaletteRegistry.Default, UiTheme.Current);
                Assert.Same(temporary, UiTheme.Current);
            }

            Assert.Same(PaletteRegistry.Default, UiTheme.Current);
        }
        finally
        {
            UiTheme.ResetForTests();
        }
    }
}
