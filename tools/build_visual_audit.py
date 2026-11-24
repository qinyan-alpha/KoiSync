#!/usr/bin/env python3
r"""
Post-process KoiSync MainGameLive visual-audit captures into:
  1. Per-stage annotated side-by-side MP4 videos (Clean | X-Ray 3D + Vector Overlay + 3-way HUD)
  2. Per-stage high-res visual contact sheets (Clean + X-Ray + Front45 + 3-way Raw/Bal/Int Waveform + 3 stacked MFP Heatmaps + Scoreboard)
  3. Master 3-way alignment & feel comparison report (triage_report.json & triage_report.md)
"""

from __future__ import annotations

import argparse
import json
import math
import pathlib
import shutil
import subprocess
import sys
from typing import Any

import numpy as np
from PIL import Image, ImageDraw, ImageFont


DEFAULT_AUDIT_ROOT = pathlib.Path(r"E:\project\c#\KoiSync\artifacts\main-game-visual-v020\visual-audit")


def load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        r"C:\Windows\Fonts\msyhbd.ttc",
        r"C:\Windows\Fonts\msyh.ttc",
        r"C:\Windows\Fonts\consolab.ttf",
        r"C:\Windows\Fonts\arialbd.ttf",
    ]
    for fp in candidates:
        if pathlib.Path(fp).exists():
            try:
                return ImageFont.truetype(fp, size)
            except Exception:
                pass
    return ImageFont.load_default()


FONT_XS = load_font(12)
FONT_SM = load_font(13)
FONT_MD = load_font(16)
FONT_LG = load_font(20)


def mfp_band(speed_units_per_sec: float) -> tuple[str, tuple[int, int, int]]:
    """MultiFunPlayer / OFS thermal color band for L0 speed (units/s on 0-100 scale)."""
    if speed_units_per_sec < 20:
        return "IDLE", (90, 95, 110)
    if speed_units_per_sec < 100:
        return "BLUE", (65, 145, 255)
    if speed_units_per_sec < 200:
        return "GREEN", (60, 210, 115)
    if speed_units_per_sec < 280:
        return "YELLOW", (245, 215, 55)
    if speed_units_per_sec < 360:
        return "ORANGE", (255, 145, 40)
    if speed_units_per_sec < 450:
        return "RED", (245, 65, 65)
    if speed_units_per_sec < 550:
        return "DEEP_RED", (200, 30, 85)
    return "OVERDRIVE", (180, 50, 235)


def valid_pt(pt: Any, w: int, h: int) -> tuple[float, float] | None:
    if not isinstance(pt, list) or len(pt) < 3:
        return None
    x, y, z = float(pt[0]), float(pt[1]), float(pt[2])
    if z <= 0.01 or not (math.isfinite(x) and math.isfinite(y)):
        return None
    if x < -100 or x > w + 100 or y < -100 or y > h + 100:
        return None
    return (x, y)


def draw_vectors(img: Image.Image, proj: dict[str, Any] | None, label_prefix: str = "") -> Image.Image:
    out = img.copy()
    if not proj:
        return out
    draw = ImageDraw.Draw(out, "RGBA")
    w, h = out.size

    orig = valid_pt(proj.get("orig"), w, h)
    end = valid_pt(proj.get("end"), w, h)
    obs = valid_pt(proj.get("obs"), w, h)
    cal = valid_pt(proj.get("cal"), w, h)
    inw = valid_pt(proj.get("inw"), w, h)
    outp = valid_pt(proj.get("out"), w, h)

    # Shaft axis: cyan line from root (circle) to tip (diamond)
    if orig and end:
        draw.line([orig, end], fill=(0, 235, 255, 230), width=3)
        r = 5
        draw.ellipse([orig[0] - r, orig[1] - r, orig[0] + r, orig[1] + r], fill=(0, 180, 255, 230), outline=(255, 255, 255, 240), width=2)
        d = 6
        draw.polygon([(end[0], end[1] - d), (end[0] + d, end[1]), (end[0], end[1] + d), (end[0] - d, end[1])],
                     fill=(0, 255, 220, 230), outline=(255, 255, 255, 240))

    # Inward opening normal: red arrow from calibrated entry
    if cal and inw:
        draw.line([cal, inw], fill=(255, 65, 80, 235), width=3)
        r = 4
        draw.ellipse([inw[0] - r, inw[1] - r, inw[0] + r, inw[1] + r], fill=(255, 65, 80, 240))

    # Observed entry: green crosshair
    if obs:
        x, y = obs
        draw.line([(x - 10, y), (x + 10, y)], fill=(50, 255, 95, 240), width=2)
        draw.line([(x, y - 10), (x, y + 10)], fill=(50, 255, 95, 240), width=2)
        draw.ellipse([x - 7, y - 7, x + 7, y + 7], outline=(50, 255, 95, 240), width=2)

    # Calibrated entry: yellow ring
    if cal:
        x, y = cal
        draw.ellipse([x - 11, y - 11, x + 11, y + 11], outline=(255, 230, 50, 240), width=2)

    # Output reconstructed sleeve: orange square (if offset from cal)
    if outp and cal:
        if math.hypot(outp[0] - cal[0], outp[1] - cal[1]) > 4:
            x, y = outp
            draw.line([cal, outp], fill=(255, 155, 45, 210), width=2)
            draw.rectangle([x - 5, y - 5, x + 5, y + 5], outline=(255, 155, 45, 240), width=2)

    if label_prefix:
        draw.rectangle([4, 4, 275, 24], fill=(10, 12, 18, 175))
        draw.text((8, 6), label_prefix, fill=(240, 245, 255), font=FONT_SM)
    return out


