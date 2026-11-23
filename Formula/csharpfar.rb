class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.126/CSharpFar-v1.0.126-osx-arm64.tar.gz"
      sha256 "cd7497e1a6b7187c8d0bd397eecfac926377fc8f35b50674120f2007d367c5dd"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.126/CSharpFar-v1.0.126-osx-x64.tar.gz"
      sha256 "989149e54456acf9f849a21695feb0e12aaa89231a4bd21607c7eb12594a59c4"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
