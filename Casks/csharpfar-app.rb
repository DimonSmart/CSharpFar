cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.125"
  sha256 arm: "d700c6f02a3021ffdf79e971f4a3ff9a45f176a81f804f4e92ecad8eaff4df74",
         intel: "25ac6f049f743cf39c683d58ff499b2d687d82f7761c9db3b8280b890acd6836"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar.app opens its own native window with an embedded terminal.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
