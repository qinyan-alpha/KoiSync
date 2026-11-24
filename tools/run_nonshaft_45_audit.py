#!/usr/bin/env python3
r"""
Full 45-pose Non-Shaft Visual + 3-Way Feel Calibration Audit Runner for KoiSync MainGame.
Covers all 6 non-shaft kinematic families:
  1. aibu_0..10 (11 native InteractionItem poses)
  2. kplug_khe_f_* + kplug_khh_f_64 (12 kPlug pair Head / RightFinger poses)
  3. masturbation (6 native + 7 kPlug = 13 poses: RightFinger / LeftFinger / SceneAnchor / AnimatedProp)
  4. lesbian (3 native + 4 kPlug = 7 poses: Pelvis / Head / RightFinger)
  5. peeping (2 native SceneAnchor poses)
"""

from __future__ import annotations

import argparse
import collections
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
    ("nonshaft_01_aibu",         0, "@frames=16;@batch:all-aibu:0:20",            360),
    ("nonshaft_02_kplug_pair",   0, "@frames=16;@batch:kplug-nonshaft-pair:0:25", 420),
    ("nonshaft_03_masturbation", 1, "@frames=16;@batch:all-masturbation:0:25",    480),
    ("nonshaft_04_lesbian",      2, "@frames=16;@batch:all-lesbian:0:20",         360),
    ("nonshaft_05_peeping",      0, "@frames=16;@batch:all-peeping:0:10",         240),
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
    clean_cnt = sum(1 for r in all_rows if not r.get("flags"))
    by_eff = collections.defaultdict(list)
    for r in all_rows:
        by_eff[r["effector"]].append(r)

    foreplay_rows = [r for r in all_rows if r["effector"] != "AnimatedProp"]
    prop_rows = [r for r in all_rows if r["effector"] == "AnimatedProp"]
    fp_preserved = sum(1 for r in foreplay_rows if r["bal"]["l0Min"] >= 20.0 and r["bal"]["l0Max"] <= 80.5)

    md = [
        "# KoiSync MainGame Full 45-Pose Non-Shaft Audit Report (Plan A Foreplay Default)",
        "",
        f"- **Total Non-Shaft Poses Audited**: **{len(unique_keys)} / 45** ({len(all_rows)} stages)",
        f"- **Zero-Flag Clean Stages**: **{clean_cnt} / {len(all_rows)}** ({100.0 * clean_cnt / max(1, len(all_rows)):.1f}%)",
        f"- **Plan A Headroom Gate (`Balanced L0 ∈ [20..80]` on Foreplay Poses, preserving `[0..20]` for Insertion contrast)**: **{fp_preserved} / {len(foreplay_rows)}**",
        f"- **Physical Insertion Prop (`AnimatedProp` dildo) Stages**: **{len(prop_rows)}**",
        "",
        "## Summary by Kinematic Family (`Effector`)",
        "| Effector | Poses | Stages | Mean CD (mm) | Raw Bands | Balanced (Plan A) Bands | Intense (Plan A) Bands | Bal L0 Min..Max |",
        "| :--- | :---: | :---: | :---: | :--- | :--- | :--- | :---: |",
    ]
    for eff, erows in sorted(by_eff.items()):
        up = len({r["key"] for r in erows})
        mean_cd = sum(r["contactMeanMm"] for r in erows) / max(1, len(erows))
        rb = dict(collections.Counter(r["raw"]["mfpBand"] for r in erows))
        bb = dict(collections.Counter(r["bal"]["mfpBand"] for r in erows))
        ib = dict(collections.Counter(r["int"]["mfpBand"] for r in erows))
        bmin = min(r["bal"]["l0Min"] for r in erows)
        bmax = max(r["bal"]["l0Max"] for r in erows)
        md.append(f"| `{eff}` | {up} | {len(erows)} | {mean_cd:.1f} | `{rb}` | `{bb}` | `{ib}` | {bmin:.1f}..{bmax:.1f} |")

    md += [
        "",
        "## Chunk Coverage",
        "| Chunk | Poses | Stages | Flagged |",
        "| :--- | :---: | :---: | :---: |",
    ]
    for subdir, up, st_n, fl_n in chunk_summary:
        md.append(f"| `{subdir}` | {up} | {st_n} | {fl_n} |")

    md += [
        "",
        "## Complete Stage-by-Stage Table (All 45 Non-Shaft Poses)",
        "| Chunk | Pose | Stage | Effector | CD (mm) | Travel Δ (mm) | Raw (Amp/Spd/Band) | Balanced Plan A (L0 / Amp / Spd / Band) | Intense Plan A (L0 / Amp / Spd / Band) | Flags |",
        "| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |",
    ]
    for r in sorted(all_rows, key=lambda x: (x["chunk"], x["key"], x["stage"])):
        rw, bl, it = r["raw"], r["bal"], r["int"]
        fl = ", ".join(r["flags"]) if r["flags"] else "CLEAN"
        md.append(
            f"| `{r['chunk']}` | `{r['key']}` ({r['name']}) | `{r['stage']}` | `{r['effector']}` | {r['contactMeanMm']} | "
            f"{r['depthTravelMm']} | {rw['l0Amp']}/{rw['speedMean']:.0f}({rw['mfpBand']}) | "
            f"{bl['l0Min']}..{bl['l0Max']} / **{bl['l0Amp']}** / {bl['speedMean']:.0f} ({bl['mfpBand']}) | "
            f"{it['l0Min']}..{it['l0Max']} / **{it['l0Amp']}** / {it['speedMean']:.0f} ({it['mfpBand']}) | {fl} |"
        )

    (out_dir / "master_report.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    print(f"\n=== NON-SHAFT MASTER REPORT WRITTEN -> {out_dir / 'master_report.md'} ===", flush=True)
    print(f"Poses={len(unique_keys)}, Stages={len(all_rows)}, Clean={clean_cnt}/{len(all_rows)}, PlanAHeadroom={fp_preserved}/{len(foreplay_rows)}", flush=True)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--card", default=DEFAULT_CARD)
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--skip-video", action="store_true")
    args = ap.parse_args()

    selected = CHUNKS
    if args.only:
        selected = [c for c in CHUNKS if any(c[0].startswith(p) or p in c[0] for p in args.only)]

    failures = []
    for subdir, mode, specs, timeout in selected:
        if not run_chunk(subdir, mode, specs, timeout, args.card, args.skip_video):
            failures.append(subdir)

    compile_master_report(AUDIT_BASE / "nonshaft45_master")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
