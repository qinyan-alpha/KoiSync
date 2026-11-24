#!/usr/bin/env python3
"""
Procedural Blender 5.2 Builder for the KoiSync SR6 6-DOF Parallel-Linkage Robotic Arm Showcase.
Run via:
  "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --python build_sr6_scene.py

Outputs:
  - SR6_Showcase_Master.blend      (16s 5-Math-Manifold Showcase Reel + embedded Live KoiSync N-Panel)
  - SR6_Act1_6DOF_Explainer.blend  (12s Act 1 6-Axis Sequential Explainer: L0->L1->L2->R0->R1->R2->Combo)
  - previews/01_hero_3quarter.png, 02_front_split.png, 03_top_lissajous_trail.png, 04_dual_cube_pillar.png
"""
from __future__ import annotations

import math
import pathlib
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = pathlib.Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

import sr6_kinematics as sr6
import live_koisync_addon as live


# ---------------------------------------------------------------------------
# Materials (Blender 5.2 Principled BSDF — Industrial Cyber-Precision Palette)
# ---------------------------------------------------------------------------

def make_pbr_mat(name: str, color, metallic: float = 0.0, roughness: float = 0.35,
                 emission_color=None, emission_strength: float = 0.0,
                 alpha: float = 1.0, transmission: float = 0.0):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color[:3], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission_color is not None and emission_strength > 0.0:
        bsdf.inputs["Emission Color"].default_value = (*emission_color[:3], 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    if transmission > 0.0 and "Transmission Weight" in bsdf.inputs:
        bsdf.inputs["Transmission Weight"].default_value = transmission
    if alpha < 0.999 and "Alpha" in bsdf.inputs:
        bsdf.inputs["Alpha"].default_value = alpha
        try:
            mat.surface_render_method = "BLENDED"
        except Exception:
            pass
    return mat


def create_all_materials() -> dict:
    mats = {}
    mats["cnc_dark"] = make_pbr_mat("Mat_CNC_Dark", (0.045, 0.055, 0.072), metallic=0.88, roughness=0.28)
    mats["cnc_mid"] = make_pbr_mat("Mat_CNC_Mid", (0.11, 0.135, 0.175), metallic=0.86, roughness=0.25)
    mats["titanium"] = make_pbr_mat("Mat_Brushed_Titanium", (0.68, 0.73, 0.80), metallic=0.96, roughness=0.19)
    mats["carbon"] = make_pbr_mat("Mat_Carbon_Horn", (0.03, 0.035, 0.045), metallic=0.25, roughness=0.34)
    mats["gold"] = make_pbr_mat("Mat_Gold_Joint", (0.90, 0.66, 0.22), metallic=0.98, roughness=0.17)
    mats["sleeve"] = make_pbr_mat("Mat_Sleeve_Body", (0.06, 0.10, 0.16), metallic=0.68, roughness=0.22)
    mats["servo_case"] = make_pbr_mat("Mat_Servo_Case", (0.08, 0.095, 0.12), metallic=0.82, roughness=0.30)
    mats["cyan_led"] = make_pbr_mat("Mat_Cyan_LED", (0.04, 0.80, 1.0), metallic=0.0, roughness=0.15,
                                    emission_color=(0.04, 0.85, 1.0), emission_strength=4.5)
    mats["magenta_led"] = make_pbr_mat("Mat_Magenta_LED", (0.85, 0.14, 1.0), metallic=0.0, roughness=0.15,
                                       emission_color=(0.88, 0.16, 1.0), emission_strength=4.0)
    mats["trail_cyan"] = make_pbr_mat("Mat_Trail_Cyan", (0.01, 0.78, 1.0), metallic=0.0, roughness=0.1,
                                      emission_color=(0.02, 0.82, 1.0), emission_strength=3.6)
    mats["trail_magenta"] = make_pbr_mat("Mat_Trail_Magenta", (0.86, 0.12, 1.0), metallic=0.0, roughness=0.1,
                                         emission_color=(0.90, 0.14, 1.0), emission_strength=3.2)
    mats["floor"] = make_pbr_mat("Mat_Studio_Floor", (0.022, 0.028, 0.038), metallic=0.35, roughness=0.24)
    mats["grid_line"] = make_pbr_mat("Mat_Grid_Line", (0.06, 0.36, 0.48), metallic=0.0, roughness=0.2,
                                     emission_color=(0.06, 0.48, 0.65), emission_strength=1.1)
    mats["ref_glass"] = make_pbr_mat("Mat_Ref_Glass", (0.14, 0.76, 0.96), metallic=0.10, roughness=0.06,
                                     alpha=0.09, transmission=0.0)
    mats["ref_frame"] = make_pbr_mat("Mat_Ref_Frame", (0.08, 0.84, 1.0), metallic=0.2, roughness=0.2,
                                     emission_color=(0.08, 0.84, 1.0), emission_strength=2.8)
    return mats


# ---------------------------------------------------------------------------
# Clean BMesh Geometry Builders
# ---------------------------------------------------------------------------

def make_collection(name: str, parent=None):
    col = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(col)
    return col


def link_obj(name: str, mesh, col, mat=None, smooth: bool = True):
    obj = bpy.data.objects.new(name, mesh)
    col.objects.link(obj)
    if mat is not None:
        if isinstance(mat, list):
            for m in mat:
                obj.data.materials.append(m)
        else:
            obj.data.materials.append(mat)
    if smooth and hasattr(mesh, "polygons"):
        for poly in mesh.polygons:
            poly.use_smooth = True
    return obj


def add_beveled_box(bm: bmesh.types.BMesh, center: tuple, size: tuple, bevel: float = 0.018, mat_idx: int = 0):
    ret = bmesh.ops.create_cube(bm, size=1.0)
    verts = ret["verts"]
    sx, sy, sz = size
    cx, cy, cz = center
    for v in verts:
        v.co.x = v.co.x * sx + cx
        v.co.y = v.co.y * sy + cy
        v.co.z = v.co.z * sz + cz
    edges = list({e for v in verts for e in v.link_edges})
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.material_index = mat_idx
    if bevel > 0.0:
        res = bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=2, affect="EDGES", clamp_overlap=True)
        for f in res.get("faces", []):
            f.material_index = mat_idx


