class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.117/CSharpFar-v1.0.117-osx-arm64.tar.gz"
      sha256 "92c6572ef346f03560f4a2faa6ba9fe7f3bf7d57b6418b9a2569dad494cea03a"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.117/CSharpFar-v1.0.117-osx-x64.tar.gz"
      sha256 "54a383d286cb2eae7e833939633a8361e54888f1e3ab8365182c16de1ec8bd17"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