def compute_drive_metrics(rows: list[dict[str, Any]], key: str) -> dict[str, Any]:
    ts = [float(r["t"]) for r in rows]
    l0s = [float(r[key][0]) * 100.0 for r in rows]
    l1s = [float(r[key][1]) * 100.0 for r in rows]
    l2s = [float(r[key][2]) * 100.0 for r in rows]
    r0s = [float(r[key][3]) * 100.0 for r in rows]
    r1s = [float(r[key][4]) * 100.0 for r in rows]
    r2s = [float(r[key][5]) * 100.0 for r in rows]

    seg_speeds = []
    for i in range(1, len(rows)):
        dt = ts[i] - ts[i - 1]
        if dt > 1e-4:
            seg_speeds.append(abs(l0s[i] - l0s[i - 1]) / dt)
    mean_spd = float(np.mean(seg_speeds)) if seg_speeds else 0.0
    max_spd = float(np.max(seg_speeds)) if seg_speeds else 0.0
    band_name, _ = mfp_band(mean_spd)
    return {
        "l0Min": round(min(l0s), 1),
        "l0Max": round(max(l0s), 1),
        "l0Amp": round(max(l0s) - min(l0s), 1),
        "l0Mid": round(0.5 * (max(l0s) + min(l0s)), 1),
        "l1Amp": round(max(l1s) - min(l1s), 1),
        "l2Amp": round(max(l2s) - min(l2s), 1),
        "r0Amp": round(max(r0s) - min(r0s), 1),
        "r1Amp": round(max(r1s) - min(r1s), 1),
        "r2Amp": round(max(r2s) - min(r2s), 1),
        "speedMean": round(mean_spd, 1),
        "speedMax": round(max_spd, 1),
        "mfpBand": band_name,
    }