def add_cylinder(bm: bmesh.types.BMesh, center: tuple, radius: float, depth: float,
                 segments: int = 28, axis: str = "Z", mat_idx: int = 0, cap_ends: bool = True):
    ret = bmesh.ops.create_cone(
        bm, cap_ends=cap_ends, cap_tris=False, segments=segments,
        radius1=radius, radius2=radius, depth=depth
    )
    verts = ret["verts"]
    rot = Matrix.Identity(4)
    if axis == "X":
        rot = Matrix.Rotation(math.pi * 0.5, 4, "Y")
    elif axis == "Y":
        rot = Matrix.Rotation(math.pi * 0.5, 4, "X")
    cx, cy, cz = center
    for v in verts:
        p = rot @ v.co
        v.co.x = p.x + cx
        v.co.y = p.y + cy
        v.co.z = p.z + cz
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mat_idx


def add_uv_sphere(bm: bmesh.types.BMesh, center: tuple, radius: float, segs: int = 20, rings: int = 12, mat_idx: int = 0):
    ret = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=radius)
    cx, cy, cz = center
    for v in ret["verts"]:
        v.co.x += cx
        v.co.y += cy
        v.co.z += cz
    for f in {f for v in ret["verts"] for f in v.link_faces}:
        f.material_index = mat_idx


def add_torus(bm: bmesh.types.BMesh, center: tuple, major_r: float, minor_r: float,
              major_seg: int = 44, minor_seg: int = 14, mat_idx: int = 0):
    cx, cy, cz = center
    ring_verts = []
    for i in range(major_seg):
        theta = 2.0 * math.pi * i / major_seg
        ct, st = math.cos(theta), math.sin(theta)
        ring = []
        for j in range(minor_seg):
            phi = 2.0 * math.pi * j / minor_seg
            cp, sp = math.cos(phi), math.sin(phi)
            r = major_r + minor_r * cp
            ring.append(bm.verts.new((cx + r * ct, cy + r * st, cz + minor_r * sp)))
        ring_verts.append(ring)
    for i in range(major_seg):
        r0 = ring_verts[i]
        r1 = ring_verts[(i + 1) % major_seg]
        for j in range(minor_seg):
            j1 = (j + 1) % minor_seg
            f = bm.faces.new((r0[j], r1[j], r1[j1], r0[j1]))
            f.material_index = mat_idx


