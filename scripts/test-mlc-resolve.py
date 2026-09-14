#!/usr/bin/env python3
"""Prove Launch Control MLC search order matches CMD / csproj / Resolve-MlcRoot.ps1."""

from __future__ import annotations

import os
import tempfile
import unittest
from pathlib import Path

MARKER = Path("src") / "LaunchControl.Standard" / "LaunchControl.Standard.csproj"
CHRISTOPHER = Path(r"C:\Users\christopher.owen\Projects\master-launch-control")
REPO = Path(__file__).resolve().parents[1]


def candidates(
    *,
    mlc_root: str | None,
    userprofile: str,
    localappdata: str | None,
    repo_root: str,
) -> list[str]:
    """Same order as scripts/Resolve-MlcRoot.ps1 and the LC csproj."""
    out: list[str] = []

    def add(path: str | None) -> None:
        if not path:
            return
        full = os.path.normpath(path)
        if full not in out:
            out.append(full)

    add(mlc_root)
    add(os.path.join(userprofile, "Projects", "master-launch-control"))
    add(str(CHRISTOPHER))
    add(os.path.join(repo_root, "..", "master-launch-control"))
    if localappdata:
        add(os.path.join(localappdata, "HulkReNaymer", "master-launch-control"))
    else:
        add(os.path.join(repo_root, "launch-control", ".mlc"))
    return out


def resolve(existing: set[str], **kwargs: str | None) -> str | None:
    marker = os.path.normpath(str(MARKER))
    have = {os.path.normpath(p) for p in existing}
    for candidate in candidates(**kwargs):
        if os.path.normpath(os.path.join(candidate, marker)) in have:
            return candidate
    return None


def plant(root: Path, repo: str) -> str:
    marker = root / repo / MARKER
    marker.parent.mkdir(parents=True, exist_ok=True)
    marker.write_text("<!-- test marker -->\n", encoding="utf-8")
    return str((root / repo).resolve())


class SearchOrderTests(unittest.TestCase):
    def test_source_files_document_the_same_paths(self) -> None:
        files = [
            REPO / "scripts" / "HulkReNaymer-LaunchControl.cmd",
            REPO / "scripts" / "Resolve-MlcRoot.ps1",
            REPO / "launch-control" / "HulkReNaymer.LaunchControl.csproj",
        ]
        needles = [
            r"%USERPROFILE%\Projects\master-launch-control",
            r"C:\Users\christopher.owen\Projects\master-launch-control",
            "master-launch-control",
            r"%LOCALAPPDATA%\HulkReNaymer\master-launch-control",
            "git clone --depth 1",
            "https://github.com/uberslaw/master-launch-control.git",
        ]
        cmd = (REPO / "scripts" / "HulkReNaymer-LaunchControl.cmd").read_text(encoding="utf-8")
        self.assertIn('-p:MlcRoot="%MLC_RESOLVED%"', cmd)
        self.assertNotIn("pause\n  exit /b 0", cmd.lower())

        for path in files:
            text = path.read_text(encoding="utf-8")
            for needle in needles:
                self.assertIn(needle, text, f"{path.name} missing {needle!r}")

    def test_explicit_mlc_root_wins(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            explicit = plant(root, "explicit-mlc")
            user = plant(root, "home/Projects/master-launch-control")
            picked = resolve(
                {os.path.join(explicit, str(MARKER)), os.path.join(user, str(MARKER))},
                mlc_root=explicit,
                userprofile=str(root / "home"),
                localappdata=str(root / "lad"),
                repo_root=str(root / "HulkReNaymer"),
            )
            self.assertEqual(os.path.normpath(picked or ""), os.path.normpath(explicit))

    def test_invalid_mlc_root_falls_through(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            user = plant(root, "home/Projects/master-launch-control")
            picked = resolve(
                {os.path.join(user, str(MARKER))},
                mlc_root=str(root / "missing-mlc"),
                userprofile=str(root / "home"),
                localappdata=str(root / "lad"),
                repo_root=str(root / "HulkReNaymer"),
            )
            self.assertEqual(os.path.normpath(picked or ""), os.path.normpath(user))

    def test_userprofile_before_christopher_and_sibling_and_cache(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            user = plant(root, "home/Projects/master-launch-control")
            sibling = plant(root, "master-launch-control")
            cache = plant(root, "lad/HulkReNaymer/master-launch-control")
            existing = {
                os.path.join(user, str(MARKER)),
                os.path.join(str(CHRISTOPHER), str(MARKER)),
                os.path.join(sibling, str(MARKER)),
                os.path.join(cache, str(MARKER)),
            }
            picked = resolve(
                existing,
                mlc_root=None,
                userprofile=str(root / "home"),
                localappdata=str(root / "lad"),
                repo_root=str(root / "HulkReNaymer"),
            )
            self.assertEqual(os.path.normpath(picked or ""), os.path.normpath(user))

    def test_sibling_before_localappdata_cache(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            sibling = plant(root, "master-launch-control")
            cache = plant(root, "lad/HulkReNaymer/master-launch-control")
            picked = resolve(
                {os.path.join(sibling, str(MARKER)), os.path.join(cache, str(MARKER))},
                mlc_root=None,
                userprofile=str(root / "home"),
                localappdata=str(root / "lad"),
                repo_root=str(root / "HulkReNaymer"),
            )
            self.assertEqual(os.path.normpath(picked or ""), os.path.normpath(sibling))

    def test_cache_is_last_before_clone(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            cache = plant(root, "lad/HulkReNaymer/master-launch-control")
            picked = resolve(
                {os.path.join(cache, str(MARKER))},
                mlc_root=None,
                userprofile=str(root / "home"),
                localappdata=str(root / "lad"),
                repo_root=str(root / "HulkReNaymer"),
            )
            self.assertEqual(os.path.normpath(picked or ""), os.path.normpath(cache))

    def test_missing_everything_returns_none(self) -> None:
        picked = resolve(
            set(),
            mlc_root=None,
            userprofile="/tmp/no-such-home",
            localappdata="/tmp/no-such-lad",
            repo_root="/tmp/HulkReNaymer",
        )
        self.assertIsNone(picked)

    def test_candidate_order(self) -> None:
        ordered = candidates(
            mlc_root="/explicit",
            userprofile="/home/op",
            localappdata="/lad",
            repo_root="/src/HulkReNaymer",
        )
        self.assertEqual(
            [os.path.normpath(p) for p in ordered],
            [
                os.path.normpath("/explicit"),
                os.path.normpath("/home/op/Projects/master-launch-control"),
                os.path.normpath(str(CHRISTOPHER)),
                os.path.normpath("/src/master-launch-control"),
                os.path.normpath("/lad/HulkReNaymer/master-launch-control"),
            ],
        )


if __name__ == "__main__":
    unittest.main()
