class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.131/CSharpFar-v1.0.131-osx-arm64.tar.gz"
      sha256 "844aca00cdc33d599a293dce22c10ca240f3946efb9f27f0901e0409da8d0062"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.131/CSharpFar-v1.0.131-osx-x64.tar.gz"
      sha256 "7701a1f2db51ac123638a2d8b0673f41b6af4797a0ac3ef56064e7094116923e"
    end
  end

  def install
    libexec.install "csharpfar"
    libexec.install "bundled-extensions" if File.directory?("bundled-extensions")
    bin.install_symlink libexec/"csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
