#!/usr/bin/env python3
"""
Blender 5.2 Live KoiSync SR6 6-DOF Bridge & Showcase Panel (N-Panel -> "KoiSync SR6").
Can be run directly from Blender's Text Editor (embedded inside SR6_Showcase_Master.blend)
OR installed as a lightweight script. Supports:
  1. Real-time 60 FPS live sync from Koikatsu / Studio via http://127.0.0.1:41289/state
  2. One-click baking of real KoiSync 6-axis .funscript recordings
  3. One-click baking of Act 1 6-DOF Explainer & all 5 SR6 Math Limit Manifolds + glowing neon trails
"""
from __future__ import annotations

import json
import math
import pathlib
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

# ---------------------------------------------------------------------------
# Transform Helpers (Zero-Drift Exact Endpoint Placement for Linkages)
# ---------------------------------------------------------------------------

def _place_span_obj(obj, p0, p1):
    """Places a unit-Z mesh (local Z in [0..1]) cleanly from world p0 to world p1."""
    if obj is None:
        return
    v0 = Vector(p0)
    v1 = Vector(p1)
    dv = v1 - v0
    length = dv.length
    if length < 1e-6:
        return
    obj.location = v0
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = dv.normalized().to_track_quat("Z", "X")
    obj.scale = (1.0, 1.0, length)


def _basis_to_quat(axes_tuple):
    bx, by, bz = axes_tuple
    m = Matrix((
        (bx[0], by[0], bz[0]),
        (bx[1], by[1], bz[1]),
        (bx[2], by[2], bz[2]),
    ))
    return m.to_quaternion()


def apply_sr6_pose_to_scene(l0: float, l1: float, l2: float, r0: float, r1: float, r2: float, keyframe: int = None) -> dict:
    """Updates EVERY moving part of the SR6 robot arm + optional reference pillar to exact 6-DOF kinematics."""
    pose = sr6.solve_sr6_pose(l0, l1, l2, r0, r1, r2)
    objs = bpy.data.objects

    spans = (
        ("Horn_UL", pose["servo_UL"], pose["horn_UL"]),
        ("Horn_LL", pose["servo_LL"], pose["horn_LL"]),
        ("Horn_UR", pose["servo_UR"], pose["horn_UR"]),
        ("Horn_LR", pose["servo_LR"], pose["horn_LR"]),
        ("Horn_Pitch", pose["servo_Pitch"], pose["horn_Pitch"]),
        ("Rod_UL", pose["horn_UL"], pose["joint_L"]),
        ("Rod_LL", pose["horn_LL"], pose["joint_L"]),
        ("Rod_UR", pose["horn_UR"], pose["joint_R"]),
        ("Rod_LR", pose["horn_LR"], pose["joint_R"]),
        ("Rod_Pitch", pose["horn_Pitch"], pose["pitch_lug"]),
    )
    for name, a, b in spans:
        o = objs.get(name)
        if o is not None:
            _place_span_obj(o, a, b)
            if keyframe is not None:
                o.keyframe_insert(data_path="location", frame=keyframe)
                o.keyframe_insert(data_path="rotation_quaternion", frame=keyframe)
                o.keyframe_insert(data_path="scale", frame=keyframe)

    # Spherical brass ball joints at horn tips, gimbal pivots & pitch lug
    joints = (
        ("Joint_Horn_UL", pose["horn_UL"]),
        ("Joint_Horn_LL", pose["horn_LL"]),
        ("Joint_Horn_UR", pose["horn_UR"]),
        ("Joint_Horn_LR", pose["horn_LR"]),
        ("Joint_Horn_Pitch", pose["horn_Pitch"]),
        ("Joint_Gimbal_L", pose["joint_L"]),
        ("Joint_Gimbal_R", pose["joint_R"]),
        ("Joint_Pitch_Lug", pose["pitch_lug"]),
    )
    for name, pos in joints:
        o = objs.get(name)
        if o is not None:
            o.location = Vector(pos)
            if keyframe is not None:
                o.keyframe_insert(data_path="location", frame=keyframe)

    # Gimbal cradle (tracks left-right + pitch)
    gimbal = objs.get("Gimbal_Cradle")
    if gimbal is not None:
        gimbal.location = Vector(pose["carrier_pos"])
        gimbal.rotation_mode = "QUATERNION"
        gimbal.rotation_quaternion = _basis_to_quat(pose["gimbal_axes"])
        if keyframe is not None:
            gimbal.keyframe_insert(data_path="location", frame=keyframe)
            gimbal.keyframe_insert(data_path="rotation_quaternion", frame=keyframe)

    # Rotating cylindrical actuator core (gimbal + R0 twist!)
    sleeve = objs.get("Actuator_Sleeve")
    if sleeve is not None:
        sleeve.location = Vector(pose["carrier_pos"])
        sleeve.rotation_mode = "QUATERNION"
        sleeve.rotation_quaternion = _basis_to_quat(pose["sleeve_axes"])
        if keyframe is not None:
            sleeve.keyframe_insert(data_path="location", frame=keyframe)
            sleeve.keyframe_insert(data_path="rotation_quaternion", frame=keyframe)

    # Side-by-side non-sensitive Reference Pillar inside Reference Cube (offset +X by 2.85 units)
    ref_pillar = objs.get("Ref_Sync_Pillar")
    if ref_pillar is not None:
        cp = pose["carrier_pos"]
        ref_pillar.location = Vector((cp[0] + 2.85, cp[1], cp[2]))
        ref_pillar.rotation_mode = "QUATERNION"
        ref_pillar.rotation_quaternion = _basis_to_quat(pose["sleeve_axes"])
        if keyframe is not None:
            ref_pillar.keyframe_insert(data_path="location", frame=keyframe)
            ref_pillar.keyframe_insert(data_path="rotation_quaternion", frame=keyframe)

    return pose


