cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.96"
  sha256 arm: "a5621d20409aa931828e1fd1a1bc2bb89bb52036d28e13800080c6b83723347f",
         intel: "12459bc27d0f2e98201ee1ffb1d38a2d26783c5a254eb93aea818691d036adcb"

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
