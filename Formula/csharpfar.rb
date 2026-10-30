class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.107/CSharpFar-v1.0.107-osx-arm64.tar.gz"
      sha256 "007b9065bc771ccdc104600910e24a76ea6a48c937b83a12622c3beb2246e69f"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.107/CSharpFar-v1.0.107-osx-x64.tar.gz"
      sha256 "7cb8745d57a260cd293c1f7264db6670be33ae8ac97f05cbdb234883f89c73db"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
