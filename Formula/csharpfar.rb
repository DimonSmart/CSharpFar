class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.105/CSharpFar-v1.0.105-osx-arm64.tar.gz"
      sha256 "1beb65815b0dd05958afadaca99af6d7aae4247f9139634e7ff3f2b2fa837a14"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.105/CSharpFar-v1.0.105-osx-x64.tar.gz"
      sha256 "16b3705c2ef63285e1a0baf2379e31d487168adea5048409869e6c8513a54bd3"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
