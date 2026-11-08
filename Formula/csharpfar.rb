class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.119/CSharpFar-v1.0.119-osx-arm64.tar.gz"
      sha256 "596590d2da0ca42bfe3358b12b43421fa0050062dea02e20a822e5f51e8a5844"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.119/CSharpFar-v1.0.119-osx-x64.tar.gz"
      sha256 "147a74c2a0f006f338b1670f425a454975ced62da4052f6cacc0cff15b6e82b2"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
