#!/usr/bin/env bash
#
# Build the file-set published as a release: Helm chart tarball, source
# tarball, native library + header, SBOM, image-inspect, SHA256SUMS.
#
# Used by `make release-assets` for the local dry-run. The GitHub
# Actions release workflow runs the same steps inline because it
# additionally pushes/signs/attests against external infrastructure
# that has no business running on a developer machine.
#
# Required environment:
#   VERSION       — release tag (vMAJOR.MINOR.PATCH[-PRERELEASE])
#   RELEASE_DIR   — output directory (will be wiped and re-created)
#   IMAGE_PREFIX  — local Docker image prefix (default: bess-ems)
#   HELM_CHART    — path to Helm chart (default: deploy/helm/bess-ems)
#   HELM          — Helm binary (default: helm)
#   DOCKER        — Docker binary (default: docker)
#   SYFT_IMAGE    — Syft image for SBOM (default: anchore/syft:v1.17.0)
#
# This script aborts on first error and asserts that every expected
# artefact exists before printing SHA256SUMS — partial success would
# undermine the whole point of a local dry-run.
set -euo pipefail

: "${VERSION:?VERSION is required, e.g. VERSION=v1.0.0}"
: "${RELEASE_DIR:?RELEASE_DIR is required, e.g. RELEASE_DIR=artifacts/release-local}"
IMAGE_PREFIX="${IMAGE_PREFIX:-bess-ems}"
HELM_CHART="${HELM_CHART:-deploy/helm/bess-ems}"
HELM="${HELM:-helm}"
DOCKER="${DOCKER:-docker}"
SYFT_IMAGE="${SYFT_IMAGE:-anchore/syft:v1.17.0}"
ALLOW_DIRTY="${ALLOW_DIRTY:-0}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

scripts/validate-release-version.sh "${VERSION}"
vbare="${VERSION#v}"
image="${IMAGE_PREFIX}-runtime:latest"