def add_helical_ribbon(bm: bmesh.types.BMesh, radius: float, z_min: float, z_max: float,
                       turns: float = 1.25, width_z: float = 0.065, thickness: float = 0.018,
                       phase_offset: float = 0.0, steps: int = 72, mat_idx: int = 0):
    """Creates a crisp raised 3D helical indicator ribbon around a cylinder so R0 twist pops on camera!"""
    rings = []
    for i in range(steps + 1):
        u = i / float(steps)
        ang = phase_offset + u * turns * 2.0 * math.pi
        z = z_min + u * (z_max - z_min)
        ca, sa = math.cos(ang), math.sin(ang)
        r_in = radius - 0.004
        r_out = radius + thickness
        v0 = bm.verts.new((r_in * ca, r_in * sa, z - width_z * 0.5))
        v1 = bm.verts.new((r_out * ca, r_out * sa, z - width_z * 0.35))
        v2 = bm.verts.new((r_out * ca, r_out * sa, z + width_z * 0.35))
        v3 = bm.verts.new((r_in * ca, r_in * sa, z + width_z * 0.5))
        rings.append((v0, v1, v2, v3))
    for i in range(steps):
        a = rings[i]
        b = rings[i + 1]
        for k in range(4):
            k1 = (k + 1) % 4
            f = bm.faces.new((a[k], b[k], b[k1], a[k1]))
            f.material_index = mat_idx


# ---------------------------------------------------------------------------
# Assemble Complete SR6 6-DOF Mechanical Robot Scene
# ---------------------------------------------------------------------------

def build_chassis_and_studio(col_chassis, mats: dict):
    # 1) Studio Floor + subtle calibrated cyber grid
    bm_fl = bmesh.new()
    bmesh.ops.create_grid(bm_fl, x_segments=2, y_segments=2, size=18.0)
    mesh_fl = bpy.data.meshes.new("Studio_Floor_Mesh")
    bm_fl.to_mesh(mesh_fl)
    bm_fl.free()
    link_obj("Studio_Floor", mesh_fl, col_chassis, mats["floor"], smooth=False)

    # Subtle glowing floor grid lines
    bm_gr = bmesh.new()
    for i in range(-6, 7):
        pos = i * 0.8
        add_beveled_box(bm_gr, (pos, 0.0, 0.002), (0.008, 9.6, 0.003), bevel=0.0, mat_idx=0)
        add_beveled_box(bm_gr, (0.0, pos, 0.002), (9.6, 0.008, 0.003), bevel=0.0, mat_idx=0)
    mesh_gr = bpy.data.meshes.new("Studio_Grid_Mesh")
    bm_gr.to_mesh(mesh_gr)
    bm_gr.free()
    link_obj("Studio_Grid", mesh_gr, col_chassis, mats["grid_line"], smooth=False)

    # 2) CNC Machined Baseplate & Twin Servo Towers (matching exact SR6 servo positions!)
    neutral = sr6.solve_sr6_pose(0.5, 0.5, 0.5, 0.5, 0.5, 0.5)
    sUL = neutral["servo_UL"]  # (+0.73, +0.9008, +1.75)
    sLL = neutral["servo_LL"]  # (+0.73, +0.9008, +1.45)
    sP = neutral["servo_Pitch"]  # (0.0, +0.92, +2.07)

    bm = bmesh.new()
    # Heavy chamfered baseplate (mat 0: cnc_dark, mat 1: cnc_mid, mat 2: gold, mat 3: cyan_led, mat 4: titanium)
    add_beveled_box(bm, (0.0, 0.30, 0.075), (2.40, 2.35, 0.15), bevel=0.025, mat_idx=0)
    # Recessed top accent plate
    add_beveled_box(bm, (0.0, 0.30, 0.156), (2.10, 2.02, 0.02), bevel=0.008, mat_idx=1)
    # Front cyan LED bar on baseplate
    add_beveled_box(bm, (0.0, -0.86, 0.075), (1.75, 0.025, 0.024), bevel=0.004, mat_idx=3)
    # 4 corner brass hex bolts
    for sx in (-1.04, 1.04):
        for sy in (-0.72, 1.32):
            add_cylinder(bm, (sx, sy, 0.168), 0.048, 0.036, segments=6, axis="Z", mat_idx=2)

    # Twin CNC Side Servo Towers at X = +/-0.555, Y = sUL[1] (+0.90)
    for side in (-1.0, 1.0):
        tx = side * 0.52
        ty = sUL[1]
        # Main vertical CNC upright
        add_beveled_box(bm, (tx, ty + 0.08, 1.05), (0.26, 0.42, 1.85), bevel=0.022, mat_idx=0)
        # Angled forward gusset brace
        add_beveled_box(bm, (tx, ty - 0.18, 0.48), (0.16, 0.38, 0.68), bevel=0.016, mat_idx=1)
        # Side cyan LED accent vertical strip
        add_beveled_box(bm, (tx + side * 0.132, ty + 0.08, 0.72), (0.012, 0.028, 0.75), bevel=0.002, mat_idx=3)

        # Upper & Lower High-Torque Servo Housings + Shaft Hubs reaching X = +/-0.73!
        for sz in (sUL[2], sLL[2]):
            # Servo housing block
            add_beveled_box(bm, (side * 0.56, ty, sz), (0.30, 0.32, 0.22), bevel=0.016, mat_idx=1)
            # Titanium output spline shaft extending out to horn plane (X = side * 0.73)
            add_cylinder(bm, (side * 0.70, ty, sz), 0.055, 0.10, segments=24, axis="X", mat_idx=4)
            # Glowing cyan servo status ring
            add_cylinder(bm, (side * 0.715, ty, sz), 0.072, 0.018, segments=28, axis="X", mat_idx=3)
            # Gold center shaft bolt
            add_cylinder(bm, (side * 0.745, ty, sz), 0.026, 0.028, segments=6, axis="X", mat_idx=2)

    # Top CNC Cross-Bridge connecting the twin towers + mounting the 5th Rear Pitch Servo
    add_beveled_box(bm, (0.0, sUL[1] + 0.08, 2.02), (1.36, 0.34, 0.18), bevel=0.02, mat_idx=0)
    # 5th Rear Pitch Servo Housing at (0, sP[1], sP[2])
    add_beveled_box(bm, (0.0, sP[1] + 0.10, sP[2]), (0.28, 0.32, 0.24), bevel=0.016, mat_idx=1)
    add_cylinder(bm, (0.0, sP[1], sP[2]), 0.052, 0.26, segments=24, axis="X", mat_idx=4)
    add_cylinder(bm, (0.0, sP[1] - 0.05, sP[2]), 0.070, 0.018, segments=24, axis="Y", mat_idx=3)

    mesh_ch = bpy.data.meshes.new("SR6_Chassis_Mesh")
    bm.to_mesh(mesh_ch)
    bm.free()
    link_obj(
        "SR6_Chassis", mesh_ch, col_chassis,
        [mats["cnc_dark"], mats["cnc_mid"], mats["gold"], mats["cyan_led"], mats["titanium"]],
        smooth=False,
    )


