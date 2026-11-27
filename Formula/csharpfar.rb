class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.133/CSharpFar-v1.0.133-osx-arm64.tar.gz"
      sha256 "b620c4bde48a0884869068a6d2148baed11b84ec43eb83f4f3f3282f4211abe8"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.133/CSharpFar-v1.0.133-osx-x64.tar.gz"
      sha256 "d58b71d2175cdaabb4ba77956cedffe4a327490357160a1c388f4d374fa7a038"
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
