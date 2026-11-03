class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.115/CSharpFar-v1.0.115-osx-arm64.tar.gz"
      sha256 "43b8859f3e3b83a0393286dc196e339de1657e4ea176f62af28be8e24106e1ee"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.115/CSharpFar-v1.0.115-osx-x64.tar.gz"
      sha256 "ce4cabeb2326fd931f07ef51574adf4b5b05cbbd775ce823f9b32dba0ffcfcc5"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
