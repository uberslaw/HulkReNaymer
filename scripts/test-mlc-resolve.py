#!/usr/bin/env python3
"""Prove Launch Control builds against the in-repo LaunchControl.Standard."""

from __future__ import annotations

import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
LC_PROJ = REPO / "launch-control" / "HulkReNaymer.LaunchControl.csproj"
STANDARD = REPO / "launch-control" / "LaunchControl.Standard" / "LaunchControl.Standard.csproj"
CMD = REPO / "scripts" / "HulkReNaymer-LaunchControl.cmd"
README = REPO / "README.md"


class VendoredStandardTests(unittest.TestCase):
    def test_vendored_standard_exists(self) -> None:
        self.assertTrue(STANDARD.is_file(), f"missing {STANDARD}")
        for rel in (
            "Host/LaunchControlWindow.xaml",
            "Host/LaunchControlWindow.xaml.cs",
            "Host/LaunchControlProfile.cs",
            "Themes/ThemeResources.xaml",
            "Theme/ThemeSettingsWindow.xaml",
        ):
            path = STANDARD.parent / rel
            self.assertTrue(path.is_file(), f"missing {path}")

    def test_csproj_defaults_to_relative_standard(self) -> None:
        text = LC_PROJ.read_text(encoding="utf-8")
        self.assertIn(
            'Include="LaunchControl.Standard\\LaunchControl.Standard.csproj"',
            text,
        )
        self.assertIn(r'Compile Remove="LaunchControl.Standard\**"', text)
        self.assertNotIn(r"%USERPROFILE%\Projects\master-launch-control", text)
        self.assertNotIn("RequireLaunchControlStandard", text)
        self.assertNotIn("Clone https://github.com/uberslaw/master-launch-control", text)

    def test_cmd_builds_without_mlc_root(self) -> None:
        text = CMD.read_text(encoding="utf-8")
        self.assertIn('dotnet build "%LC_PROJ%" -c Release', text)
        self.assertNotIn("Resolve-MlcRoot.ps1", text)
        self.assertNotIn('-p:MlcRoot="%MLC_RESOLVED%"', text)
        self.assertNotIn(r"%USERPROFILE%\Projects\master-launch-control", text)
        self.assertNotIn("Clone Master Launch Control to %USERPROFILE%", text)
        self.assertNotIn("pause\n  exit /b 0", text.lower())
        self.assertIn("See the dotnet errors above.", text)

    def test_readme_does_not_require_mlc_clone(self) -> None:
        text = README.read_text(encoding="utf-8")
        self.assertIn("launch-control/LaunchControl.Standard", text)
        self.assertIn("You do **not** need to clone Master Launch Control", text)
        self.assertNotIn("git clone --depth 1", text)
        self.assertNotIn(r"%USERPROFILE%\Projects\master-launch-control", text)


if __name__ == "__main__":
    unittest.main()
