cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.114"
  sha256 arm: "0905e046f695e8f09d07aba620d4df71d1f9152359d5e6dde4ee84bb7f54741a",
         intel: "e589fc2aac916104e5026d2d9c5ed3ea3169861ca07bcae1b85bab3c69b9135e"

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