def rebuild_neon_trail(curve_name: str, points: list, bevel_depth: float = 0.007, mat_name: str = "Mat_Cyan_LED",
                       animate_comet_frames: int = 0, comet_window_ratio: float = 0.22):
    """Updates or creates a sleek glowing 3D neon comet trail with animated bevel_factor_start/end!"""
    obj = bpy.data.objects.get(curve_name)
    if obj is None:
        cdata = bpy.data.curves.new(curve_name + "_Data", type="CURVE")
        cdata.dimensions = "3D"
        obj = bpy.data.objects.new(curve_name, cdata)
        col = bpy.data.collections.get("04_Neon_Trails") or bpy.context.scene.collection
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
        # Animate glowing laser-comet tail smoothly tracking the tip across the entire sequence!
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


def bake_sequence_to_scene(frames: list, fps: int = 60, build_trails: bool = True, trail_stride: int = 2, comet_ratio: float = 0.24):
    """Bakes a list of (L0, L1, L2, R0, R1, R2) tuples into 60fps Blender keyframes + glowing 3D laser-comet trails."""
    scene = bpy.context.scene
    scene.render.fps = fps
    scene.frame_start = 1
    scene.frame_end = max(1, len(frames))

    # Clear old animation data on rig parts so re-baking is instant & clean
    for name in (
        "Horn_UL", "Horn_LL", "Horn_UR", "Horn_LR", "Horn_Pitch",
        "Rod_UL", "Rod_LL", "Rod_UR", "Rod_LR", "Rod_Pitch",
        "Joint_Horn_UL", "Joint_Horn_LL", "Joint_Horn_UR", "Joint_Horn_LR", "Joint_Horn_Pitch",
        "Joint_Gimbal_L", "Joint_Gimbal_R", "Joint_Pitch_Lug",
        "Gimbal_Cradle", "Actuator_Sleeve", "Ref_Sync_Pillar",
    ):
        o = bpy.data.objects.get(name)
        if o is not None and o.animation_data is not None:
            o.animation_data_clear()

    tip_pts = []
    base_pts = []
    for idx, cmd in enumerate(frames):
        fr = idx + 1
        pose = apply_sr6_pose_to_scene(*cmd, keyframe=fr)
        if build_trails and (idx % max(1, trail_stride) == 0):
            tip_pts.append(pose["tip_pos"])
            base_pts.append(pose["base_pos"])

    if build_trails:
        rebuild_neon_trail("Trail_Tip_Neon", tip_pts, bevel_depth=0.0075, mat_name="Mat_Trail_Cyan",
                           animate_comet_frames=len(frames), comet_window_ratio=comet_ratio)
        rebuild_neon_trail("Trail_Base_Neon", base_pts, bevel_depth=0.0055, mat_name="Mat_Trail_Magenta",
                           animate_comet_frames=len(frames), comet_window_ratio=comet_ratio)
    scene.frame_set(1)


