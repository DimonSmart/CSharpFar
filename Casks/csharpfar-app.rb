cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.115"
  sha256 arm: "99b5a897d592f68843d0b67a1bce91f1620ff8a5b03bb3755df6c1da727e7e1d",
         intel: "09ba8298faef3485ae8f666824c2b3b75aa54d9de097bef40b5aa6dc01954abd"

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
