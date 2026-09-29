cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.77"
  sha256 arm: "68fd8e502fcb07e921136352b878c5fc3c9a8cbf34f749e3ef5ba25cc47f2149",
         intel: "54232974a49334826f40018e7c316258c896574ad3b44b45835a5cd3bb718504"

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
