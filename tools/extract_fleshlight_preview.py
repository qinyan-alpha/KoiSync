"""Extract the locally installed DragonHVR sleeve as a compact KoiSync mesh.

The generated resource contains geometry only. KoiSync supplies its own
diagnostic material so the preview does not depend on DragonHVR at runtime.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import struct

import UnityPy
from UnityPy.export.MeshExporter import export_mesh_obj
from UnityPy.helpers.MeshHelper import MeshHandler


def find_mesh(environment, object_name: str):
    for reader in environment.objects:
        if reader.type.name != "GameObject":
            continue
        game_object = reader.read()
        if getattr(game_object, "m_Name", "") != object_name:
            continue
        material_names = []
        mesh = None
        for pair in game_object.m_Component:
            pointer = pair.component
            if pointer.type.name == "MeshFilter":
                mesh = pointer.read().m_Mesh.read()
            elif pointer.type.name == "MeshRenderer":
                for material_pointer in pointer.read().m_Materials:
                    material_names.append(material_pointer.read().m_Name)
        if mesh is not None:
            return reader.path_id, mesh, material_names
    raise RuntimeError(f"GameObject {object_name!r} with a MeshFilter was not found")


def sha256(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", help="DragonHVR_Data/data.unity3d")
    parser.add_argument("mesh", help="output .meshbin path")
    parser.add_argument("metadata", help="output provenance JSON path")
    parser.add_argument("--obj", help="optional audit OBJ path")
    parser.add_argument("--object-name", default="onaho")
    args = parser.parse_args()

    environment = UnityPy.load(args.source)
    game_object_path_id, mesh, material_names = find_mesh(environment, args.object_name)
    handler = MeshHandler(mesh)
    handler.process()
    vertices = [tuple(float(value) for value in vertex[:3]) for vertex in handler.m_Vertices]
    triangles = [tuple(int(value) for value in triangle) for group in handler.get_triangles() for triangle in group]
    if not vertices or not triangles:
        raise RuntimeError("The selected preview mesh is empty")
    flat_indices = [index for triangle in triangles for index in triangle]
    minimum = [min(vertex[axis] for vertex in vertices) for axis in range(3)]
    maximum = [max(vertex[axis] for vertex in vertices) for axis in range(3)]

    os.makedirs(os.path.dirname(os.path.abspath(args.mesh)), exist_ok=True)
    with open(args.mesh, "wb") as output:
        output.write(b"KSPM")
        output.write(struct.pack("<IIIff", 1, len(vertices), len(flat_indices), minimum[1], maximum[1]))
        for vertex in vertices:
            output.write(struct.pack("<fff", *vertex))
        output.write(struct.pack(f"<{len(flat_indices)}i", *flat_indices))

    if args.obj:
        os.makedirs(os.path.dirname(os.path.abspath(args.obj)), exist_ok=True)
        with open(args.obj, "w", encoding="utf-8", newline="") as output:
            output.write(export_mesh_obj(mesh, material_names))

    report = {
        "schema": "koisync-contact-preview-source-v1",
        "source": os.path.abspath(args.source),
        "source_sha256": sha256(args.source),
        "game_object": args.object_name,
        "game_object_path_id": game_object_path_id,
        "mesh": mesh.m_Name,
        "mesh_path_id": mesh.object_reader.path_id,
        "materials": material_names,
        "vertices": len(vertices),
        "triangles": len(triangles),
        "bounds_min": minimum,
        "bounds_max": maximum,
        "entrance_axis": "+Y",
        "entrance_plane_local_y": minimum[1],
        "closed_end_local_y": maximum[1],
        "output": os.path.abspath(args.mesh),
        "output_sha256": sha256(args.mesh),
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.metadata)), exist_ok=True)
    with open(args.metadata, "w", encoding="utf-8") as output:
        json.dump(report, output, ensure_ascii=False, indent=2)
        output.write("\n")
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
