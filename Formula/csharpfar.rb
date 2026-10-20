class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.104/CSharpFar-v1.0.104-osx-arm64.tar.gz"
      sha256 "74984893c9dec8a4a61465287c5bdb476db08334251be31755f24454f5e8bee4"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.104/CSharpFar-v1.0.104-osx-x64.tar.gz"
      sha256 "0dbb2c08b0f803a0502c91eb466bc89d9b506b49b022a4ee9cca90b87316f6fe"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
