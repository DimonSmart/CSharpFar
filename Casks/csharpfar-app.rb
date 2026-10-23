cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.105"
  sha256 arm: "cd227e9ff4d5f3f2b69c9b2112b94dc84b687da04e2b477e8815cdd0f2c64408",
         intel: "7549d4c93308395e6fbcec0e3f9d65ae5442722f6b54e94ff4ba03aad77b6c96"

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
