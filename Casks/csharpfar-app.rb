cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.86"
  sha256 arm: "284bae5ee552e4062137eb44c149625b3845404fedefafcd0baa832af2005741",
         intel: "8ff84350b7d745ba7e275b3ddcbaf0aa16e00032b98b260fd16bfcd67f7f41ab"

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
