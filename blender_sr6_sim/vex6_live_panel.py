#!/usr/bin/env python3
"""
Blender 5.2 Live KoiSync VEX-6 Single-Arm 6-DOF Industrial Robot Panel (N-Panel -> "VEX-6 机械臂").
Drives the Concept-A ceramic-white articulated 6-axis industrial arm + open ring-claw gripper via:
  1. Live 60 FPS sync from Koikatsu / Studio (http://127.0.0.1:41289/state)
  2. Real KoiSync 6-axis .funscript import (.funscript + .surge/.sway/.twist/.roll/.pitch)
  3. One-click Act 1 6-DOF Explainer & Act 5 5-Math-Manifold Showcase Reel + Laser-Comet Trails
  4. One-click Light Robotics Lab vs Dark Cyber Trail lighting mode
"""
from __future__ import annotations

import json
import re
import socket
import sys
import threading
import time
import urllib.request

import bpy
from mathutils import Matrix, Vector

SIM_DIR = r"E:\project\c#\KoiSync\blender_sr6_sim"
if SIM_DIR not in sys.path:
    sys.path.insert(0, SIM_DIR)

import sr6_kinematics as sr6
import vex6_kinematics as vex6


def _basis_to_quat(axes_tuple):
    bx, by, bz = axes_tuple
    m = Matrix((
        (bx[0], by[0], bz[0]),
        (bx[1], by[1], bz[1]),
        (bx[2], by[2], bz[2]),
    ))
    return m.to_quaternion()


def _set_pose(obj, pos, axes_tuple, keyframe=None):
    if obj is None:
        return
    obj.location = Vector(pos)
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = _basis_to_quat(axes_tuple)
    if keyframe is not None:
        obj.keyframe_insert(data_path="location", frame=keyframe)
        obj.keyframe_insert(data_path="rotation_quaternion", frame=keyframe)


def apply_vex6_pose_to_scene(l0: float, l1: float, l2: float, r0: float, r1: float, r2: float, keyframe=None) -> dict:
    pose = vex6.solve_vex6_ik(l0, l1, l2, r0, r1, r2)
    objs = bpy.data.objects
    _set_pose(objs.get("VEX6_J1_Turret"), pose["turret_pos"], pose["turret_axes"], keyframe)
    _set_pose(objs.get("VEX6_J2_UpperArm"), pose["shoulder_pos"], pose["upper_axes"], keyframe)
    _set_pose(objs.get("VEX6_J3_ForearmTube"), pose["elbow_pos"], pose["fore_tube_axes"], keyframe)
    _set_pose(objs.get("VEX6_J4_ForearmBody"), pose["j4_pos"], pose["fore_body_axes"], keyframe)
    _set_pose(objs.get("VEX6_J5_WristClevis"), pose["wrist_pos"], pose["wrist_clevis_axes"], keyframe)
    _set_pose(objs.get("VEX6_J6_RingClaw"), pose["tcp_pos"], pose["claw_axes"], keyframe)
    _set_pose(objs.get("VEX6_Inner_Twist_Ring"), pose["tcp_pos"], pose["inner_ring_axes"], keyframe)
    return pose


