# Shared helpers, sourced by build.sh and deploy.sh.

# Print the comment block at the top of a script, without the shebang.
usage() {
    sed -n '2,/^[^#]/p' "$1" | sed '$d' | sed 's/^# \{0,1\}//'
}

# One source of truth for the version: <Version> in Directory.Build.props,
# overridable with --version. It must be numeric because it also becomes the
# assembly version.
resolve_version() {
    local repo="$1" version="$2"
    if [ -z "$version" ]; then
        version="$(sed -n 's:.*<Version>[[:space:]]*\([^<[:space:]]*\)[[:space:]]*</Version>.*:\1:p' "$repo/Directory.Build.props" | head -n 1)"
        if [ -z "$version" ]; then
            echo "No <Version> in $repo/Directory.Build.props. Add one or pass --version." >&2
            return 1
        fi
    fi
    if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ ]]; then
        echo "Version '$version' is not numeric (expected 1.2.3 or 1.2.3.4)." >&2
        return 1
    fi
    printf '%s\n' "$version"
}

resolve_dotnet() {
    if [ -n "${DOTNET:-}" ]; then
        if [ -x "$DOTNET" ] || command -v "$DOTNET" >/dev/null 2>&1; then printf '%s\n' "$DOTNET"; return 0; fi
        echo "dotnet not found at DOTNET='$DOTNET'." >&2
        return 1
    fi
    if command -v dotnet >/dev/null 2>&1; then command -v dotnet; return 0; fi
    if [ -x "$HOME/.dotnet/dotnet" ]; then printf '%s\n' "$HOME/.dotnet/dotnet"; return 0; fi
    echo "The .NET SDK was not found. Install .NET SDK 10 (see global.json), add dotnet to PATH, or set DOTNET." >&2
    return 1
}

sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d ' ' -f 1
    else shasum -a 256 "$1" | cut -d ' ' -f 1; fi
}
