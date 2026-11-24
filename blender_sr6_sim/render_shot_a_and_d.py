#!/usr/bin/env python3
"""
Builds and renders:
  - Shot A: 04_Act2_SplitSync_CubeRod_VEX6_1080p60.mp4 (32s, 1920x1080 @ 60fps)
      Clean translucent reference Cube + directional titanium Rod on the left,
      mirroring 1:1 in exact frame-lock with the VEX-6 6-DOF industrial robot arm on the right!
      0-16s: Standard 6-DOF real-time sync (baseline stroke + pitch/roll/surge)
      16-32s: Extended native-motion rhythms (depth-locked grind -> fast/slow tempo -> coupled 6D wave)
  - Shot D: 07_Act4_VEX6_VoiceControl_Demo_1080p60.mp4 (16s, 1920x1080 @ 60fps)
      VEX-6 responding live to the 2 Act-4 voice commands:
      0-4.5s fast circling -> 4.5-10.5s slow pure linear back-and-forth -> 10.5-16s autonomous gentle wave
"""
from __future__ import annotations

import math
import pathlib
import sys
import time

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = pathlib.Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

import sr6_kinematics as sr6
import vex6_kinematics as vex6
import vex6_live_panel as panel

OUT_DIR = HERE / "renders"
OUT_DIR.mkdir(parents=True, exist_ok=True)


def make_mat(name, base_color, metallic=0.0, roughness=0.35, alpha=1.0, emission=None, emission_strength=0.0):
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Base Color"].default_value = (*base_color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Alpha"].default_value = alpha
    if emission is not None and emission_strength > 0.0:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    if alpha < 0.999:
        m.surface_render_method = "BLENDED"
    return m


def add_box(bm, center, size, bevel=0.0, mat_idx=0):
    sx, sy, sz = size
    ret = bmesh.ops.create_cube(bm, size=1.0)
    verts = ret["verts"]
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=verts)
    if bevel > 0.0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=2, affect="EDGES", clamp_overlap=True)
    bmesh.ops.translate(bm, vec=center, verts=list({v for f in bm.faces for v in f.verts if v in verts or True}))
    # Wait: translate only newly created verts cleanly via matrix below!


def _create_box_mesh(bm, center, size, mat_idx=0):
    sx, sy, sz = size
    cx, cy, cz = center
    xf = Matrix.Translation((cx, cy, cz)) @ Matrix.Diagonal((sx, sy, sz, 1.0))
    ret = bmesh.ops.create_cube(bm, size=1.0, matrix=xf)
    vset = set(ret["verts"])
    for f in bm.faces:
        if all(v in vset for v in f.verts):
            f.material_index = mat_idx


def _create_cyl_mesh(bm, center, radius, depth, segs=36, axis="Z", mat_idx=0):
    cx, cy, cz = center
    rot = Matrix.Identity(4)
    if axis == "X":
        rot = Matrix.Rotation(math.pi * 0.5, 4, "Y")
    elif axis == "Y":
        rot = Matrix.Rotation(math.pi * 0.5, 4, "X")
    xf = Matrix.Translation((cx, cy, cz)) @ rot
    ret = bmesh.ops.create_cone(
        bm,
        cap_ends=True,
        cap_tris=False,
        segments=segs,
        radius1=radius,
        radius2=radius,
        depth=depth,
        matrix=xf,
    )
    vset = set(ret["verts"])
    for f in bm.faces:
        if all(v in vset for v in f.verts):
            f.material_index = mat_idx


