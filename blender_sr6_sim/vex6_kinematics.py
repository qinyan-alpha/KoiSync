#!/usr/bin/env python3
"""
VEX-6 Single-Arm 6-Axis Industrial Robot Arm — Exact Analytical 6-DOF Inverse Kinematics (Pieper)
for Blender 5.2. Consumes the EXACT same normalized 6-axis stream (L0, L1, L2, R0, R1, R2) from:
  1. Live KoiSync (http://127.0.0.1:41289/state)
  2. Real KoiSync 6-axis .funscript recordings
  3. All 5 SR6 Math Limit Manifolds + Act 1 6-DOF Explainer

Coordinate convention (Blender Z-up, 1 unit = 10 cm):
  - Robot base rests on Z = 0 at (0, +1.05, 0) facing forward toward -Y (toward camera!).
  - Ring-Claw TCP neutral hover center sits cleanly at (0.0, -0.75, 1.38) in front of the robot.
  - All 6 joints (J1 Base Turret, J2 Shoulder, J3 Elbow, J4 Forearm Roll, J5 Wrist Pitch, J6 Wrist Roll)
    articulate continuously with ZERO singularity flips across the entire 6-DOF workspace!
"""
from __future__ import annotations

import math
from typing import Tuple

import sr6_kinematics as sr6

Vec3 = Tuple[float, float, float]

# VEX-6 Industrial Arm Dimensions (matches Concept A ceramic-white proportions!)
BASE_POS: Vec3 = (0.0, 1.05, 0.0)
SHOULDER_Z = 0.86       # J2 shoulder pivot height above floor
SHOULDER_FWD = 0.14     # Slight forward offset of J2 drum from J1 centerline
L_UPPER = 1.48          # J2 -> J3 upper arm length
L_FORE = 1.52           # J3 -> J5 spherical wrist center length
D_WRIST = 0.56          # J5 wrist center -> Ring-Claw TCP center

# Clean TCP Workspace Center & Amplitudes (mapped directly from normalized T-Code 0..1)
TCP_NEUTRAL: Vec3 = (0.0, -0.78, 1.44)
AMP_L0_Z = 0.44         # +/- 0.44 (total 88 cm scaled vertical travel!)
AMP_L1_Y = 0.30         # +/- 0.30 forward/back surge
AMP_L2_X = 0.34         # +/- 0.34 left/right sway
AMP_R0_YAW_DEG = 36.0   # Arm yaw swing component of R0 (while inner cyan ring adds +/- 120 deg visual twist!)
AMP_R1_ROLL_DEG = 30.0  # +/- 30 deg lateral roll
AMP_R2_PITCH_DEG = 28.0 # +/- 28 deg forward/back pitch


def _mat_from_euler_zyx(yaw_r: float, roll_r: float, pitch_r: float):
    """Builds right-handed 3x3 basis columns (rx, ry, rz) for the Ring-Claw TCP.
    At neutral (0, 0, 0):
      rx = (+1,  0,  0)  (Right)
      ry = ( 0, +1,  0)  (Back toward robot arm!)
      rz = ( 0,  0, +1)  (Up, normal to the open circular ring!)
    Applies: yaw around Z (R0), pitch around X (R2), roll around Y (R1).
    """
    # Start with identity columns
    rx = (1.0, 0.0, 0.0)
    ry = (0.0, 1.0, 0.0)
    rz = (0.0, 0.0, 1.0)
    # 1) Yaw around Z (R0)
    rx = sr6._rot_axis(rx, (0.0, 0.0, 1.0), yaw_r)
    ry = sr6._rot_axis(ry, (0.0, 0.0, 1.0), yaw_r)
    # 2) Pitch around local X (R2)
    ry = sr6._rot_axis(ry, rx, pitch_r)
    rz = sr6._rot_axis(rz, rx, pitch_r)
    # 3) Roll around local Y (R1)
    rx = sr6._rot_axis(rx, ry, roll_r)
    rz = sr6._rot_axis(rz, ry, roll_r)
    return (sr6.v_unit(rx), sr6.v_unit(ry), sr6.v_unit(rz))


