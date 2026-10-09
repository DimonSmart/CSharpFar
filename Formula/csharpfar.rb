class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  depends_on :macos

  on_macos do
    on_arm do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.91/CSharpFar-v1.0.91-osx-arm64.tar.gz"
      sha256 "4cd2feebd6212d93d8c83301e6f86f58efbd6aa0bdfc79a2698dc48e20d3e1ea"
    end

    on_intel do
      url "https://github.com/DimonSmart/CSharpFar/releases/download/v1.0.91/CSharpFar-v1.0.91-osx-x64.tar.gz"
      sha256 "842309c58ff9e8ab30df09909871f8044c4f5407b3035f993f9b86be69c03262"
    end
  end

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
