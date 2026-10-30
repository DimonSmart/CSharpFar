cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.107"
  sha256 arm: "f08bdf85cd0fb702572731ff9943a3e614b19dcd89dfedcb698254e3f29323f9",
         intel: "10b6369ee62687629b917c32648043e02774c808c6cfa1d15d02b14db2535884"

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
