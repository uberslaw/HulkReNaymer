from pathlib import Path

from hulkrenaymer.apply import commit_preview, undo_last
from hulkrenaymer.engine import build_preview
from hulkrenaymer.favorites import parse_mapping
from hulkrenaymer.models import Rules
from hulkrenaymer.scan import scan_directory
from hulkrenaymer.seed import seed_demo


def test_parse_mapping():
    text = "OldName01.pdf|NewName01.pdf\nOldName02.pdf,NewName02.pdf\n# comment\n"
    mapping = parse_mapping(text)
    assert mapping["OldName01.pdf"] == "NewName01.pdf"
    assert mapping["OldName02.pdf"] == "NewName02.pdf"


def test_rename_and_undo(tmp_path, monkeypatch):
    monkeypatch.setenv("HULKRENAYMER_DATA", str(tmp_path / "data"))
    folder = tmp_path / "docs"
    folder.mkdir()
    (folder / "final report.docx").write_text("a")
    (folder / "cost plan.xlsx").write_text("b")
    items, _ = scan_directory(folder)
    rules = Rules(
        case_mode="title",
        replace_enabled=True,
        find=" ",
        replace_with="_",
        add_enabled=True,
        prefix="PROJECT123_",
    )
    rows = build_preview(items, rules)
    result = commit_preview(rows, rules)
    assert result["renamed"] == 2
    assert (folder / "PROJECT123_Final_Report.docx").exists()
    assert (folder / "PROJECT123_Cost_Plan.xlsx").exists()
    undone = undo_last()
    assert undone["undone"] == 2
    assert (folder / "final report.docx").exists()


def test_swap_names(tmp_path, monkeypatch):
    monkeypatch.setenv("HULKRENAYMER_DATA", str(tmp_path / "data"))
    folder = tmp_path / "swap"
    folder.mkdir()
    (folder / "alpha.txt").write_text("a")
    (folder / "beta.txt").write_text("b")
    items, _ = scan_directory(folder)
    mapping = {"alpha.txt": "beta.txt", "beta.txt": "alpha.txt"}
    rows = build_preview(items, Rules(mapping=mapping))
    result = commit_preview(rows, Rules(mapping=mapping))
    assert result["renamed"] == 2
    assert (folder / "alpha.txt").read_text() == "b"
    assert (folder / "beta.txt").read_text() == "a"


def test_seed_and_exif(tmp_path):
    root = seed_demo(tmp_path / "demo")
    photos = list((root / "photos").glob("DSC*.jpg"))
    assert len(photos) == 3
    items, _ = scan_directory(root / "photos", wildcard="DSC*.jpg")
    assert any(item.exif.get("date") for item in items)
    music = next(item for item in scan_directory(root / "music")[0] if item.name.endswith(".mp3"))
    assert music.id3.get("artist") == "Hulk Smash Band"
