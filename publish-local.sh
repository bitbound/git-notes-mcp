#!/usr/bin/env bash
set -euo pipefail

package_id="Bitbound.GitNotesMcp"
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_file="$repo_dir/Bitbound.GitNotesMcp/Bitbound.GitNotesMcp.csproj"
local_source="${NUGET_LOCAL_SOURCE:-$HOME/.nuget/local-packages}"

mkdir -p "$local_source"

highest=""
for package in "$local_source/$package_id".*.nupkg; do
    [[ -e "$package" ]] || continue
    base="$(basename "$package" .nupkg)"
    found="${base#"$package_id".}"
    [[ "$found" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || continue

    if [[ -z "$highest" ]] || [[ "$(printf '%s\n%s\n' "$highest" "$found" | sort -V | tail -n 1)" == "$found" ]]; then
        highest="$found"
    fi
done

if [[ -n "$highest" ]]; then
    IFS=. read -r major minor patch <<< "$highest"
    version="$major.$minor.$((patch + 1))"
else
    version="1.0.0"
fi

dotnet pack "$project_file" -c Release -p:VersionPrefix="$version" -o "$local_source"

echo
echo "Published $package_id $version"
echo "Source   : $local_source"
echo "Package  : $local_source/$package_id.$version.nupkg"
