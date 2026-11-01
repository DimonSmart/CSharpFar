class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.112/CSharpFar-v1.0.112-osx-arm64.tar.gz"
      sha256 "caca1ddf4e798223a183a5648a7e395c43ba75bf1103ca0cc28bcf01bcb6b750"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.112/CSharpFar-v1.0.112-osx-x64.tar.gz"
      sha256 "bf94c004a0d0733029eaf73ee7fcbdf1980665d8f714612f09ddd1a2da237974"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
