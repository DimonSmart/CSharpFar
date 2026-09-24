class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.72"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.72/CSharpFar-v1.0.72-osx-arm64.tar.gz"
    sha256 "8575b9f37fbc4be08076952d3c639592b4ac0361614eb9be2998c2c8f1f5dfea"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.72/CSharpFar-v1.0.72-osx-x64.tar.gz"
    sha256 "7f2914b9beef0592120962f6e54ca3b7625c1a4d2fc31b92342c7a6a9d7f0ae5"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
