#!/usr/bin/env sh
#
# Reports the .editorconfig / analyzer diagnostics BE currently emits.
#
# Builds Expenses.sln with TreatWarningsAsErrors off so the build reaches the end and every
# diagnostic is emitted, instead of stopping at the first error. Prints a count per diagnostic
# id, then the deduplicated file:line list grouped by id.
#
# Usage:
#   sh .claude/skills/be-editorconfig-fix/diagnostics.sh [options]
#
#   --ide0005          also sets GenerateDocumentationFile=true, which is what IDE0005
#                      (unnecessary using) needs to run at all, and mutes the XML-doc
#                      warnings that switch brings with it
#   --counts-only      skip the per-site listing
#   --solution PATH    default BE/Expenses.sln

set -eu

solution="BE/Expenses.sln"
ide0005=0
counts_only=0

while [ $# -gt 0 ]; do
    case "$1" in
        --ide0005)     ide0005=1 ;;
        --counts-only) counts_only=1 ;;
        --solution)    shift; solution="${1:?--solution needs a path}" ;;
        -h|--help)     sed -n '3,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *)             echo "unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

set -- build "$solution" -p:TreatWarningsAsErrors=false --no-incremental -v:q -nologo

if [ "$ide0005" -eq 1 ]; then
    set -- "$@" -p:GenerateDocumentationFile=true
    # %3B is MSBuild's escape for ';' — an unescaped one is read as the end of the switch.
    set -- "$@" -p:NoWarn=CS1591%3BCS1587
fi

raw=$(mktemp)
hits=$(mktemp)
trap 'rm -f "$raw" "$hits"' EXIT

build_exit=0
dotnet "$@" > "$raw" 2>&1 || build_exit=$?

# Diagnostic lines look like:
#   C:\...\BE\Expenses.Domain\Purchase.cs(71,34): warning IDE2006: Blank line ... (https://...) [C:\...csproj]
# Emit them as: <id> TAB <path-relative-to-BE>:<line> TAB <message>
awk '
match($0, /\([0-9]+,[0-9]+\): (warning|error) [A-Za-z]+[0-9]+: /) {
    file = substr($0, 1, RSTART - 1)
    rest = substr($0, RSTART)

    lnum = rest; sub(/^\(/, "", lnum); sub(/,.*/, "", lnum)

    tail = rest; sub(/^[^:]*: (warning|error) /, "", tail)
    id = tail; sub(/:.*/, "", id)

    msg = tail; sub(/^[A-Za-z]+[0-9]+: /, "", msg)
    sub(/ \(https:\/\/.*/, "", msg)      # drop the docs link
    sub(/ \[[^]]*\]$/, "", msg)          # drop the trailing [project.csproj]

    sub(/^.*[\/\\]BE[\/\\]/, "", file)   # paths are absolute and often backslashed
    gsub(/\\/, "/", file)

    printf "%s\t%s:%s\t%s\n", id, file, lnum, msg
}
' "$raw" | sort -u > "$hits"

if [ ! -s "$hits" ]; then
    if [ "$build_exit" -ne 0 ]; then
        # No diagnostics parsed AND a failed build means the build never got far enough to
        # analyze anything — that is not a clean tree.
        echo "Build failed before producing diagnostics (exit $build_exit). Raw output:"
        cat "$raw"
        exit 1
    fi
    echo "Clean: no analyzer or code-style diagnostics."
    exit 0
fi

echo "== counts by diagnostic =="
awk -F'\t' '
{ count[$1]++; if (!($1 in message)) message[$1] = $3 }
END { for (id in count) printf "%5d  %-10s %s\n", count[id], id, message[id] }
' "$hits" | sort -rn

[ "$counts_only" -eq 1 ] && exit 1

echo
echo "== sites =="
awk -F'\t' '{ count[$1]++ } END { for (id in count) print count[id], id }' "$hits" |
    sort -rn |
    while read -r _ id; do
        echo "-- $id"
        # -k2,2n so line 67 sorts before line 106 within a file, not after it.
        awk -F'\t' -v id="$id" '$1 == id { print $2 }' "$hits" |
            sort -t: -k1,1 -k2,2n |
            sed 's/^/   /'
    done

# Non-zero whenever the tree has diagnostics, so an && chain or a hook can gate on this.
exit 1
