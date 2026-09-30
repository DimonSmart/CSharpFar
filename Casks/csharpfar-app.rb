cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.81"
  sha256 arm: "ca64edeef467687230bb10ebbac7d4073281752aa10789a7fa52f6ebe31b5c19",
         intel: "26ab95bd6c8de1497c8ee9629468854423bd8a4efcde03291d406f01fd561f81"

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
