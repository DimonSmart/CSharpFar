cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.80"
  sha256 arm: "22ed4f7b3d1af6e58508ee4c65c91e980320bfd00f20be352d95580b14a19b02",
         intel: "79c33c77897d93a2aa0bce4749e275a41b71804d19c3fa844cd2d233e49fc7c6"

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
