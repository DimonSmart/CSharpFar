cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.119"
  sha256 arm: "49972a3d66b2500ec080f62db557a71b9b9f0dd1749942d46820383df2e96f4a",
         intel: "960e38f2a744bd4d418dc36c380ba2c4bdfd89291459e30afcd832bd6be97850"

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
