cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.78"
  sha256 arm: "6bd63585fab38ee609b393e221ffcf1eaea8c975544943c8543fbdb3a14ab337",
         intel: "670f78ee6a461393ae9c93e756e6604ab2618f1466d11ca0344a0077aee4c996"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar is a terminal application. Opening CSharpFar.app launches it in Terminal.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
