class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.120/CSharpFar-v1.0.120-osx-arm64.tar.gz"
      sha256 "9d9a775e7c3f78b15e6b8c2b549b0d5cf3489d9c82b6fb90e1464458e883455a"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.120/CSharpFar-v1.0.120-osx-x64.tar.gz"
      sha256 "45d4218600c7b616e5d014a8a721a70be42b0468cff913a8c9f32885389df475"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
