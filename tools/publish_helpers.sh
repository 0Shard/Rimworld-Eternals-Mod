#!/bin/bash

# Local operations only: sourcing this file cannot enter the publication workflow.
publish_is_private_path() {
    case "/$1/" in
        */AGENTS.md/*|*/CLAUDE.md/*|*/.agents/*|*/.codex/*|*/.claude/*|*/.planning/*|*/.gsd/*|*/.mcp.json/*|*/.workshop-config/*)
            return 0 ;;
    esac
    return 1
}

publish_assert_public_tree() {
    local directory="$1" path
    while IFS= read -r -d '' path; do
        if publish_is_private_path "${path#"$directory"/}"; then
            echo "ERROR: private path in public export: ${path#"$directory"/}" >&2
            return 1
        fi
    done < <(find "$directory" -mindepth 1 -print0)
}

publish_assert_public_index() {
    local repo="$1" path
    while IFS= read -r -d '' path; do
        if publish_is_private_path "$path"; then
            echo "ERROR: private path in public index: $path" >&2
            return 1
        fi
    done < <(git -C "$repo" ls-files -z)
}

publish_stage_export() {
    local repo="$1" destination="$2" pattern
    local -a exclusions=(CLAUDE.md .claude .planning Eternal/.planning
        Eternal/Source/Eternal/.fossil Example RimWorld RimWorld-Decompiled Mods docs
        'tmpclaude-*' publish.sh workshop-upload.sh deploy-to-rimworld.sh .gsd)
    git -C "$repo" archive HEAD | tar -x -C "$destination" || return
    if [[ -f "$repo/Eternal/About/Preview.png" && ! -f "$destination/Eternal/About/Preview.png" ]]; then
        cp "$repo/Eternal/About/Preview.png" "$destination/Eternal/About/Preview.png" || return
    fi
    for pattern in "${exclusions[@]}"; do
        find "$destination" -path "$destination/$pattern" -prune -exec rm -rf -- {} + || return
    done
    # Apply the private agent/config boundary at every depth, including tracked files.
    find "$destination" -mindepth 1 \( -name AGENTS.md -o -name CLAUDE.md \
        -o -name .agents -o -name .codex -o -name .claude -o -name .planning \
        -o -name .gsd -o -name .mcp.json -o -name .workshop-config \) \
        -prune -exec rm -rf -- {} + || return
    publish_assert_public_tree "$destination"
}

publish_create_archive() (
    set -euo pipefail
    local package="$1" destination="$2" backend="${3:-auto}" staging package_name
    [[ -d "$package" ]] || { echo "ERROR: package directory missing" >&2; return 1; }
    package=$(cd -- "$package" && pwd)
    destination="$(cd -- "$(dirname -- "$destination")" && pwd)/$(basename -- "$destination")"
    package_name=$(basename -- "$package")
    staging=$(mktemp -d "$(dirname -- "$destination")/.eternal-archive.XXXXXXXX")
    trap 'rm -rf -- "$staging"' EXIT
    (cd -- "$(dirname -- "$package")" && find -L "$package_name" -type f | LC_ALL=C sort) > "$staging/expected"
    if [[ "$backend" == auto ]]; then
        if command -v zip >/dev/null; then backend=zip; else backend=pwsh; fi
    fi
    if [[ "$backend" == zip ]]; then
        (cd -- "$(dirname -- "$package")" && zip -qr "$staging/package.zip" "$package_name/")
        zip -T "$staging/package.zip" >/dev/null
        LC_ALL=C zip -sf "$staging/package.zip" | sed -n 's/^  //p' | \
            sed '/\/$/d' | LC_ALL=C sort > "$staging/actual"
    elif [[ "$backend" == pwsh ]]; then
        PUBLISH_PACKAGE="$package" PUBLISH_ARCHIVE="$staging/package.zip" \
            PUBLISH_MANIFEST="$staging/actual" pwsh -NoProfile -Command '
                $ErrorActionPreference = "Stop"
                Compress-Archive -Path $env:PUBLISH_PACKAGE -DestinationPath $env:PUBLISH_ARCHIVE
                $archive = [IO.Compression.ZipFile]::OpenRead($env:PUBLISH_ARCHIVE)
                try {
                    $names = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith("/") } | ForEach-Object { $_.FullName.Replace("\", "/") })
                    [IO.File]::WriteAllLines($env:PUBLISH_MANIFEST, $names, [Text.UTF8Encoding]::new($false))
                    foreach ($entry in $archive.Entries) { $stream = $entry.Open(); try { $stream.CopyTo([IO.Stream]::Null) } finally { $stream.Dispose() } }
                } finally { $archive.Dispose() }
            '
        LC_ALL=C sort -o "$staging/actual" "$staging/actual"
    else
        echo "ERROR: unknown archive backend: $backend" >&2
        return 1
    fi
    diff -u "$staging/expected" "$staging/actual"
    # The candidate lives on the destination filesystem; rename only verified output.
    mv -f -- "$staging/package.zip" "$destination"
)
