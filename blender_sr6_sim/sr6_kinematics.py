#!/usr/bin/env python3
"""
Exact 1:1 SR6 6-DOF Parallel-Linkage Forward Kinematics, Multi-Axis Funscript Loader,
and SR6 6-DOF Mathematical Limit Trajectory Engine (ported 1:1 from KoiSync SR6DeviceRotation.cs
& SR6MathManifoldEngine.cs) for Blender 5.2 robotic arm simulation & video production.

Coordinate convention:
  - Internal SR6 reference frame (nb-3d-simulator / SR6DeviceRotation.cs) uses millimeters, Y-up:
      X: +Left / -Right  (servo towers at X = +/-58.5 mm, horns at X = +/-73.0 mm)
      Y: +Up / -Down     (upper servo at Y=0, lower servo at Y=30? Wait: verify vertical sign below!)
      Z: +Forward / -Back (servo shafts at Z = 49.92 mm, carrier swings forward along +Z)
  - Converted cleanly to Blender Z-up meters (SCALE = 0.01 -> 100 mm = 1.0 Blender unit):
      Blender X =  SR6_X * SCALE
      Blender Y = -SR6_Z * SCALE  (so +Y in Blender is back/chassis, -Y faces camera front!)
      Blender Z =  SR6_Y * SCALE  (shifted upward so chassis base rests cleanly on Z = 0!)
"""
from __future__ import annotations

import json
import math
import pathlib
from typing import Dict, List, Optional, Tuple

SCALE = 0.01  # 100 mm = 1.0 Blender unit (ideal for EEVEE shadows, bevels, and DOF)

Vec3 = Tuple[float, float, float]


def clamp(v: float, lo: float, hi: float) -> float:
    return lo if v < lo else (hi if v > hi else v)


def v_add(a: Vec3, b: Vec3) -> Vec3:
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def v_sub(a: Vec3, b: Vec3) -> Vec3:
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def v_mul(a: Vec3, s: float) -> Vec3:
    return (a[0] * s, a[1] * s, a[2] * s)


def v_dot(a: Vec3, b: Vec3) -> float:
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def v_cross(a: Vec3, b: Vec3) -> Vec3:
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def v_len(a: Vec3) -> float:
    return math.sqrt(v_dot(a, a))


def v_unit(a: Vec3) -> Vec3:
    l = v_len(a)
    return (a[0] / l, a[1] / l, a[2] / l) if l > 1e-9 else (0.0, 0.0, 0.0)


def _travel(v: float, span: float) -> float:
    return (clamp(v, 0.0, 0.9999) / 0.9999 * 2.0 - 1.0) * span


def _servo_val(h: float, v: float) -> float:
    h /= 100.0
    v /= 100.0
    sq = h * h + v * v
    cos_arg = clamp((sq - 28125.0) / (100.0 * math.sqrt(max(1e-9, sq))), -1.0, 1.0)
    return 637.0 * (math.atan2(h, v) + math.acos(cos_arg) - 3.14159)


def _servo_angle(pulse: float, sign: float) -> float:
    return (pulse / (1000000.0 / 330.0) * 2.0 - 1.0) * math.pi * sign


def _arm_tip(x: float, y: float, angle: float, side: float) -> Vec3:
    # Exact 50 mm servo horn around shaft at (x + 14.5*side, y, 49.92)
    return (x + 14.5 * side, y - 50.0 * math.cos(angle), 49.92 - 50.0 * math.sin(angle))


def _intersect_arms(upper: Vec3, lower: Vec3) -> Tuple[Vec3, Vec3, float]:
    center = v_mul(v_add(upper, lower), 0.5)
    delta = v_sub(upper, lower)
    r = math.sqrt(max(0.0, 175.0 * 175.0 - v_dot(delta, delta) * 0.25))
    # Cross(delta, +X) points into +Z (forward carrier side)
    normal = v_unit(v_cross(delta, (1.0, 0.0, 0.0)))
    return v_add(center, v_mul(normal, r)), center, r


def _turn_y(p: Vec3, ang: float) -> Vec3:
    ca, sa = math.cos(ang), math.sin(ang)
    return (p[0] * ca + p[2] * sa, p[1], -p[0] * sa + p[2] * ca)


