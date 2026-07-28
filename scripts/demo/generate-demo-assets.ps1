param(
    [string]$Fixture = "docs/demo/filesystem",
    [string]$Scenario = "scripts/demo/readme-demo.json",
    [string]$Output = "artifacts/demo"
)

$ErrorActionPreference = "Stop"

dotnet run --project src/CSharpFar.DemoRecorder/CSharpFar.DemoRecorder.csproj -- `
    --fixture $Fixture `
    --scenario $Scenario `
    --output $Output
