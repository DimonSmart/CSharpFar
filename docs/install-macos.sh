#!/usr/bin/env bash
set -euo pipefail

CASK="dimonsmart/csharpfar/csharpfar-app"
TAP="dimonsmart/csharpfar"
TAP_URL="https://github.com/DimonSmart/CSharpFar.git"
APP="/Applications/CSharpFar.app"
BINARY="$APP/Contents/Resources/csharpfar"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "CSharpFar macOS installer can only be used on macOS." >&2
  exit 1
fi

BREW=""
if command -v brew >/dev/null 2>&1; then
  BREW_CANDIDATE="$(command -v brew)"
  if [[ -f "$BREW_CANDIDATE" && -x "$BREW_CANDIDATE" ]]; then
    if [[ "$BREW_CANDIDATE" = /* ]]; then
      BREW="$BREW_CANDIDATE"
    else
      BREW="$(cd "$(dirname "$BREW_CANDIDATE")" && pwd -P)/$(basename "$BREW_CANDIDATE")"
    fi
  fi
fi
if [[ -z "$BREW" && -x "/opt/homebrew/bin/brew" ]]; then
  BREW="/opt/homebrew/bin/brew"
fi
if [[ -z "$BREW" && -x "/usr/local/bin/brew" ]]; then
  BREW="/usr/local/bin/brew"
fi

if [[ -z "$BREW" ]]; then
  cat >&2 <<'EOF'
Homebrew is required to install CSharpFar, but it was not found.
Install Homebrew from https://brew.sh/ and run this installer again.

Official Homebrew install command:
  /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
EOF
  exit 1
fi

if ! "$BREW" tap | grep -Fxq "$TAP"; then
  "$BREW" tap "$TAP" "$TAP_URL"
fi

"$BREW" update

WAS_INSTALLED=false
PREVIOUS_VERSION=""
if "$BREW" list --cask --versions "$CASK" >/dev/null 2>&1; then
  WAS_INSTALLED=true
  if [[ -x "$BINARY" ]]; then
    PREVIOUS_VERSION="$("$BINARY" --version 2>/dev/null || true)"
  fi

  "$BREW" upgrade     --cask     --appdir=/Applications     "$CASK"
else
  if [[ -e "$APP" ]]; then
    cat >&2 <<'EOF'
CSharpFar.app already exists in /Applications but is not managed by Homebrew.

Remove or move the existing application and run this installer again.
EOF
    exit 1
  fi

  "$BREW" install     --cask     --appdir=/Applications     "$CASK"
fi

/usr/bin/xattr -dr com.apple.quarantine "$APP" >/dev/null 2>&1 || true

if ! XATTR_OUTPUT="$(/usr/bin/xattr -r -l "$APP" 2>&1)"; then
  cat >&2 <<'EOF'
CSharpFar was installed or updated, but the macOS quarantine verification step failed.

Run this command manually and start CSharpFar again:
  xattr -dr com.apple.quarantine /Applications/CSharpFar.app
EOF
  exit 1
fi

if printf '%s\n' "$XATTR_OUTPUT" | grep -Fq "com.apple.quarantine"; then
  cat >&2 <<'EOF'
CSharpFar was installed or updated, but macOS quarantine could not be removed.

Run this command manually and start CSharpFar again:
  xattr -dr com.apple.quarantine /Applications/CSharpFar.app
EOF
  exit 1
fi

if [[ ! -x "$BINARY" ]]; then
  echo "CSharpFar package installation completed, but the installed executable is missing." >&2
  exit 1
fi

VERSION_OUTPUT="$("$BINARY" --version)"
if [[ "$WAS_INSTALLED" == true && -n "$PREVIOUS_VERSION" && "$PREVIOUS_VERSION" == "$VERSION_OUTPUT" ]]; then
  echo "$VERSION_OUTPUT is already installed."
else
  echo "$VERSION_OUTPUT installed successfully."
fi