# ---------------------------------------------------------------------------
# Background HTTP Poller for Live 60 FPS Koikatsu / Studio Viewport Sync!
# ---------------------------------------------------------------------------

class _LiveBridgeState:
    running = False
    url = "http://127.0.0.1:41289/state"
    connected = False
    status_text = "未连接"
    axes = [0.5, 0.5, 0.5, 0.5, 0.5, 0.5]
    trail_pts = []
    thread = None
    lock = threading.Lock()


def _poll_worker():
    while _LiveBridgeState.running:
        try:
            req = urllib.request.Request(_LiveBridgeState.url, method="GET")
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
            with _LiveBridgeState.lock:
                _LiveBridgeState.connected = True
                _LiveBridgeState.axes = vals
                _LiveBridgeState.status_text = f"在线 · {pose_lbl}"
        except Exception:
            with _LiveBridgeState.lock:
                _LiveBridgeState.connected = False
                _LiveBridgeState.status_text = "等待 KoiSync (41289)..."
            time.sleep(0.35)
        time.sleep(0.016)


def _live_timer():
    if not _LiveBridgeState.running:
        return None
    with _LiveBridgeState.lock:
        vals = list(_LiveBridgeState.axes)
        conn = _LiveBridgeState.connected
    if conn:
        pose = apply_sr6_pose_to_scene(*vals, keyframe=None)
        _LiveBridgeState.trail_pts.append(pose["tip_pos"])
        if len(_LiveBridgeState.trail_pts) > 150:
            _LiveBridgeState.trail_pts = _LiveBridgeState.trail_pts[-150:]
        if len(_LiveBridgeState.trail_pts) % 3 == 0:
            rebuild_neon_trail("Trail_Tip_Neon", _LiveBridgeState.trail_pts, bevel_depth=0.013, mat_name="Mat_Cyan_LED")
    return 0.016


# ---------------------------------------------------------------------------
# Blender 5.2 N-Panel Operators & UI
# ---------------------------------------------------------------------------

class KOISYNC_OT_ToggleLive(bpy.types.Operator):
    bl_idname = "koisync.toggle_live"
    bl_label = "切换 KoiSync 实时同步"

    def execute(self, context):
        if _LiveBridgeState.running:
            _LiveBridgeState.running = False
            _LiveBridgeState.connected = False
            _LiveBridgeState.status_text = "已断开"
        else:
            _LiveBridgeState.running = True
            _LiveBridgeState.trail_pts = []
            _LiveBridgeState.thread = threading.Thread(target=_poll_worker, daemon=True)
            _LiveBridgeState.thread.start()
            bpy.app.timers.register(_live_timer, first_interval=0.02)
        return {"FINISHED"}


class KOISYNC_OT_BakePreset(bpy.types.Operator):
    bl_idname = "koisync.bake_preset"
    bl_label = "烘焙 SR6 预设轨迹"
    preset: bpy.props.StringProperty(default="reel_all")
    duration: bpy.props.FloatProperty(default=8.0)

    def execute(self, context):
        _LiveBridgeState.running = False
        frames = sr6.generate_showcase_sequence(self.preset, duration_s=self.duration, fps=60)
        bake_sequence_to_scene(frames, fps=60, build_trails=True)
        self.report({"INFO"}, f"已烘焙 {self.preset} ({len(frames)} 帧 @ 60fps)")
        return {"FINISHED"}


