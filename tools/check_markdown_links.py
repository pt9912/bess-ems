#!/usr/bin/env python3
"""Validate local links and host-local path references in Markdown files.

The checker is intentionally dependency-free so it can run in a slim Python
container. It validates local file and directory targets, ignores external
network URLs, checks anchors for Markdown targets, and rejects host-local
absolute paths such as /Development/... or C:\\Users\\... in prose.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable
from urllib.parse import unquote, urlsplit


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

EXTERNAL_SCHEMES = {
    "data",
    "ftp",
    "http",
    "https",
    "mailto",
    "tel",
    "urn",
}

MARKDOWN_SUFFIXES = {".md", ".markdown", ".mdown", ".mkdn"}

FENCE_RE = re.compile(r"^\s*(```+|~~~+)")
HEADING_RE = re.compile(r"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$")
REFERENCE_DEF_RE = re.compile(r"^\s{0,3}\[([^\]]+)\]:\s*(.+?)\s*$")
HTML_ANCHOR_RE = re.compile(r"""<(?:a|[^>]+)\s+[^>]*(?:id|name)=["']([^"']+)["']""", re.IGNORECASE)
HOST_UNIX_PATH_RE = re.compile(
    r"(?<![A-Za-z0-9_.:/-])"
    r"(/(?:Development|home|Users|Volumes|mnt|media|tmp)/[^\s<>)\]\"']*)"
)
WINDOWS_DRIVE_PATH_RE = re.compile(r"(?<![A-Za-z0-9_])([A-Za-z]:\\[^\s<>)\]\"']*)")
WINDOWS_UNC_PATH_RE = re.compile(r"(?<![A-Za-z0-9_])(\\\\[A-Za-z0-9_.-]+\\[^\s<>)\]\"']*)")


@dataclass(frozen=True)
class Link:
    source: Path
    line: int
    raw_target: str


@dataclass(frozen=True)
class LinkError:
    source: Path
    line: int
    target: str
    message: str


def iter_markdown_files(root: Path) -> Iterable[Path]:
    for current_root, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in IGNORED_DIRS]
        current = Path(current_root)
        for file_name in files:
            path = current / file_name
            if path.suffix.lower() in MARKDOWN_SUFFIXES:
                yield path


def strip_inline_code(line: str) -> str:
    result: list[str] = []
    in_code = False
    i = 0
    while i < len(line):
        if line[i] == "`":
            in_code = not in_code
            result.append(" ")
        elif in_code:
            result.append(" ")
        else:
            result.append(line[i])
        i += 1
    return "".join(result)


def extract_inline_targets(line: str) -> Iterable[str]:
    line = strip_inline_code(line)
    cursor = 0
    while True:
        label_end = line.find("](", cursor)
        if label_end == -1:
            return
        target_start = label_end + 2
        target_end = find_link_target_end(line, target_start)
        if target_end == -1:
            cursor = target_start
            continue
        yield line[target_start:target_end]
        cursor = target_end + 1


def find_link_target_end(line: str, start: int) -> int:
    depth = 0
    escaped = False
    in_angle = False
    for index in range(start, len(line)):
        char = line[index]
        if escaped:
            escaped = False
            continue
        if char == "\\":
            escaped = True
            continue
        if char == "<":
            in_angle = True
            continue
        if char == ">" and in_angle:
            in_angle = False
            continue
        if in_angle:
            continue
        if char == "(":
            depth += 1
            continue
        if char == ")":
            if depth == 0:
                return index
            depth -= 1
    return -1


def extract_reference_target(line: str) -> str | None:
    match = REFERENCE_DEF_RE.match(line)
    if match is None:
        return None
    return match.group(2)


def normalize_link_target(raw_target: str) -> str:
    target = raw_target.strip()
    if not target:
        return ""
    if target.startswith("<"):
        closing = target.find(">")
        if closing != -1:
            return target[1:closing].strip()
    if target[0] in {"'", '"'}:
        return ""
    parts = target.split()
    return parts[0].strip()


def iter_links(markdown_file: Path) -> Iterable[Link]:
    in_fence = False
    for line_no, line in enumerate(markdown_file.read_text(encoding="utf-8").splitlines(), start=1):
        if FENCE_RE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue

        reference_target = extract_reference_target(line)
        if reference_target is not None:
            target = normalize_link_target(reference_target)
            if target:
                yield Link(markdown_file, line_no, target)

        for raw_target in extract_inline_targets(line):
            target = normalize_link_target(raw_target)
            if target:
                yield Link(markdown_file, line_no, target)


def strip_trailing_path_punctuation(path: str) -> str:
    return path.rstrip(".,;:")


def iter_absolute_path_references(markdown_file: Path) -> Iterable[Link]:
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
                    yield Link(markdown_file, line_no, target)


def is_external_or_special(target: str) -> bool:
    if target.startswith("//"):
        return True
    scheme = urlsplit(target).scheme.lower()
    return scheme in EXTERNAL_SCHEMES


def split_local_target(target: str) -> tuple[str, str]:
    split = urlsplit(target)
    path = split.path
    fragment = unquote(split.fragment)
    return path, fragment


def resolve_target_path(root: Path, source: Path, link_path: str) -> Path:
    decoded = unquote(link_path)
    if decoded.startswith("/"):
        return (root / decoded.lstrip("/")).resolve()
    return (source.parent / decoded).resolve()


def is_within_root(root: Path, candidate: Path) -> bool:
    try:
        candidate.relative_to(root)
        return True
    except ValueError:
        return False


def markdown_anchor_slug(text: str) -> str:
    text = re.sub(r"<[^>]+>", "", text)
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)
    text = text.replace("`", "")
    text = text.strip().lower()
    text = "".join(char for char in text if char.isalnum() or char.isspace() or char in {"-", "_"})
    text = re.sub(r"[\s-]+", "-", text).strip("-")
    return text


def ascii_slug_variant(slug: str) -> str:
    normalized = unicodedata.normalize("NFKD", slug)
    return normalized.encode("ascii", "ignore").decode("ascii")


def markdown_anchors(markdown_file: Path) -> set[str]:
    anchors: set[str] = set()
    seen_slugs: dict[str, int] = {}
    in_fence = False

    for line in markdown_file.read_text(encoding="utf-8").splitlines():
        if FENCE_RE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue

        for html_anchor in HTML_ANCHOR_RE.findall(line):
            anchors.add(html_anchor)

        match = HEADING_RE.match(line)
        if match is None:
            continue

        heading = match.group(2).strip()
        explicit_id_match = re.search(r"\s+\{#([A-Za-z0-9_.:-]+)\}\s*$", heading)
        if explicit_id_match:
            anchors.add(explicit_id_match.group(1))
            heading = heading[: explicit_id_match.start()].strip()

        base_slug = markdown_anchor_slug(heading)
        if not base_slug:
            continue

        duplicate_index = seen_slugs.get(base_slug, 0)
        seen_slugs[base_slug] = duplicate_index + 1
        slug = base_slug if duplicate_index == 0 else f"{base_slug}-{duplicate_index}"
        anchors.add(slug)

        ascii_slug = ascii_slug_variant(slug)
        if ascii_slug:
            anchors.add(ascii_slug)

    return anchors


def validate_link(root: Path, link: Link, anchor_cache: dict[Path, set[str]]) -> LinkError | None:
    target = link.raw_target
    if is_external_or_special(target):
        return None

    link_path, fragment = split_local_target(target)
    target_path = resolve_target_path(root, link.source, link_path or link.source.name)

    if not is_within_root(root, target_path):
        return LinkError(link.source, link.line, target, "target escapes repository root")

    if not target_path.exists():
        return LinkError(link.source, link.line, target, "target path does not exist")

    if fragment and target_path.suffix.lower() in MARKDOWN_SUFFIXES:
        anchors = anchor_cache.setdefault(target_path, markdown_anchors(target_path))
        normalized_fragment = fragment.lstrip("#")
        if normalized_fragment not in anchors and normalized_fragment.removeprefix("user-content-") not in anchors:
            return LinkError(link.source, link.line, target, "target anchor does not exist")

    return None


def run(root: Path) -> int:
    root = root.resolve()
    anchor_cache: dict[Path, set[str]] = {}
    errors: list[LinkError] = []
    link_count = 0
    absolute_path_count = 0
    file_count = 0

    for markdown_file in sorted(iter_markdown_files(root)):
        file_count += 1
        for path_reference in iter_absolute_path_references(markdown_file):
            absolute_path_count += 1
            errors.append(
                LinkError(
                    path_reference.source,
                    path_reference.line,
                    path_reference.raw_target,
                    "absolute host path reference is not allowed",
                )
            )

        for link in iter_links(markdown_file):
            link_count += 1
            error = validate_link(root, link, anchor_cache)
            if error is not None:
                errors.append(error)

    for error in errors:
        source = error.source.relative_to(root)
        print(f"{source}:{error.line}: {error.message}: {error.target}", file=sys.stderr)

    if errors:
        print(
            f"[markdown-links] FAIL: {len(errors)} error(s) across {file_count} Markdown file(s), "
            f"{link_count} link(s), {absolute_path_count} absolute path reference(s)",
            file=sys.stderr,
        )
        return 1

    print(
        f"[markdown-links] OK: {file_count} Markdown file(s), "
        f"{link_count} local/external link(s), {absolute_path_count} absolute path reference(s) scanned"
    )
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate local links in Markdown files.")
    parser.add_argument("--root", default=".", help="Repository root to scan (default: current directory).")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    return run(Path(args.root))


if __name__ == "__main__":
    raise SystemExit(main())
