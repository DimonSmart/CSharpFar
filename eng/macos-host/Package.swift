// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "CSharpFarDesktopHost",
    platforms: [.macOS(.v11)],
    products: [.executable(name: "CSharpFarHost", targets: ["CSharpFarHost"])],
    dependencies: [
        .package(url: "https://github.com/migueldeicaza/SwiftTerm.git", exact: "1.14.0")
    ],
    targets: [
        .executableTarget(
            name: "CSharpFarHost",
            dependencies: [.product(name: "SwiftTerm", package: "SwiftTerm")],
            path: "Sources")
    ]
)
