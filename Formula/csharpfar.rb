class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.128/CSharpFar-v1.0.128-osx-arm64.tar.gz"
      sha256 "542e7dd2aca78d97de944a09ad82e04bf5919837812379e0e1371dfa18514c14"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.128/CSharpFar-v1.0.128-osx-x64.tar.gz"
      sha256 "6b8851e5496626d54fc1fe03729d31add08330035328967a35e10c13234e5561"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
