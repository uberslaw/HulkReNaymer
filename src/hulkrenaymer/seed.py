from __future__ import annotations

from io import BytesIO
from pathlib import Path

from mutagen.id3 import ID3, TALB, TIT2, TPE1
from PIL import Image
import piexif


DEMO_FILES = {
    "project": [
        "final report.docx",
        "cost plan.xlsx",
        "site photos.zip",
        "QLD Office Report.docx",
        "brisbane OFFICE asset REGISTER.xlsx",
        "AssetRegister.xlsx",
        "ProjectReport.pdf",
        "MeetingNotes.docx",
        "Report.pdf",
        "Photo001.jpg",
    ],
    "exports": [
        "EXPORT_20260914_Asset_Report_001.csv",
        "EXPORT_20260914_Asset_Report_002.csv",
        "IMG_2026_0001.jpg",
    ],
    "photos": [
        "DSC0001.jpg",
        "DSC0002.jpg",
        "DSC0003.jpg",
        "IMG001.jpg",
        "IMG002.jpg",
        "IMG003.jpg",
        "FILE01.JPEG",
    ],
    "music": [
        "Track01.mp3",
    ],
    "Brisbane": [
        "AssetList.xlsx",
    ],
}


def _tiny_jpeg_bytes(color: tuple[int, int, int], taken: str) -> bytes:
    image = Image.new("RGB", (320, 240), color=color)
    buffer = BytesIO()
    image.save(buffer, format="JPEG", quality=85)
    payload = buffer.getvalue()
    exif_dict = {
        "0th": {
            piexif.ImageIFD.Make: "HulkCam",
            piexif.ImageIFD.Model: "Gamma",
            piexif.ImageIFD.DateTime: taken.encode("utf-8"),
        },
        "Exif": {
            piexif.ExifIFD.DateTimeOriginal: taken.encode("utf-8"),
            piexif.ExifIFD.DateTimeDigitized: taken.encode("utf-8"),
        },
    }
    exif_bytes = piexif.dump(exif_dict)
    out = BytesIO()
    Image.open(BytesIO(payload)).save(out, format="JPEG", quality=85, exif=exif_bytes)
    return out.getvalue()


def _tiny_mp3(path: Path) -> None:
    frame_header = bytes.fromhex("FF FB 90 64")
    payload = frame_header + (b"\x00" * 104)
    path.write_bytes(payload * 40)
    tags = ID3()
    tags["TPE1"] = TPE1(encoding=3, text=["Hulk Smash Band"])
    tags["TALB"] = TALB(encoding=3, text=["Gamma Rays"])
    tags["TIT2"] = TIT2(encoding=3, text=["Street Inspection"])
    tags.save(path)


def seed_demo(root: str | Path | None = None) -> Path:
    base = Path(root) if root else Path.cwd() / "demo_files"
    base.mkdir(parents=True, exist_ok=True)
    photo_taken = "2026:09:14 09:30:00"
    colors = {
        "DSC0001.jpg": (46, 180, 68),
        "DSC0002.jpg": (30, 140, 50),
        "DSC0003.jpg": (80, 210, 90),
        "IMG001.jpg": (20, 90, 40),
        "IMG002.jpg": (40, 120, 60),
        "IMG003.jpg": (60, 160, 80),
        "FILE01.JPEG": (10, 70, 30),
        "Photo001.jpg": (90, 200, 70),
        "IMG_2026_0001.jpg": (50, 150, 50),
    }

    for folder, names in DEMO_FILES.items():
        target = base / folder
        target.mkdir(parents=True, exist_ok=True)
        for name in names:
            path = target / name
            suffix = path.suffix.lower()
            if suffix in {".jpg", ".jpeg"}:
                path.write_bytes(_tiny_jpeg_bytes(colors.get(name, (40, 160, 50)), photo_taken))
            elif suffix == ".mp3":
                _tiny_mp3(path)
            else:
                path.write_text(f"HulkReNaymer sample: {name}\n", encoding="utf-8")

    mapping = base / "asset_register.txt"
    mapping.write_text(
        "\n".join(
            [
                "IMG001.jpg|AU123456_HP-ZBook.jpg",
                "IMG002.jpg|AU123457_HP-EliteBook.jpg",
                "IMG003.jpg|AU123458_HP-ZBook.jpg",
            ]
        )
        + "\n",
        encoding="utf-8",
    )
    return base
