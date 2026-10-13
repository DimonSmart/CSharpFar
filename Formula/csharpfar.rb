class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.94/CSharpFar-v1.0.94-osx-arm64.tar.gz"
      sha256 "0363dab58c357530acd2307ba81626815b7e9b2cd27ac4599166276a1add09a9"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.94/CSharpFar-v1.0.94-osx-x64.tar.gz"
      sha256 "43bcfbbfd3b1d6a5490840be496bdf34021db971ff2b1ac211edc726b2b7112e"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