def _from_to_vec(p: Vec3, cross_v: Vec3, dot_v: float) -> Vec3:
    denom = max(1e-6, 1.0 + dot_v)
    c1 = v_cross(cross_v, p)
    c2 = v_cross(cross_v, c1)
    return v_add(v_add(p, c1), v_mul(c2, 1.0 / denom))


def _rot_axis(v: Vec3, axis: Vec3, rad: float) -> Vec3:
    k = v_unit(axis)
    c, s = math.cos(rad), math.sin(rad)
    return v_add(v_add(v_mul(v, c), v_mul(v_cross(k, v), s)), v_mul(k, v_dot(k, v) * (1.0 - c)))


# Vertical offset in mm so the SR6 chassis base sits cleanly on Blender Z = 0.0 with generous sleeve clearance
BASE_Z_OFFSET_MM = 175.0
# Forward center offset in mm so chassis towers (+Y) and carrier (-Y) balance symmetrically around world origin
CENTER_Y_OFFSET_MM = -140.0


def sr6_mm_to_blender(p: Vec3) -> Vec3:
    """Maps SR6 kinematics (X=left, Y=down-ish/vertical, Z=forward) into Blender Z-up meters (SCALE=0.01).
    Verified empirically: in SR6DeviceRotation, increasing L0 moves carrier along -Y (so Blender Z = -Y + BASE_Z_OFFSET_MM).
    """
    return (
        p[0] * SCALE,
        -(p[2] + CENTER_Y_OFFSET_MM) * SCALE,
        (-p[1] + BASE_Z_OFFSET_MM) * SCALE,
    )


def sr6_dir_to_blender(d: Vec3) -> Vec3:
    return v_unit((d[0], -d[2], -d[1]))