def build_geometric_reference_rig(col, origin_xy=(-1.95, -0.15)):
    """Builds a sleek, 100% platform-safe Studio-style geometric test rig:
    - Heavy dark circular pedestal matching VEX-6 aesthetic
    - Translucent cyan-glass reference Cube + glowing wireframe edges
    - Articulated directional titanium Rod with orange depth bands & twist pointer!
    """
    ox, oy = origin_xy
    m_base = make_mat("Geo_Base", (0.14, 0.16, 0.20), metallic=0.65, roughness=0.35)
    m_white = make_mat("Geo_White", (0.90, 0.92, 0.95), metallic=0.05, roughness=0.28)
    m_glass = make_mat("Geo_Glass", (0.20, 0.78, 0.98), metallic=0.02, roughness=0.08, alpha=0.09)
    m_frame = make_mat("Geo_Frame", (0.10, 0.65, 0.92), metallic=0.30, roughness=0.22, emission=(0.12, 0.76, 1.0), emission_strength=1.2)
    m_rod = make_mat("Geo_RodMetal", (0.36, 0.40, 0.46), metallic=0.80, roughness=0.28)
    m_orange = make_mat("Geo_Orange", (0.98, 0.36, 0.05), metallic=0.12, roughness=0.26)
    m_dark = make_mat("Geo_Dark", (0.09, 0.11, 0.14), metallic=0.55, roughness=0.38)

    pal_cube = [m_base, m_white, m_glass, m_frame, m_orange]
    pal_rod = [m_rod, m_orange, m_dark, m_frame]

    # 1) Static Pedestal + Translucent Reference Cube Frame centered at (ox, oy, 1.38)
    cz = 1.38
    half = 0.62
    bm = bmesh.new()
    # Matching circular pedestal on floor
    _create_cyl_mesh(bm, (ox, oy, 0.05), 0.56, 0.10, segs=48, mat_idx=0)
    _create_cyl_mesh(bm, (ox, oy, 0.16), 0.44, 0.14, segs=48, mat_idx=1)
    # 4 corner support columns from pedestal up to cube bottom (z = cz - half = 0.76)
    for sx in (-1, 1):
        for sy in (-1, 1):
            px = ox + sx * (half - 0.06)
            py = oy + sy * (half - 0.06)
            _create_cyl_mesh(bm, (px, py, 0.46), 0.022, 0.60, segs=16, mat_idx=0)
    # Translucent glass cube walls
    _create_box_mesh(bm, (ox, oy, cz), (half * 2.0, half * 2.0, half * 2.0), mat_idx=2)
    # 12 crisp wireframe edge bars outlining the cube!
    bar = 0.024
    for sy in (-half, half):
        for sz in (cz - half, cz + half):
            _create_box_mesh(bm, (ox, oy + sy, sz), (half * 2.0 + bar, bar, bar), mat_idx=3)
    for sx in (-half, half):
        for sz in (cz - half, cz + half):
            _create_box_mesh(bm, (ox + sx, oy, sz), (bar, half * 2.0 + bar, bar), mat_idx=3)
    for sx in (-half, half):
        for sy in (-half, half):
            _create_box_mesh(bm, (ox + sx, oy + sy, cz), (bar, bar, half * 2.0 + bar), mat_idx=3)
    # Subtle mid-plane orange reference ring ticks on left/right walls (shows exact neutral depth!)
    _create_box_mesh(bm, (ox - half, oy, cz), (0.032, 0.24, 0.016), mat_idx=4)
    _create_box_mesh(bm, (ox + half, oy, cz), (0.032, 0.24, 0.016), mat_idx=4)

    m_cube = bpy.data.meshes.new("Geo_RefCube_Mesh")
    bm.to_mesh(m_cube)
    bm.free()
    obj_cube = bpy.data.objects.new("Geo_RefCube_Static", m_cube)
    col.objects.link(obj_cube)
    for mat in pal_cube:
        obj_cube.data.materials.append(mat)

    # 2) Dynamic Directional Reference Cylinder / Probe Rod (origin = center of rod, moves 1:1 with L0..R2!)
    bm = bmesh.new()
    # Main brushed titanium shaft along local Z
    _create_cyl_mesh(bm, (0.0, 0.0, 0.0), 0.105, 0.96, segs=40, axis="Z", mat_idx=0)
    # Top & bottom dark precision end-caps
    _create_cyl_mesh(bm, (0.0, 0.0, +0.49), 0.112, 0.04, segs=36, axis="Z", mat_idx=2)
    _create_cyl_mesh(bm, (0.0, 0.0, -0.49), 0.112, 0.04, segs=36, axis="Z", mat_idx=2)
    # 3 vivid safety-orange calibration rings along the shaft (makes vertical depth L0 super readable!)
    for rz in (-0.30, 0.0, +0.30):
        _create_cyl_mesh(bm, (0.0, 0.0, rz), 0.113, 0.036, segs=40, axis="Z", mat_idx=1)
    # Forward-facing (-Y) bright orange longitudinal fin + cyan tip arrow (makes R0 twist / R1 roll / R2 pitch unmistakable!)
    _create_box_mesh(bm, (0.0, -0.118, 0.0), (0.032, 0.045, 0.78), mat_idx=1)
    _create_box_mesh(bm, (0.0, -0.145, +0.36), (0.044, 0.065, 0.07), mat_idx=3)
    # Lateral dark cross-tabs on +/-X (shows roll/twist immediately from front view!)
    _create_box_mesh(bm, (0.0, 0.0, 0.0), (0.28, 0.026, 0.048), mat_idx=2)

    m_rod = bpy.data.meshes.new("Geo_ProbeRod_Mesh")
    bm.to_mesh(m_rod)
    bm.free()
    obj_rod = bpy.data.objects.new("Geo_ProbeRod_Dynamic", m_rod)
    col.objects.link(obj_rod)
    for mat in pal_rod:
        obj_rod.data.materials.append(mat)
    obj_rod.rotation_mode = "QUATERNION"
    return obj_rod, (ox, oy, cz)