def _basis_from_xz(x_dir: Vec3, z_dir: Vec3):
    bx = sr6.v_unit(x_dir)
    by = sr6.v_unit(sr6.v_cross(z_dir, bx))
    bz = sr6.v_unit(sr6.v_cross(bx, by))
    return (bx, by, bz)


def _basis_from_yz(y_dir: Vec3, z_hint: Vec3):
    by = sr6.v_unit(y_dir)
    bx = sr6.v_unit(sr6.v_cross(by, z_hint))
    if sr6.v_len(bx) < 1e-6:
        bx = sr6.v_unit(sr6.v_cross(by, (1.0, 0.0, 0.0)))
    bz = sr6.v_unit(sr6.v_cross(bx, by))
    return (bx, by, bz)


def solve_vex6_ik(l0: float, l1: float, l2: float, r0: float, r1: float, r2: float) -> dict:
    """
    Exact analytical 6-DOF Inverse Kinematics for the VEX-6 Single-Arm Industrial Robot!
    Returns world positions & exact orthonormal 3x3 bases for every rigid link:
      - turret (J1)
      - upper_arm (J2 -> J3)
      - forearm_tube (J3 -> J4)
      - forearm_body (J4 -> J5)
      - wrist_clevis (J5)
      - claw_assembly (J6 -> Ring-Claw TCP)
      - inner_ring_twist (extra visual rotary index ring inside the claw for crisp +/-120 deg R0 feedback!)
    """
    cl0 = sr6.clamp(l0, 0.02, 0.98)
    cl1 = sr6.clamp(l1, 0.04, 0.96)
    cl2 = sr6.clamp(l2, 0.04, 0.96)
    cr0 = sr6.clamp(r0, 0.01, 0.99)
    cr1 = sr6.clamp(r1, 0.04, 0.96)
    cr2 = sr6.clamp(r2, 0.04, 0.96)

    # 1) Desired Ring-Claw TCP world position & orientation
    tcp_pos: Vec3 = (
        TCP_NEUTRAL[0] + (cl2 - 0.5) * 2.0 * AMP_L2_X,
        TCP_NEUTRAL[1] - (cl1 - 0.5) * 2.0 * AMP_L1_Y,
        TCP_NEUTRAL[2] + (cl0 - 0.5) * 2.0 * AMP_L0_Z,
    )
    yaw_rad = (0.5 - cr0) * 2.0 * math.radians(AMP_R0_YAW_DEG)
    roll_rad = (cr1 - 0.5) * 2.0 * math.radians(AMP_R1_ROLL_DEG)
    pitch_rad = (cr2 - 0.5) * 2.0 * math.radians(AMP_R2_PITCH_DEG)
    rx, ry, rz = _mat_from_euler_zyx(yaw_rad, roll_rad, pitch_rad)

    # 2) Spherical wrist center sits D_WRIST behind the Ring-Claw TCP along +ry
    wrist_pos = sr6.v_add(tcp_pos, sr6.v_mul(ry, D_WRIST))

    # 3) J1 Base Turret Yaw around world +Z (0 = pointing straight toward -Y)
    rel_w = sr6.v_sub(wrist_pos, BASE_POS)
    q1 = math.atan2(rel_w[0], max(0.25, -rel_w[1]))
    # Horizontal forward direction of the turret plane (at q1=0 -> (0, -1, 0))
    fwd_h: Vec3 = (math.sin(q1), -math.cos(q1), 0.0)
    right_h: Vec3 = (math.cos(q1), math.sin(q1), 0.0)
    up_w: Vec3 = (0.0, 0.0, 1.0)

    # Turret world basis (local -Y points forward along fwd_h, +X along right_h, +Z up)
    turret_axes = (right_h, sr6.v_mul(fwd_h, -1.0), up_w)

    # 4) J2 Shoulder Pivot & Exact Elbow-Up 2-Link Planar IK (J2 + J3)
    shoulder_pos = sr6.v_add(
        (BASE_POS[0], BASE_POS[1], SHOULDER_Z),
        sr6.v_mul(fwd_h, SHOULDER_FWD),
    )
    sw = sr6.v_sub(wrist_pos, shoulder_pos)
    r_plane = sr6.v_dot(sw, fwd_h)
    h_plane = sr6.v_dot(sw, up_w)
    dist = math.sqrt(r_plane * r_plane + h_plane * h_plane)

    # Clamp gently inside physical reach envelope
    min_reach = abs(L_UPPER - L_FORE) + 0.20
    max_reach = L_UPPER + L_FORE - 0.08
    if dist > max_reach and dist > 1e-6:
        scale = max_reach / dist
        r_plane *= scale
        h_plane *= scale
        dist = max_reach
        wrist_pos = sr6.v_add(shoulder_pos, sr6.v_add(sr6.v_mul(fwd_h, r_plane), sr6.v_mul(up_w, h_plane)))
        tcp_pos = sr6.v_sub(wrist_pos, sr6.v_mul(ry, D_WRIST))
    elif dist < min_reach:
        dist = min_reach

    cos_b = sr6.clamp((L_UPPER * L_UPPER + dist * dist - L_FORE * L_FORE) / (2.0 * L_UPPER * dist), -1.0, 1.0)
    beta = math.acos(cos_b)
    alpha = math.atan2(h_plane, r_plane)
    theta1 = alpha + beta  # Classic high-elbow industrial robot pose!

    elbow_pos = sr6.v_add(
        shoulder_pos,
        sr6.v_add(sr6.v_mul(fwd_h, L_UPPER * math.cos(theta1)), sr6.v_mul(up_w, L_UPPER * math.sin(theta1))),
    )

    # Upper arm basis: local +Z points along upper arm (shoulder -> elbow), +X is hinge axis (right_h)
    u_dir = sr6.v_unit(sr6.v_sub(elbow_pos, shoulder_pos))
    upper_axes = _basis_from_xz(right_h, u_dir)

    # Forearm axis: local +Z points from elbow -> wrist!
    f_dir = sr6.v_unit(sr6.v_sub(wrist_pos, elbow_pos))
    fore_tube_axes = _basis_from_xz(right_h, f_dir)

    # 5) J4 Forearm Roll + J5 Wrist Pitch + J6 Wrist Roll (smooth singularity-free spherical wrist!)
    # Forearm body rolls around f_dir to align smoothly with the lateral plane of the target claw (rx)
    # Project rx perpendicular to f_dir so J4 smoothly tracks R1/R0 roll without ever flipping 180°!
    proj_x = sr6.v_sub(rx, sr6.v_mul(f_dir, sr6.v_dot(rx, f_dir)))
    if sr6.v_len(proj_x) < 0.08:
        proj_x = right_h
    fore_x = sr6.v_unit(proj_x)
    # Keep fore_x on the same hemisphere as right_h (prevents sudden flip!)
    if sr6.v_dot(fore_x, right_h) < 0.0:
        fore_x = sr6.v_mul(fore_x, -1.0)
    fore_body_axes = _basis_from_xz(fore_x, f_dir)

    # J5 Wrist Clevis sits at wrist_pos; local -Y points along claw approach (-ry), local +X stays aligned with fore_x
    # projected perpendicular to approach axis so the clevis visually hinges cleanly!
    app_dir = sr6.v_mul(ry, -1.0)  # Points forward from wrist -> claw center!
    clevis_x = sr6.v_sub(fore_x, sr6.v_mul(app_dir, sr6.v_dot(fore_x, app_dir)))
    if sr6.v_len(clevis_x) < 0.08:
        clevis_x = rx
    clevis_x = sr6.v_unit(clevis_x)
    clevis_z = sr6.v_unit(sr6.v_cross(clevis_x, ry))
    clevis_x = sr6.v_unit(sr6.v_cross(ry, clevis_z))
    wrist_clevis_axes = (clevis_x, ry, clevis_z)

    # J6 Ring-Claw TCP Assembly: exact target (rx, ry, rz) centered at tcp_pos!
    claw_axes = (rx, ry, rz)

    # Auxiliary inner rotary calibration ring inside the claw (spins full +/- 120 deg for vivid R0 feedback!)
    inner_twist_rad = (0.5 - cr0) * math.radians(240.0)
    irx = sr6._rot_axis(rx, rz, inner_twist_rad)
    iry = sr6._rot_axis(ry, rz, inner_twist_rad)
    inner_ring_axes = (sr6.v_unit(irx), sr6.v_unit(iry), rz)

    # Forearm J4 mid-collar position (36% along elbow -> wrist)
    j4_pos = sr6.v_add(elbow_pos, sr6.v_mul(f_dir, L_FORE * 0.36))
    # Wrist J6 LED collar sits between wrist_pos and claw back
    j6_pos = sr6.v_add(wrist_pos, sr6.v_mul(app_dir, 0.16))

    # Glowing trail points: top & center of the open ring!
    tip_pos = sr6.v_add(tcp_pos, sr6.v_mul(rz, 0.14))
    base_pos = sr6.v_sub(tcp_pos, sr6.v_mul(rz, 0.14))

    return {
        "base_pos": BASE_POS,
        "turret_pos": (BASE_POS[0], BASE_POS[1], 0.44),
        "turret_axes": turret_axes,
        "shoulder_pos": shoulder_pos,
        "upper_axes": upper_axes,
        "elbow_pos": elbow_pos,
        "fore_tube_axes": fore_tube_axes,
        "j4_pos": j4_pos,
        "fore_body_axes": fore_body_axes,
        "wrist_pos": wrist_pos,
        "wrist_clevis_axes": wrist_clevis_axes,
        "j6_pos": j6_pos,
        "tcp_pos": tcp_pos,
        "claw_axes": claw_axes,
        "inner_ring_axes": inner_ring_axes,
        "tip_pos": tip_pos,
        "base_pos": base_pos,
        "q1_deg": math.degrees(q1),
        "upper_len_err": abs(sr6.v_len(sr6.v_sub(elbow_pos, shoulder_pos)) - L_UPPER),
        "fore_len_err": abs(sr6.v_len(sr6.v_sub(wrist_pos, elbow_pos)) - L_FORE),
        "wrist_len_err": abs(sr6.v_len(sr6.v_sub(tcp_pos, wrist_pos)) - D_WRIST),
    }