def compute_stage_stats(rows: list[dict[str, Any]], meta: dict[str, Any]) -> dict[str, Any]:
    if not rows:
        return {"frames": 0}
    cds = [float(r["cdMm"]) for r in rows]
    depths = [float(r["depthMm"]) for r in rows]
    mats = [float(r["mat"]) for r in rows]
    engs = [float(r["eng"]) for r in rows]
    lens = [float(r["lenMm"]) for r in rows]
    in_l0s = [float(r["inL0"]) * 100.0 for r in rows]

    has_3way = ("rawAx" in rows[0]) and ("balAx" in rows[0]) and ("intAx" in rows[0])
    primary = compute_drive_metrics(rows, "balAx" if has_3way else "ax")
    raw_m = compute_drive_metrics(rows, "rawAx") if has_3way else primary
    bal_m = compute_drive_metrics(rows, "balAx") if has_3way else primary
    int_m = compute_drive_metrics(rows, "intAx") if has_3way else primary

    cycle_sec = float(meta.get("cycleSec") or 1.2)
    freq_hz = 1.0 / cycle_sec if cycle_sec > 0.05 else 0.0

    cam_ang = float(meta.get("camAngleDeg", 90.0))
    vis_px = int(meta.get("penisVisPx", -1))
    near_px = int(meta.get("nearEntryPx", -1))
    shaft_span = float(meta.get("shaftSpan", -1.0))

    # Geometry, camera & feel triage flags (evaluate feel against Balanced output)
    flags = []
    if np.mean(cds) > 20.0 or np.max(cds) > 35.0:
        flags.append(f"HIGH_CONTACT_DIST(mean={np.mean(cds):.1f}mm,max={np.max(cds):.1f}mm)")
    if min(engs) < 0.8:
        flags.append(f"ENGAGEMENT_DROP(min={min(engs):.2f})")
    if np.mean(mats) < 0.01 and meta.get("contact") in ("VAGINAL", "ANAL", "ORAL"):
        flags.append("ZERO_MAT_INSERTION")
    if vis_px >= 0 and vis_px < 15:
        flags.append(f"PENIS_BURIED_OR_OCCLUDED(visPx={vis_px})")
    if cam_ang < 60.0:
        flags.append(f"FORESHORTENED_CAM({cam_ang:.0f}deg)")
    if bal_m["l0Amp"] < 25.0:
        flags.append(f"LOW_STIM_AMP(BAL_L0={bal_m['l0Amp']:.1f})")
    if bal_m["speedMean"] < 100.0:
        flags.append(f"LOW_SPEED_BAND({bal_m['mfpBand']}:{bal_m['speedMean']:.0f}u/s)")

    return {
        "key": meta.get("key", ""),
        "name": meta.get("name", ""),
        "stage": meta.get("stage", ""),
        "contact": meta.get("contact", ""),
        "effector": meta.get("effector", ""),
        "entrySource": meta.get("entrySource", ""),
        "previewLoaded": bool(meta.get("previewLoaded", False)),
        "camAngleDeg": round(cam_ang, 1),
        "penisVisPx": vis_px,
        "nearEntryPx": near_px,
        "shaftSpan": round(shaft_span, 2),
        "nativeSetPlay": bool(meta.get("nativeSetPlay", False)),
        "has3Way": has_3way,
        "frames": len(rows),
        "cycleSec": round(cycle_sec, 3),
        "freqHz": round(freq_hz, 2),
        "contactMeanMm": round(float(np.mean(cds)), 2),
        "contactMaxMm": round(float(np.max(cds)), 2),
        "depthMinMm": round(float(min(depths)), 1),
        "depthMaxMm": round(float(max(depths)), 1),
        "depthTravelMm": round(float(max(depths) - min(depths)), 1),
        "modelLenMm": round(float(np.median(lens)), 1),
        "matMean": round(float(np.mean(mats)), 3),
        "engMin": round(float(min(engs)), 2),
        "tipBoundPct": round(100.0 * sum(1 for r in rows if r.get("tipBound")) / len(rows), 0),
        "inL0Amp": round(max(in_l0s) - min(in_l0s), 1),
        "l0Min": primary["l0Min"],
        "l0Max": primary["l0Max"],
        "l0Amp": primary["l0Amp"],
        "l1Amp": primary["l1Amp"],
        "l2Amp": primary["l2Amp"],
        "r0Amp": primary["r0Amp"],
        "r1Amp": primary["r1Amp"],
        "r2Amp": primary["r2Amp"],
        "speedMean": primary["speedMean"],
        "speedMax": primary["speedMax"],
        "mfpBand": primary["mfpBand"],
        "raw": raw_m,
        "bal": bal_m,
        "int": int_m,
        "flags": flags,
    }


