class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.75"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.75/CSharpFar-v1.0.75-osx-arm64.tar.gz"
    sha256 "927309019e654537c44be45e2d0e9f049f768f1841264173b7613310cb6f1e38"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.75/CSharpFar-v1.0.75-osx-x64.tar.gz"
    sha256 "b9c132c4f60c4e5fc8ad5179abac0dc98c73f39324cd2849b83401afcc77ddc1"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
