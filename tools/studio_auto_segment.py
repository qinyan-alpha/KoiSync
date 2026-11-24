#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
KoiSync.Studio Auto-Segmentation & Loop Seam Analyzer (`koisync.studio.director.v1`)

Fuses:
  1. Studio Scene PNG embedded Timeline XML:
     - NodesConstraints (`constraintEnabled`) attachment boundaries
     - Camera hard cuts (`cameraPos` discontinuities)
     - Hip/root bone pose teleports (`guideObjectPos` discontinuities)
     - Climax / prop activation burst clusters (`objectEnabled` bursts)
     - Multi-track 3D bone joint seam matching (position + velocity RMS in cm)
  2. Recorded 6-axis `.funscript` (L0 + optional surge/sway/twist/roll/pitch):
     - Real device L0 amplitude & stroke frequency (Hz)
     - Rhythm / speed-escalation change-point detection inside long segments
     - 6-axis funscript seam alignment
  3. Optional Visual Keyframe Extraction (for VLM / visual semantic tagging):
     - `--video <preview.mp4>` extracts 1 crisp midpoint frame per segment via ffmpeg
     - `--capture-live` asks running CharaStudio (127.0.0.1:41290) to seek & render each midpoint frame
     - Builds a labeled contact sheet `<stem>_segments_sheet.jpg` for fast visual review
  4. Optional `--apply-labels <labels.json>` and `--push-live` hot-reload into CharaStudio.
