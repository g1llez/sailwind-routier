"""List mesh child names under market_stall (8) in level1."""
from __future__ import annotations

import UnityPy

from sailwind_paths import level

LEVEL = level("level1")
STALL = "market_stall (8)"


def main():
    env = UnityPy.load(str(LEVEL))
    transforms = {}
    gameobjects = {}

    for obj in env.objects:
        if obj.type.name == "Transform":
            transforms[obj.path_id] = obj.read()
        elif obj.type.name == "GameObject":
            gameobjects[obj.path_id] = obj.read()

    # map father -> children
    children = {}
    root_tid = None
    for tid, td in transforms.items():
        go_id = td.m_GameObject.path_id if td.m_GameObject else 0
        go_name = gameobjects[go_id].m_Name if go_id in gameobjects else ""
        father = td.m_Father.path_id if td.m_Father else 0
        children.setdefault(father, []).append((tid, go_name or td.m_Name))

        if go_name == STALL:
            root_tid = tid

    if root_tid is None:
        print("stall not found")
        return

    def walk(tid, depth=0):
        for child_tid, name in children.get(tid, []):
            indent = "  " * depth
            print(f"{indent}{name}")
            walk(child_tid, depth + 1)

    print(f"Hierarchy under {STALL}:")
    walk(root_tid)


if __name__ == "__main__":
    main()