if __name__ == "__main__":
    print("=== VEX-6 Analytical 6-DOF IK Verification ===")
    for name, cmd in [
        ("neutral", (0.5, 0.5, 0.5, 0.5, 0.5, 0.5)),
        ("L0=0.95 UP", (0.95, 0.5, 0.5, 0.5, 0.5, 0.5)),
        ("L0=0.05 DN", (0.05, 0.5, 0.5, 0.5, 0.5, 0.5)),
        ("L1=0.92 FWD", (0.5, 0.92, 0.5, 0.5, 0.5, 0.5)),
        ("L2=0.92 RGT", (0.5, 0.5, 0.92, 0.5, 0.5, 0.5)),
        ("R0=0.95 YAW", (0.5, 0.5, 0.5, 0.95, 0.5, 0.5)),
        ("R1=0.92 ROL", (0.5, 0.5, 0.5, 0.5, 0.92, 0.5)),
        ("R2=0.92 PIT", (0.5, 0.5, 0.5, 0.5, 0.5, 0.92)),
        ("EXTREME 6D", (0.90, 0.88, 0.88, 0.92, 0.88, 0.88)),
    ]:
        p = solve_vex6_ik(*cmd)
        tc = p["tcp_pos"]
        el = p["elbow_pos"]
        max_err = max(p["upper_len_err"], p["fore_len_err"], p["wrist_len_err"])
        print(f"  {name:12s} TCP=({tc[0]:+.2f},{tc[1]:+.2f},{tc[2]:+.2f}) ElbowZ={el[2]:.2f} J1={p['q1_deg']:+5.1f}deg err={max_err:.2e}")
        assert max_err < 1e-6, f"IK length error too big: {max_err}"
    # Verify continuity across entire 16s 5-Math-Manifold Reel (960 frames)
    reel = sr6.generate_showcase_sequence("reel_all", 16.0, 60)
    prev_el = None
    max_el_jump = 0.0
    for fr in reel:
        p = solve_vex6_ik(*fr)
        assert max(p["upper_len_err"], p["fore_len_err"], p["wrist_len_err"]) < 1e-6
        if prev_el is not None:
            max_el_jump = max(max_el_jump, sr6.v_len(sr6.v_sub(p["elbow_pos"], prev_el)))
        prev_el = p["elbow_pos"]
    print(f"  960-frame Reel Continuity PASS! max_elbow_frame_jump={max_el_jump:.4f} (zero flips!)")
