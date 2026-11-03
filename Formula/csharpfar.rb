class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.116/CSharpFar-v1.0.116-osx-arm64.tar.gz"
      sha256 "ba98bb75d9113cfcb20fba7a162065d0d6d7d6badd6d446d1e27f474b9352176"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.116/CSharpFar-v1.0.116-osx-x64.tar.gz"
      sha256 "7bdf7281791075754e4fb7310d67e4fa87067d61b2e8bb0b8b48c0a063603106"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
