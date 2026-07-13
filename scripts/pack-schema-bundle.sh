#!/usr/bin/env bash
#
# Packs the device-mapping field-contract bundle: config/schema/*.json +
# config/schema/vectors/*.json (golden vectors, ADR 0013 §5.2) + the schema
# CHANGELOG + a bundle manifest carrying schema_version/min_supported
# (Breaking-Bump-Rollout, ADR 0013 §2). Reproducible: pinned entry order,
# mtime, ownership and gzip -n, proven by a second pack that must be
# byte-identical.
#
# This is the SINGLE packing path, called by both `make release-assets`
# (scripts/build-release-assets.sh) and the tag-driven release workflow
# (.github/workflows/release.yml). The workflow used to carry an inline
# mirror of this logic; it drifted the moment §5.2 added the vectors —
# exactly the hand-mirror failure class ADR 0013 exists to end. Keep both
# callers on this script.
#
# Usage: pack-schema-bundle.sh <version-without-v> <source-date-epoch> <out-file>
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <version-without-v> <source-date-epoch> <out-file>" >&2
  exit 1
fi
version="$1"
epoch="$2"
out="$3"

# A malformed epoch makes tar --mtime="@" silently substitute a constant
# (warn, exit 0); the cmp self-check proves determinism, not timestamp
# correctness, so guard it here.
case "${epoch}" in
  ''|*[!0-9]*)
    echo "[pack-schema-bundle] SOURCE_DATE_EPOCH='${epoch}' is not a positive integer epoch" >&2
    exit 1
    ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"
mkdir -p "$(dirname "${out}")"

pack() {
  local stage="$1" target="$2"
  rm -rf "${stage}"
  mkdir -p "${stage}/schema/vectors"
  cp config/schema/*.json config/schema/CHANGELOG.md "${stage}/schema/"
  cp config/schema/vectors/*.json "${stage}/schema/vectors/"
  printf '{\n  "name": "bess-ems-device-mapping-schemas",\n  "version": "%s",\n  "schema_version": "v1",\n  "min_supported": "v1"\n}\n' "${version}" > "${stage}/schema/bundle.json"
  tar --sort=name --mtime="@${epoch}" --owner=0 --group=0 --numeric-owner \
    -C "${stage}" -cf - schema | gzip -n > "${target}"
  rm -rf "${stage}"
}

pack "${out}.stage" "${out}"
# Prove the acceptance criterion ("reproduzierbares Release-Asset"): a second
# independent pack from the same inputs must be byte-identical.
pack "${out}.stage-verify" "${out}.verify"
if ! cmp -s "${out}" "${out}.verify"; then
  echo "[pack-schema-bundle] schema bundle is not reproducible (bytes differ between two packs)" >&2
  exit 1
fi
rm -f "${out}.verify"
test -f "${out}"
echo "[pack-schema-bundle] ${out} packed reproducibly (schemas + vectors, epoch ${epoch})"