def draw_hud_frame(clean: Image.Image, xray: Image.Image, row: dict[str, Any], meta: dict[str, Any], idx: int, total: int) -> Image.Image:
    w, h = clean.size  # 640x360
    has_3way = ("rawAx" in row) and ("balAx" in row) and ("intAx" in row)
    hud_h = 108 if has_3way else 84
    canvas = Image.new("RGB", (w * 2, h + hud_h), (16, 19, 26))
    canvas.paste(clean, (0, 0))
    canvas.paste(xray, (w, 0))

    draw = ImageDraw.Draw(canvas)
    draw.line([(w, 0), (w, h)], fill=(65, 75, 95), width=2)
    draw.rectangle([6, 6, 235, 26], fill=(12, 14, 20))
    draw.text((12, 9), "REAL GAME (UNOBSTRUCTED)", fill=(230, 238, 250), font=FONT_SM)
    draw.rectangle([w + 6, 6, w + 325, 26], fill=(12, 14, 20))
    draw.text((w + 12, 9), "3D X-RAY + VECTORS (G=Obs Y=Cal C=Shaft R=Inw)", fill=(245, 235, 160), font=FONT_SM)

    y0 = h + 6
    title = f"{meta.get('key')} [{meta.get('stage')}]  {meta.get('name')}  ({meta.get('contact')} / {meta.get('effector')})  Frame {idx+1}/{total}  t={row['t']:.2f}s"
    draw.text((12, y0), title, fill=(240, 245, 255), font=FONT_MD)

    cd = float(row["cdMm"])
    cd_col = (80, 235, 120) if cd < 10 else ((245, 210, 60) if cd < 22 else (255, 80, 80))
    metrics = (
        f"ContactDist: {cd:.1f} mm  |  Real Depth: {row['depthMm']:.1f} mm / Len {row['lenMm']:.0f} mm  |  "
        f"MatIns: {row['mat']:.2f}  |  Eng: {row['eng']:.2f}  |  TipPin: {row['tipBound']}  |  EntrySrc: {row.get('entrySrc','')}"
    )
    draw.text((12, y0 + 25), metrics, fill=cd_col, font=FONT_SM)

    by = y0 + 48
    if has_3way:
        comp = [
            ("RAW L0", float(row["rawAx"][0]), (165, 175, 195)),
            ("BAL L0", float(row["balAx"][0]), (255, 155, 45)),
            ("INT L0", float(row["intAx"][0]), (245, 70, 75)),
        ]
        cx = 12
        for lbl, val, col in comp:
            draw.text((cx, by), f"{lbl}:{val*100:04.1f}", fill=col, font=FONT_SM)
            bx0 = cx + 92
            bw = 305
            draw.rectangle([bx0, by + 2, bx0 + bw, by + 14], fill=(34, 40, 54), outline=(80, 90, 110))
            # 80% golden window tick
            x80 = bx0 + int(0.80 * bw)
            draw.line([(x80, by + 1), (x80, by + 15)], fill=(90, 200, 120), width=1)
            fw = int(max(0.0, min(1.0, val)) * bw)
            draw.rectangle([bx0, by + 2, bx0 + fw, by + 14], fill=col)
            cx += 420
        by += 26

    # 6-axis live bars (Balanced if 3-way)
    ax_names = ["L0", "L1", "L2", "R0", "R1", "R2"]
    ax_vals = row["balAx"] if has_3way else row["ax"]
    bx = 12
    for i, name in enumerate(ax_names):
        val = float(ax_vals[i])
        draw.text((bx, by), f"{name}:{val*100:04.1f}", fill=(200, 210, 230), font=FONT_SM)
        bar_x = bx + 62
        bar_w = 132
        draw.rectangle([bar_x, by + 2, bar_x + bar_w, by + 14], fill=(38, 44, 58), outline=(80, 90, 110))
        fill_w = int(max(0.0, min(1.0, val)) * bar_w)
        bar_col = (255, 150, 50) if i == 0 else (75, 190, 255)
        draw.rectangle([bar_x, by + 2, bar_x + fill_w, by + 14], fill=bar_col)
        bx += 208
    return canvas


def draw_dashed_polyline(draw: ImageDraw.ImageDraw, pts: list[tuple[int, int]], fill: tuple[int, int, int], width: int = 2) -> None:
    for i in range(len(pts) - 1):
        if i % 2 == 0:
            draw.line([pts[i], pts[i + 1]], fill=fill, width=width)