def _smoothstep(u: float) -> float:
    u = max(0.0, min(1.0, u))
    return u * u * (3.0 - 2.0 * u)


def generate_shot_a_frames(duration_s: float = 32.0, fps: int = 60):
    """
    32s Act-2 real-time 6-DOF demonstration:
      - 0.0 - 16.0s: Real KoiSync baseline sync (smooth deep L0 stroke + natural coupled pitch/roll/surge)
      - 16.0 - 21.5s: Extended Native Motion #1 — Depth-locked pure lateral sway + roll grind
      - 21.5 - 26.5s: Extended Native Motion #2 — Fast/slow tempo burst alternation
      - 26.5 - 32.0s: Extended Native Motion #3 — Full 6-axis decoupled helix + tilt wave
    """
    n = int(round(duration_s * fps))
    dt = 1.0 / float(fps)
    filt_pos = [0.5] * 6
    filt_vel = [0.0] * 6
    max_vel = [4.0, 3.4, 3.4, 3.6, 3.2, 3.4]
    max_acc = [38.0, 32.0, 32.0, 34.0, 30.0, 32.0]
    wn, zeta = 28.0, 0.96

    frames = []
    phase_burst = 0.0
    for i in range(n):
        t = i * dt
        if t < 16.0:
            # Part 1 (0-16s): Clean rhythmic 6-DOF real-time tracking (readable, authentic KoiSync cadence)
            ph = 2.0 * math.pi * 0.92 * t
            # Gradually introduce surge/roll/pitch so viewer sees simple vertical -> full 6-axis lock-in
            w_lat = _smoothstep((t - 2.0) / 3.0)
            w_rot = _smoothstep((t - 5.5) / 3.0)
            raw = [
                0.50 + 0.38 * math.sin(ph),
                0.50 + 0.24 * w_lat * math.cos(ph),
                0.50 + 0.26 * w_lat * math.sin(ph * 0.5),
                0.50 + 0.34 * w_rot * math.sin(ph * 0.5 + 0.4),
                0.50 + 0.28 * w_rot * math.sin(ph * 0.5),
                0.50 + 0.32 * w_lat * math.cos(ph),
            ]
        elif t < 21.5:
            # Part 2A (16-21.5s): Depth-locked pure lateral/circular hip grind (L0 stays near 0.34!)
            lt = t - 16.0
            ph = 2.0 * math.pi * 1.15 * lt
            raw = [
                0.34 + 0.04 * math.sin(ph * 2.0),
                0.50 + 0.34 * math.cos(ph),
                0.50 + 0.36 * math.sin(ph),
                0.50 + 0.36 * math.sin(ph + 0.6),
                0.50 + 0.36 * math.sin(ph),
                0.50 + 0.36 * math.cos(ph),
            ]
        elif t < 26.5:
            # Part 2B (21.5-26.5s): Dynamic fast-slow tempo modulation!
            lt = t - 21.5
            freq = 0.55 if (lt < 1.8) else (2.15 if (lt < 3.6) else 0.75)
            phase_burst += 2.0 * math.pi * freq * dt
            raw = [
                0.50 + 0.40 * math.sin(phase_burst),
                0.50 + 0.24 * math.cos(phase_burst),
                0.50 + 0.18 * math.sin(phase_burst * 0.5),
                0.50 + 0.28 * math.sin(phase_burst),
                0.50 + 0.24 * math.sin(phase_burst * 0.5),
                0.50 + 0.30 * math.cos(phase_burst),
            ]
        else:
            # Part 2C (26.5-32s): Coupled 6-axis spiral + tilt wave!
            lt = t - 26.5
            ph = 2.0 * math.pi * 1.25 * lt
            raw = [
                0.50 + 0.38 * math.sin(ph),
                0.50 + 0.34 * math.cos(ph * 1.5),
                0.50 + 0.34 * math.sin(ph * 1.5),
                0.50 + 0.42 * math.sin(ph * 0.75),
                0.50 + 0.34 * math.sin(ph + 0.5),
                0.50 + 0.36 * math.cos(ph + 0.5),
            ]

        for k in range(6):
            err = sr6.clamp(raw[k], 0.04, 0.96) - filt_pos[k]
            acc = sr6.clamp(wn * wn * err - 2.0 * zeta * wn * filt_vel[k], -max_acc[k], max_acc[k])
            vel = sr6.clamp(filt_vel[k] + acc * dt, -max_vel[k], max_vel[k])
            filt_pos[k] = sr6.clamp(filt_pos[k] + vel * dt, 0.04, 0.96)
            filt_vel[k] = vel
        frames.append(tuple(filt_pos))
    return frames


