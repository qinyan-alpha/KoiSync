"""Build the standalone Unity 5.6 device prefabs and their Sideloader zipmod.

This is an unrigged device-asset lane; it does not use character-card slots.
All Unity inputs, intermediate assets, logs and reports remain in --output.
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import shutil
import subprocess
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
GUID = "org.qinnyann.koisync.previewassets"
BUNDLE = "koisync/preview_assets.unity3d"
GROUP = 736240
VERSION = "1.0.0"


def table(rows):
    text = io.StringIO(newline="")
    csv.writer(text, lineterminator="\n").writerows(rows)
    return text.getvalue().encode("utf-8-sig")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--mesh", type=Path, default=ROOT / "src/KoiSync.Plugin/Assets/FleshlightPreview.meshbin")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        raise FileExistsError(f"Use a new output directory: {output}")
    if not args.unity.is_file() or not args.mesh.is_file():
        raise FileNotFoundError("Unity editor or source mesh is missing")
    project = output / "unity-project"
    editor = project / "Assets/Editor"
    shaders = project / "Assets/Shaders"
    source = output / "source"
    bundles = output / "bundles"
    for directory in (editor, shaders, source, project / "ProjectSettings"):
        directory.mkdir(parents=True, exist_ok=True)
    shutil.copy2(ROOT / "tools/preview-assets/BuildPreviewAssets.cs", editor)
    shutil.copy2(ROOT / "tools/preview-assets/DevicePreview.shader", shaders)
    shutil.copy2(args.mesh, source / "FleshlightPreview.meshbin")
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 5.6.2f1\n", encoding="utf-8")
    command = [str(args.unity.resolve()), "-batchmode", "-quit", "-projectPath", str(project),
               "-executeMethod", "BuildPreviewAssets.Build", "-logFile", str(output / "unity.log"),
               "-koisyncMesh", str(source / "FleshlightPreview.meshbin"), "-koisyncOutput", str(bundles)]
    (output / "unity-command.json").write_text(json.dumps(command, indent=2), encoding="utf-8")
    completed = subprocess.run(command, check=False)
    report_path = bundles / "unity-build.json"
    if completed.returncode or not report_path.is_file():
        raise RuntimeError(f"Unity build failed ({completed.returncode}); see {output / 'unity.log'}")
    unity = json.loads(report_path.read_text(encoding="utf-8-sig"))
    manifest = ET.Element("manifest", {"schema-ver": "1"})
    for key, value in {"guid": GUID, "name": "KoiSync Device Preview Assets", "version": VERSION,
                       "author": "qinnyann", "description": "Standalone sleeve and fixed-rod prefabs for KoiSync; Studio items included.",
                       "game": "Koikatsu"}.items():
        ET.SubElement(manifest, key).text = value
    contract = {"schema": "koisync.preview-assets.v1", "guid": GUID, "version": VERSION, "bundle": BUNDLE,
                "sleeve": {"prefab": "KoiSyncSleeve", "entry": "Entry", "inward_axis": "+Y", "units": "metres"},
                "rod": {"prefab": "KoiSyncFixedRod", "base": "RodBase", "end": "RodEnd", "radius_m": .012, "length_m": .18},
                "studio": {"group": GROUP, "category": 1, "sleeve_id": 1, "rod_id": 2},
                "geometry_origin": "Locally installed DragonHVR 2.0.1 / onaho / obj2; source Unity 6000.0.62f1",
                "material": "KoiSync transparent diagnostic material; no source-game shader dependency"}
    entries = {
        "manifest.xml": ET.tostring(manifest, encoding="utf-8", xml_declaration=True),
        "koisync-preview-assets.json": json.dumps(contract, ensure_ascii=False, indent=2).encode("utf-8"),
        "README.txt": ("KoiSync Device Preview Assets 1.0.0\n"
                       "Install one copy under mods/qinnyann/KoiSync/. Restart the game or reload zipmods.\n"
                       "KoiSync 0.1.34+ loads these prefabs in Studio and the main game.\n"
                       "Studio: Add > Items > KoiSync > Device Preview. Manual items are ordinary static Studio props.\n"
                       "The automatic KoiSync preview uses a separate transient instance driven by six-axis output.\n"
                       "Pink: sleeve, entry at local origin with +Y inward. Cyan: fixed rod.\n"
                       "No character-card slot, custom scripts, colliders, or source-game installation is needed at runtime.\n").encode("utf-8"),
        "abdata/studio/info/koisync/ItemGroup_koisync.csv": table([["ID", "Name"], [GROUP, "KoiSync"]]),
        f"abdata/studio/info/koisync/ItemCategory_00_{GROUP}.csv": table([["ID", "Name"], [1, "Device Preview"]]),
        f"abdata/studio/info/koisync/ItemList_00_{GROUP}_1.csv": table([
            ["ID", "Group", "Category", "Name", "Manifest", "Bundle", "Asset", "ChildRoot", "Animation", "Color1", "Pattern1", "Color2", "Pattern2", "Color3", "Pattern3", "Scale", "Emission"],
            [1, GROUP, 1, "KoiSync Sleeve (23.7 cm)", "", BUNDLE, "KoiSyncSleeve", ""] + ["FALSE"] * 7 + ["TRUE", "FALSE"],
            [2, GROUP, 1, "KoiSync Fixed Rod (18 cm)", "", BUNDLE, "KoiSyncFixedRod", ""] + ["FALSE"] * 7 + ["TRUE", "FALSE"],
        ]),
    }
    delivery = output / "delivery"
    delivery.mkdir()
    package = delivery / f"[qinnyann] KoiSync Device Preview Assets v{VERSION}.zipmod"
    with zipfile.ZipFile(package, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in entries.items():
            archive.writestr(name, data)
        archive.write(bundles / "preview_assets.unity3d", "abdata/" + BUNDLE)
        archive.write(bundles / "preview.png", "preview.png")
    with zipfile.ZipFile(package) as archive:
        corrupt = archive.testzip()
        if corrupt:
            raise RuntimeError(f"Zipmod CRC failed: {corrupt}")
        ET.fromstring(archive.read("manifest.xml"))
        members = archive.namelist()
    report = {"status": "BUILT_OFFLINE_NOT_GAME_VALIDATED", "zipmod": str(package), "guid": GUID, "version": VERSION,
              "bundle": BUNDLE, "members": members, "unity": unity, "crc": "PASS", "gameValidated": False,
              "sourceMesh": str(args.mesh.resolve()), "intermediatesRetained": True}
    (output / "result.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"status": report["status"], "zipmod": str(package), "result": str(output / "result.json")}, ensure_ascii=False))


if __name__ == "__main__":
    main()
