cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.70"
  sha256 arm: "c4c3cdec2bcf564889583c41df7323e26848815e88e70f9ea5e1a4152a3afc47",
         intel: "4497e20e3bb9a9374d8aff73eaf977b5681589fc75640b5274fc69885c6ded27"

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