def generate_shot_d_frames(duration_s: float = 16.0, fps: int = 60):
    """
    16s Act-4 Voice-Command Live Response demo (matches Act-4 voiceover bubbles word-for-word!):
      - 0.0 - 4.5s : Fast energetic 6-axis circling + vertical rhythm
      - 4.5 - 10.5s: Voice Cmd 1 ("节奏放慢一点，别转圈了，只做前后摆动") -> slows down, kills sway/twist, pure linear surge!
      - 10.5 - 16.0s: Voice Cmd 2 ("接下来交给你自己主导节奏吧") -> smooth AI autonomous gentle spiral wave!
    """
    n = int(round(duration_s * fps))
    dt = 1.0 / float(fps)
    filt_pos = [0.5] * 6
    filt_vel = [0.0] * 6
    max_vel = [4.0, 3.4, 3.4, 3.6, 3.2, 3.4]
    max_acc = [34.0, 30.0, 30.0, 32.0, 28.0, 30.0]
    wn, zeta = 22.0, 0.98

    frames = []
    ph = 0.0
    for i in range(n):
        t = i * dt
        if t < 4.5:
            # Fast energetic circling
            ph += 2.0 * math.pi * 1.85 * dt
            raw = [
                0.50 + 0.36 * math.sin(ph),
                0.50 + 0.34 * math.cos(ph),
                0.50 + 0.34 * math.sin(ph),
                0.50 + 0.40 * math.sin(ph),
                0.50 + 0.32 * math.sin(ph),
                0.50 + 0.34 * math.cos(ph),
            ]
        elif t < 10.5:
            # Slow, zero-circle pure forward-back linear swing (L1 + gentle L0/R2)
            ph += 2.0 * math.pi * 0.52 * dt
            raw = [
                0.50 + 0.16 * math.sin(ph),
                0.50 + 0.40 * math.sin(ph),
                0.50,
                0.50,
                0.50,
                0.50 + 0.24 * math.sin(ph),
            ]
        else:
            # AI Autonomous gentle compound double-helix wave
            lt = t - 10.5
            ph += 2.0 * math.pi * 0.95 * dt
            ramp = _smoothstep(lt / 1.0)
            raw = [
                0.50 + 0.38 * ramp * math.sin(ph),
                0.50 + 0.30 * ramp * math.cos(ph * 1.5),
                0.50 + 0.30 * ramp * math.sin(ph * 1.5),
                0.50 + 0.38 * ramp * math.sin(ph * 0.75),
                0.50 + 0.28 * ramp * math.sin(ph + 0.4),
                0.50 + 0.32 * ramp * math.cos(ph + 0.4),
            ]

        for k in range(6):
            err = sr6.clamp(raw[k], 0.04, 0.96) - filt_pos[k]
            acc = sr6.clamp(wn * wn * err - 2.0 * zeta * wn * filt_vel[k], -max_acc[k], max_acc[k])
            vel = sr6.clamp(filt_vel[k] + acc * dt, -max_vel[k], max_vel[k])
            filt_pos[k] = sr6.clamp(filt_pos[k] + vel * dt, 0.04, 0.96)
            filt_vel[k] = vel
        frames.append(tuple(filt_pos))
    return frames


