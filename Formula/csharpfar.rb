class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "1.0.68"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.68/CSharpFar-v1.0.68-osx-arm64.tar.gz"
    sha256 "fcff59f1110137d56eeff99f3fc5d7715bf3cff9463320f63b2847b356963f30"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.68/CSharpFar-v1.0.68-osx-x64.tar.gz"
    sha256 "66c63ebacff66879bff98296ff64f946aa315ecff78360d87bef865227e1d943"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
