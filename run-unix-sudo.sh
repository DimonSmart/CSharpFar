#!/usr/bin/env bash
set -euo pipefail

export PATH="$PATH:$HOME/.dotnet:/usr/share/dotnet:/usr/local/share/dotnet:/snap/bin"

dotnet_path="$(command -v dotnet || true)"
if [[ -z "$dotnet_path" ]]; then
    echo "Linux dotnet SDK was not found in this WSL distribution."
    echo "Installing dotnet-sdk-10.0 with apt. sudo may ask for your Ubuntu password."
    sudo apt-get update
    sudo apt-get install -y dotnet-sdk-10.0
    hash -r
    dotnet_path="$(command -v dotnet || true)"
fi

if [[ -z "$dotnet_path" ]]; then
    echo "dotnet is still not available after installation attempt." >&2
    exit 127
fi

"$dotnet_path" build src/CSharpFar.Host.Unix/CSharpFar.Host.Unix.csproj -c Debug

echo "Requesting sudo before launching CSharpFar..."
sudo -v
echo "Launching CSharpFar with sudo:"
sudo id

sudo env \
    PATH="$PATH" \
    TERM="${TERM:-xterm-256color}" \
    DOTNET_ROOT="${DOTNET_ROOT:-}" \
    "$dotnet_path" ./src/CSharpFar.Host.Unix/bin/Debug/net10.0/csharpfar.dll
