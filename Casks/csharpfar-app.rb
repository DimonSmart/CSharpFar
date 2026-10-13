cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.94"
  sha256 arm: "c8df051d436ca4a7becfa434c5ac53e55eb4b69a4c918bb195949d17ba88a9f2",
         intel: "8153cd8ef379a30c33625a915995c1f713371b0052dfe62b8b3fa7ba21a49b9c"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar is a terminal application. Opening CSharpFar.app launches it in Terminal.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