def build_moving_linkages(col_links, col_carriage, mats: dict):
    # 1) Reusable Unit-Z Servo Horn Mesh (local Z in [0..1])
    bm_h = bmesh.new()
    # Main carbon arm
    add_beveled_box(bm_h, (0.0, 0.0, 0.50), (0.044, 0.095, 1.04), bevel=0.012, mat_idx=0)
    # Titanium hub & tip reinforcement rings
    add_cylinder(bm_h, (0.0, 0.0, 0.0), 0.068, 0.054, segments=24, axis="X", mat_idx=1)
    add_cylinder(bm_h, (0.0, 0.0, 1.0), 0.050, 0.050, segments=20, axis="X", mat_idx=1)
    mesh_horn = bpy.data.meshes.new("SR6_Horn_Mesh")
    bm_h.to_mesh(mesh_horn)
    bm_h.free()

    for hname in ("Horn_UL", "Horn_LL", "Horn_UR", "Horn_LR", "Horn_Pitch"):
        link_obj(hname, mesh_horn.copy(), col_links, [mats["carbon"], mats["titanium"]], smooth=False)

    # 2) Reusable Unit-Z Machined Titanium Pushrod Mesh (local Z in [0..1])
    bm_r = bmesh.new()
    add_cylinder(bm_r, (0.0, 0.0, 0.50), 0.022, 0.94, segments=20, axis="Z", mat_idx=0)
    # Machined anodized clevis end collars at both ends
    add_cylinder(bm_r, (0.0, 0.0, 0.07), 0.034, 0.08, segments=16, axis="Z", mat_idx=1)
    add_cylinder(bm_r, (0.0, 0.0, 0.93), 0.034, 0.08, segments=16, axis="Z", mat_idx=1)
    mesh_rod = bpy.data.meshes.new("SR6_Pushrod_Mesh")
    bm_r.to_mesh(mesh_rod)
    bm_r.free()

    for rname in ("Rod_UL", "Rod_LL", "Rod_UR", "Rod_LR", "Rod_Pitch"):
        link_obj(rname, mesh_rod.copy(), col_links, [mats["titanium"], mats["cnc_dark"]], smooth=True)

    # 3) Machined Brass Spherical Rod-End Ball Joints
    bm_j = bmesh.new()
    add_uv_sphere(bm_j, (0.0, 0.0, 0.0), 0.050, segs=20, rings=12, mat_idx=0)
    mesh_joint = bpy.data.meshes.new("SR6_Joint_Mesh")
    bm_j.to_mesh(mesh_joint)
    bm_j.free()

    for jname in ("Joint_Horn_UL", "Joint_Horn_LL", "Joint_Horn_UR", "Joint_Horn_LR", "Joint_Horn_Pitch",
                  "Joint_Gimbal_L", "Joint_Gimbal_R", "Joint_Pitch_Lug"):
        link_obj(jname, mesh_joint.copy(), col_links, mats["gold"], smooth=True)

    # 4) Dual-Axis CNC Gimbal Cradle (connects Joint_Gimbal_L at +X=0.728, Joint_Gimbal_R at -X=0.728,
    #    and Rear Pitch Lug at (0, +0.58, +0.36) in local gimbal space!)
    bm_g = bmesh.new()
    # Main CNC outer gimbal ring around the rotating sleeve (radius = 0.44)
    add_torus(bm_g, (0.0, 0.0, 0.0), major_r=0.45, minor_r=0.058, major_seg=48, minor_seg=16, mat_idx=0)
    # Upper bearing collar ring (6th Servo R0 housing)
    add_torus(bm_g, (0.0, 0.0, 0.36), major_r=0.42, minor_r=0.038, major_seg=44, minor_seg=14, mat_idx=1)
    # Cyan LED indicator halo on top bearing collar
    add_torus(bm_g, (0.0, 0.0, 0.40), major_r=0.43, minor_r=0.012, major_seg=48, minor_seg=10, mat_idx=2)
    # Left & Right trunnion yoke arms connecting outer ring (+/-0.45) to exact pushrod pivots (+/-0.728)
    for side in (-1.0, 1.0):
        add_cylinder(bm_g, (side * 0.59, 0.0, 0.0), 0.042, 0.29, segments=20, axis="X", mat_idx=1)
        # Vertical side structural struts linking lower ring (Z=0) to upper bearing collar (Z=0.36)
        add_cylinder(bm_g, (side * 0.44, 0.0, 0.18), 0.028, 0.36, segments=16, axis="Z", mat_idx=1)
    # Rear pitch clevis bracket extending cleanly to exact pitch lug (0, +0.58, +0.36)!
    add_beveled_box(bm_g, (0.0, 0.49, 0.18), (0.085, 0.22, 0.36), bevel=0.014, mat_idx=0)
    add_cylinder(bm_g, (0.0, 0.58, 0.36), 0.040, 0.12, segments=16, axis="X", mat_idx=1)
    # 6th Servo (R0 Twist Drive Motor) neatly pod-mounted on rear-side of upper bearing collar
    add_beveled_box(bm_g, (0.0, 0.50, 0.0), (0.20, 0.22, 0.18), bevel=0.014, mat_idx=0)

    mesh_gimbal = bpy.data.meshes.new("Gimbal_Cradle_Mesh")
    bm_g.to_mesh(mesh_gimbal)
    bm_g.free()
    link_obj("Gimbal_Cradle", mesh_gimbal, col_carriage, [mats["cnc_dark"], mats["titanium"], mats["cyan_led"]], smooth=True)

    # 5) Sleek Industrial Rotating Cylindrical Actuator Core (100% clean non-sensitive robotics aesthetic!)
    #    Includes twin raised 3D helical ribbons (cyan + gold) so R0 twist is super crisp & readable!
    bm_s = bmesh.new()
    # Core anodized cylinder (height = 1.50 -> Z in [-0.75..+0.75], radius = 0.33)
    add_cylinder(bm_s, (0.0, 0.0, 0.0), 0.33, 1.48, segments=48, axis="Z", mat_idx=0)
    # Precision titanium top & bottom chamfer caps
    add_cylinder(bm_s, (0.0, 0.0, 0.74), 0.355, 0.08, segments=48, axis="Z", mat_idx=1)
    add_cylinder(bm_s, (0.0, 0.0, -0.74), 0.345, 0.08, segments=48, axis="Z", mat_idx=1)
    # Glowing cyan optical crown ring at tip (Z = +0.78, matches tip_pos!)
    add_torus(bm_s, (0.0, 0.0, 0.785), major_r=0.26, minor_r=0.020, major_seg=44, minor_seg=12, mat_idx=2)
    # Glowing magenta ring at base (Z = -0.78, matches base_pos!)
    add_torus(bm_s, (0.0, 0.0, -0.785), major_r=0.25, minor_r=0.016, major_seg=44, minor_seg=10, mat_idx=4)
    # Twin 3D helical twist-indicator ribbons wrapped around cylinder body!
    add_helical_ribbon(bm_s, radius=0.33, z_min=-0.64, z_max=0.64, turns=1.0, width_z=0.052, thickness=0.018, phase_offset=0.0, mat_idx=2)
    add_helical_ribbon(bm_s, radius=0.33, z_min=-0.64, z_max=0.64, turns=1.0, width_z=0.045, thickness=0.016, phase_offset=math.pi, mat_idx=3)

    mesh_sleeve = bpy.data.meshes.new("Actuator_Sleeve_Mesh")
    bm_s.to_mesh(mesh_sleeve)
    bm_s.free()
    link_obj(
        "Actuator_Sleeve", mesh_sleeve, col_carriage,
        [mats["sleeve"], mats["titanium"], mats["cyan_led"], mats["gold"], mats["magenta_led"]],
        smooth=True,
    )


