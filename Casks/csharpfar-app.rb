cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.109"
  sha256 arm: "671a9e172f68f37ef543d22a496d87784c2cd9b3aa0a2ac5713745e8b2e15680",
         intel: "84065b767add9131dbdc11bcaa2db3ae13487de1e995ba592bf7487e83e5ccc3"

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
