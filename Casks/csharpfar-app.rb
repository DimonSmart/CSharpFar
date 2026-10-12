cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.92"
  sha256 arm: "72b7a127c034853860c2cf16f1e28e3cce845cdcde1c9c4c1935e1d24c010955",
         intel: "178b27d66d7ba786eb24a7581ec9daae81d7ebb65da9166dd4e7aca0b895e90f"

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
