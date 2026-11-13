#!/usr/bin/env bash
set -euo pipefail

if (( $# != 2 )); then
  echo "Usage: $0 <CSharpFar.app> <arm64|x86_64>" >&2
  exit 2
fi

app="$1"
expected_arch="$2"

case "$expected_arch" in
  arm64|x86_64) ;;
  *) echo "Unsupported macOS architecture: $expected_arch" >&2; exit 2 ;;
esac

fail() {
  echo "::error::$*" >&2
  exit 1
}

plist="$app/Contents/Info.plist"
/usr/bin/plutil -lint "$plist" >/dev/null || fail "Invalid Info.plist: $plist"

entry_name="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$plist")"
[[ "$entry_name" == "CSharpFarHost" ]] ||
  fail "CFBundleExecutable must name the native CSharpFarHost, not '$entry_name'"

host="$app/Contents/MacOS/$entry_name"
cli="$app/Contents/Resources/csharpfar"
[[ -x "$host" ]] || fail "Native app entry point is missing or not executable: $host"
[[ -x "$cli" ]] || fail "Bundled CLI is missing or not executable: $cli"
[[ ! -e "$app/Contents/MacOS/CSharpFar" ]] ||
  fail "Obsolete script launcher must not ship in the application bundle"

for binary in "$host" "$cli"; do
  kind="$(/usr/bin/file -b "$binary")"
  [[ "$kind" == *Mach-O* ]] || fail "Expected a native Mach-O executable: $binary ($kind)"
  archs="$(/usr/bin/lipo -archs "$binary")"
  [[ "$archs" == "$expected_arch" ]] ||
    fail "Wrong executable architecture for $binary: '$archs', expected '$expected_arch'"
done

macho_count=0
while IFS= read -r -d '' candidate; do
  kind="$(/usr/bin/file -b "$candidate")"
  [[ "$kind" == *Mach-O* ]] || continue
  archs="$(/usr/bin/lipo -archs "$candidate")"
  [[ " $archs " == *" $expected_arch "* ]] ||
    fail "Incompatible Mach-O dependency: $candidate ($archs); expected $expected_arch"
  ((macho_count += 1))
done < <(/usr/bin/find "$app" -type f -print0)

(( macho_count >= 2 )) || fail "Expected at least two native Mach-O executables in the bundle"
echo "Verified native $expected_arch app entry point and $macho_count bundled Mach-O files."
