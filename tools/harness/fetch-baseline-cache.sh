#!/usr/bin/env bash
# fetch-baseline-cache.sh — materialisiert/verifiziert das committet-vendored
# AI-Harness-Betriebsregelwerk (ADR 0014, MR-004).
#
# Modi:
#   (ohne Arg)        re-vendor: lädt das Release-Bundle lab-regelwerk.zip,
#                     entpackt nach .harness/baseline/<tag>/regelwerk/, erzeugt
#                     SHA256SUMS neu und verifiziert. BRAUCHT NETZ. Trigger:
#                     Baseline-Bump (neuer Tag in harness/conventions.md §Baseline).
#   --verify [<tag>]  nur offline-Integritätsprüfung (sha256sum -c). Kein Netz —
#                     für CI, Audit, frischen Checkout.
#
# Tag = Single Source of Truth aus harness/conventions.md §Baseline
# (Zeile "**Stand:** vX.Y.Z"), alternativ explizit als Argument vX.Y.Z.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

repo="pt9912/ai-harness-course"
conventions="harness/conventions.md"

derive_tag() {
  local t=""
  if [ -f "$conventions" ]; then
    t="$(grep -m1 '\*\*Stand:\*\*' "$conventions" \
       | grep -oE 'v[0-9]+\.[0-9]+\.[0-9]+' | head -1 || true)"
  fi
  printf '%s' "$t"
}

mode="revendor"
tag=""
while [ $# -gt 0 ]; do
  case "$1" in
    --verify) mode="verify" ;;
    v[0-9]*)  tag="$1" ;;
    -h|--help) grep -E '^#( |$)' "$0" | sed -E 's/^# ?//'; exit 0 ;;
    *) echo "fetch-baseline-cache: unbekanntes Argument '$1'" >&2; exit 2 ;;
  esac
  shift
done

[ -n "$tag" ] || tag="$(derive_tag)"
if ! printf '%s' "$tag" | grep -qE '^v[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo "fetch-baseline-cache: kein gültiger Tag (harness/conventions.md §Baseline" \
       "oder Argument vX.Y.Z)" >&2
  exit 2
fi

baseline=".harness/baseline/${tag}"
sums="${baseline}/SHA256SUMS"

verify() {
  command -v sha256sum >/dev/null 2>&1 || { echo "fetch-baseline-cache: sha256sum fehlt" >&2; exit 2; }
  [ -f "$sums" ] || { echo "fetch-baseline-cache: $sums fehlt — erst re-vendor" >&2; exit 1; }
  echo "fetch-baseline-cache: verify ${baseline}/regelwerk gegen SHA256SUMS"
  ( cd "$baseline" && sha256sum -c SHA256SUMS )
  echo "fetch-baseline-cache: verify ok"
}

revendor() {
  command -v curl  >/dev/null 2>&1 || { echo "fetch-baseline-cache: curl fehlt"  >&2; exit 2; }
  command -v unzip >/dev/null 2>&1 || { echo "fetch-baseline-cache: unzip fehlt" >&2; exit 2; }
  local url="https://github.com/${repo}/releases/download/${tag}/lab-regelwerk.zip"
  local tmp; tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT
  echo "fetch-baseline-cache: lade $url"
  curl -fsSL -o "$tmp/lab-regelwerk.zip" "$url"
  rm -rf "${baseline}/regelwerk"
  mkdir -p "${baseline}/regelwerk"
  unzip -oq "$tmp/lab-regelwerk.zip" -d "${baseline}/regelwerk"
  ( cd "$baseline" && sha256sum regelwerk/*.md > SHA256SUMS )
  echo "fetch-baseline-cache: ${baseline}/regelwerk (re)materialisiert, SHA256SUMS erzeugt"
  verify
}

case "$mode" in
  verify)   verify ;;
  revendor) revendor ;;
esac