def build_reference_cube_pillar(col_ref, mats: dict):
    """Builds the clean non-sensitive Reference Cube + Synchronized Pillar next to the SR6 (at X = +2.85)."""
    neutral = sr6.solve_sr6_pose(0.5, 0.5, 0.5, 0.5, 0.5, 0.5)
    cx = neutral["carrier_pos"][0] + 2.85
    cy = neutral["carrier_pos"][1]
    cz = neutral["carrier_pos"][2]

    # Clean CNC plinth base under the reference cube
    bm_b = bmesh.new()
    add_beveled_box(bm_b, (cx, cy, 0.075), (1.85, 1.85, 0.15), bevel=0.022, mat_idx=0)
    mesh_b = bpy.data.meshes.new("Ref_Base_Mesh")
    bm_b.to_mesh(mesh_b)
    bm_b.free()
    link_obj("Ref_Base", mesh_b, col_ref, mats["cnc_dark"], smooth=False)

    # Translucent clean glass cube (zero EEVEE transmission noise!)
    bm_c = bmesh.new()
    add_beveled_box(bm_c, (cx, cy, cz), (1.65, 1.65, 2.25), bevel=0.02, mat_idx=0)
    mesh_c = bpy.data.meshes.new("Ref_Cube_Glass_Mesh")
    bm_c.to_mesh(mesh_c)
    bm_c.free()
    link_obj("Ref_Cube_Glass", mesh_c, col_ref, mats["ref_glass"], smooth=False)

    # Crisp glowing wireframe edge cage around the cube
    bm_w = bmesh.new()
    hx, hy, hz = 0.83, 0.83, 1.13
    t = 0.018
    for sx in (-hx, hx):
        for sy in (-hy, hy):
            add_beveled_box(bm_w, (cx + sx, cy + sy, cz), (t, t, hz * 2.0), bevel=0.0, mat_idx=0)
    for sx in (-hx, hx):
        for sz in (-hz, hz):
            add_beveled_box(bm_w, (cx + sx, cy, cz + sz), (t, hy * 2.0, t), bevel=0.0, mat_idx=0)
    for sy in (-hy, hy):
        for sz in (-hz, hz):
            add_beveled_box(bm_w, (cx, cy + sy, cz + sz), (hx * 2.0, t, t), bevel=0.0, mat_idx=0)
    mesh_w = bpy.data.meshes.new("Ref_Cube_Cage_Mesh")
    bm_w.to_mesh(mesh_w)
    bm_w.free()
    link_obj("Ref_Cube_Cage", mesh_w, col_ref, mats["ref_frame"], smooth=False)

    # Synchronized Pillar inside the Reference Cube (driven 1:1 with Actuator_Sleeve!)
    bm_p = bmesh.new()
    add_cylinder(bm_p, (0.0, 0.0, 0.0), 0.24, 1.38, segments=36, axis="Z", mat_idx=0)
    add_helical_ribbon(bm_p, radius=0.24, z_min=-0.60, z_max=0.60, turns=1.0, width_z=0.055, thickness=0.016, phase_offset=0.0, mat_idx=1)
    add_torus(bm_p, (0.0, 0.0, 0.70), major_r=0.22, minor_r=0.020, major_seg=36, minor_seg=10, mat_idx=1)
    mesh_p = bpy.data.meshes.new("Ref_Sync_Pillar_Mesh")
    bm_p.to_mesh(mesh_p)
    bm_p.free()
    link_obj("Ref_Sync_Pillar", mesh_p, col_ref, [mats["titanium"], mats["cyan_led"]], smooth=True)


