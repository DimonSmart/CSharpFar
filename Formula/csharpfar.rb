class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.80"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.80/CSharpFar-v1.0.80-osx-arm64.tar.gz"
    sha256 "4a3763258c84342e719f98daec6481d07c4968adf47290d5058245f18c12ea30"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.80/CSharpFar-v1.0.80-osx-x64.tar.gz"
    sha256 "f28170510b575b5af5dc2294d7e819a37551dbb5a342a9b5f7216d5d8586878c"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
