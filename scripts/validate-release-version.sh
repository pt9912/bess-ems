#!/usr/bin/env bash
#
# Shared release-version validator. Single source of truth for the
# accepted tag/version shape, used by both `make release-assets` and
# the GitHub Actions release workflow so the local dry-run and the
# CI gate cannot drift.
#
# Accepts: vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-PRERELEASE.
#
# The PRERELEASE segment follows SemVer 2.0.0 §9: a hyphen, then one or
# more dot-separated identifiers, where each identifier matches
# [0-9A-Za-z-]+ (non-empty). This rejects empty identifiers like
# `v1.2.3-rc..1` or trailing-dot `v1.2.3-rc.`.
#
# Rejects: build metadata (`+...`), empty prerelease (`v1.2.3-`),
# empty prerelease identifiers (`v1.2.3-rc..1`), missing components.
#
# Tolerated (intentional deviation from SemVer 2.0.0): leading zeros in
# numeric identifiers (e.g. `v01.0.0`). Upstream tooling does not enforce
# the SemVer "no leading zeros" rule and historical project conventions
# sometimes pad — keep the regex permissive on that axis to avoid
# surprises.
#
# Usage: scripts/validate-release-version.sh vMAJOR.MINOR.PATCH[-PRERELEASE]
set -euo pipefail

version="${1:-}"
if [[ -z "${version}" ]]; then
  echo "[validate-release-version] usage: $0 <vMAJOR.MINOR.PATCH[-PRERELEASE]>" >&2
  exit 2
fi

if [[ ! "${version}" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]]; then
  echo "[validate-release-version] '${version}' must match vMAJOR.MINOR.PATCH[-PRERELEASE] (no build metadata, no empty prerelease identifiers)" >&2
  exit 1
fi
