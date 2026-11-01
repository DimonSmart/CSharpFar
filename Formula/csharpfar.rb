class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.109/CSharpFar-v1.0.109-osx-arm64.tar.gz"
      sha256 "84db9c2ac6c89059f66def9c1ac6badcf7b6278baaa148ae694072cab988dd79"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.109/CSharpFar-v1.0.109-osx-x64.tar.gz"
      sha256 "db76a7738f2d7c6aa4359b828787317c53e4b3b049d85bd09be96d6aa28c3485"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