# Guard RELEASE_DIR: this script wipes it with `rm -rf`, so a typo or
# accidental override like RELEASE_DIR=docs would nuke tracked repo
# content. Two layers:
#   1. Literal-shape check: must start with `artifacts/`, no absolute
#      paths, no `..` segments.
#   2. Resolved-path containment check: after canonicalisation the
#      directory must sit under ${repo_root}/artifacts. Catches symlink
#      games and oddities the literal check misses.
case "${RELEASE_DIR}" in
  /*|*..*|""|artifacts|artifacts/)
    echo "[build-release-assets] RELEASE_DIR='${RELEASE_DIR}' rejected — must be a path under 'artifacts/' (no absolute paths, no '..', no bare 'artifacts')" >&2
    exit 1
    ;;
  artifacts/*) : ;;
  *)
    echo "[build-release-assets] RELEASE_DIR='${RELEASE_DIR}' rejected — must start with 'artifacts/'" >&2
    exit 1
    ;;
esac
release_abs="$(realpath -m "${RELEASE_DIR}")"
artifacts_abs="$(realpath -m "${repo_root}/artifacts")"
case "${release_abs}/" in
  "${artifacts_abs}/"*) : ;;
  *)
    echo "[build-release-assets] RELEASE_DIR resolved to '${release_abs}', outside '${artifacts_abs}/' — refusing to rm -rf" >&2
    exit 1
    ;;
esac

# Refuse a dirty working tree: the source tarball is built from
# `git archive HEAD` while the Docker image, Helm chart, and native
# header come from the working tree. A dirty tree means those artefacts
# can disagree, which is the opposite of what a release dry-run should
# prove. ALLOW_DIRTY=1 documents the deliberate override (e.g. iterating
# on the asset pipeline itself without committing each tweak).
if [[ "${ALLOW_DIRTY}" != "1" ]]; then
  if ! git diff-index --quiet HEAD -- || [[ -n "$(git ls-files --others --exclude-standard)" ]]; then
    echo "[build-release-assets] working tree is dirty — refusing to package inconsistent artefacts." >&2
    echo "[build-release-assets] commit/stash your changes, or re-run with ALLOW_DIRTY=1 if you accept the risk." >&2
    git status --short >&2
    exit 1
  fi
fi

if ! "${DOCKER}" image inspect "${image}" >/dev/null 2>&1; then
  echo "[build-release-assets] runtime image '${image}' not present — run 'make build' first" >&2
  exit 1
fi

rm -rf "${RELEASE_DIR}"
mkdir -p "${RELEASE_DIR}"

helm_tarball="${RELEASE_DIR}/bess-ems-${vbare}.tgz"
source_tarball="${RELEASE_DIR}/bess-ems-${vbare}-source.tar.gz"
native_so="${RELEASE_DIR}/libbattery_control_core-${VERSION}-linux-x86_64.so"
native_header="${RELEASE_DIR}/battery_control_core.h"
image_inspect="${RELEASE_DIR}/image-inspect.json"
sbom_file="${RELEASE_DIR}/sbom.spdx.json"
sums_file="${RELEASE_DIR}/SHA256SUMS"

echo "[build-release-assets] packaging Helm chart"
"${HELM}" package "${HELM_CHART}" \
  --version "${vbare}" \
  --app-version "${vbare}" \
  --destination "${RELEASE_DIR}"
test -f "${helm_tarball}"

echo "[build-release-assets] git archive source tarball"
git archive \
  --format=tar.gz \
  --prefix="bess-ems-${vbare}/" \
  --output "${source_tarball}" \
  HEAD
test -f "${source_tarball}"

echo "[build-release-assets] extracting native library + header from ${image}"
container_id="$("${DOCKER}" create "${image}")"
trap '"${DOCKER}" rm "${container_id}" >/dev/null 2>&1 || true' EXIT
"${DOCKER}" cp "${container_id}:/app/native/libbattery_control_core.so" "${native_so}"
"${DOCKER}" rm "${container_id}" >/dev/null
trap - EXIT
cp native/battery_control_core/include/battery_control_core.h "${native_header}"
test -f "${native_so}"
test -f "${native_header}"

"${DOCKER}" image inspect "${image}" > "${image_inspect}"
test -s "${image_inspect}"

echo "[build-release-assets] generating SBOM via ${SYFT_IMAGE}"
# Capture syft's SPDX-JSON on stdout and let the HOST shell redirect
# to the file. Earlier versions bind-mounted an output directory and
# let syft write the file inside the container, which produced
# root-owned output that the developer could no longer `chown`. The
# pipe pattern sidesteps the UID/GID dance entirely — the resulting
# file is owned by the user running `make release-assets`. `-q`
# suppresses syft's progress logs on stderr from cluttering the
# terminal; real errors still appear because we do not redirect stderr.
"${DOCKER}" run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock \
  "${SYFT_IMAGE}" \
  -q \
  "docker:${image}" \
  -o spdx-json \
  > "${sbom_file}"
test -s "${sbom_file}"

echo "[build-release-assets] packaging device-mapping schema bundle"
# ADR 0013 §5.1: the config/schema/*.json set is a published, versioned contract that
# an external field simulator generates against. Bundle it with the schema CHANGELOG
# and a manifest carrying schema_version + min_supported (Breaking-Bump-Rollout, ADR §2).
# Reproducible: pin entry order, mtime, and ownership, and strip the gzip header mtime
# (gzip -n), so the SHA256 is stable run-to-run and the published checksum is verifiable.
schema_bundle="${RELEASE_DIR}/bess-ems-schemas-${vbare}.tar.gz"
SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH:-$(git -C "${repo_root}" log -1 --format=%ct HEAD)}"
build_schema_bundle() {
  local stage="$1" out="$2"
  rm -rf "${stage}"
  mkdir -p "${stage}/schema"
  cp config/schema/*.json config/schema/CHANGELOG.md "${stage}/schema/"
  printf '{\n  "name": "bess-ems-device-mapping-schemas",\n  "version": "%s",\n  "schema_version": "v1",\n  "min_supported": "v1"\n}\n' "${vbare}" > "${stage}/schema/bundle.json"
  tar --sort=name --mtime="@${SOURCE_DATE_EPOCH}" --owner=0 --group=0 --numeric-owner \
    -C "${stage}" -cf - schema | gzip -n > "${out}"
  rm -rf "${stage}"
}
build_schema_bundle "${RELEASE_DIR}/.schema-stage" "${schema_bundle}"
# Prove the acceptance criterion ("reproduzierbares Release-Asset"): a second
# independent pack from the same inputs must be byte-identical.
build_schema_bundle "${RELEASE_DIR}/.schema-stage-verify" "${schema_bundle}.verify"
if ! cmp -s "${schema_bundle}" "${schema_bundle}.verify"; then
  echo "[build-release-assets] schema bundle is not reproducible (bytes differ between two packs)" >&2
  exit 1
fi
rm -f "${schema_bundle}.verify"
test -f "${schema_bundle}"

echo "[build-release-assets] SHA256SUMS"
(
  cd "${RELEASE_DIR}"
  find . -maxdepth 1 -type f \
    ! -name '*.log' \
    ! -name 'SHA256SUMS' \
    -printf '%f\n' \
    | sort \
    | xargs sha256sum > SHA256SUMS
)
test -s "${sums_file}"
cat "${sums_file}"

echo "[build-release-assets] done — see ${RELEASE_DIR}/"
