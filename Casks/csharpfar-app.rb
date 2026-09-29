cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.79"
  sha256 arm: "7ab8af6d39b5ef46aedd65d2d0ed68095d49355b0c76c22b24da976b9e732b68",
         intel: "328a2cf01adbf3d5756e9c4c97957725bd55c0fafb6881318fbc4dc13553390e"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar is a terminal application. Opening CSharpFar.app launches it in Terminal.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
