#!/usr/bin/env python3
"""
Blender 5.2 Procedural Industrial CAD Builder for the Concept-A "VEX-6" Single-Arm 6-Axis Robot
with Multi-Segmented Open Ring-Claw Gripper and Fictional Star-Hexagon "VEX-6" Emblem!

100% platform-safe industrial robotic arm aesthetic (matte ceramic white + titanium gray +
safety-orange accent rings + cyan wrist LED + embossed fictional VEX-6 logo).

Run via:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --python build_vex6_scene.py
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
import vex6_kinematics as vex6
import vex6_live_panel as panel


# ---------------------------------------------------------------------------
# Concept-A Ceramic-White Industrial Robotics Materials
# ---------------------------------------------------------------------------

def make_mat(name: str, color, metallic: float = 0.0, roughness: float = 0.3,
             emission_color=None, emission_strength: float = 0.0):
    mat = bpy.data.materials.new(name=name)
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color[:3], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission_color is not None and emission_strength > 0.0:
        bsdf.inputs["Emission Color"].default_value = (*emission_color[:3], 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    return mat


def create_vex6_materials() -> dict:
    return {
        "white": make_mat("Mat_Ceramic_White", (0.86, 0.87, 0.88), metallic=0.04, roughness=0.25),
        "titanium": make_mat("Mat_Titanium_Silver", (0.48, 0.51, 0.55), metallic=0.88, roughness=0.24),
        "gunmetal": make_mat("Mat_Dark_Gunmetal", (0.09, 0.10, 0.12), metallic=0.76, roughness=0.32),
        "rubber": make_mat("Mat_Rubber_Pad", (0.045, 0.048, 0.055), metallic=0.0, roughness=0.78),
        "orange": make_mat("Mat_Safety_Orange", (0.92, 0.38, 0.05), metallic=0.28, roughness=0.26),
        "cyan_led": make_mat("Mat_Cyan_LED", (0.04, 0.84, 0.96), metallic=0.0, roughness=0.14,
                             emission_color=(0.04, 0.88, 1.0), emission_strength=4.5),
        "trail_cyan": make_mat("Mat_Trail_Cyan", (0.02, 0.80, 1.0), metallic=0.0, roughness=0.10,
                               emission_color=(0.02, 0.85, 1.0), emission_strength=3.8),
        "logo": make_mat("Mat_Logo_Dark", (0.04, 0.045, 0.055), metallic=0.18, roughness=0.30),
        "floor": make_mat("Mat_Lab_Floor", (0.68, 0.71, 0.75), metallic=0.08, roughness=0.36),
        "grid": make_mat("Mat_Lab_Grid", (0.42, 0.46, 0.52), metallic=0.0, roughness=0.30,
                         emission_color=(0.05, 0.56, 0.72), emission_strength=0.0),
    }


# ---------------------------------------------------------------------------
# Clean BMesh CAD Primitives
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


def add_box(bm, center, size, bevel=0.018, mat_idx=0, rot_euler=None):
    ret = bmesh.ops.create_cube(bm, size=1.0)
    verts = ret["verts"]
    sx, sy, sz = size
    cx, cy, cz = center
    rot = Matrix.Identity(4)
    if rot_euler is not None:
        rot = (
            Matrix.Rotation(rot_euler[2], 4, "Z")
            @ Matrix.Rotation(rot_euler[1], 4, "Y")
            @ Matrix.Rotation(rot_euler[0], 4, "X")
        )
    for v in verts:
        p = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz))
        p = rot @ p
        v.co.x = p.x + cx
        v.co.y = p.y + cy
        v.co.z = p.z + cz
    edges = list({e for v in verts for e in v.link_edges})
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mat_idx
    if bevel > 0.0:
        res = bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=2, affect="EDGES", clamp_overlap=True)
        for f in res.get("faces", []):
            f.material_index = mat_idx


def add_cyl(bm, center, r1, depth, r2=None, segs=32, axis="Z", mat_idx=0):
    if r2 is None:
        r2 = r1
    ret = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r1, radius2=r2, depth=depth)
    rot = Matrix.Identity(4)
    if axis == "X":
        rot = Matrix.Rotation(math.pi * 0.5, 4, "Y")
    elif axis == "Y":
        rot = Matrix.Rotation(math.pi * 0.5, 4, "X")
    cx, cy, cz = center
    for v in ret["verts"]:
        p = rot @ v.co
        v.co.x = p.x + cx
        v.co.y = p.y + cy
        v.co.z = p.z + cz
    for f in {f for v in ret["verts"] for f in v.link_faces}:
        f.material_index = mat_idx


def add_torus(bm, center, major_r, minor_r, maj_seg=40, min_seg=14, axis="Z", mat_idx=0):
    rot = Matrix.Identity(4)
    if axis == "X":
        rot = Matrix.Rotation(math.pi * 0.5, 4, "Y")
    elif axis == "Y":
        rot = Matrix.Rotation(math.pi * 0.5, 4, "X")
    cx, cy, cz = center
    rings = []
    for i in range(maj_seg):
        th = 2.0 * math.pi * i / maj_seg
        ct, st = math.cos(th), math.sin(th)
        ring = []
        for j in range(min_seg):
            ph = 2.0 * math.pi * j / min_seg
            r = major_r + minor_r * math.cos(ph)
            p = rot @ Vector((r * ct, r * st, minor_r * math.sin(ph)))
            ring.append(bm.verts.new((p.x + cx, p.y + cy, p.z + cz)))
        rings.append(ring)
    for i in range(maj_seg):
        r0, r1 = rings[i], rings[(i + 1) % maj_seg]
        for j in range(min_seg):
            j1 = (j + 1) % min_seg
            f = bm.faces.new((r0[j], r1[j], r1[j1], r0[j1]))
            f.material_index = mat_idx


def add_curved_jaw_arc(bm, r_inner, r_outer, z_half, ang_start_deg, ang_end_deg, steps=16, mat_idx=0, z_center=0.0):
    """Builds a solid curved industrial arc segment in the XY plane around (0, 0, z_center)."""
    rings = []
    for i in range(steps + 1):
        u = i / float(steps)
        ang = math.radians(ang_start_deg + u * (ang_end_deg - ang_start_deg))
        ca, sa = math.cos(ang), math.sin(ang)
        v0 = bm.verts.new((r_inner * ca, r_inner * sa, z_center - z_half))
        v1 = bm.verts.new((r_outer * ca, r_outer * sa, z_center - z_half))
        v2 = bm.verts.new((r_outer * ca, r_outer * sa, z_center + z_half))
        v3 = bm.verts.new((r_inner * ca, r_inner * sa, z_center + z_half))
        rings.append((v0, v1, v2, v3))
    for i in range(steps):
        a, b = rings[i], rings[i + 1]
        for k in range(4):
            k1 = (k + 1) % 4
            f = bm.faces.new((a[k], b[k], b[k1], a[k1]))
            f.material_index = mat_idx
    # Cap both ends
    f0 = bm.faces.new((rings[0][3], rings[0][2], rings[0][1], rings[0][0]))
    f1 = bm.faces.new((rings[-1][0], rings[-1][1], rings[-1][2], rings[-1][3]))
    f0.material_index = mat_idx
    f1.material_index = mat_idx


def add_vex6_star_hex_logo(bm, origin, x_axis, y_axis, normal_axis, scale=0.14, depth=0.008, mat_idx=0):
    """
    Constructs the exact fictional Concept-A 'VEX-6' geometric emblem in crisp 3D relief:
      - Interlocking sharp 4-point star + broken hexagon outer frame (zero real-world brand resemblance!)
    """
    ox, oy, oz = origin
    ux, uy, uz = x_axis
    vx, vy, vz = y_axis
    nx, ny, nz = normal_axis

    def to_w(px, py, pz):
        return (
            ox + px * ux + py * vx + pz * nx,
            oy + px * uy + py * vy + pz * ny,
            oz + px * uz + py * vz + pz * nz,
        )

    def add_bar(p0, p1, half_w):
        dx, dy = p1[0] - p0[0], p1[1] - p0[1]
        l = math.hypot(dx, dy)
        if l < 1e-6:
            return
        tx, ty = -dy / l * half_w, dx / l * half_w
        pts2 = [(p0[0] - tx, p0[1] - ty), (p1[0] - tx, p1[1] - ty), (p1[0] + tx, p1[1] + ty), (p0[0] + tx, p0[1] + ty)]
        bot = [bm.verts.new(to_w(a, b, 0.0)) for a, b in pts2]
        top = [bm.verts.new(to_w(a, b, depth)) for a, b in pts2]
        f_top = bm.faces.new(top)
        f_top.material_index = mat_idx
        for k in range(4):
            k1 = (k + 1) % 4
            fs = bm.faces.new((bot[k], bot[k1], top[k1], top[k]))
            fs.material_index = mat_idx

    # 1) 4-point star (shifted slightly left like Concept A)
    star_cx = -0.18 * scale
    r_out = 0.72 * scale
    r_in = 0.18 * scale
    star_pts = []
    for i in range(8):
        ang = i * (math.pi / 4.0)
        r = r_out if (i % 2 == 0) else r_in
        star_pts.append((star_cx + r * math.cos(ang), r * math.sin(ang)))
    bot_s = [bm.verts.new(to_w(a, b, 0.0)) for a, b in star_pts]
    top_s = [bm.verts.new(to_w(a, b, depth)) for a, b in star_pts]
    f_st = bm.faces.new(top_s)
    f_st.material_index = mat_idx
    for k in range(8):
        k1 = (k + 1) % 8
        fs = bm.faces.new((bot_s[k], bot_s[k1], top_s[k1], top_s[k]))
        fs.material_index = mat_idx

    # 2) Interlocking broken hexagon on the right side (4 connected hex edges)
    hx_c = +0.18 * scale
    hr = 0.66 * scale
    hex_verts = [(hx_c + hr * math.cos(math.radians(a)), hr * math.sin(math.radians(a))) for a in (120, 60, 0, -60, -120)]
    for i in range(len(hex_verts) - 1):
        add_bar(hex_verts[i], hex_verts[i + 1], half_w=0.075 * scale)


def attach_3d_stencil_text(parent_obj, name: str, text_str: str, local_pos, local_euler, size: float, mat, col):
    """Attaches razor-sharp 3D extruded stencil lettering ('VEX-6') directly parented onto the arm armor!"""
    fcurve = bpy.data.curves.new(name + "_Font", type="FONT")
    fcurve.body = text_str
    fcurve.align_x = "CENTER"
    fcurve.align_y = "CENTER"
    fcurve.size = size
    fcurve.extrude = 0.004
    fcurve.space_character = 1.12
    tobj = bpy.data.objects.new(name, fcurve)
    col.objects.link(tobj)
    tobj.data.materials.append(mat)
    tobj.parent = parent_obj
    tobj.location = Vector(local_pos)
    tobj.rotation_euler = local_euler
    return tobj


# ---------------------------------------------------------------------------
# Build All VEX-6 Robot Assemblies (1:1 Faithful to Concept A!)
# ---------------------------------------------------------------------------

def build_studio_floor(col_env, mats):
    bm_fl = bmesh.new()
    bmesh.ops.create_grid(bm_fl, x_segments=2, y_segments=2, size=18.0)
    m_fl = bpy.data.meshes.new("Lab_Floor_Mesh")
    bm_fl.to_mesh(m_fl)
    bm_fl.free()
    link_obj("Lab_Floor", m_fl, col_env, mats["floor"], smooth=False)

    bm_gr = bmesh.new()
    for i in range(-6, 7):
        pos = i * 0.9
        add_box(bm_gr, (pos, 0.0, 0.002), (0.007, 10.8, 0.002), bevel=0.0, mat_idx=0)
        add_box(bm_gr, (0.0, pos, 0.002), (10.8, 0.007, 0.002), bevel=0.0, mat_idx=0)
    m_gr = bpy.data.meshes.new("Lab_Grid_Mesh")
    bm_gr.to_mesh(m_gr)
    bm_gr.free()
    link_obj("Lab_Grid", m_gr, col_env, mats["grid"], smooth=False)


def build_vex6_robot(col_static, col_arm, mats):
    # Shared multi-material palette index map for VEX-6:
    # 0=white, 1=titanium, 2=gunmetal, 3=orange, 4=cyan_led, 5=rubber, 6=logo
    pal = [mats["white"], mats["titanium"], mats["gunmetal"], mats["orange"], mats["cyan_led"], mats["rubber"], mats["logo"]]

    # 1) Static Tiered Circular Robotics Base Pedestal at BASE_POS = (0, +1.05, 0)
    bx, by, bz = vex6.BASE_POS
    bm = bmesh.new()
    # Heavy dark circular flange ring on table
    add_cyl(bm, (bx, by, 0.055), 0.64, 0.11, r2=0.60, segs=48, mat_idx=2)
    # 4 radial bolted mounting ears around flange
    for ang_deg in (45, 135, 225, 315):
        a = math.radians(ang_deg)
        ex, ey = bx + 0.62 * math.cos(a), by + 0.62 * math.sin(a)
        add_cyl(bm, (ex, ey, 0.06), 0.11, 0.12, segs=20, mat_idx=1)
        add_cyl(bm, (ex, ey, 0.13), 0.042, 0.04, segs=6, mat_idx=2)
    # Ceramic-white lower pedestal drum
    add_cyl(bm, (bx, by, 0.25), 0.49, 0.28, r2=0.47, segs=48, mat_idx=0)
    # Signature safety-orange seam ring on pedestal
    add_torus(bm, (bx, by, 0.38), 0.475, 0.015, maj_seg=48, min_seg=10, mat_idx=3)
    # Titanium upper rotary bearing race
    add_cyl(bm, (bx, by, 0.42), 0.46, 0.06, segs=48, mat_idx=1)
    # Rear industrial I/O & servo cable junction box (+Y side)
    add_box(bm, (bx, by + 0.48, 0.26), (0.38, 0.26, 0.36), bevel=0.022, mat_idx=0)
    add_box(bm, (bx, by + 0.61, 0.24), (0.26, 0.04, 0.22), bevel=0.008, mat_idx=2)
    m_base = bpy.data.meshes.new("VEX6_Base_Mesh")
    bm.to_mesh(m_base)
    bm.free()
    link_obj("VEX6_Base_Static", m_base, col_static, pal, smooth=True)

    # 2) J1 Turret Housing (local origin at (0, 1.05, 0.44); shoulder pivot is at local (0, -0.14, +0.42))
    bm = bmesh.new()
    add_cyl(bm, (0.0, 0.0, 0.08), 0.42, 0.16, r2=0.38, segs=44, mat_idx=1)
    add_box(bm, (0.0, 0.04, 0.26), (0.50, 0.46, 0.32), bevel=0.032, mat_idx=0)
    # Twin side shoulder yoke towers embracing J2 drum at (0, -0.14, 0.42)
    for side in (-1.0, 1.0):
        add_box(bm, (side * 0.23, -0.06, 0.34), (0.12, 0.38, 0.36), bevel=0.020, mat_idx=0)
    # Large exposed J2 harmonic-drive bearing drum along X
    add_cyl(bm, (0.0, -vex6.SHOULDER_FWD, vex6.SHOULDER_Z - 0.44), 0.23, 0.56, segs=36, axis="X", mat_idx=1)
    add_cyl(bm, (0.0, -vex6.SHOULDER_FWD, vex6.SHOULDER_Z - 0.44), 0.15, 0.60, segs=32, axis="X", mat_idx=2)
    # Orange shoulder drum accent ring
    add_torus(bm, (0.18, -vex6.SHOULDER_FWD, vex6.SHOULDER_Z - 0.44), 0.232, 0.015, maj_seg=36, min_seg=10, axis="X", mat_idx=3)
    add_torus(bm, (-0.18, -vex6.SHOULDER_FWD, vex6.SHOULDER_Z - 0.44), 0.232, 0.015, maj_seg=36, min_seg=10, axis="X", mat_idx=3)
    m_tur = bpy.data.meshes.new("VEX6_Turret_Mesh")
    bm.to_mesh(m_tur)
    bm.free()
    link_obj("VEX6_J1_Turret", m_tur, col_arm, pal, smooth=True)

    # 3) J2 Upper Arm (local origin at shoulder, extends along +Z from 0 to L_UPPER = 1.48)
    bm = bmesh.new()
    L1 = vex6.L_UPPER
    # Ceramic-white sculpted main upper-arm housing
    add_box(bm, (0.0, 0.0, L1 * 0.5), (0.34, 0.38, L1 + 0.12), bevel=0.048, mat_idx=0)
    # Rear dark-titanium structural rib + lower side mechanical vent accents (keeps white panel clean behind VEX-6 logo!)
    add_box(bm, (0.0, 0.175, L1 * 0.50), (0.20, 0.05, L1 * 0.72), bevel=0.012, mat_idx=1)
    for side in (-1.0, 1.0):
        add_box(bm, (side * 0.165, 0.09, L1 * 0.28), (0.024, 0.11, L1 * 0.24), bevel=0.008, mat_idx=1)
        add_box(bm, (side * 0.166, -0.10, L1 * 0.28), (0.020, 0.04, L1 * 0.16), bevel=0.005, mat_idx=3)
    # Concentric exposed bearing caps at Shoulder (Z=0) & Elbow (Z=L1)
    for z_j in (0.0, L1):
        add_cyl(bm, (0.0, 0.0, z_j), 0.21, 0.42, segs=36, axis="X", mat_idx=1)
        add_cyl(bm, (0.0, 0.0, z_j), 0.145, 0.45, segs=32, axis="X", mat_idx=2)
        add_torus(bm, (0.215, 0.0, z_j), 0.11, 0.018, maj_seg=28, min_seg=10, axis="X", mat_idx=1)
        add_torus(bm, (-0.215, 0.0, z_j), 0.11, 0.018, maj_seg=28, min_seg=10, axis="X", mat_idx=1)
    # Crisp 3D-embossed VEX-6 Star+Hexagon emblem on clean ceramic-white BOTH left (-X) and right (+X) sides!
    add_vex6_star_hex_logo(bm, (-0.174, 0.0, L1 * 0.66), (0, -1, 0), (0, 0, 1), (-1, 0, 0), scale=0.14, depth=0.008, mat_idx=6)
    add_vex6_star_hex_logo(bm, (+0.174, 0.0, L1 * 0.66), (0, +1, 0), (0, 0, 1), (+1, 0, 0), scale=0.14, depth=0.008, mat_idx=6)
    m_up = bpy.data.meshes.new("VEX6_UpperArm_Mesh")
    bm.to_mesh(m_up)
    bm.free()
    obj_up = link_obj("VEX6_J2_UpperArm", m_up, col_arm, pal, smooth=True)
    attach_3d_stencil_text(obj_up, "VEX6_Text_Upper_L", "VEX-6", (-0.176, 0.0, L1 * 0.49), (math.pi * 0.5, 0.0, -math.pi * 0.5), 0.086, mats["logo"], col_arm)
    attach_3d_stencil_text(obj_up, "VEX6_Text_Upper_R", "VEX-6", (+0.176, 0.0, L1 * 0.49), (math.pi * 0.5, 0.0, +math.pi * 0.5), 0.086, mats["logo"], col_arm)

    # 4) J3 Forearm Tube (local origin at elbow, extends along +Z from 0 to L_FORE * 0.36 = 0.547)
    L_t = vex6.L_FORE * 0.36
    bm = bmesh.new()
    # White rounded elbow shoulder shell
    add_cyl(bm, (0.0, 0.0, 0.08), 0.23, 0.28, r2=0.20, segs=36, axis="Z", mat_idx=0)
    # Signature safety-orange elbow collar band (Concept A exact match!)
    add_cyl(bm, (0.0, 0.0, 0.24), 0.206, 0.048, segs=36, axis="Z", mat_idx=3)
    # Brushed titanium cylindrical J4 drive barrel
    add_cyl(bm, (0.0, 0.0, 0.40), 0.185, 0.28, r2=0.175, segs=36, axis="Z", mat_idx=1)
    # Dark gunmetal rotary joint interface ring at Z = L_t
    add_cyl(bm, (0.0, 0.0, L_t), 0.195, 0.06, segs=36, axis="Z", mat_idx=2)
    m_ft = bpy.data.meshes.new("VEX6_ForearmTube_Mesh")
    bm.to_mesh(m_ft)
    bm.free()
    link_obj("VEX6_J3_ForearmTube", m_ft, col_arm, pal, smooth=True)

    # 5) J4 Forearm Rectangular Armor Module (origin at j4_pos, extends along +Z from 0 to L_FORE * 0.64 = 0.973)
    L_b = vex6.L_FORE * 0.64
    bm = bmesh.new()
    # J4 entry rotary knuckle
    add_cyl(bm, (0.0, 0.0, 0.06), 0.17, 0.28, segs=32, axis="X", mat_idx=2)
    # Signature sleek chamfered ceramic-white forearm armor block
    add_box(bm, (0.0, 0.0, L_b * 0.50), (0.31, 0.33, L_b * 0.72), bevel=0.045, mat_idx=0)
    # Top safety-orange engineering status slot (local +Y is UP on forearm -> sits crisply on top face like Concept A!)
    add_box(bm, (0.0, +0.166, L_b * 0.58), (0.036, 0.016, 0.18), bevel=0.004, mat_idx=3)
    # Twin side wrist-yoke ears reaching smoothly to wrist pivot Z = L_b
    for side in (-1.0, 1.0):
        add_box(bm, (side * 0.135, 0.0, L_b * 0.87), (0.065, 0.26, 0.26), bevel=0.018, mat_idx=0)
        add_cyl(bm, (side * 0.165, 0.0, L_b), 0.085, 0.03, segs=24, axis="X", mat_idx=1)
    # Recessed dark cavity between wrist yoke ears
    add_box(bm, (0.0, 0.0, L_b * 0.84), (0.20, 0.22, 0.16), bevel=0.012, mat_idx=2)
    # Crisp 3D-embossed VEX-6 emblem + horizontal text aligned along forearm (+Z = along arm, +Y = world UP!)
    add_vex6_star_hex_logo(bm, (-0.158, 0.0, L_b * 0.32), (0, 0, 1), (0, 1, 0), (-1, 0, 0), scale=0.105, depth=0.007, mat_idx=6)
    add_vex6_star_hex_logo(bm, (+0.158, 0.0, L_b * 0.64), (0, 0, -1), (0, 1, 0), (+1, 0, 0), scale=0.105, depth=0.007, mat_idx=6)
    m_fb = bpy.data.meshes.new("VEX6_ForearmBody_Mesh")
    bm.to_mesh(m_fb)
    bm.free()
    obj_fb = link_obj("VEX6_J4_ForearmBody", m_fb, col_arm, pal, smooth=True)
    attach_3d_stencil_text(obj_fb, "VEX6_Text_Fore_L", "VEX-6", (-0.160, 0.0, L_b * 0.56), (0.0, -math.pi * 0.5, 0.0), 0.095, mats["logo"], col_arm)
    attach_3d_stencil_text(obj_fb, "VEX6_Text_Fore_R", "VEX-6", (+0.160, 0.0, L_b * 0.40), (0.0, +math.pi * 0.5, 0.0), 0.095, mats["logo"], col_arm)

    # 6) J5 Wrist Pitch Clevis (origin at wrist_pos; local -Y points forward toward Ring-Claw!)
    bm = bmesh.new()
    add_cyl(bm, (0.0, 0.0, 0.0), 0.115, 0.21, segs=28, axis="X", mat_idx=2)
    add_cyl(bm, (0.0, -0.10, 0.0), 0.125, 0.18, r2=0.135, segs=32, axis="Y", mat_idx=1)
    m_wc = bpy.data.meshes.new("VEX6_WristClevis_Mesh")
    bm.to_mesh(m_wc)
    bm.free()
    link_obj("VEX6_J5_WristClevis", m_wc, col_arm, pal, smooth=True)

    # 7) J6 End-Effector: Multi-Segmented OPEN RING-SHAPED MECHANICAL CLAW GRIPPER!
    #    Origin is at the exact Ring Center TCP (0, 0, 0); wrist hub sits at +Y = +0.34..+0.44;
    #    Open jaw gap faces forward (-Y) so the ring silhouette is 100% open industrial robotic claw!
    bm = bmesh.new()
    # Rotary Wrist Hub + Glowing Cyan LED Calibration Ring (exact Concept A detail!)
    add_cyl(bm, (0.0, 0.42, 0.0), 0.145, 0.14, segs=36, axis="Y", mat_idx=1)
    add_torus(bm, (0.0, 0.44, 0.0), 0.148, 0.017, maj_seg=40, min_seg=12, axis="Y", mat_idx=4)
    add_torus(bm, (0.0, 0.36, 0.0), 0.135, 0.013, maj_seg=36, min_seg=10, axis="Y", mat_idx=4)
    # Ceramic-white dual-knuckle mounting yoke bridging wrist hub to the two curved claw arms
    add_box(bm, (0.0, 0.33, 0.0), (0.36, 0.14, 0.16), bevel=0.020, mat_idx=0)
    # Recessed circular optical sensor bore cleanly flush inside yoke (leaves open ring interior completely clear!)
    add_cyl(bm, (0.0, 0.355, 0.0), 0.070, 0.055, segs=28, axis="Y", mat_idx=2)

    # Twin Symmetrical Multi-Segmented Curved Ring-Claw Jaws (Right side: +64° -> -64°, Left side: +116° -> +244°!)
    # Angles in XY plane: +90° = +Y (back toward wrist), -90° = -Y (open front claw mouth!)
    for side in (-1.0, 1.0):
        # Knuckle hinge clevis pin at +/-X
        kx = side * 0.165
        add_cyl(bm, (kx, 0.33, 0.0), 0.056, 0.18, segs=24, axis="Z", mat_idx=1)
        add_cyl(bm, (kx, 0.33, 0.0), 0.028, 0.20, segs=16, axis="Z", mat_idx=2)

    # Right Jaw (2 articulated segments with exposed mid-knuckle pin!)
    # Segment 1 (proximal): +62° down to -2°
    add_curved_jaw_arc(bm, r_inner=0.335, r_outer=0.425, z_half=0.068, ang_start_deg=-2.0, ang_end_deg=62.0, steps=14, mat_idx=0)
    add_curved_jaw_arc(bm, r_inner=0.305, r_outer=0.340, z_half=0.058, ang_start_deg=2.0, ang_end_deg=56.0, steps=12, mat_idx=5)  # Rubber grip pad!
    # Segment 2 (distal finger): -8° down to -66° (leaves clean 48° open mouth at -90°!)
    add_curved_jaw_arc(bm, r_inner=0.335, r_outer=0.415, z_half=0.062, ang_start_deg=-66.0, ang_end_deg=-8.0, steps=14, mat_idx=0)
    add_curved_jaw_arc(bm, r_inner=0.305, r_outer=0.340, z_half=0.054, ang_start_deg=-62.0, ang_end_deg=-12.0, steps=12, mat_idx=5)
    # Mid-knuckle titanium hinge connecting Segment 1 & 2 on Right Jaw (~ -5 deg)
    mk_ang = math.radians(-5.0)
    add_cyl(bm, (0.38 * math.cos(mk_ang), 0.38 * math.sin(mk_ang), 0.0), 0.044, 0.15, segs=20, axis="Z", mat_idx=1)
    add_cyl(bm, (0.38 * math.cos(mk_ang), 0.38 * math.sin(mk_ang), 0.0), 0.022, 0.17, segs=14, axis="Z", mat_idx=2)
    # Distal claw tip cap (-66 deg)
    tip_r = math.radians(-66.0)
    add_cyl(bm, (0.365 * math.cos(tip_r), 0.365 * math.sin(tip_r), 0.0), 0.042, 0.125, segs=16, axis="Z", mat_idx=1)

    # Left Jaw (exact mirror: Segment 1: 118° -> 182°, Segment 2: 188° -> 246°)
    add_curved_jaw_arc(bm, r_inner=0.335, r_outer=0.425, z_half=0.068, ang_start_deg=118.0, ang_end_deg=182.0, steps=14, mat_idx=0)
    add_curved_jaw_arc(bm, r_inner=0.305, r_outer=0.340, z_half=0.058, ang_start_deg=124.0, ang_end_deg=178.0, steps=12, mat_idx=5)
    add_curved_jaw_arc(bm, r_inner=0.335, r_outer=0.415, z_half=0.062, ang_start_deg=188.0, ang_end_deg=246.0, steps=14, mat_idx=0)
    add_curved_jaw_arc(bm, r_inner=0.305, r_outer=0.340, z_half=0.054, ang_start_deg=192.0, ang_end_deg=242.0, steps=12, mat_idx=5)
    mk_l = math.radians(185.0)
    add_cyl(bm, (0.38 * math.cos(mk_l), 0.38 * math.sin(mk_l), 0.0), 0.044, 0.15, segs=20, axis="Z", mat_idx=1)
    add_cyl(bm, (0.38 * math.cos(mk_l), 0.38 * math.sin(mk_l), 0.0), 0.022, 0.17, segs=14, axis="Z", mat_idx=2)
    tip_l = math.radians(246.0)
    add_cyl(bm, (0.365 * math.cos(tip_l), 0.365 * math.sin(tip_l), 0.0), 0.042, 0.125, segs=16, axis="Z", mat_idx=1)

    m_claw = bpy.data.meshes.new("VEX6_RingClaw_Mesh")
    bm.to_mesh(m_claw)
    bm.free()
    link_obj("VEX6_J6_RingClaw", m_claw, col_arm, pal, smooth=True)

    # 8) Sleek Inner Cyan Optical Twist Indicator Arc (subtle precision R0 orientation marker inside the ring!)
    bm = bmesh.new()
    add_curved_jaw_arc(bm, r_inner=0.255, r_outer=0.270, z_half=0.008, ang_start_deg=-35.0, ang_end_deg=35.0, steps=12, mat_idx=4)
    add_curved_jaw_arc(bm, r_inner=0.255, r_outer=0.270, z_half=0.008, ang_start_deg=145.0, ang_end_deg=215.0, steps=12, mat_idx=4)
    add_box(bm, (0.0, -0.262, 0.0), (0.018, 0.048, 0.018), bevel=0.003, mat_idx=3)  # Orange forward pointer tick!
    m_ir = bpy.data.meshes.new("VEX6_InnerRing_Mesh")
    bm.to_mesh(m_ir)
    bm.free()
    link_obj("VEX6_Inner_Twist_Ring", m_ir, col_arm, pal, smooth=True)


def add_cam(name, loc, target, lens, col):
    cd = bpy.data.cameras.new(name + "_Data")
    cd.lens = lens
    cd.clip_start = 0.05
    cd.clip_end = 100.0
    co = bpy.data.objects.new(name, cd)
    col.objects.link(co)
    co.location = Vector(loc)
    co.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    return co


def add_light(name, loc, target, energy, size, color, col):
    ld = bpy.data.lights.new(name + "_Data", type="AREA")
    ld.energy = energy
    ld.size = size
    ld.color = color
    lo = bpy.data.objects.new(name, ld)
    col.objects.link(lo)
    lo.location = Vector(loc)
    lo.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    return lo


def setup_env(col_env):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.render.fps = 60

    world = bpy.data.worlds.new("VEX6_Lab_World")
    scene.world = world
    try:
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.look = "AgX - Medium High Contrast"
    except Exception:
        scene.view_settings.view_transform = "Filmic"
    scene.view_settings.exposure = 0.15

    # Clean Softbox Laboratory 3-Point Lighting
    focus = (0.0, 0.15, 1.35)
    add_light("Light_Lab_Key", (-3.8, -3.5, 4.4), focus, energy=420.0, size=3.2, color=(1.0, 0.98, 0.96), col=col_env)
    add_light("Light_Lab_Fill", (3.6, -3.2, 2.6), focus, energy=210.0, size=3.0, color=(0.86, 0.94, 1.0), col=col_env)
    add_light("Light_Lab_Rim", (2.6, 3.8, 3.6), focus, energy=260.0, size=2.4, color=(0.85, 0.96, 1.0), col=col_env)

    # 3 Crisp Cameras (matching Concept A's exact hero angle, close-up claw angle, & front split-screen!)
    cam_hero = add_cam("Cam_VEX6_Hero", (-4.65, -4.15, 3.05), (0.0, 0.10, 1.44), lens=38.0, col=col_env)
    add_cam("Cam_VEX6_Claw", (-1.62, -2.22, 2.02), (-0.02, -0.52, 1.42), lens=52.0, col=col_env)
    add_cam("Cam_VEX6_Front", (0.0, -5.25, 1.90), (0.0, 0.15, 1.38), lens=42.0, col=col_env)
    scene.camera = cam_hero


def embed_panel_script():
    src = HERE / "vex6_live_panel.py"
    if src.is_file():
        txt = bpy.data.texts.new("VEX6_Live_Panel.py")
        txt.write(src.read_text(encoding="utf-8"))
        txt.use_module = True


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = create_vex6_materials()

    col_static = make_collection("01_VEX6_Base")
    col_arm = make_collection("02_VEX6_Articulated_Arm")
    col_env = make_collection("03_Studio_Lights_Cameras")

    build_studio_floor(col_env, mats)
    build_vex6_robot(col_static, col_arm, mats)
    setup_env(col_env)
    embed_panel_script()

    previews = HERE / "vex6_previews"
    previews.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene

    # 1) Bake & save VEX6_Act1_6DOF_Explainer.blend (12s 6-axis explainer, clean bright lab look, no trails)
    panel.set_studio_theme(dark_mode=False)
    frames_act1 = sr6.generate_showcase_sequence("act1_6dof_intro", duration_s=12.0, fps=60)
    panel.bake_vex6_sequence(frames_act1, fps=60, build_trails=False)
    scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "VEX6_Act1_6DOF_Explainer.blend"))

    # 2) Bake & save VEX6_RobotArm_Master.blend (16s 5-Math-Manifold Reel, clean no trails, ready for live 41289 sync!)
    frames_reel = sr6.generate_showcase_sequence("reel_all", duration_s=16.0, fps=60)
    panel.bake_vex6_sequence(frames_reel, fps=60, build_trails=False)
    scene.camera = bpy.data.objects["Cam_VEX6_Hero"]
    scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "VEX6_RobotArm_Master.blend"))
    print("[VEX6 BUILD] Clean trail-free VEX6_RobotArm_Master.blend + VEX6_Act1_6DOF_Explainer.blend saved.", flush=True)


if __name__ == "__main__":
    main()