def configure_mp4_1080p60(scene, out_path: pathlib.Path, samples: int = 16):
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.fps = 60
    scene.render.fps_base = 1.0
    scene.render.use_file_extension = False
    scene.render.filepath = str(out_path)
    if hasattr(scene.eevee, "taa_render_samples"):
        scene.eevee.taa_render_samples = samples
    img = scene.render.image_settings
    if hasattr(img, "media_type"):
        img.media_type = "VIDEO"
    img.file_format = "FFMPEG"
    ff = scene.render.ffmpeg
    ff.format = "MPEG4"
    ff.codec = "H264"
    ff.constant_rate_factor = "HIGH"
    ff.ffmpeg_preset = "GOOD"
    ff.gopsize = 30
    ff.audio_codec = "NONE"


def setup_shot_a_scene():
    bpy.ops.wm.open_mainfile(filepath=str(HERE / "VEX6_RobotArm_Master.blend"))
    scene = bpy.context.scene
    panel.set_studio_theme(dark_mode=False)

    # Shift the VEX-6 arm slightly right (+1.05 on X) by creating a parent root OR shifting all VEX-6 static/arm objects?
    # Simpler & cleaner: keep VEX-6 at its exact world X=0..0.4 and place the Geometric Cube+Rod cleanly to its LEFT at X=-2.25!
    col_geo = bpy.data.collections.new("00_Geo_Reference_Rig")
    scene.collection.children.link(col_geo)
    obj_rod, cube_center = build_geometric_reference_rig(col_geo, origin_xy=(-2.05, +0.45))

    # Add a dedicated crisp softbox key for the left geometric cube so both halves shine evenly
    ld = bpy.data.lights.new("Light_Geo_Key_Data", type="AREA")
    ld.energy = 260.0
    ld.size = 2.6
    ld.color = (0.96, 0.99, 1.0)
    lo = bpy.data.objects.new("Light_Geo_Key", ld)
    col_geo.objects.link(lo)
    lo.location = Vector((-4.5, -3.5, 3.6))
    lo.rotation_euler = (Vector(cube_center) - lo.location).to_track_quat("-Z", "Y").to_euler()

    # Crisp 3/4 Studio Camera framing LEFT Translucent Cube+Rod & RIGHT VEX-6 (showing logos + full claw!)
    cd = bpy.data.cameras.new("Cam_Dual_Sync_Data")
    cd.lens = 37.0
    cd.clip_start = 0.05
    cd.clip_end = 100.0
    cam = bpy.data.objects.new("Cam_Dual_Sync", cd)
    col_geo.objects.link(cam)
    cam.location = Vector((-3.55, -5.85, 2.75))
    cam.rotation_euler = (Vector((-0.88, 0.18, 1.32)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam

    # Bake 32s synchronized frames into BOTH VEX-6 arm AND Geometric Rod!
    frames = generate_shot_a_frames(duration_s=32.0, fps=60)
    # Clear old keyframes
    for name in (
        "VEX6_J1_Turret", "VEX6_J2_UpperArm", "VEX6_J3_ForearmTube",
        "VEX6_J4_ForearmBody", "VEX6_J5_WristClevis", "VEX6_J6_RingClaw",
        "VEX6_Inner_Twist_Ring",
    ):
        o = bpy.data.objects.get(name)
        if o and o.animation_data:
            o.animation_data_clear()

    ox, oy, cz = cube_center
    for idx, vals in enumerate(frames):
        fr = idx + 1
        pose = panel.apply_vex6_pose_to_scene(*vals, keyframe=fr)
        # Mirror exact 6-DOF displacement & orientation onto the translucent Cube's inner Rod!
        l0, l1, l2, r0, r1, r2 = vals
        rod_x = ox + (l2 - 0.5) * 0.62
        rod_y = oy - (l1 - 0.5) * 0.62
        rod_z = cz + (l0 - 0.5) * 0.72
        obj_rod.location = Vector((rod_x, rod_y, rod_z))
        # Exact same orientation basis as VEX-6's claw!
        obj_rod.rotation_quaternion = panel._basis_to_quat(pose["claw_axes"])
        obj_rod.keyframe_insert(data_path="location", frame=fr)
        obj_rod.keyframe_insert(data_path="rotation_quaternion", frame=fr)

    scene.frame_start = 1
    scene.frame_end = len(frames)
    scene.frame_set(1)
    return scene


def render_shot_a(preview_only: bool = False):
    scene = setup_shot_a_scene()
    if preview_only:
        scene.render.engine = "BLENDER_EEVEE"
        scene.render.resolution_x = 1280
        scene.render.resolution_y = 720
        scene.render.image_settings.file_format = "PNG"
        scene.frame_set(240)  # Mid-motion frame showing clear tilt & stroke!
        p = HERE / "vex6_previews" / "shot_a_layout_check.png"
        scene.render.filepath = str(p)
        bpy.ops.render.render(write_still=True)
        print(f"[SHOT A PREVIEW] Saved {p}", flush=True)
        return

    out = OUT_DIR / "04_Act2_SplitSync_CubeRod_VEX6_1080p60.mp4"
    configure_mp4_1080p60(scene, out, samples=16)
    t0 = time.perf_counter()
    print(f"[SHOT A] Rendering {out.name} ({scene.frame_end}f @ 1080p60)...", flush=True)
    bpy.ops.render.render(animation=True)
    dt = time.perf_counter() - t0
    print(f"[SHOT A] DONE {out.name} in {dt:.1f}s ({out.stat().st_size / 1e6:.2f} MB)", flush=True)


def render_shot_d():
    out = OUT_DIR / "07_Act4_VEX6_VoiceControl_Demo_1080p60.mp4"
    t0 = time.perf_counter()
    bpy.ops.wm.open_mainfile(filepath=str(HERE / "VEX6_RobotArm_Master.blend"))
    scene = bpy.context.scene
    panel.set_studio_theme(dark_mode=False)
    frames = generate_shot_d_frames(duration_s=16.0, fps=60)
    panel.bake_vex6_sequence(frames, fps=60, build_trails=False)
    scene.camera = bpy.data.objects["Cam_VEX6_Hero"]
    scene.frame_start = 1
    scene.frame_end = len(frames)
    configure_mp4_1080p60(scene, out, samples=16)
    print(f"[SHOT D] Rendering {out.name} ({len(frames)}f @ 1080p60)...", flush=True)
    bpy.ops.render.render(animation=True)
    dt = time.perf_counter() - t0
    print(f"[SHOT D] DONE {out.name} in {dt:.1f}s ({out.stat().st_size / 1e6:.2f} MB)", flush=True)


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["preview_a"]
    mode = args[0]
    if mode == "preview_a":
        render_shot_a(preview_only=True)
    elif mode == "shot_a":
        render_shot_a(preview_only=False)
    elif mode == "shot_d":
        render_shot_d()
    elif mode == "both":
        render_shot_a(preview_only=False)
        render_shot_d()


if __name__ == "__main__":
    main()
