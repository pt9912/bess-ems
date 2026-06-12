#!/usr/bin/env python3
"""Reject host-local absolute path references in Markdown files.

Rest sensor since the d-check migration (2026-06-12): local link and
anchor validation moved to d-check (digest-pinned container image,
configured in .d-check.yml). This checker keeps the one rule a generic
reference checker cannot cover: host-local absolute paths such as
/Development/..., C:\\Users\\... or \\\\server\\share in prose leak
machine-specific layouts and are rejected.

Dependency-free so it can run in a slim Python container.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


IGNORED_DIRS = {
    ".git",
    ".github",
    ".vs",
    ".vscode",
    ".idea",
    "bin",
    "obj",
    "out",
    "publish",
    "TestResults",
    "coverage",
    "artifacts",
}

MARKDOWN_SUFFIXES = {".md", ".markdown", ".mdown", ".mkdn"}

FENCE_RE = re.compile(r"^\s*(```+|~~~+)")
HOST_UNIX_PATH_RE = re.compile(
    r"(?<![A-Za-z0-9_.:/-])"
    r"(/(?:Development|home|Users|Volumes|mnt|media|tmp)/[^\s<>)\]\"']*)"
)
WINDOWS_DRIVE_PATH_RE = re.compile(r"(?<![A-Za-z0-9_])([A-Za-z]:\\[^\s<>)\]\"']*)")
WINDOWS_UNC_PATH_RE = re.compile(r"(?<![A-Za-z0-9_])(\\\\[A-Za-z0-9_.-]+\\[^\s<>)\]\"']*)")


@dataclass(frozen=True)
class PathReference:
    source: Path
    line: int
    target: str


def iter_markdown_files(root: Path) -> Iterable[Path]:
    for current_root, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in IGNORED_DIRS]
        current = Path(current_root)
        for file_name in files:
            path = current / file_name
            if path.suffix.lower() in MARKDOWN_SUFFIXES:
                yield path


def strip_trailing_path_punctuation(path: str) -> str:
    return path.rstrip(".,;:")


def iter_absolute_path_references(markdown_file: Path) -> Iterable[PathReference]:
    in_fence = False
    for line_no, line in enumerate(markdown_file.read_text(encoding="utf-8").splitlines(), start=1):
        if FENCE_RE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue

        for pattern in (HOST_UNIX_PATH_RE, WINDOWS_DRIVE_PATH_RE, WINDOWS_UNC_PATH_RE):
            for match in pattern.finditer(line):
                target = strip_trailing_path_punctuation(match.group(1))
                if target:
                    yield PathReference(markdown_file, line_no, target)


def run(root: Path) -> int:
    root = root.resolve()
    errors: list[PathReference] = []
    file_count = 0

    for markdown_file in sorted(iter_markdown_files(root)):
        file_count += 1
        errors.extend(iter_absolute_path_references(markdown_file))

    for error in errors:
        source = error.source.relative_to(root)
        print(
            f"{source}:{error.line}: absolute host path reference is not allowed: {error.target}",
            file=sys.stderr,
        )

    if errors:
        print(
            f"[markdown-links] FAIL: {len(errors)} absolute path reference(s) "
            f"across {file_count} Markdown file(s)",
            file=sys.stderr,
        )
        return 1

    print(
        f"[markdown-links] OK: {file_count} Markdown file(s), "
        "0 absolute path reference(s)"
    )
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Reject host-local absolute path references in Markdown files.",
    )
    parser.add_argument("--root", default=".", help="Repository root to scan (default: current directory).")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    return run(Path(args.root))


if __name__ == "__main__":
    raise SystemExit(main())
