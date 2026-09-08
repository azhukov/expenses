#!/usr/bin/env sh
#
# Reports every FE style, type and structure diagnostic in one run.
#
# `npm run style` chains its four gates with && and stops at the first failure, which is right for
# CI's exit code but wrong as a worklist. This runs all four regardless, and prints a count per
# diagnostic id followed by the file:line sites grouped by id.
#
# Ids are the tools' own: PRETTIER (a file the formatter would rewrite), an ESLint rule name, a
# TypeScript TSnnnn code, or an FEnnn structure rule from scripts/check-structure.mjs.
#
# Usage:
#   sh .claude/skills/fe-style-fix/diagnostics.sh [options]
#
#   --counts-only      skip the per-site listing
#   --dir PATH         default FE

set -eu

fe_dir="FE"
counts_only=0

while [ $# -gt 0 ]; do
    case "$1" in
        --counts-only) counts_only=1 ;;
        --dir)         shift; fe_dir="${1:?--dir needs a path}" ;;
        -h|--help)     sed -n '3,18p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *)             echo "unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done

cd "$fe_dir"

hits=$(mktemp)
raw=$(mktemp)
trap 'rm -f "$hits" "$raw"' EXIT

# Each gate appends: <id> TAB <file:line> TAB <message>

# Prettier — the unit is a file, not a line; there is nothing to fix by hand, so the message is
# the same for every hit.
npx prettier --list-different "src/**/*.{ts,tsx,css}" "e2e/**/*.ts" "scripts/**/*.mjs" "*.{ts,js,json,html}" \
    > "$raw" 2>/dev/null || true
sed 's|^|PRETTIER\t|; s|$|\tFile is not formatted — npm run format rewrites it|' "$raw" >> "$hits"

# ESLint — JSON, because the pretty formatter wraps long messages and loses the rule id off the end.
npx eslint . --format json > "$raw" 2>/dev/null || true
node -e '
const results = JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"))
const cwd = process.cwd().split("\\").join("/") + "/"
for (const file of results) {
  const path = file.filePath.split("\\").join("/").replace(cwd, "")
  for (const m of file.messages) {
    process.stdout.write(`${m.ruleId ?? "PARSE"}\t${path}:${m.line}\t${m.message.split("\n")[0]}\n`)
  }
}
' "$raw" >> "$hits" 2>/dev/null || true

# tsc — --force because a build-mode run reuses .tsbuildinfo and stays silent for projects it
# considers up to date, which reads as "clean" when it means "not checked".
npx tsc -b --force --pretty false > "$raw" 2>&1 || true
awk '
match($0, /\([0-9]+,[0-9]+\): error TS[0-9]+: /) {
    file = substr($0, 1, RSTART - 1)
    rest = substr($0, RSTART)
    lnum = rest; sub(/^\(/, "", lnum); sub(/,.*/, "", lnum)
    tail = rest; sub(/^[^:]*: error /, "", tail)
    id = tail; sub(/:.*/, "", id)
    msg = tail; sub(/^TS[0-9]+: /, "", msg)
    printf "%s\t%s:%s\t%s\n", id, file, lnum, msg
}
' "$raw" >> "$hits"

# Structure — already grouped by id; re-emit its site lines in the shared shape.
node scripts/check-structure.mjs > "$raw" 2>&1 || true
awk '
/^-- FE[0-9]+:/ { id = $2; sub(/:$/, "", id); msg = $0; sub(/^-- FE[0-9]+: /, "", msg); next }
/^   / && id    { path = $1; printf "%s\t%s\t%s\n", id, path, msg }
' "$raw" >> "$hits"

sort -u -o "$hits" "$hits"

if [ ! -s "$hits" ]; then
    echo "Clean: format, lint, types and structure all pass."
    exit 0
fi

echo "== counts by diagnostic =="
awk -F'\t' '
{ count[$1]++; if (!($1 in message)) message[$1] = $3 }
END { for (id in count) printf "%5d  %-42s %s\n", count[id], id, message[id] }
' "$hits" | sort -rn

[ "$counts_only" -eq 1 ] && exit 1

echo
echo "== sites =="
awk -F'\t' '{ count[$1]++ } END { for (id in count) print count[id], id }' "$hits" |
    sort -rn |
    while read -r _ id; do
        echo "-- $id"
        awk -F'\t' -v id="$id" '$1 == id { print $2 }' "$hits" |
            sort -t: -k1,1 -k2,2n |
            sed 's/^/   /'
    done

exit 1
