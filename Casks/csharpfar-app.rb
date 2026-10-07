cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.90"
  sha256 arm: "ea1212e9e675422026b73f0e68d1dee01a3ac94fa67af8383165c54e2c9d2c53",
         intel: "97523c683d5820b39830c0aa35f3d3bca0b650c5f4923c92a384482a83d8b0f9"

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
