from datetime import datetime
from pathlib import Path

from hulkrenaymer.engine import apply_rules_to_item, build_preview
from hulkrenaymer.models import FileItem, Rules


def make_item(name: str, folder: str = "project", **kwargs) -> FileItem:
    stem = Path(name).stem
    ext = Path(name).suffix
    defaults = dict(
        path=f"/tmp/{folder}/{name}",
        name=name,
        stem=stem,
        ext=ext,
        folder=folder,
        parent=f"/tmp/{folder}",
        is_dir=False,
        size=100,
        created=datetime(2026, 9, 14, 9, 0, 0),
        modified=datetime(2026, 9, 14, 10, 0, 0),
        accessed=datetime(2026, 9, 14, 11, 0, 0),
    )
    defaults.update(kwargs)
    return FileItem(**defaults)


def test_prefix_suffix_and_replace():
    item = make_item("Report.pdf")
    new, _ = apply_rules_to_item(item, Rules(add_enabled=True, prefix="Project-A_"), 1)
    assert new == "Project-A_Report.pdf"

    item = make_item("AssetRegister.xlsx")
    new, _ = apply_rules_to_item(item, Rules(add_enabled=True, suffix="_Approved"), 1)
    assert new == "AssetRegister_Approved.xlsx"

    item = make_item("QLD Office Report.docx")
    new, _ = apply_rules_to_item(
        item,
        Rules(replace_enabled=True, find="Office", replace_with="Regional"),
        1,
    )
    assert new == "QLD Regional Report.docx"


def test_remove_first_characters_and_insert():
    item = make_item("IMG_2026_0001.jpg")
    new, _ = apply_rules_to_item(item, Rules(remove_enabled=True, remove_first_n=4), 1)
    assert new == "2026_0001.jpg"

    item = make_item("ProjectReport.pdf")
    new, _ = apply_rules_to_item(item, Rules(add_enabled=True, insert="-", insert_at=8), 1)
    assert new == "Project-Report.pdf"


def test_title_case():
    item = make_item("brisbane OFFICE asset REGISTER.xlsx")
    new, _ = apply_rules_to_item(item, Rules(case_mode="title"), 1)
    assert new == "Brisbane Office Asset Register.xlsx"


def test_example_a_project_documents():
    rules = Rules(
        case_mode="title",
        replace_enabled=True,
        find=" ",
        replace_with="_",
        add_enabled=True,
        prefix="PROJECT123_",
    )
    expected = {
        "final report.docx": "PROJECT123_Final_Report.docx",
        "cost plan.xlsx": "PROJECT123_Cost_Plan.xlsx",
        "site photos.zip": "PROJECT123_Site_Photos.zip",
    }
    for old, want in expected.items():
        new, _ = apply_rules_to_item(make_item(old), rules, 1)
        assert new == want


def test_example_c_strip_export_prefix():
    rules = Rules(replace_enabled=True, find="EXPORT_20260914_", replace_with="")
    new, _ = apply_rules_to_item(make_item("EXPORT_20260914_Asset_Report_001.csv"), rules, 1)
    assert new == "Asset_Report_001.csv"


def test_folder_prefix():
    item = make_item("AssetList.xlsx", folder="Brisbane")
    new, _ = apply_rules_to_item(item, Rules(folder_enabled=True, folder_mode="prefix", folder_separator="_"), 1)
    assert new == "Brisbane_AssetList.xlsx"


def test_extension_change():
    item = make_item("FILE01.JPEG")
    new, _ = apply_rules_to_item(item, Rules(name_enabled=True, ext_mode="lower"), 1)
    assert new == "FILE01.jpeg"


def test_numbering_and_dates():
    items = [make_item(f"Photo{i}.jpg") for i in range(1, 4)]
    rules = Rules(numbering_enabled=True, number_start=1, number_pad=3, number_separator="_", number_position="suffix")
    rows = build_preview(items, rules)
    assert [row.new_name for row in rows] == ["Photo1_001.jpg", "Photo2_002.jpg", "Photo3_003.jpg"]

    item = make_item("MeetingNotes.docx")
    new, _ = apply_rules_to_item(
        item,
        Rules(date_enabled=True, date_source="modified", date_format="%Y-%m-%d", date_position="prefix"),
        1,
    )
    assert new == "2026-09-14_MeetingNotes.docx"


def test_photo_workflow_with_exif():
    item = make_item("DSC0001.jpg", exif={"date": "2026:09:14 09:30:00", "width": 320, "height": 240})
    rules = Rules(
        name_enabled=True,
        name_mode="remove",
        add_enabled=True,
        suffix="Site-Inspection",
        date_enabled=True,
        date_source="exif",
        date_format="%Y-%m-%d",
        numbering_enabled=True,
        number_pad=3,
    )
    new, _ = apply_rules_to_item(item, rules, 1)
    assert new == "2026-09-14_Site-Inspection_001.jpg"


def test_csv_mapping_and_regex():
    item = make_item("IMG001.jpg")
    new, _ = apply_rules_to_item(item, Rules(mapping={"IMG001.jpg": "AU123456_HP-ZBook.jpg"}), 1)
    assert new == "AU123456_HP-ZBook.jpg"

    item = make_item("Invoice 2026 - Client Name.pdf")
    new, _ = apply_rules_to_item(
        item,
        Rules(regex_enabled=True, regex_pattern=r"^(Invoice \d+) - (.+)$", regex_replace=r"$2 - $1"),
        1,
    )
    assert new == "Client Name - Invoice 2026.pdf"


def test_conflicts_and_blank_names():
    items = [make_item("a.txt"), make_item("b.txt")]
    rules = Rules(name_enabled=True, name_mode="fixed", name_fixed="same")
    rows = build_preview(items, rules)
    assert all(row.status == "conflict" for row in rows)

    blank = build_preview([make_item("keep.txt")], Rules(name_enabled=True, name_mode="remove", ext_mode="remove"))
    assert blank[0].status == "invalid"


def test_id3_tokens():
    item = make_item("Track01.mp3", id3={"artist": "Hulk Smash Band", "title": "Street Inspection", "album": "Gamma Rays"})
    new, _ = apply_rules_to_item(
        item,
        Rules(name_enabled=True, name_mode="fixed", name_fixed="{id3.artist} - {id3.title}"),
        1,
    )
    assert new == "Hulk Smash Band - Street Inspection.mp3"