def solve_sr6_pose(l0: float, l1: float, l2: float, r0: float, r1: float, r2: float) -> dict:
    """
    Solves exact 6-DOF SR6 parallel-linkage geometry in Blender Z-up coordinates!
    Inputs: normalized T-Code [0..1] for L0 (stroke), L1 (surge), L2 (sway), R0 (twist), R1 (roll), R2 (pitch).
    Returns world positions of every servo shaft, horn tip, pushrod endpoint, gimbal cradle, rear pitch arm,
    and rotated cylindrical actuator basis (X_right, Y_fwd, Z_up) + twist angle!
    """
    cl0 = clamp(l0, 0.02, 0.98)
    cl1 = clamp(l1, 0.04, 0.96)
    cl2 = clamp(l2, 0.04, 0.96)
    cr0 = clamp(r0, 0.01, 0.99)
    cr1 = clamp(r1, 0.05, 0.95)
    cr2 = clamp(r2, 0.04, 0.96)

    roll = _travel(cr1, 3000.0)
    surge = _travel(cl1, 3000.0)
    stroke = _travel(cl0, 6000.0)

    a = _servo_val(16248.0 - surge, 1500.0 + stroke + roll)
    b = _servo_val(16248.0 - surge, 1500.0 - stroke - roll)
    c = _servo_val(16248.0 - surge, 1500.0 - stroke + roll)
    d = _servo_val(16248.0 - surge, 1500.0 + stroke - roll)

    ll = _servo_angle(1515.105 - a, 0.5) - math.pi
    ul = _servo_angle(1515.105 + b, 0.5)
    lr = _servo_angle(1515.105 + d, -0.5) - math.pi
    ur = _servo_angle(1515.105 - c, -0.5)

    # Fixed servo shaft pivots (at horn plane X = +/-73.0 mm)
    sUL = (58.5 + 14.5, 0.0, 49.92)
    sLL = (58.5 + 14.5, 30.0, 49.92)
    sUR = (-58.5 - 14.5, 0.0, 49.92)
    sLR = (-58.5 - 14.5, 30.0, 49.92)

    # 50 mm servo horn tips
    hUL = _arm_tip(58.5, 0.0, ul, 1.0)
    hLL = _arm_tip(58.5, 30.0, ll, 1.0)
    hUR = _arm_tip(-58.5, 0.0, ur, -1.0)
    hLR = _arm_tip(-58.5, 30.0, lr, -1.0)

    left, lC, lRad = _intersect_arms(hUL, hLL)
    right, rC, rRad = _intersect_arms(hUR, hLR)

    dist = v_len(v_sub(right, left))
    if dist > 145.5:
        adj = v_mul(v_unit(v_sub(right, left)), (dist - 145.5) * 0.5)
        left = v_add(left, adj)
        right = v_sub(right, adj)

    sway = 60.0 * (cl2 - 0.5)
    left = v_add(lC, _turn_y(v_sub(left, lC), sway / max(1.0, lRad)))
    right = v_add(rC, _turn_y(v_sub(right, rC), sway / max(1.0, rRad)))

    # Carrier orientation basis from left->right gimbal vector
    from_v = (-1.0, 0.0, 0.0)
    to_v = v_unit(v_sub(right, left))
    cross_v = v_cross(from_v, to_v)
    dot_v = v_dot(from_v, to_v)
    cX = v_unit(_from_to_vec((1.0, 0.0, 0.0), cross_v, dot_v))
    cY = v_unit(_from_to_vec((0.0, 1.0, 0.0), cross_v, dot_v))
    cZ = v_unit(_from_to_vec((0.0, 0.0, 1.0), cross_v, dot_v))

    # Apply R2 pitch (+/- 28 deg around carrier X axis!)
    pitch_rad = (cr2 - 0.5) * math.radians(56.0)
    pY = v_unit(_rot_axis(cY, cX, pitch_rad))
    pZ = v_unit(_rot_axis(cZ, cX, pitch_rad))

    # Apply R0 twist (+/- 120 deg around sleeve axis pY!)
    twist_rad = (0.5 - cr0) * math.radians(240.0)
    tX = v_unit(_rot_axis(cX, pY, twist_rad))
    tZ = v_unit(_rot_axis(pZ, pY, twist_rad))

    center_mm = v_mul(v_add(left, right), 0.5)

    # 5th Servo (Rear Pitch Drive): mounted at center-rear tower (0, -28, 52), driving a rear pitch lug on the carrier!
    sPitch_mm = (0.0, -32.0, 48.0)
    # Rear pitch lug sits behind carrier center along -pZ (56 mm) and slightly above (-pY * 32 mm)
    pitch_lug_mm = v_add(center_mm, v_add(v_mul(pZ, -58.0), v_mul(pY, -36.0)))
    # Solve rear servo horn angle (50 mm horn in Y-Z plane) aimed cleanly toward pitch_lug
    dy_p = pitch_lug_mm[1] - sPitch_mm[1]
    dz_p = pitch_lug_mm[2] - sPitch_mm[2]
    ang_p = math.atan2(dy_p, max(20.0, dz_p)) - 0.55
    hPitch_mm = (0.0, sPitch_mm[1] + 50.0 * math.sin(ang_p), sPitch_mm[2] + 50.0 * math.cos(ang_p))

    # Sleeve tip (+up in Blender = -pY in SR6) and base (+pY in SR6) for glowing 3D trajectory trails
    sleeve_half_len_mm = 78.0
    tip_mm = v_add(center_mm, v_mul(pY, -sleeve_half_len_mm))
    base_mm = v_add(center_mm, v_mul(pY, +sleeve_half_len_mm))

    # Convert orthonormal bases into Blender right-handed Z-up columns (X=side, Y=fwd, Z=up)
    # Note: SR6 +X -> B +X; SR6 -Z -> B +Y; SR6 -Y -> B +Z (det = (+1)*(-1)*(-1) = +1 right-handed!)
    bx_gimbal = sr6_dir_to_blender(cX)
    bz_gimbal = sr6_dir_to_blender(v_mul(pY, -1.0))
    by_gimbal = v_unit(v_cross(bz_gimbal, bx_gimbal))
    bx_gimbal = v_unit(v_cross(by_gimbal, bz_gimbal))

    bx_sleeve = sr6_dir_to_blender(tX)
    bz_sleeve = bz_gimbal
    by_sleeve = v_unit(v_cross(bz_sleeve, bx_sleeve))
    bx_sleeve = v_unit(v_cross(by_sleeve, bz_sleeve))

    return {
        "servo_UL": sr6_mm_to_blender(sUL),
        "servo_LL": sr6_mm_to_blender(sLL),
        "servo_UR": sr6_mm_to_blender(sUR),
        "servo_LR": sr6_mm_to_blender(sLR),
        "servo_Pitch": sr6_mm_to_blender(sPitch_mm),
        "horn_UL": sr6_mm_to_blender(hUL),
        "horn_LL": sr6_mm_to_blender(hLL),
        "horn_UR": sr6_mm_to_blender(hUR),
        "horn_LR": sr6_mm_to_blender(hLR),
        "horn_Pitch": sr6_mm_to_blender(hPitch_mm),
        "joint_L": sr6_mm_to_blender(left),
        "joint_R": sr6_mm_to_blender(right),
        "pitch_lug": sr6_mm_to_blender(pitch_lug_mm),
        "carrier_pos": sr6_mm_to_blender(center_mm),
        "tip_pos": sr6_mm_to_blender(tip_mm),
        "base_pos": sr6_mm_to_blender(base_mm),
        # 3x3 column vectors (X, Y, Z) in Blender world space
        "gimbal_axes": (bx_gimbal, by_gimbal, bz_gimbal),
        "sleeve_axes": (bx_sleeve, by_sleeve, bz_sleeve),
        "twist_rad": twist_rad,
        "pitch_rad": pitch_rad,
        "raw_rod_mm": (v_len(v_sub(left, hUL)), v_len(v_sub(left, hLL)), v_len(v_sub(right, hUR)), v_len(v_sub(right, hLR))),
    }


