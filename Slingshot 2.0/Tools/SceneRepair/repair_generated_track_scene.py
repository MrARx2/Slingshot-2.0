#!/usr/bin/env python3
"""Remove one embedded GeneratedTrack hierarchy from a Unity YAML scene.

The script keeps every unrelated scene document. It discovers descendants through
Transform parent links, then removes their GameObjects, components, and locally
serialized meshes. The source is never modified.
"""

from __future__ import annotations

import argparse
import os
import re
from collections import defaultdict


DOC_RE = re.compile(rb"^--- !u!(\d+) &(-?\d+)")
FILE_ID_RE = re.compile(rb"\{fileID: (-?\d+)")


def first_file_id(line: bytes) -> int | None:
    match = FILE_ID_RE.search(line)
    return int(match.group(1)) if match else None


def inspect_scene(path: str, generated_name: bytes):
    doc_types: dict[int, int] = {}
    game_object_names: dict[int, bytes] = {}
    game_object_components: dict[int, list[int]] = defaultdict(list)
    component_game_object: dict[int, int] = {}
    transform_parent: dict[int, int] = {}
    transform_game_object: dict[int, int] = {}
    local_mesh_references: dict[int, set[int]] = defaultdict(set)

    current_id = None
    current_type = None
    in_component_list = False

    with open(path, "rb", buffering=1024 * 1024) as source:
        for line in source:
            header = DOC_RE.match(line)
            if header:
                current_type = int(header.group(1))
                current_id = int(header.group(2))
                doc_types[current_id] = current_type
                in_component_list = False
                continue
            if current_id is None:
                continue

            stripped = line.lstrip()
            if current_type == 1:
                if stripped.startswith(b"m_Component:"):
                    in_component_list = True
                elif in_component_list and stripped.startswith(b"- component:"):
                    value = first_file_id(stripped)
                    if value is not None:
                        game_object_components[current_id].append(value)
                elif stripped.startswith(b"m_Name:"):
                    game_object_names[current_id] = stripped.split(b":", 1)[1].strip()
                elif in_component_list and not stripped.startswith(b"-"):
                    in_component_list = False
            else:
                if stripped.startswith(b"m_GameObject:"):
                    value = first_file_id(stripped)
                    if value is not None:
                        component_game_object[current_id] = value
                        if current_type in (4, 224):
                            transform_game_object[current_id] = value
                elif current_type in (4, 224) and stripped.startswith(b"m_Father:"):
                    value = first_file_id(stripped)
                    if value is not None:
                        transform_parent[current_id] = value
                elif current_type in (33, 64) and stripped.startswith(b"m_Mesh:"):
                    value = first_file_id(stripped)
                    if value is not None:
                        local_mesh_references[current_id].add(value)

    root_game_objects = {
        game_object_id
        for game_object_id, name in game_object_names.items()
        if name.startswith(generated_name)
    }
    if len(root_game_objects) != 1:
        raise RuntimeError(
            f"Expected exactly one {generated_name.decode()} root, found {len(root_game_objects)}"
        )

    root_game_object = next(iter(root_game_objects))
    root_transforms = {
        component_id
        for component_id in game_object_components[root_game_object]
        if doc_types.get(component_id) in (4, 224)
    }
    if len(root_transforms) != 1:
        raise RuntimeError(f"Expected one root Transform, found {len(root_transforms)}")
    root_transform = next(iter(root_transforms))

    children: dict[int, list[int]] = defaultdict(list)
    for transform_id, parent_id in transform_parent.items():
        children[parent_id].append(transform_id)

    generated_transforms: set[int] = set()
    pending = [root_transform]
    while pending:
        transform_id = pending.pop()
        if transform_id in generated_transforms:
            continue
        generated_transforms.add(transform_id)
        pending.extend(children.get(transform_id, ()))

    generated_game_objects = {
        transform_game_object[transform_id]
        for transform_id in generated_transforms
        if transform_id in transform_game_object
    }
    generated_components = {
        component_id
        for game_object_id in generated_game_objects
        for component_id in game_object_components.get(game_object_id, ())
    }
    generated_meshes = {
        mesh_id
        for component_id in generated_components
        for mesh_id in local_mesh_references.get(component_id, ())
        if doc_types.get(mesh_id) == 43
    }
    removed_ids = generated_game_objects | generated_components | generated_meshes

    return {
        "root_game_object": root_game_object,
        "root_transform": root_transform,
        "game_objects": generated_game_objects,
        "components": generated_components,
        "meshes": generated_meshes,
        "removed_ids": removed_ids,
    }


def write_repaired(source_path: str, output_path: str, removed_ids: set[int]):
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)
    current_removed = False
    with open(source_path, "rb", buffering=1024 * 1024) as source, open(
        output_path, "wb", buffering=1024 * 1024
    ) as output:
        for line in source:
            header = DOC_RE.match(line)
            if header:
                current_removed = int(header.group(2)) in removed_ids
            if not current_removed:
                output.write(line)


def remove_stale_references(path: str, root_transform: int):
    with open(path, "rb") as source:
        data = source.read()

    escaped_root = str(root_transform).encode()
    modification = re.compile(
        rb"\n    - target: \{fileID: [^\n]+\}\r?\n"
        rb"      propertyPath: trackRoot\r?\n"
        rb"      value: *\r?\n"
        rb"      objectReference: \{fileID: " + escaped_root + rb"\}\r?\n"
    )
    data, modification_count = modification.subn(b"\n", data)
    root_line = re.compile(
        rb"^  - \{fileID: " + escaped_root + rb"\}\r?\n", re.MULTILINE
    )
    data, root_count = root_line.subn(b"", data)

    if modification_count != 1 or root_count != 1:
        raise RuntimeError(
            f"Expected one trackRoot override and one SceneRoots entry; "
            f"removed {modification_count} and {root_count}"
        )

    with open(path, "wb") as output:
        output.write(data)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("output")
    args = parser.parse_args()

    result = inspect_scene(args.source, b"GeneratedTrack_")
    write_repaired(args.source, args.output, result["removed_ids"])
    remove_stale_references(args.output, result["root_transform"])

    print(
        "removed",
        len(result["game_objects"]),
        "game objects,",
        len(result["components"]),
        "components,",
        len(result["meshes"]),
        "meshes",
    )
    print("source_bytes", os.path.getsize(args.source))
    print("output_bytes", os.path.getsize(args.output))
    print("root_transform", result["root_transform"])


if __name__ == "__main__":
    main()
