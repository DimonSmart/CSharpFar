#!/usr/bin/env sh
set -eu

repo_url="https://dimonsmart.github.io/CSharpFar/apt"
keyring="/etc/apt/keyrings/csharpfar.gpg"
source_list="/etc/apt/sources.list.d/csharpfar.list"
package_name="csharpfar"

tmp_key=""

cleanup() {
  if [ -n "$tmp_key" ] && [ -f "$tmp_key" ]; then
    rm -f "$tmp_key"
  fi
}

trap cleanup EXIT INT TERM

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Required command not found: $1" >&2
    echo "This installer supports Debian/Ubuntu-based distributions with APT." >&2
    exit 1
  fi
}

sudo_if_needed() {
  if [ "$(id -u)" -eq 0 ]; then
    "$@"
  else
    if ! command -v sudo >/dev/null 2>&1; then
      echo "This installer requires root privileges. Please install sudo or run the script as root." >&2
      exit 1
    fi

    sudo "$@"
  fi
}

require_command apt-get
require_command dpkg
require_command curl
require_command install
require_command tee
require_command mktemp

arch="$(dpkg --print-architecture)"

case "$arch" in
  amd64)
    ;;
  *)
    echo "Unsupported architecture: $arch" >&2
    echo "Currently CSharpFar APT packages are published only for amd64." >&2
    exit 1
    ;;
esac

echo "Installing CSharpFar APT repository..."

sudo_if_needed install -m 0755 -d /etc/apt/keyrings

echo "Adding repository key..."
tmp_key="$(mktemp)"
curl -fsSL "$repo_url/csharpfar-archive-keyring.gpg" -o "$tmp_key"
sudo_if_needed install -m 0644 "$tmp_key" "$keyring"

echo "Adding APT source..."
printf '%s\n' "deb [arch=$arch signed-by=$keyring] $repo_url stable main" \
  | sudo_if_needed tee "$source_list" >/dev/null

echo "Updating package index..."
sudo_if_needed apt-get update

echo "Installing $package_name..."
sudo_if_needed apt-get install -y "$package_name"

echo "Installed:"
if ! "$package_name" --version; then
  echo "Warning: $package_name was installed, but '$package_name --version' failed." >&2
fi
