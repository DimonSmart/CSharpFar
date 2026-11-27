class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.130/CSharpFar-v1.0.130-osx-arm64.tar.gz"
      sha256 "b2674ccad3d64d174e1b2df71135f719c3a7f65149e5ae76ca6081171f3a36c9"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.130/CSharpFar-v1.0.130-osx-x64.tar.gz"
      sha256 "1b4035da92eb5df0c1da20b1ec697d8bc2bea13b2ccf573a64e501513e4f8ab5"
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
