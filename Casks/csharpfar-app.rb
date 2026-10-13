cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "1.0.95"
  sha256 arm: "12612d1be5e01178260d63d09f37fdc14af07c7d9863d90505adba44a4697d72",
         intel: "e7e09e2fec227bfcb719549b2b00273247f6d25378e64625df95b53c3db32f1b"

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
