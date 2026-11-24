#!/usr/bin/env python3
r"""
Full 234-pose shaft-involved visual + 3-way feel calibration audit runner for KoiSync MainGame.
Runs all 234 poses (native sonyu 28 + native houshi 34 + FMF 3P 16 + MMF 3P 6 + kPlug sonyu 77 + kPlug houshi 73)
in clean, resumable chunks using the GPU-verified perpendicular auto-camera solver, builds contact sheets + MP4s,
and compiles a unified master triage report ranking any alignment, visibility, or stimulation outliers.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
import time
from typing import Any


PROJECT_ROOT = pathlib.Path(r"E:\project\c#\KoiSync")
CASE_ROOT = PROJECT_ROOT / r"artifacts\main-game-visual-v020"
AUDIT_BASE = CASE_ROOT / "visual-audit"
RUN_PS1 = PROJECT_ROOT / r"tools\run-visual-audit.ps1"
BUILD_PY = PROJECT_ROOT / r"tools\build_visual_audit.py"
PYTHON_EXE = pathlib.Path(r"E:\project\c#\KoikatuModFactory\.venv\Scripts\python.exe")
DEFAULT_CARD = r"E:\Galgames\kk\UserData\chara\female\qinnyann\长夜.png"

CHUNKS = [
    # (subdir, mode, batch_spec, timeout_sec)
    ("full234_01_native_sonyu",   0, "@frames=16;@batch:native-sonyu:0:35",   600),
    ("full234_02_native_houshi",  0, "@frames=16;@batch:native-houshi:0:40",  660),
    ("full234_03_all_3p",         3, "@frames=16;@batch:all-3p:0:30",         480),
    ("full234_04_all_mmf",        4, "@frames=16;@batch:all-mmf:0:20",        360),
    ("full234_05_kplug_sonyu_a",  0, "@frames=16;@batch:kplug-sonyu:0:26",    600),
    ("full234_06_kplug_sonyu_b",  0, "@frames=16;@batch:kplug-sonyu:26:26",   600),
    ("full234_07_kplug_sonyu_c",  0, "@frames=16;@batch:kplug-sonyu:52:35",   600),
    ("full234_08_kplug_houshi_a", 0, "@frames=16;@batch:kplug-houshi:0:25",   600),
    ("full234_09_kplug_houshi_b", 0, "@frames=16;@batch:kplug-houshi:25:25",  600),
    ("full234_10_kplug_houshi_c", 0, "@frames=16;@batch:kplug-houshi:50:35",  600),
]


def run_chunk(subdir: str, mode: int, specs: str, timeout: int, card: str, skip_video: bool) -> bool:
    out_dir = AUDIT_BASE / subdir
    complete_marker = out_dir / "complete.txt"
    report_json = out_dir / "triage_report.json"

    if not complete_marker.exists():
        print(f"\n=== [{subdir}] Launching Mode={mode} specs={specs} ===", flush=True)
        cmd = [
            "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(RUN_PS1),
            "-Mode", str(mode),
            "-Subdir", subdir,
            "-Specs", specs,
            "-TimeoutSeconds", str(timeout),
        ]
        if card:
            cmd += ["-Card", card]
        t0 = time.time()
        res = subprocess.run(cmd)
        elapsed = time.time() - t0
        if res.returncode != 0 or not complete_marker.exists():
            print(f"  [ERROR] Chunk {subdir} failed after {elapsed:.1f}s (rc={res.returncode})", flush=True)
            return False
        print(f"  [CAPTURED] {subdir} finished in {elapsed:.1f}s", flush=True)
    else:
        print(f"\n=== [{subdir}] Capture already complete, checking post-process ===", flush=True)

    if not report_json.exists():
        py_cmd = [str(PYTHON_EXE), "-X", "utf8", str(BUILD_PY), "--root", str(out_dir)]
        if skip_video:
            py_cmd.append("--skip-video")
        res = subprocess.run(py_cmd)
        if res.returncode != 0:
            print(f"  [ERROR] Post-process failed for {subdir}", flush=True)
            return False
    return True


def compile_master_report(out_dir: pathlib.Path) -> None:
    out_dir.mkdir(parents=True, exist_ok=True)
    all_rows: list[dict[str, Any]] = []
    chunk_summary: list[tuple[str, int, int, int]] = []

    for subdir, mode, _, _ in CHUNKS:
        rj = AUDIT_BASE / subdir / "triage_report.json"
        if not rj.exists():
            chunk_summary.append((subdir, 0, 0, 0))
            continue
        rows = json.loads(rj.read_text(encoding="utf-8-sig"))
        unique_poses = len({r["key"] for r in rows})
        flagged = sum(1 for r in rows if r.get("flags"))
        chunk_summary.append((subdir, unique_poses, len(rows), flagged))
        for r in rows:
            r["chunk"] = subdir
            r["mode"] = mode
            all_rows.append(r)

    (out_dir / "master_report.json").write_text(
        json.dumps(all_rows, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    unique_keys = {r["key"] for r in all_rows}
    geom_issues = [r for r in all_rows if any(
        f.startswith(("HIGH_CONTACT_DIST", "ENGAGEMENT_DROP", "ZERO_MAT_INSERTION")) for f in r.get("flags", [])
    )]
    cam_issues = [r for r in all_rows if any(
        f.startswith(("PENIS_BURIED", "FORESHORTENED")) for f in r.get("flags", [])
    )]
    feel_issues = [r for r in all_rows if any(
        f.startswith(("LOW_STIM_AMP", "LOW_SPEED_BAND")) for f in r.get("flags", [])
    )]
    clean_cnt = sum(1 for r in all_rows if not r.get("flags"))

    # Aggregate raw vs balanced vs intense stats
    def band_counts(key: str) -> dict[str, int]:
        bc: dict[str, int] = {}
        for r in all_rows:
            b = r[key]["mfpBand"]
            bc[b] = bc.get(b, 0) + 1
        return bc

    raw_bands = band_counts("raw")
    bal_bands = band_counts("bal")
    int_bands = band_counts("int")

    md = [
        "# KoiSync MainGame Full 234-Pose Visual & 3-Way Feel Master Audit Report",
        "",
        f"- **Total Unique Poses Audited**: **{len(unique_keys)}**",
        f"- **Total Animation Stages Audited (`WLoop` / `SLoop`)**: **{len(all_rows)}**",
        f"- **100% Clean Stages (Aligned + Unobstructed Perp Cam + Target Band)**: **{clean_cnt} / {len(all_rows)}** ({100.0 * clean_cnt / max(1, len(all_rows)):.1f}%)",
        f"- **Geometry Flags (`CD>20mm` / `Eng<0.8` / `ZeroMat`)**: **{len(geom_issues)}**",
        f"- **Camera Visibility Flags (`VisPx<15` / `Ang<60°`)**: **{len(cam_issues)}**",
        f"- **Feel Flags (`BalAmp<25` / `BalSpd<100`)**: **{len(feel_issues)}**",
        "",
        "## MultiFunPlayer Speed Band Distribution (All Audited Stages)",
        f"- **Raw 原计算**: `{raw_bands}`",
        f"- **Balanced 均衡分析**: `{bal_bands}`",
        f"- **Intense 强爽分析**: `{int_bands}`",
        "",
        "## Chunk Coverage Summary",
        "| Chunk | Unique Poses | Stages | Flagged Stages |",
        "| :--- | :---: | :---: | :---: |",
    ]
    for subdir, up, st_n, fl_n in chunk_summary:
        md.append(f"| `{subdir}` | {up} | {st_n} | {fl_n} |")

    if geom_issues:
        md += [
            "",
            "## Priority 1: Geometry Alignment Outliers (Sorted by ContactDist / Engagement)",
            "| Chunk | Pose | Stage | Contact | EntrySrc | CD Mean/Max (mm) | Depth (mm) | EngMin | Cam (Ang/VisPx) | Flags |",
            "| :--- | :--- | :---: | :---: | :--- | :---: | :---: | :---: | :---: | :--- |",
        ]
        for r in sorted(geom_issues, key=lambda x: (-x["contactMeanMm"], x["engMin"])):
            md.append(
                f"| `{r['chunk']}` | `{r['key']}` ({r['name']}) | `{r['stage']}` | {r['contact']} | {r['entrySource']} | "
                f"{r['contactMeanMm']}/{r['contactMaxMm']} | {r['depthMinMm']}..{r['depthMaxMm']} | {r['engMin']} | "
                f"{r['camAngleDeg']}°/{r['penisVisPx']}px | {', '.join(r['flags'])} |"
            )

    if cam_issues:
        md += [
            "",
            "## Priority 2: Low Penis Visibility / Camera Outliers",
            "| Chunk | Pose | Stage | Cam (Ang/VisPx/Span) | CD (mm) | Depth (mm) | Flags |",
            "| :--- | :--- | :---: | :---: | :---: | :---: | :--- |",
        ]
        for r in sorted(cam_issues, key=lambda x: x["penisVisPx"]):
            md.append(
                f"| `{r['chunk']}` | `{r['key']}` ({r['name']}) | `{r['stage']}` | {r['camAngleDeg']}°/{r['penisVisPx']}px/{r['shaftSpan']} | "
                f"{r['contactMeanMm']} | {r['depthMinMm']}..{r['depthMaxMm']} | {', '.join(r['flags'])} |"
            )

    if feel_issues:
        md += [
            "",
            "## Priority 3: Feel / Stimulation Outliers",
            "| Chunk | Pose | Stage | Depth (mm) | Raw (Amp/Spd/Band) | Bal (Amp/Spd/Band) | Int (Amp/Spd/Band) | Flags |",
            "| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :--- |",
        ]
        for r in sorted(feel_issues, key=lambda x: x["bal"]["speedMean"]):
            md.append(
                f"| `{r['chunk']}` | `{r['key']}` ({r['name']}) | `{r['stage']}` | {r['depthMinMm']}..{r['depthMaxMm']} (Δ{r['depthTravelMm']}) | "
                f"{r['raw']['l0Amp']}/{r['raw']['speedMean']}({r['raw']['mfpBand']}) | "
                f"{r['bal']['l0Amp']}/{r['bal']['speedMean']}({r['bal']['mfpBand']}) | "
                f"{r['int']['l0Amp']}/{r['int']['speedMean']}({r['int']['mfpBand']}) | {', '.join(r['flags'])} |"
            )

    (out_dir / "master_report.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    print(f"\n=== MASTER REPORT WRITTEN -> {out_dir / 'master_report.md'} ===", flush=True)
    print(f"Poses={len(unique_keys)}, Stages={len(all_rows)}, Clean={clean_cnt}, GeomFlags={len(geom_issues)}, CamFlags={len(cam_issues)}, FeelFlags={len(feel_issues)}", flush=True)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--card", default=DEFAULT_CARD)
    ap.add_argument("--only", nargs="*", help="Optional subset of chunk prefixes to run")
    ap.add_argument("--skip-video", action="store_true")
    args = ap.parse_args()

    selected = CHUNKS
    if args.only:
        selected = [c for c in CHUNKS if any(c[0].startswith(p) or p in c[0] for p in args.only)]

    failures = []
    for subdir, mode, specs, timeout in selected:
        ok = run_chunk(subdir, mode, specs, timeout, args.card, args.skip_video)
        if not ok:
            failures.append(subdir)

    compile_master_report(AUDIT_BASE / "full234_master")
    if failures:
        print(f"\nWARNING: {len(failures)} chunk(s) had errors: {failures}", flush=True)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