"""
from __future__ import annotations

import argparse
import base64
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

ROOT_RE = re.compile(rb'<root\s+[^>]*duration="([^"]+)"[^>]*>')
AXIS_SUFFIXES = [
    ("L0", ""),
    ("L1", ".surge"),
    ("L2", ".sway"),
    ("R0", ".twist"),
    ("R1", ".roll"),
    ("R2", ".pitch"),
]


def default_output_stem(scene_path: Path, game_root: Path) -> Path:
    scene_root = (game_root / "UserData" / "Studio" / "scene").resolve()
    full = scene_path.resolve()
    try:
        rel = full.relative_to(scene_root)
    except ValueError:
        rel = Path("External") / full.name
    return (game_root / "UserData" / "KoiSyncStudio" / rel).with_suffix("")


def extract_timeline_xml(scene_path: Path) -> ET.Element:
    data = scene_path.read_bytes()
    m = ROOT_RE.search(data)
    if not m:
        raise ValueError(f"No Timeline <root duration=...> XML found in {scene_path}")
    start = m.start()
    end = data.find(b"</root>", start)
    if end < 0:
        raise ValueError(f"Malformed Timeline XML (missing </root>) in {scene_path}")
    return ET.fromstring(data[start : end + 7].decode("utf-8", errors="ignore"))


def parse_kf_vec(k: ET.Element) -> list[float] | None:
    a = k.attrib
    if "valueX" in a:
        try:
            return [float(a.get("valueX", 0)), float(a.get("valueY", 0)), float(a.get("valueZ", 0))]
        except ValueError:
            return None
    if "value" in a:
        nums = [float(x) for x in re.findall(r"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?", a["value"])]
        if len(nums) >= 3:
            return nums[:3]
    return None


def sample_vec_track(pts: list[tuple[float, list[float]]], t: float) -> list[float]:
    if t <= pts[0][0]:
        return pts[0][1]
    if t >= pts[-1][0]:
        return pts[-1][1]
    lo, hi = 0, len(pts) - 1
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if pts[mid][0] <= t:
            lo = mid
        else:
            hi = mid
    t0, p0 = pts[lo]
    t1, p1 = pts[hi]
    a = 0.0 if t1 <= t0 else (t - t0) / (t1 - t0)
    return [p0[c] + a * (p1[c] - p0[c]) for c in range(3)]


def load_funscripts(stem_or_l0: Path) -> dict[str, list[tuple[float, float]]]:
    base = stem_or_l0
    if base.name.lower().endswith(".l0.funscript"):
        base = base.with_name(base.name[:-13])
    elif base.suffix.lower() == ".funscript":
        base = base.with_suffix("")

    axes: dict[str, list[tuple[float, float]]] = {}
    for axis_name, suffix in AXIS_SUFFIXES:
        candidates = [Path(str(base) + suffix + ".funscript")]
        if axis_name == "L0":
            candidates.append(Path(str(base) + ".L0.funscript"))
        for cand in candidates:
            if cand.exists():
                try:
                    raw = json.loads(cand.read_text(encoding="utf-8-sig"))
                    actions = raw.get("actions") or []
                    pts = sorted(
                        (float(a["at"]) / 1000.0, max(0.0, min(100.0, float(a["pos"]))))
                        for a in actions
                        if "at" in a and "pos" in a
                    )
                    if len(pts) >= 2:
                        axes[axis_name] = pts
                    break
                except Exception:
                    pass
    return axes


def sample_scalar_track(pts: list[tuple[float, float]], t: float) -> float:
    if t <= pts[0][0]:
        return pts[0][1]
    if t >= pts[-1][0]:
        return pts[-1][1]
    lo, hi = 0, len(pts) - 1
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if pts[mid][0] <= t:
            lo = mid
        else:
            hi = mid
    t0, v0 = pts[lo]
    t1, v1 = pts[hi]
    a = 0.0 if t1 <= t0 else (t - t0) / (t1 - t0)
    return v0 + a * (v1 - v0)


def parse_timeline_signals(root: ET.Element) -> dict[str, Any]:
    dur = float(root.attrib.get("duration", "0") or 0.0)
    nc_times: list[float] = []
    cam_pts: list[tuple[float, list[float]]] = []
    pos_tracks: list[tuple[str, list[tuple[float, list[float]]]]] = []
    obj_on_events: list[tuple[float, str]] = []
    obj_off_events: list[tuple[float, str]] = []
    late_obj_tracks: list[dict[str, Any]] = []

    for it in root.findall(".//interpolable"):
        if it.attrib.get("enabled", "true").lower() == "false":
            continue
        iid = it.attrib.get("id", "")
        owner = it.attrib.get("owner", "")
        oid = it.attrib.get("objectIndex", "")
        gpath = it.attrib.get("guideObjectPath", "")
        ks = it.findall("keyframe")
        if not ks:
            continue

        if iid == "constraintEnabled":
            for k in ks:
                t = float(k.attrib.get("time", 0))
                if 0.4 < t < dur - 0.4:
                    nc_times.append(t)
        elif "camera" in (owner + iid).lower() and "pos" in iid.lower():
            for k in ks:
                v = parse_kf_vec(k)
                if v:
                    cam_pts.append((float(k.attrib.get("time", 0)), v))
        elif iid == "guideObjectPos":
            pts: list[tuple[float, list[float]]] = []
            for k in ks:
                v = parse_kf_vec(k)
                if v:
                    pts.append((float(k.attrib.get("time", 0)), v))
            if len(pts) >= 6:
                pts.sort(key=lambda x: x[0])
                lbl = f"obj{oid}:{gpath.split('/')[-1] or 'root'}"
                pos_tracks.append((lbl, pts))
        elif iid == "objectEnabled":
            t_vals = sorted((float(k.attrib.get("time", 0)), str(k.attrib.get("value", "")).lower() == "true") for k in ks)
            if t_vals:
                first_t = t_vals[0][0]
                if first_t > 2.0:
                    late_obj_tracks.append({"obj": oid, "firstTime": round(first_t, 2), "keys": len(t_vals)})
                # Ignore high-frequency repeating FX blinkers (>12 keys across the scene);
                # climax/shooting props are sparse one-shot / short-episode activations (<=12 keys).
                if len(t_vals) <= 12:
                    for idx_k, (kt, kv) in enumerate(t_vals):
                        if kt > 1.0:
                            if kv and (idx_k == 0 or not t_vals[idx_k - 1][1]):
                                obj_on_events.append((kt, oid))
                            elif (not kv) and idx_k > 0 and t_vals[idx_k - 1][1]:
                                obj_off_events.append((kt, oid))

    # 1. Cluster NC boundaries (merge within 1.5s)
    nc_clusters: list[float] = []
    for t in sorted(nc_times):
        if not nc_clusters or t - nc_clusters[-1] > 1.5:
            nc_clusters.append(round(t, 2))

    # 2. Detect camera hard cuts (dt <= 0.15s and move > 0.12m)
    cam_pts.sort(key=lambda x: x[0])
    cam_cuts: list[float] = []
    for i in range(1, len(cam_pts)):
        t0, p0 = cam_pts[i - 1]
        t1, p1 = cam_pts[i]
        dist = math.sqrt(sum((a - b) ** 2 for a, b in zip(p0, p1)))
        if (t1 - t0) <= 0.15 and dist > 0.12:
            if not cam_cuts or t1 - cam_cuts[-1] > 1.5:
                cam_cuts.append(round(t1, 2))

    # 3. Detect hip/root pose teleports
    pose_cuts: list[float] = []
    for lbl, pts in pos_tracks:
        if "hips" in lbl.lower() or "root" in lbl.lower():
            for i in range(1, len(pts)):
                dt = pts[i][0] - pts[i - 1][0]
                dist = math.sqrt(sum((a - b) ** 2 for a, b in zip(pts[i][1], pts[i - 1][1])))
                if dt <= 0.15 and dist > 0.12:
                    pose_cuts.append(round(pts[i][0], 2))
    pose_clusters: list[float] = []
    for t in sorted(pose_cuts):
        if not pose_clusters or t - pose_clusters[-1] > 2.0:
            pose_clusters.append(t)

    # 4. Detect climax prop-activation bursts (>= 3 DISTINCT sparse objectEnabled tracks turning ON within 4.5s in final 45% of scene)
    climax_windows: list[tuple[float, float]] = []
    late_ons = sorted((t, oid) for t, oid in obj_on_events if t >= dur * 0.55)
    i = 0
    while i < len(late_ons):
        j = i
        distinct_ids: set[str] = set()
        while j < len(late_ons) and late_ons[j][0] - late_ons[i][0] <= 4.5:
            distinct_ids.add(late_ons[j][1])
            j += 1
        if len(distinct_ids) >= 3:
            c_start = late_ons[i][0]
            # Look for matching off-events of those same props (or any sparse prop) within 1.5..14s
            offs = [t for t, oid in obj_off_events if oid in distinct_ids and c_start + 1.0 <= t <= min(dur, c_start + 14.0)]
            c_end = max(offs) if offs else min(dur, late_ons[j - 1][0] + 5.0)
            if climax_windows and c_start <= climax_windows[-1][1] + 2.0:
                climax_windows[-1] = (climax_windows[-1][0], round(max(climax_windows[-1][1], c_end), 2))
            else:
                climax_windows.append((round(c_start, 2), round(c_end, 2)))
            i = j
        else:
            i += 1

    return {
        "duration": round(dur, 3),
        "nc_boundaries": nc_clusters,
        "cam_cuts": cam_cuts,
        "pose_cuts": pose_clusters,
        "climax_windows": climax_windows,
        "pos_tracks": pos_tracks,
        "late_object_tracks": late_obj_tracks,
    }


def detect_l0_change_points(l0_pts: list[tuple[float, float]], s0: float, s1: float) -> list[float]:
    """Inside a long segment (>28s), detect sustained stroke-frequency or amplitude shifts from recorded L0."""
    if s1 - s0 < 28.0:
        return []
    win = 4.0
    step = 2.0
    feats: list[tuple[float, float, float]] = []  # (mid_t, hz, amp)
    t = s0 + 1.0
    while t + win <= s1 - 1.0:
        dt = 0.05
        n = int(win / dt)
        vals = [sample_scalar_track(l0_pts, t + k * dt) for k in range(n)]
        amp = max(vals) - min(vals)
        rev = 0
        prev_d = 0
        for k in range(1, n):
            d = vals[k] - vals[k - 1]
            if abs(d) < 1.5:
                continue
            cd = 1 if d > 0 else -1
            if prev_d != 0 and cd != prev_d:
                rev += 1
            prev_d = cd
        hz = (rev / 2.0) / win
        feats.append((t + win * 0.5, hz, amp))
        t += step

    cuts: list[float] = []
    for i in range(2, len(feats) - 2):
        prev_hz = (feats[i - 2][1] + feats[i - 1][1]) * 0.5
        next_hz = (feats[i + 1][1] + feats[i + 2][1]) * 0.5
        prev_amp = (feats[i - 2][2] + feats[i - 1][2]) * 0.5
        next_amp = (feats[i + 1][2] + feats[i + 2][2]) * 0.5
        hz_ratio = max(prev_hz, next_hz) / max(0.15, min(prev_hz, next_hz))
        amp_diff = abs(next_amp - prev_amp)
        if (hz_ratio >= 1.55 and abs(next_hz - prev_hz) >= 0.45) or (amp_diff >= 28.0 and (prev_amp < 12.0 or next_amp < 12.0)):
            ct = round(feats[i][0], 2)
            if ct - s0 >= 8.0 and s1 - ct >= 8.0 and (not cuts or ct - cuts[-1] >= 10.0):
                cuts.append(ct)
    return cuts


def detect_bone_rhythm_splits(pos_tracks: list[tuple[str, list[tuple[float, list[float]]]]], s0: float, s1: float) -> list[float]:
    """Fallback change-point detector inside long segments (>26s) when no funscript is recorded yet."""
    if s1 - s0 < 26.0:
        return []
    # Find tracks that end or start mid-segment (e.g., leg/elbow stops moving when pose shifts!)
    mid_events: list[float] = []
    for _, pts in pos_tracks:
        t_first, t_last = pts[0][0], pts[-1][0]
        if s0 + 7.0 <= t_first <= s1 - 7.0:
            mid_events.append(t_first)
        if s0 + 7.0 <= t_last <= s1 - 7.0:
            mid_events.append(t_last)
    if len(mid_events) < 3:
        return []
    mid_events.sort()
    cuts: list[float] = []
    i = 0
    while i < len(mid_events):
        j = i
        while j < len(mid_events) and mid_events[j] - mid_events[i] <= 3.0:
            j += 1
        if j - i >= 3:
            ct = round(sum(mid_events[i:j]) / (j - i), 2)
            if not cuts or ct - cuts[-1] >= 8.0:
                cuts.append(ct)
            i = j
        else:
            i += 1
    return cuts


def estimate_autocorr_hz(values: list[float], dt: float) -> tuple[float, float]:
    n = len(values)
    if n < 24:
        return 0.0, 0.0
    mean = sum(values) / n
    arr = [v - mean for v in values]
    var = sum(x * x for x in arr) / n
    if var < 1e-9:
        return 0.0, 0.0
    best_r = 0.0
    best_lag = 0
    min_lag = max(3, int(round(0.25 / dt)))
    max_lag = min(int(round(4.0 / dt)), n // 2)
    for lag in range(min_lag, max_lag):
        c = sum(arr[i] * arr[i + lag] for i in range(n - lag)) / ((n - lag) * var + 1e-12)
        if c > best_r:
            c_prev = sum(arr[i] * arr[i + lag - 1] for i in range(n - lag + 1)) / ((n - lag + 1) * var + 1e-12)
            c_next = sum(arr[i] * arr[i + lag + 1] for i in range(n - lag - 1)) / ((n - lag - 1) * var + 1e-12) if lag + 1 < max_lag else -1.0
            if c >= c_prev and c >= c_next:
                best_r = c
                best_lag = lag
    hz = (1.0 / (best_lag * dt)) if best_lag > 0 else 0.0
    return round(hz, 3), round(best_r, 3)


def analyze_segment(
    index: int,
    s0: float,
    s1: float,
    dur: float,
    pos_tracks: list[tuple[str, list[tuple[float, list[float]]]]],
    funscripts: dict[str, list[tuple[float, float]]],
    climax_windows: list[tuple[float, float]],
) -> dict[str, Any]:
    slen = s1 - s0
    is_climax = any(not (cw[1] < s0 + 0.5 or cw[0] > s1 - 0.5) for cw in climax_windows)
    after_climax = any(s0 >= cw[1] - 0.6 for cw in climax_windows)

    # Collect active moving bone tracks in this segment
    active_bones: list[tuple[str, list[tuple[float, list[float]]], float]] = []
    for name, pts in pos_tracks:
        sub = [p for p in pts if s0 <= p[0] <= s1]
        if len(sub) >= 4:
            xs = [p[1][0] for p in sub]
            ys = [p[1][1] for p in sub]
            zs = [p[1][2] for p in sub]
            span = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
            if span > 0.01:
                active_bones.append((name, pts, span))

    # Sample at 10Hz inside [s0+margin, s1-margin]
    margin = min(0.4, slen * 0.1)
    dt = 0.1
    ts: list[float] = []
    t = s0 + margin
    while t <= s1 - margin:
        ts.append(round(t, 2))
        t += dt

    # Compute funscript L0 metrics if available
    l0_pts = funscripts.get("L0")
    l0_amp = 0.0
    l0_hz = 0.0
    l0_corr = 0.0
    if l0_pts and len(ts) >= 16:
        l0_vals = [sample_scalar_track(l0_pts, x) for x in ts]
        l0_amp = round(max(l0_vals) - min(l0_vals), 1)
        l0_hz, l0_corr = estimate_autocorr_hz(l0_vals, dt)

    # Compute bone periodicity metrics
    best_bone_hz = 0.0
    best_bone_corr = 0.0
    max_bone_cm = 0.0
    if active_bones and len(ts) >= 20:
        for _, pts, span in active_bones:
            max_bone_cm = max(max_bone_cm, span * 100.0)
            sampled = [sample_vec_track(pts, x) for x in ts]
            spans = [max(v[c] for v in sampled) - min(v[c] for v in sampled) for c in range(3)]
            dom = spans.index(max(spans))
            hz, corr = estimate_autocorr_hz([v[dom] for v in sampled], dt)
            if corr > best_bone_corr:
                best_bone_corr = corr
                best_bone_hz = hz

    freq_hz = l0_hz if (l0_corr >= 0.55 and l0_hz > 0) else best_bone_hz
    amp_pct = l0_amp if l0_pts else round(min(100.0, max_bone_cm * 8.0), 1)
    corr_best = max(l0_corr, best_bone_corr)

    # Joint multi-track + funscript loop seam search
    loop_start = round(s0, 2)
    loop_end = round(s1, 2)
    seam_err_cm = 99.0
    if slen >= 4.0 and len(ts) >= 30 and (active_bones or l0_pts):
        bone_vecs = []
        fs_vecs = []
        fs_keys = [k for k, _ in AXIS_SUFFIXES if k in funscripts]
        for x in ts:
            bv: list[float] = []
            for _, pts, _ in active_bones:
                bv.extend(sample_vec_track(pts, x))
            bone_vecs.append(bv)
            fs_vecs.append([sample_scalar_track(funscripts[k], x) / 100.0 for k in fs_keys])

        min_span_idx = max(20, int(len(ts) * 0.30))
        best = (999.0, s0, s1, 99.0)
        for i in range(1, len(ts) - min_span_idx - 1):
            bi = bone_vecs[i]
            bi_v = [bone_vecs[i + 1][k] - bone_vecs[i - 1][k] for k in range(len(bi))]
            fi = fs_vecs[i]
            fi_v = [fs_vecs[i + 1][k] - fs_vecs[i - 1][k] for k in range(len(fi))]
            for j in range(i + min_span_idx, len(ts) - 1):
                bj = bone_vecs[j]
                bj_v = [bone_vecs[j + 1][k] - bone_vecs[j - 1][k] for k in range(len(bj))]
                bone_err_cm = 0.0
                vel_err_cm = 0.0
                if active_bones:
                    bone_err_cm = math.sqrt(sum((bi[k] - bj[k]) ** 2 for k in range(len(bi))) / len(active_bones)) * 100.0
                    vel_err_cm = math.sqrt(sum((bi_v[k] - bj_v[k]) ** 2 for k in range(len(bi))) / len(active_bones)) * 100.0
                fs_err = 0.0
                if fs_keys:
                    fj = fs_vecs[j]
                    fj_v = [fs_vecs[j + 1][k] - fs_vecs[j - 1][k] for k in range(len(fj))]
                    fs_err = math.sqrt(sum((fi[k] - fj[k]) ** 2 for k in range(len(fi))) / len(fs_keys)) * 10.0
                    fs_err += 0.5 * math.sqrt(sum((fi_v[k] - fj_v[k]) ** 2 for k in range(len(fi))) / len(fs_keys)) * 10.0
                score = bone_err_cm + 0.5 * vel_err_cm + fs_err
                if score < best[0]:
                    best = (score, ts[i], ts[j], bone_err_cm if active_bones else fs_err)
        loop_start = round(best[1], 2)
        loop_end = round(best[2], 2)
        seam_err_cm = round(best[3], 2)

    # Heuristic role & label
    if is_climax:
        role = "climax"
        label = "高潮爆发"
    elif after_climax:
        role = "afterglow"
        label = "高潮余韵"
    elif slen < 5.0:
        role = "transition"
        label = "姿态过渡"
    elif index == 0 and (amp_pct < 25.0 or freq_hz < 0.7):
        role = "intro"
        label = "开场前戏"
    elif amp_pct < 12.0 and max_bone_cm < 1.5:
        role = "foreplay"
        label = "轻柔互动"
    elif freq_hz >= 1.6:
        role = "fast"
        label = f"高速冲刺 ({freq_hz:.1f}Hz)"
    elif freq_hz >= 0.9:
        role = "medium"
        label = f"中速主循环 ({freq_hz:.1f}Hz)"
    else:
        role = "slow"
        label = f"慢速节奏 ({freq_hz:.1f}Hz)" if freq_hz > 0 else "慢速互动"

    loopable = (
        role not in ("climax", "afterglow", "transition")
        and (loop_end - loop_start) >= 2.5
        and (seam_err_cm <= 3.5 or corr_best >= 0.65)
    )

    return {
        "id": f"seg_{index + 1:02d}",
        "label": label,
        "role": role,
        "notes": f"auto: tracks={len(active_bones)}, corr={corr_best:.2f}, seam={seam_err_cm:.2f}cm",
        "start": round(s0, 2),
        "end": round(s1, 2),
        "loopStart": loop_start,
        "loopEnd": loop_end,
        "loopable": loopable,
        "frequencyHz": round(freq_hz, 2),
        "amplitudePercent": round(amp_pct, 1),
        "seamErrorCm": round(seam_err_cm if seam_err_cm < 90 else 0.0, 2),
        "defaultSpeed": 1.0,
    }


def build_director_plan(scene_path: Path, funscript_path: Path | None) -> dict[str, Any]:
    root = extract_timeline_xml(scene_path)
    sig = parse_timeline_signals(root)
    dur = sig["duration"]
    funscripts = load_funscripts(funscript_path) if funscript_path else {}

    # 1. Initial coarse boundaries from NC + camera hard cuts + pose teleports + climax windows
    raw_bounds: list[float] = [0.0, dur]
    raw_bounds.extend(sig["nc_boundaries"])
    raw_bounds.extend(sig["cam_cuts"])
    raw_bounds.extend(sig["pose_cuts"])
    for c0, c1 in sig["climax_windows"]:
        raw_bounds.append(c0)
        raw_bounds.append(c1)

    raw_bounds.sort()
    bounds: list[float] = [0.0]
    for b in raw_bounds:
        if b - bounds[-1] >= 3.2 and dur - b >= 2.0:
            bounds.append(round(b, 2))
    if dur - bounds[-1] >= 1.5:
        bounds.append(round(dur, 2))
    else:
        bounds[-1] = round(dur, 2)

    # 2. Sub-split long intervals (>26s) using recorded L0 change points (or bone track endings)
    refined = [bounds[0]]
    l0_pts = funscripts.get("L0")
    for i in range(len(bounds) - 1):
        a, b = bounds[i], bounds[i + 1]
        sub_cuts = detect_l0_change_points(l0_pts, a, b) if l0_pts else detect_bone_rhythm_splits(sig["pos_tracks"], a, b)
        for c in sub_cuts:
            if c - refined[-1] >= 5.0 and b - c >= 5.0:
                refined.append(c)
        refined.append(b)

    segments = [
        analyze_segment(i, refined[i], refined[i + 1], dur, sig["pos_tracks"], funscripts, sig["climax_windows"])
        for i in range(len(refined) - 1)
    ]

    return {
        "schema": "koisync.studio.director.v1",
        "sceneName": scene_path.stem,
        "scenePath": str(scene_path.resolve()),
        "duration": round(dur, 3),
        "funscriptUsed": bool(funscripts),
        "funscriptAxes": sorted(funscripts.keys()),
        "lateObjectTrackCount": len(sig["late_object_tracks"]),
        "segments": segments,
    }


def make_contact_sheet(frames: list[tuple[dict[str, Any], Path]], sheet_path: Path, title: str) -> None:
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        print("Pillow not installed; skipping contact sheet generation.", file=sys.stderr)
        return
    if not frames:
        return
    cols = 3 if len(frames) >= 5 else min(2, len(frames))
    rows = math.ceil(len(frames) / cols)
    tw, th, pad, hh, lh = 420, 236, 12, 42, 50
    canvas = Image.new("RGB", (cols * (tw + pad) + pad, hh + rows * (th + lh + pad) + pad), (20, 22, 32))
    draw = ImageDraw.Draw(canvas)
    try:
        ft = ImageFont.truetype("C:/Windows/Fonts/msyhbd.ttc", 20)
        fl = ImageFont.truetype("C:/Windows/Fonts/msyhbd.ttc", 14)
        fs = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 12)
    except Exception:
        ft = fl = fs = ImageFont.load_default()
    draw.text((pad, 10), title, fill=(235, 230, 255), font=ft)
    for idx, (seg, img_path) in enumerate(frames):
        r, c = idx // cols, idx % cols
        x, y = pad + c * (tw + pad), hh + pad + r * (th + lh + pad)
        draw.rectangle([x - 2, y - 2, x + tw + 2, y + th + lh + 2], fill=(32, 35, 48), outline=(90, 82, 130))
        im = Image.open(img_path).convert("RGB")
        im.thumbnail((tw, th), Image.Resampling.LANCZOS)
        canvas.paste(im, (x + (tw - im.width) // 2, y + (th - im.height) // 2))
        l1 = f"{seg['id']} [{seg['role']}] {seg['label'][:24]}"
        l2 = f"{seg['start']:.1f}-{seg['end']:.1f}s | loop {seg['loopStart']:.1f}-{seg['loopEnd']:.1f}s | {seg['frequencyHz']:.1f}Hz | err {seg['seamErrorCm']:.2f}cm"
        draw.text((x + 6, y + th + 4), l1, fill=(255, 224, 160), font=fl)
        draw.text((x + 6, y + th + 25), l2, fill=(175, 208, 255), font=fs)
    sheet_path.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(sheet_path, quality=90)
    print(f"Saved contact sheet: {sheet_path}")


def extract_video_keyframes(video_path: Path, plan: dict[str, Any], frames_dir: Path, sheet_path: Path) -> None:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise RuntimeError("ffmpeg not found on PATH; cannot extract video keyframes")
    frames_dir.mkdir(parents=True, exist_ok=True)
    collected: list[tuple[dict[str, Any], Path]] = []
    for seg in plan["segments"]:
        mid = (seg["loopStart"] + seg["loopEnd"]) * 0.5 if seg["loopable"] else (seg["start"] + seg["end"]) * 0.5
        out_png = frames_dir / f"{seg['id']}_{mid:06.2f}s.png"
        cmd = [ffmpeg, "-y", "-ss", f"{mid:.3f}", "-i", str(video_path), "-frames:v", "1", "-vf", "scale=640:-2", str(out_png)]
        subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if out_png.exists():
            collected.append((seg, out_png))
    make_contact_sheet(collected, sheet_path, f"KoiSync Studio Segments: {plan['sceneName']} (from video)")


def capture_live_keyframes(studio_url: str, plan: dict[str, Any], frames_dir: Path, sheet_path: Path) -> None:
    frames_dir.mkdir(parents=True, exist_ok=True)
    collected: list[tuple[dict[str, Any], Path]] = []
    for seg in plan["segments"]:
        mid = (seg["loopStart"] + seg["loopEnd"]) * 0.5 if seg["loopable"] else (seg["start"] + seg["end"]) * 0.5
        out_png = frames_dir / f"{seg['id']}_{mid:06.2f}s.png"
        payload = json.dumps({
            "time": round(mid, 3),
            "width": 640,
            "height": 360,
            "includeBase64": False,
            "savePath": str(out_png.resolve()),
            "settleFrames": 3,
        }).encode("utf-8")
        req = urllib.request.Request(
            studio_url.rstrip("/") + "/studio/screenshot",
            data=payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=12.0) as resp:
            res = json.loads(resp.read().decode("utf-8"))
            if not res.get("ok"):
                raise RuntimeError(f"Live screenshot failed at {mid}s: {res}")
        if out_png.exists():
            collected.append((seg, out_png))
    make_contact_sheet(collected, sheet_path, f"KoiSync Studio Segments: {plan['sceneName']} (live CharaStudio)")


def apply_labels(plan: dict[str, Any], labels_path: Path) -> None:
    raw = json.loads(labels_path.read_text(encoding="utf-8-sig"))
    items = raw.get("segments", raw) if isinstance(raw, dict) else raw
    by_id: dict[str, dict[str, Any]] = {}
    if isinstance(items, dict):
        by_id = items
    elif isinstance(items, list):
        for row in items:
            if isinstance(row, dict) and "id" in row:
                by_id[str(row["id"])] = row
    for seg in plan["segments"]:
        patch = by_id.get(seg["id"])
        if not isinstance(patch, dict):
            continue
        for k in ("label", "role", "notes", "loopable", "defaultSpeed", "loopStart", "loopEnd"):
            if k in patch:
                seg[k] = patch[k]


def _studio_request(studio_url: str, method: str, path: str, payload: dict[str, Any] | None = None, timeout: float = 12.0) -> dict[str, Any]:
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json; charset=utf-8"
    req = urllib.request.Request(studio_url.rstrip("/") + path, data=data, headers=headers, method=method)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def ensure_studio_running(studio_url: str, exe_path: Path | None, wait_seconds: float = 120.0) -> dict[str, Any]:
    try:
        st = _studio_request(studio_url, "GET", "/state", timeout=2.5)
        if st.get("ok") and (st.get("clock") or {}).get("available"):
            return st
    except Exception:
        pass
    if exe_path is not None and exe_path.exists():
        # Only launch if port isn't responding at all yet
        bridge_up = False
        try:
            _studio_request(studio_url, "GET", "/state", timeout=1.5)
            bridge_up = True
        except Exception:
            pass
        if not bridge_up:
            print(f"Launching CharaStudio: {exe_path}")
            flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0) | getattr(subprocess, "DETACHED_PROCESS", 0)
            subprocess.Popen([str(exe_path)], cwd=str(exe_path.parent), creationflags=flags)
    deadline = time.time() + wait_seconds
    while time.time() < deadline:
        time.sleep(1.5)
        try:
            st = _studio_request(studio_url, "GET", "/state", timeout=3.0)
            if st.get("ok") and (st.get("clock") or {}).get("available"):
                print("CharaStudio bridge & Timeline ready.")
                return st
        except Exception:
            pass
    raise TimeoutError(f"CharaStudio bridge & Timeline did not become ready within {wait_seconds}s")


def load_scene_live(studio_url: str, scene_path: Path, wait_seconds: float = 75.0) -> dict[str, Any]:
    print(f"Loading scene into CharaStudio: {scene_path.name}")
    # Retry briefly if Studio.Instance is still finishing initial scene boot (HTTP 409)
    load_deadline = time.time() + 20.0
    while True:
        try:
            _studio_request(studio_url, "POST", "/studio/load_scene", {"path": str(scene_path.resolve())}, timeout=12.0)
            break
        except urllib.error.HTTPError as exc:
            if exc.code == 409 and time.time() < load_deadline:
                time.sleep(1.5)
                continue
            raise
    deadline = time.time() + wait_seconds
    time.sleep(2.5)
    while time.time() < deadline:
        time.sleep(1.0)
        try:
            st = _studio_request(studio_url, "GET", "/state", timeout=4.0)
            loaded = st.get("scenePath", "")
            actors = st.get("actors") or []
            if loaded and Path(loaded).name.lower() == scene_path.name.lower() and len(actors) >= 2 and st.get("clock", {}).get("available"):
                print(f"Scene ready: {len(actors)} actors, duration={st['clock'].get('duration')}s")
                return st
        except Exception:
            pass
    raise TimeoutError(f"Scene {scene_path.name} did not finish loading within {wait_seconds}s")


def run_step_bake_live(studio_url: str, step_seconds: float = 0.05, bake_scripts: bool = False, script_step: float = 0.02, wait_seconds: float = 360.0) -> dict[str, Any]:
    mode_lbl = "StepBake prescan + 6-axis script bake" if bake_scripts else "StepBake real-bone prescan"
    print(f"Starting {mode_lbl} (prescan dt={step_seconds}s, script dt={script_step}s)...")
    _studio_request(
        studio_url,
        "POST",
        "/studio/step_bake",
        {"action": "start", "stepSeconds": step_seconds, "bakeScripts": bake_scripts, "scriptStepSeconds": script_step},
        timeout=12.0,
    )
    deadline = time.time() + wait_seconds
    last_pct = -1
    last_phase = ""
    while time.time() < deadline:
        time.sleep(0.8)
        st = _studio_request(studio_url, "GET", "/state", timeout=6.0)
        sb = st.get("stepBake") or {}
        pct = int(round(float(sb.get("progress", 0)) * 100))
        phase = str(sb.get("phase", ""))
        if pct != last_pct or phase != last_phase:
            print(f"  [{phase or 'bake'}] {pct:3d}% | {sb.get('status', '')}")
            last_pct = pct
            last_phase = phase
        if not sb.get("running"):
            return st
    raise TimeoutError(f"{mode_lbl} did not finish within {wait_seconds}s")


def merge_stepbake_telemetry(plan: dict[str, Any], stepbake_path: Path) -> None:
    """Enrich XML-segmented plan with real FinalIK/BP StepBake chapter voting, depth calibration, and climax scores."""
    if not stepbake_path.exists():
        return
    sb = json.loads(stepbake_path.read_text(encoding="utf-8-sig"))
    sb_chapters = sb.get("chapters") or []
    if not sb_chapters:
        return
    plan["stepBakeUsed"] = True
    plan["stepBakeReviewCount"] = sb.get("reviewCount", 0)
    for seg in plan["segments"]:
        mid = (seg["start"] + seg["end"]) * 0.5
        matched = None
        for ch in sb_chapters:
            if ch["start"] - 0.1 <= mid <= ch["end"] + 0.1:
                matched = ch
                break
        if matched is None:
            continue
        seg["opening"] = matched.get("openingName", "")
        seg["needsReview"] = bool(matched.get("needsReview", False))
        seg["reviewNote"] = matched.get("reviewNote", "")
        seg["topCandidates"] = matched.get("topCandidates", [])
        if matched.get("climaxDetected") and seg["role"] not in ("climax", "afterglow"):
            seg["role"] = "climax"
            seg["label"] = "高潮爆发"
            seg["loopable"] = False
        if "depthP05" in matched and "depthP95" in matched:
            seg["depthP05"] = matched["depthP05"]
            seg["depthP95"] = matched["depthP95"]
        if matched.get("openingName") == "Oral" and seg["role"] not in ("climax", "afterglow"):
            seg["role"] = "oral"
            seg["label"] = f"口部互动 ({seg['frequencyHz']:.1f}Hz)"
        elif matched.get("openingName") in ("LeftHand", "RightHand", "LeftHandVaginal", "RightHandVaginal") and seg["role"] not in ("climax", "afterglow"):
            seg["role"] = "hand"
            seg["label"] = "手部互动"


def tag_with_local_ollama_vlm(
    plan: dict[str, Any],
    frames: list[tuple[dict[str, Any], Path]],
    model: str,
    ollama_url: str = "http://127.0.0.1:11434",
) -> None:
    """100% offline local open-source VLM segment tagging via Ollama (zero cloud upload, zero moderation risk)."""
    print(f"Tagging {len(frames)} segment frames locally via Ollama VLM ({model})...")
    for seg, img_path in frames:
        b64 = base64.b64encode(img_path.read_bytes()).decode("ascii")
        cand_hint = json.dumps(seg.get("topCandidates", [])[:3], ensure_ascii=False)
        prompt = (
            "You are inspecting a 3D anime animation frame for motion-script chapter labeling. "
            f"Segment {seg['id']} ({seg['start']}s..{seg['end']}s, freq={seg['frequencyHz']}Hz, amp={seg['amplitudePercent']}%, "
            f"currentRole={seg['role']}, boneCandidates={cand_hint}). "
            "Reply ONLY with compact JSON: {\"role\":\"intro|foreplay|oral|hand|slow|medium|fast|climax|afterglow\", "
            "\"label\":\"short Chinese label <=10 chars\", \"notes\":\"short pose/camera note\"}."
        )
        payload = json.dumps({
            "model": model,
            "stream": False,
            "messages": [{"role": "user", "content": prompt, "images": [b64]}],
        }).encode("utf-8")
        req = urllib.request.Request(
            ollama_url.rstrip("/") + "/api/chat",
            data=payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(req, timeout=45.0) as resp:
                data = json.loads(resp.read().decode("utf-8"))
            txt = (data.get("message") or {}).get("content", "")
            m = re.search(r"\{.*\}", txt, re.S)
            if m:
                parsed = json.loads(m.group(0))
                if parsed.get("role"):
                    seg["role"] = str(parsed["role"]).strip().lower()
                if parsed.get("label"):
                    seg["label"] = str(parsed["label"]).strip()
                if parsed.get("notes"):
                    seg["notes"] = str(parsed["notes"]).strip()
                seg["needsReview"] = False
                print(f"  [Ollama] {seg['id']} -> [{seg['role']}] {seg['label']}")
        except Exception as exc:
            print(f"  [Ollama] {seg['id']} skipped: {exc}", file=sys.stderr)


def push_plan_live(studio_url: str, plan: dict[str, Any], save: bool = True) -> dict[str, Any]:
    body = dict(plan)
    body["save"] = save
    return _studio_request(studio_url, "POST", "/studio/segments", body, timeout=8.0)


def main() -> int:
    ap = argparse.ArgumentParser(description="Analyze a Koikatsu Studio scene card (+ optional StepBake/funscript/video/local Ollama VLM) into a .director.json plan.")
    ap.add_argument("--scene", required=True, help="Path to Studio scene .png card")
    ap.add_argument("--game-root", default=r"E:\Galgames\kk", help="Koikatsu game root (used for default UserData/KoiSyncStudio paths)")
    ap.add_argument("--funscript", default="", help="Optional recorded L0 .funscript path (auto-discovered if omitted)")
    ap.add_argument("--video", default="", help="Optional recorded preview .mp4 path to extract per-segment keyframes")
    ap.add_argument("--launch-studio", default="", help="Optional path to CharaStudio.exe to auto-launch if port 41290 is offline")
    ap.add_argument("--load-live", action="store_true", help="Load --scene into running CharaStudio via /studio/load_scene")
    ap.add_argument("--step-bake", action="store_true", help="Run live StepBake real-bone prescan in CharaStudio and fuse .stepbake.json")
    ap.add_argument("--bake-scripts", action="store_true", help="Also deterministically bake 50Hz 6-axis .funscript files after StepBake prescan")
    ap.add_argument("--feel", default="", help="Optional Studio feel preset before baking scripts (raw|balanced|intense)")
    ap.add_argument("--step-seconds", type=float, default=0.05, help="StepBake interval in seconds (default 0.05)")
    ap.add_argument("--script-step-seconds", type=float, default=0.02, help="6-axis script bake interval in seconds (default 0.02 = 50Hz)")
    ap.add_argument("--capture-live", action="store_true", help="Ask live CharaStudio bridge (127.0.0.1:41290) to capture 1 keyframe per segment")
    ap.add_argument("--ollama-vlm", default="", help="Optional 100%% offline local Ollama VLM model (e.g. qwen2.5vl:7b) for zero-cloud visual labeling")
    ap.add_argument("--ollama-url", default="http://127.0.0.1:11434", help="Local Ollama endpoint")
    ap.add_argument("--apply-labels", default="", help="Optional JSON file mapping segment id -> {label, role, notes, loopable}")
    ap.add_argument("--push-live", action="store_true", help="Push generated director plan live to running CharaStudio bridge")
    ap.add_argument("--studio-url", default=os.environ.get("KOISYNC_STUDIO_URL", "http://127.0.0.1:41290"), help="Studio Director bridge URL")
    ap.add_argument("--out", default="", help="Output .director.json path (defaults to <GameRoot>/UserData/KoiSyncStudio/<rel>.director.json)")
    args = ap.parse_args()

    scene_path = Path(args.scene).resolve()
    if not scene_path.exists():
        print(f"Scene card not found: {scene_path}", file=sys.stderr)
        return 2

    if args.launch_studio:
        ensure_studio_running(args.studio_url, Path(args.launch_studio).resolve())
    if args.load_live:
        load_scene_live(args.studio_url, scene_path)
    if args.feel:
        _studio_request(args.studio_url, "POST", "/studio/feel", {"preset": args.feel}, timeout=6.0)
    if args.step_bake or args.bake_scripts:
        run_step_bake_live(
            args.studio_url,
            step_seconds=args.step_seconds,
            bake_scripts=args.bake_scripts,
            script_step=args.script_step_seconds,
        )

    stem = default_output_stem(scene_path, Path(args.game_root))
    out_path = Path(args.out).resolve() if args.out else Path(str(stem) + ".director.json")

    fs_path: Path | None = None
    if args.funscript:
        fs_path = Path(args.funscript).resolve()
    else:
        for cand in (Path(str(stem) + ".funscript"), Path(str(stem) + ".L0.funscript")):
            if cand.exists():
                fs_path = cand
                break

    plan = build_director_plan(scene_path, fs_path)
    stepbake_path = Path(str(stem) + ".stepbake.json")
    if stepbake_path.exists():
        merge_stepbake_telemetry(plan, stepbake_path)

    if args.apply_labels:
        apply_labels(plan, Path(args.apply_labels).resolve())

    frames_dir = out_path.with_name(out_path.stem.replace(".director", "") + "_frames")
    sheet_path = out_path.with_name(out_path.stem.replace(".director", "") + "_segments_sheet.jpg")
    collected_frames: list[tuple[dict[str, Any], Path]] = []
    if args.video:
        extract_video_keyframes(Path(args.video).resolve(), plan, frames_dir, sheet_path)
        collected_frames = [(s, p) for s in plan["segments"] for p in sorted(frames_dir.glob(f"{s['id']}_*.png"))[:1]]
    elif args.capture_live:
        capture_live_keyframes(args.studio_url, plan, frames_dir, sheet_path)
        collected_frames = [(s, p) for s in plan["segments"] for p in sorted(frames_dir.glob(f"{s['id']}_*.png"))[:1]]

    if args.ollama_vlm and collected_frames:
        tag_with_local_ollama_vlm(plan, collected_frames, args.ollama_vlm, args.ollama_url)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(plan, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Scene: {plan['sceneName']} ({plan['duration']:.2f}s) | funscript={plan['funscriptUsed']} | stepBake={plan.get('stepBakeUsed', False)}")
    print(f"Segments ({len(plan['segments'])}):")
    for s in plan["segments"]:
        loop_flag = "LOOP" if s["loopable"] else "ONCE"
        rev_flag = " [?]" if s.get("needsReview") else ""
        print(
            f"  {s['id']} [{s['role']:10s}] {s['start']:6.2f}..{s['end']:6.2f}s "
            f"| seam={s['loopStart']:6.2f}..{s['loopEnd']:6.2f}s ({loop_flag}) "
            f"| {s['frequencyHz']:4.2f}Hz | amp={s['amplitudePercent']:4.1f}% | err={s['seamErrorCm']:4.2f}cm | {s['label']}{rev_flag}"
        )
    print(f"Wrote: {out_path}")

    if args.push_live:
        res = push_plan_live(args.studio_url, plan, save=True)
        print(f"Pushed live to CharaStudio: ok={res.get('ok')} segments={len(res.get('segments', []))}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
