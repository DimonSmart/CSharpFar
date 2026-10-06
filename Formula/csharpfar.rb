class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.87/CSharpFar-v1.0.87-osx-arm64.tar.gz"
      sha256 "1d180b5e1a9f71a98b12117725b896a0ed0ff18889308ffd25f8413c7cea117f"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.87/CSharpFar-v1.0.87-osx-x64.tar.gz"
      sha256 "0d4952334a45e7d60d22b1b440c93282a60fb332ac89dd99f266ccd287b839ae"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
