#!/usr/bin/env bash
set -euo pipefail

solution="CSharpFar.slnx"

mapfile -t staged_files < <(
  git diff --cached --name-only --diff-filter=ACMR |
  grep -E '\.(cs|csproj|props|targets|sln|slnx|editorconfig)$' || true
)

if [ "${#staged_files[@]}" -eq 0 ]; then
  echo "No staged .NET files to format."
  exit 0
fi

echo "Formatting staged .NET files..."

format_input=()
for file in "${staged_files[@]}"; do
  if [ -f "$file" ]; then
    format_input+=("--include" "$file")
  fi
done

if [ "${#format_input[@]}" -eq 0 ]; then
  echo "No existing staged files to format."
  exit 0
fi

dotnet format "$solution" --no-restore "${format_input[@]}"

for file in "${staged_files[@]}"; do
  if [ -f "$file" ]; then
    git add "$file"
  fi
done

echo "Formatting completed."
