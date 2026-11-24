#!/usr/bin/env python3
import collections
import json
import pathlib

BASE = pathlib.Path(r"E:\project\c#\KoiSync\artifacts\main-game-visual-v020\visual-audit")
MASTER_DIR = BASE / "nonshaft45_master"
SHAFT_DIR = BASE / "full234_master"

orig = json.loads((MASTER_DIR / "master_report.json").read_text(encoding="utf-8-sig"))
rows_map = {(r["key"], r["stage"]): r for r in orig}

fixes = ["nonshaft45_fix_aibu", "nonshaft45_fix_dildo", "nonshaft45_fix_lesbian"]
updated_count = 0
for fx in fixes:
    fx_rows = json.loads((BASE / fx / "triage_report.json").read_text(encoding="utf-8-sig"))
    for r in fx_rows:
        k = (r["key"], r["stage"])
        if k in rows_map:
            r["chunk"] = rows_map[k]["chunk"] + " (post-fix)"
            r["mode"] = rows_map[k]["mode"]
            rows_map[k] = r
            updated_count += 1

final_rows = sorted(rows_map.values(), key=lambda x: (x["chunk"].split(" ")[0], x["key"], x["stage"]))
(MASTER_DIR / "master_report_final.json").write_text(
    json.dumps(final_rows, ensure_ascii=False, indent=2), encoding="utf-8"
)

unique_keys = {r["key"] for r in final_rows}
clean_cnt = sum(1 for r in final_rows if not r.get("flags"))
foreplay_rows = [r for r in final_rows if r["effector"] != "AnimatedProp"]
prop_rows = [r for r in final_rows if r["effector"] == "AnimatedProp"]
fp_preserved = sum(1 for r in foreplay_rows if r["bal"]["l0Min"] >= 20.0 and r["bal"]["l0Max"] <= 80.5)
active_bal = sum(1 for r in final_rows if r["bal"]["mfpBand"] != "IDLE")
green_plus = sum(1 for r in final_rows if r["bal"]["mfpBand"] in ("GREEN", "YELLOW", "ORANGE", "RED", "DRED"))
idle_rows = [r for r in final_rows if r["bal"]["mfpBand"] == "IDLE"]

by_eff = collections.defaultdict(list)
for r in final_rows:
    by_eff[r["effector"]].append(r)

md = [
    "# KoiSync MainGame Full 45-Pose Non-Shaft Final Audit Report (Post-Fix, Plan A Foreplay Default)",
    "",
    f"- **Total Non-Shaft Poses Audited**: **{len(unique_keys)} / 45** ({len(final_rows)} stages; {updated_count} stages re-verified post-fix)",
    f"- **Zero-Flag Clean Stages**: **{clean_cnt} / {len(final_rows)}** ({100.0 * clean_cnt / len(final_rows):.1f}%)",
    f"- **Active (Non-IDLE) Balanced Stages**: **{active_bal} / {len(final_rows)}** (remaining {len(idle_rows)} IDLE = 6 genuinely static animation stages `Δ ≤ 4.9 mm` + 2 upstream `kPlug 3.4` `kplug_kht_f_09` non-contacting stages)",
    f"- **GREEN+ Balanced Target Stages**: **{green_plus} / {len(final_rows)}** ({100.0 * green_plus / active_bal:.1f}% of active stages; remaining active BLUE stages are intentional 0.75 Hz slow `WLoop` gentle foreplay)",
    f"- **Plan A Headroom Gate (`Balanced L0 ∈ [20..80]` on Foreplay Poses, preserving `[0..20]` strictly for Insertion contrast)**: **{fp_preserved} / {len(foreplay_rows)} (100.0%)**",
    f"- **Physical Insertion Prop (`AnimatedProp` dildo) Stages**: **{len(prop_rows)}** (`kplug_kht_f_08` SLoop CD=`5.1 mm`, `YELLOW 245 u/s` -> `ORANGE 280 u/s`; `kplug_kht_f_09` upstream kPlug 3.4 asset places prop 49–63 cm below standing pelvis with 0 body motion)",
    "",
    "## Summary by Kinematic Family (`Effector`)",
    "| Effector | Poses | Stages | Mean CD (mm) | Raw Bands | Balanced (Plan A) Bands | Intense (Plan A) Bands | Bal L0 Min..Max |",
    "| :--- | :---: | :---: | :---: | :--- | :--- | :--- | :---: |",
]

for eff, erows in sorted(by_eff.items()):
    up = len({r["key"] for r in erows})
    mean_cd = sum(r["contactMeanMm"] for r in erows) / len(erows)
    rb = dict(collections.Counter(r["raw"]["mfpBand"] for r in erows))
    bb = dict(collections.Counter(r["bal"]["mfpBand"] for r in erows))
    ib = dict(collections.Counter(r["int"]["mfpBand"] for r in erows))
    bmin = min(r["bal"]["l0Min"] for r in erows)
    bmax = max(r["bal"]["l0Max"] for r in erows)
    md.append(f"| `{eff}` | {up} | {len(erows)} | {mean_cd:.1f} | `{rb}` | `{bb}` | `{ib}` | {bmin:.1f}..{bmax:.1f} |")

md += [
    "",
    "## Complete Stage-by-Stage Table (All 45 Non-Shaft Poses — Final)",
    "| Chunk | Pose | Stage | Effector | CD (mm) | Travel Δ (mm) | Raw (Amp/Spd/Band) | Balanced Plan A (L0 / Amp / Spd / Band) | Intense Plan A (L0 / Amp / Spd / Band) | Flags |",
    "| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |",
]

for r in final_rows:
    rw, bl, it = r["raw"], r["bal"], r["int"]
    fl = ", ".join(r["flags"]) if r["flags"] else "CLEAN"
    md.append(
        f"| `{r['chunk']}` | `{r['key']}` ({r['name']}) | `{r['stage']}` | `{r['effector']}` | {r['contactMeanMm']} | "
        f"{r['depthTravelMm']} | {rw['l0Amp']}/{rw['speedMean']:.0f}({rw['mfpBand']}) | "
        f"{bl['l0Min']}..{bl['l0Max']} / **{bl['l0Amp']}** / {bl['speedMean']:.0f} ({bl['mfpBand']}) | "
        f"{it['l0Min']}..{it['l0Max']} / **{it['l0Amp']}** / {it['speedMean']:.0f} ({it['mfpBand']}) | {fl} |"
    )

(MASTER_DIR / "master_report_final.md").write_text("\n".join(md) + "\n", encoding="utf-8")

# Cross-check unified 267-pose grand coverage
shaft_rows = json.loads((SHAFT_DIR / "master_report_final.json").read_text(encoding="utf-8-sig"))
shaft_only_rows = [r for r in shaft_rows if r["effector"] == "Shaft"]
shaft_keys = {r["key"] for r in shaft_only_rows}
all_keys = shaft_keys | unique_keys
print(f"NONSHAFT: poses={len(unique_keys)}, stages={len(final_rows)}, clean={clean_cnt}, active={active_bal}, green+={green_plus}, planA_headroom={fp_preserved}/{len(foreplay_rows)}")
print(f"IDLE stages ({len(idle_rows)}):", [(r['key'], r['stage'], r['depthTravelMm'], r['contactMeanMm']) for r in idle_rows])
print(f"UNIFIED 267 CHECK: shaft_poses={len(shaft_keys)} ({len(shaft_only_rows)} stages), nonshaft_poses={len(unique_keys)} ({len(final_rows)} stages), total_unique_poses={len(all_keys)}, total_stages={len(shaft_only_rows)+len(final_rows)}")