# ---------------------------------------------------------------------------
# Multi-Axis Funscript Importer (reads KoiSync's 6-file export directly!)
# ---------------------------------------------------------------------------
AXIS_SUFFIXES = (
    ("L0", ""),
    ("L1", ".surge"),
    ("L2", ".sway"),
    ("R0", ".twist"),
    ("R1", ".roll"),
    ("R2", ".pitch"),
)


def _sample_funscript_actions(actions: List[dict], t_ms: float) -> float:
    if not actions:
        return 0.5
    if t_ms <= actions[0]["at"]:
        return clamp(float(actions[0]["pos"]) / 100.0, 0.0, 1.0)
    if t_ms >= actions[-1]["at"]:
        return clamp(float(actions[-1]["pos"]) / 100.0, 0.0, 1.0)
    lo, hi = 0, len(actions) - 1
    while lo + 1 < hi:
        mid = (lo + hi) // 2
        if actions[mid]["at"] <= t_ms:
            lo = mid
        else:
            hi = mid
    a0, a1 = actions[lo], actions[hi]
    span = max(1e-6, float(a1["at"] - a0["at"]))
    u = clamp((t_ms - a0["at"]) / span, 0.0, 1.0)
    return clamp((a0["pos"] + (a1["pos"] - a0["pos"]) * u) / 100.0, 0.0, 1.0)


def load_funscript_set(main_funscript_path: str, fps: int = 60, max_seconds: float = 30.0) -> List[Tuple[float, float, float, float, float, float]]:
    """Loads a KoiSync 6-axis funscript set (stem.funscript + .surge/.sway/.twist/.roll/.pitch.funscript)."""
    p = pathlib.Path(main_funscript_path)
    stem = str(p)
    if stem.lower().endswith(".funscript"):
        stem = stem[:-len(".funscript")]
    tracks: Dict[str, List[dict]] = {}
    max_ms = 0.0
    for key, suf in AXIS_SUFFIXES:
        fp = pathlib.Path(stem + suf + ".funscript")
        if fp.is_file():
            data = json.loads(fp.read_text(encoding="utf-8", errors="replace"))
            acts = sorted(data.get("actions") or [], key=lambda x: x["at"])
            tracks[key] = acts
            if acts:
                max_ms = max(max_ms, float(acts[-1]["at"]))
        else:
            tracks[key] = []
    dur_s = min(max_seconds, max(1.0, max_ms / 1000.0))
    n_frames = int(round(dur_s * fps))
    out = []
    for f in range(n_frames):
        t_ms = (f / float(fps)) * 1000.0
        out.append(tuple(_sample_funscript_actions(tracks[k], t_ms) for k, _ in AXIS_SUFFIXES))
    return out


# ---------------------------------------------------------------------------
# 1:1 Port of KoiSync SR6 6-DOF Math Manifolds + Act 1 6-Axis Explainer
# ---------------------------------------------------------------------------
PHI = 0.6180339887498949
SQRT2 = 1.4142135623730951
INV_SQRT3 = 0.5773502691896257
SQRT_PHI = 0.7861513777574233
INV_SQRT_PHI = 1.272019649514069