def rebuild_neon_trail(curve_name: str, points: list, bevel_depth: float = 0.008, mat_name: str = "Mat_Trail_Cyan",
                       animate_comet_frames: int = 0, comet_window_ratio: float = 0.22):
    obj = bpy.data.objects.get(curve_name)
    if obj is None:
        cdata = bpy.data.curves.new(curve_name + "_Data", type="CURVE")
        cdata.dimensions = "3D"
        obj = bpy.data.objects.new(curve_name, cdata)
        col = bpy.data.collections.get("03_Neon_Trails") or bpy.context.scene.collection
        col.objects.link(obj)
    cdata = obj.data
    if cdata.animation_data is not None:
        cdata.animation_data_clear()
    cdata.splines.clear()
    cdata.dimensions = "3D"
    cdata.bevel_depth = bevel_depth
    cdata.bevel_resolution = 4
    cdata.fill_mode = "FULL"
    cdata.bevel_factor_mapping_start = "SPLINE"
    cdata.bevel_factor_mapping_end = "SPLINE"
    cdata.bevel_factor_start = 0.0
    cdata.bevel_factor_end = 1.0
    if len(points) >= 2:
        sp = cdata.splines.new("POLY")
        sp.points.add(len(points) - 1)
        for idx, pt in enumerate(points):
            sp.points[idx].co = (pt[0], pt[1], pt[2], 1.0)
    if animate_comet_frames > 2:
        step = max(1, animate_comet_frames // 60)
        for fr in range(1, animate_comet_frames + 1, step):
            u = (fr - 1) / float(max(1, animate_comet_frames - 1))
            cdata.bevel_factor_end = u
            cdata.bevel_factor_start = max(0.0, u - comet_window_ratio)
            cdata.keyframe_insert(data_path="bevel_factor_end", frame=fr)
            cdata.keyframe_insert(data_path="bevel_factor_start", frame=fr)
        cdata.bevel_factor_end = 1.0
        cdata.bevel_factor_start = max(0.0, 1.0 - comet_window_ratio)
        cdata.keyframe_insert(data_path="bevel_factor_end", frame=animate_comet_frames)
        cdata.keyframe_insert(data_path="bevel_factor_start", frame=animate_comet_frames)
    mat = bpy.data.materials.get(mat_name)
    if mat is not None:
        if len(cdata.materials) == 0:
            cdata.materials.append(mat)
        else:
            cdata.materials[0] = mat
    return obj


def bake_vex6_sequence(frames: list, fps: int = 60, build_trails: bool = True, trail_stride: int = 2, comet_ratio: float = 0.22):
    scene = bpy.context.scene
    scene.render.fps = fps
    scene.frame_start = 1
    scene.frame_end = max(1, len(frames))

    for name in (
        "VEX6_J1_Turret", "VEX6_J2_UpperArm", "VEX6_J3_ForearmTube",
        "VEX6_J4_ForearmBody", "VEX6_J5_WristClevis", "VEX6_J6_RingClaw", "VEX6_Inner_Twist_Ring",
    ):
        o = bpy.data.objects.get(name)
        if o is not None and o.animation_data is not None:
            o.animation_data_clear()

    tip_pts = []
    for idx, cmd in enumerate(frames):
        fr = idx + 1
        pose = apply_vex6_pose_to_scene(*cmd, keyframe=fr)
        if build_trails and (idx % max(1, trail_stride) == 0):
            tip_pts.append(pose["tcp_pos"])

    if build_trails:
        rebuild_neon_trail("VEX6_Trail_Cyan", tip_pts, bevel_depth=0.0085, mat_name="Mat_Trail_Cyan",
                           animate_comet_frames=len(frames), comet_window_ratio=comet_ratio)
    scene.frame_set(1)


def set_studio_theme(dark_mode: bool):
    """Switches cleanly between Bright Modern Robotics Lab (Concept A style) and Dark Cyber Trail Studio!"""
    scene = bpy.context.scene
    world = scene.world
    if world and world.node_tree:
        bg = world.node_tree.nodes.get("Background")
        if bg:
            if dark_mode:
                bg.inputs["Color"].default_value = (0.015, 0.020, 0.030, 1.0)
                bg.inputs["Strength"].default_value = 0.70
            else:
                bg.inputs["Color"].default_value = (0.76, 0.79, 0.83, 1.0)
                bg.inputs["Strength"].default_value = 0.95
    fl_mat = bpy.data.materials.get("Mat_Lab_Floor")
    if fl_mat and fl_mat.node_tree:
        bsdf = fl_mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            if dark_mode:
                bsdf.inputs["Base Color"].default_value = (0.025, 0.030, 0.042, 1.0)
                bsdf.inputs["Roughness"].default_value = 0.24
            else:
                bsdf.inputs["Base Color"].default_value = (0.68, 0.71, 0.75, 1.0)
                bsdf.inputs["Roughness"].default_value = 0.36
    gr_mat = bpy.data.materials.get("Mat_Lab_Grid")
    if gr_mat and gr_mat.node_tree:
        bsdf = gr_mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            if dark_mode:
                bsdf.inputs["Base Color"].default_value = (0.05, 0.48, 0.62, 1.0)
                bsdf.inputs["Emission Color"].default_value = (0.05, 0.56, 0.72, 1.0)
                bsdf.inputs["Emission Strength"].default_value = 1.4
            else:
                bsdf.inputs["Base Color"].default_value = (0.42, 0.46, 0.52, 1.0)
                bsdf.inputs["Emission Strength"].default_value = 0.0


# ---------------------------------------------------------------------------
# Background HTTP Poller for Live 60 FPS Koikatsu / Studio Viewport Sync!
# ---------------------------------------------------------------------------

class _Vex6LiveState:
    running = False
    url = "http://127.0.0.1:41289/state"
    udp_port = 8000
    connected = False
    status_text = "未连接"
    axes = [0.5, 0.5, 0.5, 0.5, 0.5, 0.5]
    last_udp_ts = 0.0
    http_thread = None
    udp_thread = None
    lock = threading.Lock()


_TCODE_RE = re.compile(r"([LR][012])(\d+)")
_AXIS_IDX = {"L0": 0, "L1": 1, "L2": 2, "R0": 3, "R1": 4, "R2": 5}


def _udp_worker():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        sock.bind(("127.0.0.1", _Vex6LiveState.udp_port))
        sock.settimeout(0.25)
    except Exception:
        sock.close()
        return
    try:
        while _Vex6LiveState.running:
            try:
                data, _ = sock.recvfrom(2048)
            except socket.timeout:
                continue
            except Exception:
                break
            text = data.decode("ascii", errors="ignore")
            matches = _TCODE_RE.findall(text)
            if not matches:
                continue
            now = time.monotonic()
            with _Vex6LiveState.lock:
                for ax_name, digits in matches:
                    idx = _AXIS_IDX.get(ax_name)
                    if idx is not None:
                        _Vex6LiveState.axes[idx] = float("0." + digits)
                _Vex6LiveState.last_udp_ts = now
                _Vex6LiveState.connected = True
                _Vex6LiveState.status_text = f"在线 · UDP TCode ({_Vex6LiveState.udp_port})"
    finally:
        sock.close()


def _poll_worker():
    while _Vex6LiveState.running:
        # Yield to UDP TCode if Studio / MainGame is actively streaming UDP packets
        if (time.monotonic() - _Vex6LiveState.last_udp_ts) < 0.8:
            time.sleep(0.15)
            continue
        try:
            req = urllib.request.Request(_Vex6LiveState.url, method="GET")
            with urllib.request.urlopen(req, timeout=0.25) as r:
                st = json.loads(r.read().decode("utf-8", errors="replace"))
            ax = st.get("axes") or {}
            vals = [
                float(ax.get("L0", 50.0)) / 100.0,
                float(ax.get("L1", 50.0)) / 100.0,
                float(ax.get("L2", 50.0)) / 100.0,
                float(ax.get("R0", 50.0)) / 100.0,
                float(ax.get("R1", 50.0)) / 100.0,
                float(ax.get("R2", 50.0)) / 100.0,
            ]
            pose_lbl = st.get("poseVariantLabel") or st.get("poseName") or st.get("scene") or "在线"
            with _Vex6LiveState.lock:
                _Vex6LiveState.connected = True
                _Vex6LiveState.axes = vals
                _Vex6LiveState.status_text = f"在线 · HTTP {pose_lbl}"
        except Exception:
            with _Vex6LiveState.lock:
                if (time.monotonic() - _Vex6LiveState.last_udp_ts) >= 0.8:
                    _Vex6LiveState.connected = False
                    _Vex6LiveState.status_text = "等待 UDP:8000 / HTTP:41289..."
            time.sleep(0.35)
        time.sleep(0.016)


def _live_timer():
    if not _Vex6LiveState.running:
        return None
    with _Vex6LiveState.lock:
        vals = list(_Vex6LiveState.axes)
        conn = _Vex6LiveState.connected
    if conn:
        apply_vex6_pose_to_scene(*vals, keyframe=None)
    return 0.016


# ---------------------------------------------------------------------------
# Blender 5.2 N-Panel UI
# ---------------------------------------------------------------------------

class VEX6_OT_ToggleLive(bpy.types.Operator):
    bl_idname = "vex6.toggle_live"
    bl_label = "切换 VEX-6 实时同步"

    def execute(self, context):
        if _Vex6LiveState.running:
            _Vex6LiveState.running = False
            _Vex6LiveState.connected = False
            _Vex6LiveState.status_text = "已断开"
        else:
            _Vex6LiveState.running = True
            _Vex6LiveState.last_udp_ts = 0.0
            _Vex6LiveState.udp_thread = threading.Thread(target=_udp_worker, daemon=True)
            _Vex6LiveState.udp_thread.start()
            _Vex6LiveState.http_thread = threading.Thread(target=_poll_worker, daemon=True)
            _Vex6LiveState.http_thread.start()
            bpy.app.timers.register(_live_timer, first_interval=0.02)
        return {"FINISHED"}


class VEX6_OT_BakePreset(bpy.types.Operator):
    bl_idname = "vex6.bake_preset"
    bl_label = "烘焙 VEX-6 轨迹"
    preset: bpy.props.StringProperty(default="reel_all")
    duration: bpy.props.FloatProperty(default=8.0)

    def execute(self, context):
        _Vex6LiveState.running = False
        frames = sr6.generate_showcase_sequence(self.preset, duration_s=self.duration, fps=60)
        bake_vex6_sequence(frames, fps=60, build_trails=False)
        self.report({"INFO"}, f"已烘焙 VEX-6 {self.preset} ({len(frames)} 帧 @ 60fps)")
        return {"FINISHED"}


class VEX6_OT_ImportFunscript(bpy.types.Operator):
    bl_idname = "vex6.import_funscript"
    bl_label = "导入真实六轴 .funscript"
    filepath: bpy.props.StringProperty(subtype="FILE_PATH")
    filter_glob: bpy.props.StringProperty(default="*.funscript", options={"HIDDEN"})

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {"RUNNING_MODAL"}

    def execute(self, context):
        _Vex6LiveState.running = False
        frames = sr6.load_funscript_set(self.filepath, fps=60, max_seconds=30.0)
        bake_vex6_sequence(frames, fps=60, build_trails=False)
        self.report({"INFO"}, f"已导入真实六轴曲线 ({len(frames)} 帧 @ 60fps)")
        return {"FINISHED"}


class VEX6_OT_SetTheme(bpy.types.Operator):
    bl_idname = "vex6.set_theme"
    bl_label = "切换棚拍风格"
    dark: bpy.props.BoolProperty(default=False)

    def execute(self, context):
        set_studio_theme(self.dark)
        return {"FINISHED"}


class VEX6_OT_SwitchCamera(bpy.types.Operator):
    bl_idname = "vex6.switch_camera"
    bl_label = "切换机位"
    cam_name: bpy.props.StringProperty(default="Cam_VEX6_Hero")

    def execute(self, context):
        cam = bpy.data.objects.get(self.cam_name)
        if cam is not None:
            context.scene.camera = cam
        return {"FINISHED"}


class VEX6_OT_ToggleTrails(bpy.types.Operator):
    bl_idname = "vex6.toggle_trails"
    bl_label = "显隐轨迹尾迹"

    def execute(self, context):
        col = bpy.data.collections.get("03_Neon_Trails")
        if col is not None:
            col.hide_viewport = not col.hide_viewport
            col.hide_render = col.hide_viewport
        return {"FINISHED"}


class VEX6_PT_MainPanel(bpy.types.Panel):
    bl_label = "VEX-6 工业机械臂"
    bl_idname = "VEX6_PT_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "VEX-6 机械臂"

    def draw(self, context):
        layout = self.layout
        box = layout.box()
        box.label(text=f"联动状态: {_Vex6LiveState.status_text}")
        box.operator(
            "vex6.toggle_live",
            text=("停止实时同步 (UDP:8000 / HTTP:41289)" if _Vex6LiveState.running else "连接 KoiSync 实时同步 (Studio / 本体)"),
            depress=_Vex6LiveState.running,
        )

        box2 = layout.box()
        box2.operator("vex6.import_funscript", text="导入 .funscript 六轴文件")

        box3 = layout.box()
        box3.label(text="一键烘焙演示轨迹 (60fps):")
        r = box3.operator("vex6.bake_preset", text="Act 1 · 六轴逐轴科普 (12s)")
        r.preset, r.duration = "act1_6dof_intro", 12.0
        r = box3.operator("vex6.bake_preset", text="Act 5 · 5大数学流形串烧 (16s)")
        r.preset, r.duration = "reel_all", 16.0
        for lbl, key in (
            ("1. 虚拟支点行波", "virtual_pivot"),
            ("2. 变螺距双螺旋", "variable_helix"),
            ("3. 6D 利萨如织网", "lissajous_6d"),
            ("4. 拍频微颤共振", "beat_flutter"),
            ("5. 平滑混沌吸引子", "smooth_chaos"),
        ):
            r = box3.operator("vex6.bake_preset", text=lbl)
            r.preset, r.duration = key, 8.0

        box4 = layout.box()
        box4.label(text="棚拍风格 & 机位:")
        row_t = box4.row(align=True)
        t_l = row_t.operator("vex6.set_theme", text="亮色实验室")
        t_l.dark = False
        t_d = row_t.operator("vex6.set_theme", text="暗色霓虹棚")
        t_d.dark = True
        row_c = box4.row(align=True)
        for lbl, cn in (("3/4主视", "Cam_VEX6_Hero"), ("环爪特写", "Cam_VEX6_Claw"), ("分屏正视", "Cam_VEX6_Front")):
            c = row_c.operator("vex6.switch_camera", text=lbl)
            c.cam_name = cn


_CLASSES = (
    VEX6_OT_ToggleLive,
    VEX6_OT_BakePreset,
    VEX6_OT_ImportFunscript,
    VEX6_OT_SetTheme,
    VEX6_OT_SwitchCamera,
    VEX6_PT_MainPanel,
)


def register():
    for cls in _CLASSES:
        try:
            bpy.utils.unregister_class(cls)
        except Exception:
            pass
        bpy.utils.register_class(cls)


if __name__ == "__main__":
    register()
    print("[VEX-6] Blender 5.2 N-Panel registered (press N -> VEX-6 机械臂).")
