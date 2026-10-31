class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.108/CSharpFar-v1.0.108-osx-arm64.tar.gz"
      sha256 "fbc856f10362f8664e93ce5296e98ca9c485abe110789d465c7e5a3913253ccf"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.108/CSharpFar-v1.0.108-osx-x64.tar.gz"
      sha256 "4ead04a6fd491682321c146c7b22ec8b25194511a49cbe7befe53441ecbcc4e1"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
