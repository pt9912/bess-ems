#!/usr/bin/env bash
# Bricht ab, wenn die Total-Line-Coverage unter dem Threshold liegt.
# Verwendung:
#   bash scripts/coverage-gate.sh <go-tool-cover-func-file> [<threshold-percent>]
#
# Default-Threshold ist 90 (analog .NET-Coverage in docs/user/quality.md §3).

set -euo pipefail

usage() {
    echo "usage: $(basename "$0") <coverage-func-file> [<threshold-percent>]" >&2
    echo "  Default threshold: 90" >&2
    exit 2
}

if [[ $# -lt 1 || $# -gt 2 ]]; then
    usage
fi

func_file="$1"
threshold="${2:-90}"

if [[ ! -f "$func_file" ]]; then
    echo "coverage-gate: input file not found: $func_file" >&2
    exit 2
fi

total_line="$(tail -n1 "$func_file")"
total_pct="$(awk '{print $NF}' <<<"$total_line" | tr -d '%')"

if [[ -z "$total_pct" ]]; then
    echo "coverage-gate: could not parse total from: $total_line" >&2
    exit 2
fi

if awk -v have="$total_pct" -v want="$threshold" 'BEGIN { exit (have+0 >= want+0) ? 0 : 1 }'; then
    echo "coverage-gate: ${total_pct}% >= ${threshold}% — OK"
    exit 0
fi

echo "coverage-gate: ${total_pct}% < ${threshold}% — FAIL" >&2
exit 1
