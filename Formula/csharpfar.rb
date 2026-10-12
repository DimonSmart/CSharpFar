class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.92/CSharpFar-v1.0.92-osx-arm64.tar.gz"
      sha256 "ed813588473ffeb3b7581f10936b7073c8677be04573dad3e58e16e672b1c912"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.92/CSharpFar-v1.0.92-osx-x64.tar.gz"
      sha256 "a4eb8baaa2ee1d6c486b3ad588e5efcaeb44f312ba6d5fe0c3233db5313a1c0e"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
