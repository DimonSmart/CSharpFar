cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.100"
  sha256 arm: "49b3ddd43a459612efe5626fa1873bb59a7783c565f7a2f76e150f243a9c34fa",
         intel: "5376bc2d7ea2092729898f689ac43ceb65ade82d1e866adf6b07dd21b96b52c7"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar.app contains its own terminal window; Kitty and Terminal.app are not required.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
