cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.116"
  sha256 arm: "0e7487edfb821364c33581d715cf6a701655c135f9d2ea8ba29e7e7260da1a67",
         intel: "74648dace56027973589420252e72339bf6b84d4aa2d362d7cce5dc90b264696"

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
