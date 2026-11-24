#!/usr/bin/env python3
"""
Batch-render clean, trail-free 1080p 60fps H.264 MP4 showcase clips of the VEX-6
6-DOF industrial robot arm in Blender 5.2 LTS.

Clips generated under E:\\project\\c#\\KoiSync\\blender_sr6_sim\\renders\\:
  1. 01_Act1_6DOF_Explainer_Lab_1080p60.mp4   (12s, 720f @ 60fps, Bright Lab, 3/4 Hero)
  2. 02_Act5_MathManifolds_Dark_1080p60.mp4   (16s, 960f @ 60fps, Dark Studio, 3/4 Hero, no trails)
  3. 03_Hook_ClawCloseup_Lab_1080p60.mp4      ( 8s, 480f @ 60fps, Bright Lab, Dynamic Claw Close-Up)
"""
from __future__ import annotations

import pathlib
import sys
import time

import bpy
from mathutils import Vector

HERE = pathlib.Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

import sr6_kinematics as sr6
import vex6_live_panel as panel

OUT_DIR = HERE / "renders"
OUT_DIR.mkdir(parents=True, exist_ok=True)


def configure_mp4_1080p60(scene, out_path: pathlib.Path, samples: int = 24):
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


def aim_camera(cam_obj, loc, target, lens):
    cam_obj.location = Vector(loc)
    cam_obj.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    cam_obj.data.lens = lens


def render_clip_1():
    out = OUT_DIR / "01_Act1_6DOF_Explainer_Lab_1080p60.mp4"
    t0 = time.perf_counter()
    bpy.ops.wm.open_mainfile(filepath=str(HERE / "VEX6_Act1_6DOF_Explainer.blend"))
    scene = bpy.context.scene
    panel.set_studio_theme(dark_mode=False)
    scene.camera = bpy.data.objects["Cam_VEX6_Hero"]
    scene.frame_start = 1
    scene.frame_end = 720
    configure_mp4_1080p60(scene, out, samples=24)
    print(f"[RENDER 1/3] Starting {out.name} (720f @ 1080p60)...", flush=True)
    bpy.ops.render.render(animation=True)
    dt = time.perf_counter() - t0
    print(f"[RENDER 1/3] DONE {out.name} in {dt:.1f}s ({out.stat().st_size / 1e6:.2f} MB)", flush=True)


def render_clip_2():
    out = OUT_DIR / "02_Act5_MathManifolds_Dark_1080p60.mp4"
    t0 = time.perf_counter()
    bpy.ops.wm.open_mainfile(filepath=str(HERE / "VEX6_RobotArm_Master.blend"))
    scene = bpy.context.scene
    panel.set_studio_theme(dark_mode=True)
    scene.camera = bpy.data.objects["Cam_VEX6_Hero"]
    scene.frame_start = 1
    scene.frame_end = 960
    configure_mp4_1080p60(scene, out, samples=24)
    print(f"[RENDER 2/3] Starting {out.name} (960f @ 1080p60)...", flush=True)
    bpy.ops.render.render(animation=True)
    dt = time.perf_counter() - t0
    print(f"[RENDER 2/3] DONE {out.name} in {dt:.1f}s ({out.stat().st_size / 1e6:.2f} MB)", flush=True)


def render_clip_3():
    out = OUT_DIR / "03_Hook_ClawCloseup_Lab_1080p60.mp4"
    t0 = time.perf_counter()
    bpy.ops.wm.open_mainfile(filepath=str(HERE / "VEX6_RobotArm_Master.blend"))
    scene = bpy.context.scene
    panel.set_studio_theme(dark_mode=False)

    # Bake 8s crisp opening hook: 4s Virtual Pivot Traveling Wave + 4s Variable-Pitch Double Helix (no trails)
    f_vp = sr6.generate_showcase_sequence("virtual_pivot", duration_s=4.0, fps=60)
    f_hx = sr6.generate_showcase_sequence("variable_helix", duration_s=4.0, fps=60)
    # Cross-fade 24 frames (0.4s) smoothly between the two manifolds
    blend_n = 24
    combined = list(f_vp[:-blend_n])
    for i in range(blend_n):
        u = (i + 1) / (blend_n + 1)
        s = u * u * (3.0 - 2.0 * u)
        a = f_vp[len(f_vp) - blend_n + i]
        b = f_hx[i]
        combined.append(tuple(a[k] * (1.0 - s) + b[k] * s for k in range(6)))
    combined.extend(f_hx[blend_n:])
    panel.bake_vex6_sequence(combined, fps=60, build_trails=False)

    # Slightly wider dynamic close-up framing so full claw & forearm VEX-6 logo never clip during big swings
    cam = bpy.data.objects["Cam_VEX6_Claw"]
    aim_camera(cam, (-2.15, -2.60, 2.08), (0.0, -0.38, 1.38), lens=38.0)
    scene.camera = cam
    scene.frame_start = 1
    scene.frame_end = len(combined)
    configure_mp4_1080p60(scene, out, samples=24)
    print(f"[RENDER 3/3] Starting {out.name} ({len(combined)}f @ 1080p60)...", flush=True)
    bpy.ops.render.render(animation=True)
    dt = time.perf_counter() - t0
    print(f"[RENDER 3/3] DONE {out.name} in {dt:.1f}s ({out.stat().st_size / 1e6:.2f} MB)", flush=True)


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["all"]
    target = args[0] if args else "all"
    if target in ("1", "all"):
        render_clip_1()
    if target in ("2", "all"):
        render_clip_2()
    if target in ("3", "all"):
        render_clip_3()
    print("[VEX6 RENDER] ALL REQUESTED CLIPS COMPLETE!", flush=True)


if __name__ == "__main__":
    main()
