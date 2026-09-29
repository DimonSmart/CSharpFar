cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.76"
  sha256 arm: "9657189209d6f6c989fc14759c66c582a71c78990494ee45bdf6e65d8d142550",
         intel: "e3554db3c161e120f6560cbee96eb14663268826b9a4f4eac1f8a0371d7f61dd"

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
