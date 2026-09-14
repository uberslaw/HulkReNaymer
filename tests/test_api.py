from pathlib import Path

from fastapi.testclient import TestClient

from hulkrenaymer.seed import seed_demo
from hulkrenaymer.server import app


client = TestClient(app)


def test_health():
    res = client.get("/api/health")
    assert res.status_code == 200
    assert res.json()["name"] == "HulkReNaymer"


def test_preview_and_rename_roundtrip(tmp_path, monkeypatch):
    monkeypatch.setenv("HULKRENAYMER_DATA", str(tmp_path / "data"))
    folder = tmp_path / "batch"
    folder.mkdir()
    (folder / "EXPORT_20260914_Asset_Report_001.csv").write_text("1")
    (folder / "EXPORT_20260914_Asset_Report_002.csv").write_text("2")
    payload = {
        "path": str(folder),
        "rules": {"replace_enabled": True, "find": "EXPORT_20260914_", "replace_with": ""},
        "confirm": True,
    }
    preview = client.post("/api/preview", json=payload).json()
    assert preview["counts"]["changed"] == 2
    assert preview["rows"][0]["new_name"].startswith("Asset_Report_")
    renamed = client.post("/api/rename", json=payload).json()
    assert renamed["renamed"] == 2
    assert (folder / "Asset_Report_001.csv").exists()
    undo = client.post("/api/undo").json()
    assert undo["undone"] == 2


def test_seed_endpoint(tmp_path, monkeypatch):
    monkeypatch.chdir(tmp_path)
    res = client.post("/api/seed")
    assert res.status_code == 200
    assert Path(res.json()["path"]).exists()


def test_mapping_endpoint():
    res = client.post("/api/mapping", json={"text": "IMG001.jpg|AU123456_HP-ZBook.jpg"})
    assert res.json()["count"] == 1


def test_javascript_rule(tmp_path):
    folder = tmp_path / "js"
    folder.mkdir()
    (folder / "Track01.txt").write_text("x")
    payload = {
        "path": str(folder),
        "rules": {"js_enabled": True, "js_code": "newName = name + '_smash.' + ext;"},
    }
    preview = client.post("/api/preview", json=payload).json()
    assert preview["rows"][0]["new_name"] == "Track01_smash.txt"
