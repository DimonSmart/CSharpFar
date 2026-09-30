class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.81"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.81/CSharpFar-v1.0.81-osx-arm64.tar.gz"
    sha256 "8c296fb55f166ba04c2661a68d3e476481f713efeb650019466b56384a6d9c17"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.81/CSharpFar-v1.0.81-osx-x64.tar.gz"
    sha256 "add01c165f2449dcd5a51cc3fd95317dba1ad30b6344968ac9927543f63c7ae0"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
