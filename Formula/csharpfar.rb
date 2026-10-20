class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.103/CSharpFar-v1.0.103-osx-arm64.tar.gz"
      sha256 "187dd92293624459f54abebc7c04b727d24415091074de3f4604ffb60b3facb1"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.103/CSharpFar-v1.0.103-osx-x64.tar.gz"
      sha256 "7a6f0b7b5bc89dae13d13c3384ec7f636ec0aab0dfb0b9b04b859a5f9f9d120d"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
