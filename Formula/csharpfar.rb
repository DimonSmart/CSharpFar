class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.114/CSharpFar-v1.0.114-osx-arm64.tar.gz"
      sha256 "0234950fe133fd4f6a458a001589797bf41ecd171a56bede071a9142d16cd797"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.114/CSharpFar-v1.0.114-osx-x64.tar.gz"
      sha256 "f55e9b98955cd8922f25931d95b2d7ee5cffe16c763e9b6d7c9237e16d59e337"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