class KOISYNC_OT_ImportFunscript(bpy.types.Operator):
    bl_idname = "koisync.import_funscript"
    bl_label = "导入真实六轴 .funscript"
    filepath: bpy.props.StringProperty(subtype="FILE_PATH")
    filter_glob: bpy.props.StringProperty(default="*.funscript", options={"HIDDEN"})

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {"RUNNING_MODAL"}

    def execute(self, context):
        _LiveBridgeState.running = False
        frames = sr6.load_funscript_set(self.filepath, fps=60, max_seconds=30.0)
        bake_sequence_to_scene(frames, fps=60, build_trails=True)
        self.report({"INFO"}, f"已导入真实六轴曲线 ({len(frames)} 帧 @ 60fps)")
        return {"FINISHED"}


class KOISYNC_OT_SwitchCamera(bpy.types.Operator):
    bl_idname = "koisync.switch_camera"
    bl_label = "切换机位"
    cam_name: bpy.props.StringProperty(default="Cam_Hero_3Quarter")

    def execute(self, context):
        cam = bpy.data.objects.get(self.cam_name)
        if cam is not None:
            context.scene.camera = cam
        return {"FINISHED"}


class KOISYNC_OT_ToggleCollection(bpy.types.Operator):
    bl_idname = "koisync.toggle_collection"
    bl_label = "显隐图层"
    col_name: bpy.props.StringProperty(default="04_Neon_Trails")

    def execute(self, context):
        col = bpy.data.collections.get(self.col_name)
        if col is not None:
            col.hide_viewport = not col.hide_viewport
            col.hide_render = col.hide_viewport
        return {"FINISHED"}


class KOISYNC_PT_MainPanel(bpy.types.Panel):
    bl_label = "KoiSync SR6 六轴机械臂"
    bl_idname = "KOISYNC_PT_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "KoiSync SR6"

    def draw(self, context):
        layout = self.layout
        box = layout.box()
        box.label(text=f"实时联动状态: {_LiveBridgeState.status_text}")
        op = box.operator(
            "koisync.toggle_live",
            text=("停止实时同步 (41289)" if _LiveBridgeState.running else "连接 KoiSync 实时同步 (60fps)"),
            depress=_LiveBridgeState.running,
        )

        box2 = layout.box()
        box2.label(text="真实六轴曲线导入:")
        box2.operator("koisync.import_funscript", text="导入 .funscript 六轴文件")

        box3 = layout.box()
        box3.label(text="一键烘焙演示轨迹 (60fps):")
        r = box3.operator("koisync.bake_preset", text="Act 1 · 六轴逐轴科普 (12s)")
        r.preset, r.duration = "act1_6dof_intro", 12.0
        r = box3.operator("koisync.bake_preset", text="Act 5 · 5大数学流形串烧 (16s)")
        r.preset, r.duration = "reel_all", 16.0
        for lbl, key in (
            ("1. 虚拟支点行波", "virtual_pivot"),
            ("2. 变螺距双螺旋", "variable_helix"),
            ("3. 6D 利萨如织网", "lissajous_6d"),
            ("4. 拍频微颤共振", "beat_flutter"),
            ("5. 平滑混沌吸引子", "smooth_chaos"),
        ):
            r = box3.operator("koisync.bake_preset", text=lbl)
            r.preset, r.duration = key, 8.0

        box4 = layout.box()
        box4.label(text="机位与视觉元素:")
        row = box4.row(align=True)
        for lbl, cn in (("主视角", "Cam_Hero_3Quarter"), ("分屏正视", "Cam_Front_Split"), ("俯视轨迹", "Cam_Top_Trail"), ("双屏对照", "Cam_Dual_Compare")):
            c = row.operator("koisync.switch_camera", text=lbl)
            c.cam_name = cn
        row2 = box4.row(align=True)
        t1 = row2.operator("koisync.toggle_collection", text="显隐发光尾迹")
        t1.col_name = "04_Neon_Trails"
        t2 = row2.operator("koisync.toggle_collection", text="显隐对照方块+柱子")
        t2.col_name = "05_Ref_Cube_Pillar"


_CLASSES = (
    KOISYNC_OT_ToggleLive,
    KOISYNC_OT_BakePreset,
    KOISYNC_OT_ImportFunscript,
    KOISYNC_OT_SwitchCamera,
    KOISYNC_OT_ToggleCollection,
    KOISYNC_PT_MainPanel,
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
    print("[KoiSync SR6] Blender 5.2 N-Panel registered (press N -> KoiSync SR6 tab).")