def add_aimed_camera(name: str, loc: tuple, target: tuple, lens: float, col):
    cam_data = bpy.data.cameras.new(name + "_Data")
    cam_data.lens = lens
    cam_data.clip_start = 0.05
    cam_data.clip_end = 100.0
    cam_obj = bpy.data.objects.new(name, cam_data)
    col.objects.link(cam_obj)
    cam_obj.location = Vector(loc)
    direction = Vector(target) - Vector(loc)
    cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    return cam_obj


def add_area_light(name: str, loc: tuple, target: tuple, energy: float, size: float, color: tuple, col):
    ldata = bpy.data.lights.new(name + "_Data", type="AREA")
    ldata.energy = energy
    ldata.size = size
    ldata.color = color
    lobj = bpy.data.objects.new(name, ldata)
    col.objects.link(lobj)
    lobj.location = Vector(loc)
    direction = Vector(target) - Vector(loc)
    lobj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    return lobj


def setup_lighting_cameras_and_render(col_env):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.fps = 60

    # Deep cyber-studio dark world background (clean contrast for metallic highlights & glowing trails!)
    world = bpy.data.worlds.new("KoiSync_Cyber_World")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.012, 0.018, 0.028, 1.0)
        bg.inputs["Strength"].default_value = 0.75

    # AgX / Filmic tone mapping
    try:
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        scene.view_settings.view_transform = "Filmic"
    scene.view_settings.exposure = 0.35

    # 3-Point Dramatic Hardware Commercial Lighting
    focus = (0.0, -0.25, 1.55)
    add_area_light("Light_Key_Cool", (-3.2, -3.6, 3.8), focus, energy=360.0, size=2.4, color=(0.82, 0.94, 1.0), col=col_env)
    add_area_light("Light_Rim_Magenta", (3.6, 2.2, 3.1), focus, energy=260.0, size=1.8, color=(1.0, 0.52, 0.88), col=col_env)
    add_area_light("Light_Fill_Cyan", (2.8, -3.4, 1.8), focus, energy=140.0, size=2.2, color=(0.45, 0.88, 1.0), col=col_env)
    add_area_light("Light_Top_Spec", (0.0, 0.0, 5.2), focus, energy=180.0, size=2.8, color=(0.95, 0.97, 1.0), col=col_env)

    # 4 Ready-to-Use Video Cameras (generous headroom across full L0 stroke + full cube framing!)
    cam_hero = add_aimed_camera("Cam_Hero_3Quarter", (-4.15, -5.05, 3.35), (0.0, 0.05, 1.52), lens=42.0, col=col_env)
    add_aimed_camera("Cam_Front_Split", (0.0, -5.85, 2.05), (0.0, 0.0, 1.55), lens=44.0, col=col_env)
    add_aimed_camera("Cam_Top_Trail", (-2.45, -4.15, 4.95), (0.0, -0.20, 1.65), lens=38.0, col=col_env)
    add_aimed_camera("Cam_Dual_Compare", (1.42, -7.85, 2.75), (1.42, -0.15, 1.52), lens=40.0, col=col_env)
    scene.camera = cam_hero


