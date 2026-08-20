cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.62"
  sha256 arm: "91e0371439b141ecc4a8f84b33b5af317cd390888a6b558b56b57a98ace82aea",
         intel: "5c4c60e14e51ebde5e307c6473499ea676982b8dd7da9b6c0b95f08ae21314ca"

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
