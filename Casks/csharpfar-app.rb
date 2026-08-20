cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.63"
  sha256 arm: "0913502a727b684a1a9d61b0824a3bc7b384d6b22136f9d55b0fb247800c81c2",
         intel: "d0a15a58cb687e5938a319726c2121abbadff4a8d9181fa23d36899ebda36e50"

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