def generate_showcase_sequence(mode: str, duration_s: float = 8.0, fps: int = 60) -> List[Tuple[float, float, float, float, float, float]]:
    """
    Generates mathematically exact, C2-smooth 6-DOF SR6 animation frames [(L0, L1, L2, R0, R1, R2), ...]:
      - "act1_6dof_intro"  : Clean sequential explainer of all 6 axes (L0->L1->L2->R0->R1->R2->All 6 combined)
      - "virtual_pivot"    : Virtual Pivot Traveling Cone Wave (base-pin -> tip-pin -> traveling wave)
      - "variable_helix"   : Variable-Pitch Hourglass Double Helix (dramatic L0+R0 shear + radial bloom)
      - "lissajous_6d"     : 6D Golden-Ratio Non-Repeating Lissajous Torus Knot
      - "beat_flutter"     : Dual-Scale Macro Beat Tide + Micro-Flutter Resonance
      - "smooth_chaos"     : Zero-Mean Rossler + Forced Duffing Bounded Smooth Chaos
      - "reel_all"         : 16s Director's Showcase Reel seamlessly morphing across all 5 math families!
    """
    n = int(round(duration_s * fps))
    dt = 1.0 / float(fps)

    # C2 critically-damped tracking filter matching SR6MathManifoldEngine.cs
    filt_pos = [0.5, 0.5, 0.5, 0.5, 0.5, 0.5]
    filt_vel = [0.0] * 6
    max_vel = [4.2, 3.6, 3.6, 3.8, 3.4, 3.6]
    max_acc = [42.0, 34.0, 34.0, 36.0, 32.0, 34.0]
    wn, zeta = 32.0, 0.95

    # Chaos integrator states + zero-mean DC blocker
    rx, ry, rz = 3.2, -4.1, 0.85
    duff_x, duff_v = 0.75, 0.35
    chaos_dc = [0.0] * 5

    frames = []
    for i in range(n):
        t = i * dt
        active_mode = mode
        local_t = t
        if mode == "reel_all":
            seg = int(t // 3.2) % 5
            active_mode = ("virtual_pivot", "variable_helix", "lissajous_6d", "beat_flutter", "smooth_chaos")[seg]
            local_t = t

        if active_mode == "act1_6dof_intro":
            # 12s explainer: 1.5s each axis (L0, L1, L2, R0, R1, R2) + 3s full 6-DOF combo!
            slot = t / 1.5
            raw = [0.5, 0.5, 0.5, 0.5, 0.5, 0.5]
            if slot < 6.0:
                ax = int(slot)
                u = slot - ax
                env = math.sin(math.pi * u) ** 0.55
                amp = 0.42 if ax in (0, 3) else 0.38
                raw[ax] = 0.5 + amp * env * math.sin(2.0 * math.pi * 1.5 * u)
            else:
                u = t - 9.0
                ph = 2.0 * math.pi * 1.2 * u
                raw = [
                    0.5 + 0.38 * math.sin(ph),
                    0.5 + 0.32 * math.cos(ph),
                    0.5 + 0.32 * math.sin(ph * 1.5),
                    0.5 + 0.40 * math.sin(ph * 0.8),
                    0.5 + 0.32 * math.sin(ph),
                    0.5 + 0.35 * math.cos(ph),
                ]
        elif active_mode == "virtual_pivot":
            ph = 2.0 * math.pi * 1.35 * local_t
            zp = clamp(0.5 + 0.48 * math.sin(2.0 * math.pi * 0.22 * local_t), 0.02, 0.98)
            d_wave = 0.5 - 0.5 * math.cos(ph * 0.5)
            l0 = clamp(0.10 + 0.72 * (0.30 + 0.70 * d_wave), 0.06, 0.92)
            pitch = 0.38 * math.cos(ph)
            roll = 0.34 * math.sin(ph)
            lever = 2.0 * zp - 1.0
            l1 = -0.34 * lever * math.cos(ph)
            l2 = +0.34 * lever * math.sin(ph)
            r0 = 0.34 * math.sin(ph + zp * math.pi)
            raw = [l0, 0.5 + l1, 0.5 + l2, 0.5 + r0, 0.5 + roll, 0.5 + pitch]
        elif active_mode == "variable_helix":
            ph = 2.0 * math.pi * 1.35 * local_t
            u = (ph / (2.0 * math.pi)) % 1.0
            d_wave = (0.5 - 0.5 * math.cos((u / 0.38) * math.pi)) if u < 0.38 else (0.5 + 0.5 * math.cos(((u - 0.38) / 0.62) * math.pi))
            l0 = clamp(0.08 + 0.84 * d_wave, 0.06, 0.94)
            helix_ph = ph + 0.85 * math.sin(ph) * (1.0 - 0.5 * d_wave)
            r0 = 0.44 * math.sin(helix_ph)
            rad = 0.32 + 0.68 * math.sin(d_wave * math.pi)
            phi = ph * 1.5
            raw = [
                l0,
                0.5 + 0.34 * rad * math.cos(phi),
                0.5 + 0.34 * rad * math.sin(phi),
                0.5 + r0,
                0.5 + 0.32 * rad * math.sin(phi + 0.45),
                0.5 + 0.36 * rad * math.cos(phi + 0.45),
            ]
        elif active_mode == "lissajous_6d":
            ph = 2.0 * math.pi * 1.30 * local_t
            raw = [
                0.5 - 0.40 * math.cos(ph),
                0.5 + 0.36 * math.sin(ph * PHI + 0.3),
                0.5 + 0.36 * math.sin(ph * SQRT2 + 1.1),
                0.5 + 0.42 * math.sin(ph * INV_SQRT3),
                0.5 + 0.34 * math.sin(ph * SQRT_PHI + 0.7),
                0.5 + 0.38 * math.cos(ph * INV_SQRT_PHI + 0.2),
            ]
        elif active_mode == "beat_flutter":
            ph = 2.0 * math.pi * 1.25 * local_t
            beat_ang = local_t * 0.28 * 2.0 * math.pi
            tide = 0.5 - 0.5 * math.cos(beat_ang)
            flutter = math.sin(local_t * 10.5 * 2.0 * math.pi) * 0.14 * (0.25 + 0.75 * tide)
            base_w = 0.5 - 0.5 * math.cos(ph)
            l0 = clamp(0.12 + 0.76 * (0.25 + 0.75 * base_w * (0.45 + 0.55 * tide)) + flutter * 0.25, 0.06, 0.94)
            env = 0.30 + 0.70 * tide
            raw = [
                l0,
                0.5 + 0.34 * env * math.cos(ph * 0.75),
                0.5 + 0.34 * env * math.sin(ph * 0.75 + beat_ang * 0.5),
                0.5 + 0.28 * math.sin(ph * 0.5) + flutter * 0.9,
                0.5 + 0.32 * env * math.sin(ph + 0.4),
                0.5 + 0.36 * env * math.cos(ph + 0.4),
            ]
        else:
            # smooth_chaos (RK4 Rossler + Forced Duffing + smooth zero-mean DC centering)
            h = dt * 5.8
            a, b, c = 0.2, 0.2, 5.7
            k1x, k1y, k1z = -ry - rz, rx + a * ry, b + rz * (rx - c)
            x2, y2, z2 = rx + 0.5 * h * k1x, ry + 0.5 * h * k1y, rz + 0.5 * h * k1z
            k2x, k2y, k2z = -y2 - z2, x2 + a * y2, b + z2 * (x2 - c)
            x3, y3, z3 = rx + 0.5 * h * k2x, ry + 0.5 * h * k2y, rz + 0.5 * h * k2z
            k3x, k3y, k3z = -y3 - z3, x3 + a * y3, b + z3 * (x3 - c)
            x4, y4, z4 = rx + h * k3x, ry + h * k3y, rz + h * k3z
            k4x, k4y, k4z = -y4 - z4, x4 + a * y4, b + z4 * (x4 - c)
            rx += (h / 6.0) * (k1x + 2 * k2x + 2 * k3x + k4x)
            ry += (h / 6.0) * (k1y + 2 * k2y + 2 * k3y + k4y)
            rz += (h / 6.0) * (k1z + 2 * k2z + 2 * k3z + k4z)

            hd = dt * 4.8
            f_d = 0.50 * math.cos(1.2 * local_t)
            a1 = f_d - 0.25 * duff_v + duff_x - duff_x ** 3
            dx2, dv2 = duff_x + 0.5 * hd * duff_v, duff_v + 0.5 * hd * a1
            a2 = f_d - 0.25 * dv2 + dx2 - dx2 ** 3
            dx3, dv3 = duff_x + 0.5 * hd * dv2, duff_v + 0.5 * hd * a2
            a3 = f_d - 0.25 * dv3 + dx3 - dx3 ** 3
            dx4, dv4 = duff_x + hd * dv3, duff_v + hd * a3
            a4 = f_d - 0.25 * dv4 + dx4 - dx4 ** 3
            duff_x += (hd / 6.0) * (duff_v + 2 * dv2 + 2 * dv3 + dv4)
            duff_v += (hd / 6.0) * (a1 + 2 * a2 + 2 * a3 + a4)

            nx, ny, nz = math.tanh(rx / 6.2), math.tanh(ry / 6.2), math.tanh((rz - 2.0) / 4.8)
            nd, nv = math.tanh(duff_x / 1.10), math.tanh(duff_v / 1.30)
            l0 = clamp(0.10 + 0.80 * (0.5 + 0.5 * math.tanh((0.75 * nx + 0.45 * nd) * 1.25)), 0.06, 0.94)
            raw5 = [
                math.tanh((0.75 * ny + 0.45 * nz) * 1.2),
                math.tanh((0.70 * nd - 0.55 * nx) * 1.2),
                math.tanh((0.65 * nx + 0.65 * ny) * 1.2),
                math.tanh((0.72 * ny - 0.55 * nv) * 1.2),
                math.tanh((0.65 * nv + 0.55 * nz - 0.35 * nx) * 1.2),
            ]
            dc_a = 1.0 - math.exp(-dt / 1.8)
            for k in range(5):
                chaos_dc[k] += (raw5[k] - chaos_dc[k]) * dc_a
            raw = [
                l0,
                0.5 + 0.36 * clamp(raw5[0] - 0.85 * chaos_dc[0], -1.0, 1.0),
                0.5 + 0.36 * clamp(raw5[1] - 0.85 * chaos_dc[1], -1.0, 1.0),
                0.5 + 0.42 * clamp(raw5[2] - 0.85 * chaos_dc[2], -1.0, 1.0),
                0.5 + 0.34 * clamp(raw5[3] - 0.85 * chaos_dc[3], -1.0, 1.0),
                0.5 + 0.36 * clamp(raw5[4] - 0.85 * chaos_dc[4], -1.0, 1.0),
            ]

        # C2 smooth tracking filter
        for k in range(6):
            err = clamp(raw[k], 0.04, 0.96) - filt_pos[k]
            acc = clamp(wn * wn * err - 2.0 * zeta * wn * filt_vel[k], -max_acc[k], max_acc[k])
            vel = clamp(filt_vel[k] + acc * dt, -max_vel[k], max_vel[k])
            filt_pos[k] = clamp(filt_pos[k] + vel * dt, 0.04, 0.96)
            filt_vel[k] = vel
        frames.append(tuple(filt_pos))
    return frames


if __name__ == "__main__":
    print("=== SR6 6-DOF Kinematics Self-Test ===")
    for lbl, cmd in [
        ("neutral", (0.5, 0.5, 0.5, 0.5, 0.5, 0.5)),
        ("L0=0.90 (UP)", (0.9, 0.5, 0.5, 0.5, 0.5, 0.5)),
        ("L0=0.10 (DOWN)", (0.1, 0.5, 0.5, 0.5, 0.5, 0.5)),
        ("L1=0.88 (SURGE)", (0.5, 0.88, 0.5, 0.5, 0.5, 0.5)),
        ("L2=0.88 (SWAY)", (0.5, 0.5, 0.88, 0.5, 0.5, 0.5)),
        ("R1=0.88 (ROLL)", (0.5, 0.5, 0.5, 0.5, 0.88, 0.5)),
    ]:
        p = solve_sr6_pose(*cmd)
        c = p["carrier_pos"]
        rods = p["raw_rod_mm"]
        print(f"  {lbl:16s} carrier_B=({c[0]:+.3f},{c[1]:+.3f},{c[2]:+.3f}) rods_mm={[round(r,1) for r in rods]} tipZ={p['tip_pos'][2]:.3f}")
    seq = generate_showcase_sequence("lissajous_6d", 4.0, 60)
    print(f"  Generated lissajous_6d frames={len(seq)} sample0={[round(x,2) for x in seq[0]]} sample120={[round(x,2) for x in seq[120]]}")
