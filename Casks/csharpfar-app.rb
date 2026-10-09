cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.91"
  sha256 arm: "cb4d32b651526bd474a1d7d2e8475c647b84284313d0207371ba48ec9b053154",
         intel: "c400e32adbf7dbcaea6ca1b908f8b9b777348b342e41b2e0f9082be1d34310ff"

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
