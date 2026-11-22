class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.125/CSharpFar-v1.0.125-osx-arm64.tar.gz"
      sha256 "1f8e072a9db96f1c6922acb7391ff8dfae3da7eb93b593ae79087af4ad6a16c6"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.125/CSharpFar-v1.0.125-osx-x64.tar.gz"
      sha256 "e5962a75aba525a1f42e3ce83e3ec971a985cbf83df4f67e70895ad1efd3e665"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
