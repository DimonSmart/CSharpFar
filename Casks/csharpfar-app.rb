cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.88"
  sha256 arm: "8a5e93014b3fc5eff5d1fca5146429f6096b92a66eb492c482d281b72f254c15",
         intel: "5a4ce3f935faf96b72f7936699ed026810c06867b8e546fee0e686ba48e6c370"

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