def embed_live_script():
    """Embeds live_koisync_addon.py directly inside the .blend Text Editor with register=True!"""
    src_path = HERE / "live_koisync_addon.py"
    if not src_path.is_file():
        return
    txt = bpy.data.texts.new("KoiSync_Live_Panel.py")
    txt.write(src_path.read_text(encoding="utf-8"))
    txt.use_module = True


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def build_master_and_render():
    reset_scene()
    mats = create_all_materials()

    col_chassis = make_collection("01_Chassis_Static")
    col_links = make_collection("02_Servos_And_Linkages")
    col_carriage = make_collection("03_Actuator_Carriage")
    col_trails = make_collection("04_Neon_Trails")
    col_ref = make_collection("05_Ref_Cube_Pillar")
    col_env = make_collection("06_Studio_Lights_Cameras")

    build_chassis_and_studio(col_chassis, mats)
    build_moving_linkages(col_links, col_carriage, mats)
    build_reference_cube_pillar(col_ref, mats)
    setup_lighting_cameras_and_render(col_env)
    embed_live_script()

    # Default: hide Ref Cube+Pillar in Hero single-arm mode (1-click toggleable in N-Panel!)
    col_ref.hide_viewport = True
    col_ref.hide_render = True

    previews_dir = HERE / "previews"
    previews_dir.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene

    # 1) Build & save SR6_Act1_6DOF_Explainer.blend (12s 6-Axis Explainer)
    print("[BUILD] Baking Act 1 6-DOF Sequential Explainer (12s @ 60fps)...", flush=True)
    frames_act1 = sr6.generate_showcase_sequence("act1_6dof_intro", duration_s=12.0, fps=60)
    live.bake_sequence_to_scene(frames_act1, fps=60, build_trails=False)
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "SR6_Act1_6DOF_Explainer.blend"))

    # 2) Build & save SR6_Showcase_Master.blend (16s 5-Math-Manifold Reel + Laser-Comet Neon Trails!)
    print("[BUILD] Baking Master 5-Math-Manifold Showcase Reel (16s @ 60fps + Neon Trails)...", flush=True)
    frames_reel = sr6.generate_showcase_sequence("reel_all", duration_s=16.0, fps=60)
    live.bake_sequence_to_scene(frames_reel, fps=60, build_trails=True, trail_stride=2, comet_ratio=0.16)

    # Render Preview 1: Hero 3/4 view (during VariablePitchHelix / VirtualPivotWave dynamic tilt!)
    scene.camera = bpy.data.objects["Cam_Hero_3Quarter"]
    scene.frame_set(240)
    scene.render.filepath = str(previews_dir / "01_hero_3quarter.png")
    bpy.ops.render.render(write_still=True)

    # Render Preview 2: Clean Front Split-Screen view (for side-by-side with Koikatsu/Studio!)
    scene.camera = bpy.data.objects["Cam_Front_Split"]
    scene.frame_set(450)
    scene.render.filepath = str(previews_dir / "02_front_split.png")
    bpy.ops.render.render(write_still=True)

    # Render Preview 3: Elevated Top-Angle highlighting pure 6D Lissajous Laser-Comet Trail!
    frames_liss = sr6.generate_showcase_sequence("lissajous_6d", duration_s=8.0, fps=60)
    live.bake_sequence_to_scene(frames_liss, fps=60, build_trails=True, trail_stride=1, comet_ratio=0.45)
    scene.camera = bpy.data.objects["Cam_Top_Trail"]
    scene.frame_set(300)
    scene.render.filepath = str(previews_dir / "03_top_lissajous_trail.png")
    bpy.ops.render.render(write_still=True)

    # Render Preview 4: Dual Compare (SR6 6-DOF Arm on Left + Translucent Reference Cube & Pillar on Right!)
    col_ref.hide_viewport = False
    col_ref.hide_render = False
    col_trails.hide_viewport = True
    col_trails.hide_render = True
    scene.camera = bpy.data.objects["Cam_Dual_Compare"]
    scene.frame_set(210)
    scene.render.filepath = str(previews_dir / "04_dual_cube_pillar.png")
    bpy.ops.render.render(write_still=True)

    # Restore Master scene to clean Hero view with 16s 5-Math-Manifold Reel + Laser-Comet Neon Trails ready!
    col_ref.hide_viewport = True
    col_ref.hide_render = True
    col_trails.hide_viewport = False
    col_trails.hide_render = False
    live.bake_sequence_to_scene(frames_reel, fps=60, build_trails=True, trail_stride=2, comet_ratio=0.18)
    scene.camera = bpy.data.objects["Cam_Hero_3Quarter"]
    scene.frame_set(240)
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "SR6_Showcase_Master.blend"))
    print("[BUILD] ALL DONE! Saved polished SR6_Showcase_Master.blend + SR6_Act1_6DOF_Explainer.blend + 4 previews.", flush=True)


if __name__ == "__main__":
    build_master_and_render()