def build_contact_sheet(case_dir: pathlib.Path, rows: list[dict[str, Any]], meta: dict[str, Any], stats: dict[str, Any]) -> pathlib.Path:
    n = len(rows)
    has_3way = stats.get("has3Way", False)
    idxs = [min(n - 1, int(round(k * (n - 1) / 3.0))) for k in range(4)]

    # Pick 2 front45 frames closest to deepest (min L0) and shallowest (max L0)
    f45_indices = [i for i, r in enumerate(rows) if r.get("f45") is not None and (case_dir / f"f{i:02d}_front45_xray.png").exists()]
    if len(f45_indices) >= 2:
        deep_i = min(f45_indices, key=lambda i: rows[i]["depthMm"] * -1.0)
        out_i = max(f45_indices, key=lambda i: rows[i]["depthMm"] * -1.0)
        if deep_i == out_i:
            out_i = f45_indices[len(f45_indices) // 2]
        f45_pair = [(deep_i, "ORTHO-2 DEEPEST"), (out_i, "ORTHO-2 SHALLOWEST")]
    elif len(f45_indices) == 1:
        f45_pair = [(f45_indices[0], "ORTHO-2")]
    else:
        f45_pair = []

    tw, th = 460, 259  # 16:9 tiles
    pad = 10
    header_h = 56
    W = pad + 4 * (tw + pad)
    H = header_h + 3 * (th + pad) + pad
    sheet = Image.new("RGB", (W, H), (14, 17, 24))
    draw = ImageDraw.Draw(sheet)

    raw_s, bal_s, int_s = stats["raw"], stats["bal"], stats["int"]
    if has_3way:
        hdr = (
            f"{stats['key']} [{stats['stage']}]  {stats['name']}  |  Depth: {stats['depthMinMm']}..{stats['depthMaxMm']}mm (Δ{stats['depthTravelMm']}mm)  |  "
            f"RAW {raw_s['l0Amp']} ({raw_s['speedMean']:.0f}u/s {raw_s['mfpBand']}) -> "
            f"BAL {bal_s['l0Amp']} ({bal_s['speedMean']:.0f}u/s {bal_s['mfpBand']}) -> "
            f"INT {int_s['l0Amp']} ({int_s['speedMean']:.0f}u/s {int_s['mfpBand']})"
        )
    else:
        hdr = (
            f"{stats['key']} [{stats['stage']}]  {stats['name']}  |  ContactDist: {stats['contactMeanMm']}mm  |  "
            f"L0 Amp: {stats['l0Amp']}  Speed: {stats['speedMean']} u/s ({stats['mfpBand']})  Freq: {stats['freqHz']} Hz"
        )
    draw.rectangle([0, 0, W, header_h - 4], fill=(24, 29, 42))
    draw.text((14, 9), hdr, fill=(245, 248, 255), font=FONT_LG)
    flag_txt = (
        f"CD={stats['contactMeanMm']}mm | CamAng={stats['camAngleDeg']}deg VisPx={stats['penisVisPx']} Span={stats['shaftSpan']} | Freq={stats['freqHz']}Hz | "
        f"STATUS: " + (", ".join(stats["flags"]) if stats["flags"] else "ALL CLEAN (PERP CAM + ALIGNED + TARGET BAND)")
    )
    flag_col = (255, 110, 95) if stats["flags"] else (90, 240, 140)
    draw.text((14, 34), flag_txt, fill=flag_col, font=FONT_SM)

    # Row 1: 4 Clean unobstructed frames; Row 2: 4 X-Ray + Vector frames
    for col, fi in enumerate(idxs):
        x = pad + col * (tw + pad)
        y1 = header_h
        y2 = header_h + th + pad
        r = rows[fi]
        p_clean = case_dir / f"f{fi:02d}_side_clean.png"
        p_xray = case_dir / f"f{fi:02d}_side_xray.png"
        if p_clean.exists():
            im_c = Image.open(p_clean).convert("RGB").resize((tw, th), Image.Resampling.LANCZOS)
            dc = ImageDraw.Draw(im_c)
            dc.rectangle([4, 4, 305, 24], fill=(10, 12, 18))
            if has_3way:
                lbl_c = f"CLEAN f{fi:02d}  D={r['depthMm']:.0f}mm  RAW={r['rawAx'][0]*100:.0f} BAL={r['balAx'][0]*100:.0f}"
            else:
                lbl_c = f"CLEAN f{fi:02d}  L0={r['ax'][0]*100:.1f}  D={r['depthMm']:.0f}mm"
            dc.text((8, 6), lbl_c, fill=(235, 242, 255), font=FONT_SM)
            sheet.paste(im_c, (x, y1))
        if p_xray.exists():
            im_x = draw_vectors(Image.open(p_xray).convert("RGB"), r.get("side"), f"XRAY f{fi:02d}  CD={r['cdMm']:.1f}mm  M={r['mat']:.2f}")
            im_x = im_x.resize((tw, th), Image.Resampling.LANCZOS)
            sheet.paste(im_x, (x, y2))

    # Row 3 Left (2 tiles): Front45 X-Ray + Vectors
    y3 = header_h + 2 * (th + pad)
    for col, (fi, lbl) in enumerate(f45_pair[:2]):
        x = pad + col * (tw + pad)
        r = rows[fi]
        p_f = case_dir / f"f{fi:02d}_front45_xray.png"
        if p_f.exists():
            im_f = draw_vectors(Image.open(p_f).convert("RGB"), r.get("f45"), f"{lbl} f{fi:02d}  D={r['depthMm']:.0f}mm")
            im_f = im_f.resize((tw, th), Image.Resampling.LANCZOS)
            sheet.paste(im_f, (x, y3))

    # Row 3 Right (2 tiles merged): 3-Way L0 Waveform + 3 Stacked MultiFunPlayer Heatmap Bars + Scoreboard
    px0 = pad + 2 * (tw + pad)
    pw = 2 * tw + pad
    draw.rectangle([px0, y3, px0 + pw, y3 + th], fill=(20, 24, 34), outline=(60, 72, 96))

    gx0, gy0, gw = px0 + 44, y3 + 26, int(pw * 0.54)
    gh = th - (102 if has_3way else 76)
    title_plot = "L0: RAW(Gray) vs BAL(Orange) vs INT(Red)  [0=Deep .. 80=Gold .. 100=Out]" if has_3way else "Cycle Waveform: L0 (Orange) & R2 Pitch (Cyan)"
    draw.text((gx0, y3 + 6), title_plot, fill=(220, 230, 245), font=FONT_XS)
    draw.rectangle([gx0, gy0, gx0 + gw, gy0 + gh], fill=(12, 15, 22), outline=(70, 80, 100))

    # 0..80 Golden Window line
    y80 = gy0 + int(gh * 0.20)
    draw.line([(gx0, y80), (gx0 + gw, y80)], fill=(70, 175, 100), width=1)
    draw.text((gx0 - 34, y80 - 7), "80", fill=(100, 210, 130), font=FONT_XS)
    draw.text((gx0 - 36, gy0 - 5), "100", fill=(150, 160, 180), font=FONT_XS)
    draw.text((gx0 - 22, gy0 + gh - 10), "0", fill=(150, 160, 180), font=FONT_XS)

    if n >= 2:
        if has_3way:
            pts_raw = [(gx0 + int(i / (n - 1) * gw), gy0 + int((1.0 - float(r["rawAx"][0])) * gh)) for i, r in enumerate(rows)]
            pts_bal = [(gx0 + int(i / (n - 1) * gw), gy0 + int((1.0 - float(r["balAx"][0])) * gh)) for i, r in enumerate(rows)]
            pts_int = [(gx0 + int(i / (n - 1) * gw), gy0 + int((1.0 - float(r["intAx"][0])) * gh)) for i, r in enumerate(rows)]
            draw_dashed_polyline(draw, pts_raw, (175, 185, 205), width=2)
            draw.line(pts_int, fill=(245, 70, 75), width=2)
            draw.line(pts_bal, fill=(255, 165, 45), width=3)

            # 3 stacked MFP speed heatmap bars: RAW / BAL / INT
            strip_specs = [("RAW", "rawAx"), ("BAL", "balAx"), ("INT", "intAx")]
            hm_y = gy0 + gh + 8
            hm_h = 14
            for lbl, key in strip_specs:
                draw.text((gx0 - 36, hm_y), lbl, fill=(195, 205, 220), font=FONT_XS)
                for i in range(1, n):
                    dt = max(1e-3, float(rows[i]["t"]) - float(rows[i - 1]["t"]))
                    spd = abs(float(rows[i][key][0]) - float(rows[i - 1][key][0])) * 100.0 / dt
                    _, col = mfp_band(spd)
                    x_a = gx0 + int((i - 1) / (n - 1) * gw)
                    x_b = gx0 + int(i / (n - 1) * gw)
                    draw.rectangle([x_a, hm_y, x_b, hm_y + hm_h], fill=col)
                draw.rectangle([gx0, hm_y, gx0 + gw, hm_y + hm_h], outline=(95, 105, 125))
                hm_y += hm_h + 5
            draw.text((gx0, hm_y + 1), "MFP Bands: Blue<100 Green<200 Yel<280 Org<360 Red<450 DRed<550", fill=(170, 180, 200), font=FONT_XS)
        else:
            pts_l0 = [(gx0 + int(i / (n - 1) * gw), gy0 + int((1.0 - float(r["ax"][0])) * gh)) for i, r in enumerate(rows)]
            pts_r2 = [(gx0 + int(i / (n - 1) * gw), gy0 + int((1.0 - float(r["ax"][5])) * gh)) for i, r in enumerate(rows)]
            draw.line(pts_r2, fill=(75, 200, 255), width=2)
            draw.line(pts_l0, fill=(255, 155, 45), width=3)
            hm_y = gy0 + gh + 10
            hm_h = 18
            for i in range(1, n):
                dt = max(1e-3, float(rows[i]["t"]) - float(rows[i - 1]["t"]))
                spd = abs(float(rows[i]["ax"][0]) - float(rows[i - 1]["ax"][0])) * 100.0 / dt
                _, col = mfp_band(spd)
                draw.rectangle([gx0 + int((i - 1) / (n - 1) * gw), hm_y, gx0 + int(i / (n - 1) * gw), hm_y + hm_h], fill=col)
            draw.rectangle([gx0, hm_y, gx0 + gw, hm_y + hm_h], outline=(100, 110, 130))

    # Scoreboard (right half)
    sx = gx0 + gw + 18
    sy = y3 + 12
    lines = [
        ("GEOMETRY TRUTH", (255, 230, 130)),
        (f"EntrySrc : {stats['entrySource']}", (215, 225, 240)),
        (f"ContactD : {stats['contactMeanMm']}mm (max {stats['contactMaxMm']})", (100, 240, 140) if stats["contactMeanMm"] < 12 else (255, 110, 90)),
        (f"Depth    : {stats['depthMinMm']}..{stats['depthMaxMm']}mm (Δ{stats['depthTravelMm']})", (215, 225, 240)),
        (f"ModelLen : {stats['modelLenMm']}mm | Mat={stats['matMean']} | Eng={stats['engMin']}", (215, 225, 240)),
        ("", (0, 0, 0)),
        ("3-WAY L0 COMPARISON (Target 0..80)", (120, 220, 255)),
        (f"RAW : {raw_s['l0Min']}..{raw_s['l0Max']} A={raw_s['l0Amp']} | {raw_s['speedMean']:.0f}u/s {raw_s['mfpBand']}", (185, 195, 210)),
        (f"BAL : {bal_s['l0Min']}..{bal_s['l0Max']} A={bal_s['l0Amp']} | {bal_s['speedMean']:.0f}u/s {bal_s['mfpBand']}", (255, 175, 70)),
        (f"INT : {int_s['l0Min']}..{int_s['l0Max']} A={int_s['l0Amp']} | {int_s['speedMean']:.0f}u/s {int_s['mfpBand']}", (255, 95, 95)),
        ("", (0, 0, 0)),
        (f"BAL 6-Ax : L1={bal_s['l1Amp']} L2={bal_s['l2Amp']} R0={bal_s['r0Amp']}", (195, 210, 225)),
        (f"         : R1={bal_s['r1Amp']} R2={bal_s['r2Amp']} | Freq={stats['freqHz']}Hz", (195, 210, 225)),
    ]
    for txt, col in lines:
        if txt:
            draw.text((sx, sy), txt, fill=col, font=FONT_XS)
        sy += 18

    out_path = case_dir / "contact_sheet.png"
    sheet.save(out_path, optimize=True)
    return out_path


def encode_mp4(frames_dir: pathlib.Path, out_mp4: pathlib.Path, fps: float = 12.0) -> bool:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        for cand in (r"D:\c++ package\ffmpeg\bin\ffmpeg.exe", r"C:\ffmpeg\bin\ffmpeg.exe"):
            if pathlib.Path(cand).exists():
                ffmpeg = cand
                break
    if not ffmpeg:
        return False
    cmd = [
        ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
        "-stream_loop", "1",
        "-framerate", f"{fps:.2f}",
        "-i", str(frames_dir / "hud_%02d.png"),
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "22", "-movflags", "+faststart",
        str(out_mp4),
    ]
    res = subprocess.run(cmd, capture_output=True, text=True)
    return res.returncode == 0


def process_case(case_dir: pathlib.Path, skip_video: bool = False) -> dict[str, Any] | None:
    meta_path = case_dir / "meta.json"
    tel_path = case_dir / "telemetry.jsonl"
    if not meta_path.exists() or not tel_path.exists():
        return None
    meta = json.loads(meta_path.read_text(encoding="utf-8-sig"))
    rows = [json.loads(line) for line in tel_path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    if not rows:
        return None

    stats = compute_stage_stats(rows, meta)
    sheet_path = build_contact_sheet(case_dir, rows, meta, stats)
    stats["contactSheet"] = str(sheet_path)

    if not skip_video:
        tmp_hud = case_dir / "_hud_frames"
        tmp_hud.mkdir(exist_ok=True)
        for i, r in enumerate(rows):
            pc = case_dir / f"f{i:02d}_side_clean.png"
            px = case_dir / f"f{i:02d}_side_xray.png"
            if not pc.exists() or not px.exists():
                continue
            im_c = Image.open(pc).convert("RGB")
            im_x = draw_vectors(Image.open(px).convert("RGB"), r.get("side"))
            hud = draw_hud_frame(im_c, im_x, r, meta, i, len(rows))
            hud.save(tmp_hud / f"hud_{i:02d}.png")
        step_sec = float(meta.get("stepSec") or 0.06)
        fps = max(8.0, min(20.0, 1.0 / max(0.03, step_sec)))
        mp4_path = case_dir / "audit.mp4"
        if encode_mp4(tmp_hud, mp4_path, fps=fps):
            stats["video"] = str(mp4_path)
        shutil.rmtree(tmp_hud, ignore_errors=True)

    return stats


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", type=pathlib.Path, default=DEFAULT_AUDIT_ROOT)
    ap.add_argument("--skip-video", action="store_true")
    args = ap.parse_args()

    if not args.root.exists():
        raise FileNotFoundError(f"Audit root not found: {args.root}")

    results = []
    for sub in sorted(args.root.iterdir()):
        if not sub.is_dir():
            continue
        st = process_case(sub, skip_video=args.skip_video)
        if st:
            results.append(st)
            r, b, it = st["raw"], st["bal"], st["int"]
            print(
                f"  [OK] {st['key']:<18} {st['stage']:<8} | CD={st['contactMeanMm']:>4.1f}mm | "
                f"D={st['depthMinMm']:>5.1f}..{st['depthMaxMm']:>5.1f}mm (Δ{st['depthTravelMm']:>4.1f}) | "
                f"RAW {r['l0Amp']:>4.1f}/{r['speedMean']:>5.1f}({r['mfpBand']:<6}) -> "
                f"BAL {b['l0Amp']:>4.1f}/{b['speedMean']:>5.1f}({b['mfpBand']:<6}) -> "
                f"INT {it['l0Amp']:>4.1f}/{it['speedMean']:>5.1f}({it['mfpBand']:<6})"
            )

    (args.root / "triage_report.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    md = [
        "# KoiSync MainGame 3-Way Comparative Audit Report (Real Depth vs Raw vs Balanced vs Intense)",
        "",
        "| Pose | Stage | Cam (Ang/VisPx/Span) | ContactDist Mean/Max (mm) | Real Depth (mm) / ModelLen | Freq | Raw 原计算 (L0 / Amp / Spd / Band) | Balanced 均衡 (L0 / Amp / Spd / Band) | Intense 强爽 (L0 / Amp / Spd / Band) | Flags |",
        "| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |",
    ]
    for st in results:
        r, b, it = st["raw"], st["bal"], st["int"]
        fl = ", ".join(st["flags"]) if st["flags"] else "CLEAN"
        md.append(
            f"| `{st['key']}` ({st['name']}) | `{st['stage']}` | {st['camAngleDeg']}° / {st['penisVisPx']}px / {st['shaftSpan']} | {st['contactMeanMm']} / {st['contactMaxMm']} | "
            f"{st['depthMinMm']}..{st['depthMaxMm']} (Δ{st['depthTravelMm']}) / {st['modelLenMm']} | {st['freqHz']}Hz | "
            f"{r['l0Min']}..{r['l0Max']} / **{r['l0Amp']}** / {r['speedMean']} ({r['mfpBand']}) | "
            f"{b['l0Min']}..{b['l0Max']} / **{b['l0Amp']}** / {b['speedMean']} ({b['mfpBand']}) | "
            f"{it['l0Min']}..{it['l0Max']} / **{it['l0Amp']}** / {it['speedMean']} ({it['mfpBand']}) | {fl} |"
        )
    (args.root / "triage_report.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    print(f"\nProcessed {len(results)} stages -> {args.root / 'triage_report.md'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
