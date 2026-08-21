cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.64"
  sha256 arm: "c8bedac75c24f5c24436273a6c5280559e164714813e1d68c35c698605a794c4",
         intel: "4cf6f9fce706bd6ce400f6e55a59c9a5e0821d9bfe1d404b08359a12ac79c116"

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
