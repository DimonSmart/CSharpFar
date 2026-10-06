class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.88/CSharpFar-v1.0.88-osx-arm64.tar.gz"
      sha256 "00ec255c908e9a69a7908aa087a5a031e8fd5769e17e8569fe0b1b9ec672e426"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.88/CSharpFar-v1.0.88-osx-x64.tar.gz"
      sha256 "bf4eb7172bb074115a36f0cf6f17ed7f75dd0f28bb492b5a5e6502a8764cb463"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
