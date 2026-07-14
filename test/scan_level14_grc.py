"""Scan level14 for market_stall (8) parts and mission scroll near GRC."""
from __future__ import annotations

import math

import UnityPy

from sailwind_paths import level

TARGET = (367.9850, 2.6459, 478.1103)
LEVEL = level("level14")


def quat_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def quat_rotate(q, v):
    x, y, z, w = q
    qv = quat_mul((v[0], v[1], v[2], 0), (-x, -y, -z, w))
    return quat_mul((x, y, z, w), qv)[:3]


def world_from_chain(chain):
    pos = (0.0, 0.0, 0.0)
    rot = (0.0, 0.0, 0.0, 1.0)
    names = []
    for lx, ly, lz, qx, qy, qz, qw, name in chain:
        if name:
            names.append(name)
        rx, ry, rz = quat_rotate(rot, (lx, ly, lz))
        pos = (pos[0] + rx, pos[1] + ry, pos[2] + rz)
        rot = quat_mul(rot, (qx, qy, qz, qw))
    return pos, "/".join(names)


def main():
    env = UnityPy.load(str(LEVEL))
    transforms = {}
    gameobjects = {}

    for obj in env.objects:
        if obj.type.name == "Transform":
            transforms[obj.path_id] = obj.read()
        elif obj.type.name == "GameObject":
            gameobjects[obj.path_id] = obj.read()

    tf_by_go = {}
    for tid, td in transforms.items():
        go_id = td.m_GameObject.path_id if td.m_GameObject else 0
        go_name = gameobjects[go_id].m_Name if go_id in gameobjects else ""
        tf_by_go[tid] = (td, go_name)

    def build_world(tid):
        chain = []
        cur = tid
        seen = set()
        while cur and cur in tf_by_go and cur not in seen:
            seen.add(cur)
            td, go_name = tf_by_go[cur]
            chain.append(
                (
                    td.m_LocalPosition.x,
                    td.m_LocalPosition.y,
                    td.m_LocalPosition.z,
                    td.m_LocalRotation.x,
                    td.m_LocalRotation.y,
                    td.m_LocalRotation.z,
                    td.m_LocalRotation.w,
                    go_name,
                )
            )
            cur = td.m_Father.path_id if td.m_Father else 0
        chain.reverse()
        return world_from_chain(chain)

    print("--- market_stall (8) parts near GRC ---")
    for tid in tf_by_go:
        pos, path = build_world(tid)
        if "market_stall (8)" not in path:
            continue
        d = math.hypot(pos[0] - TARGET[0], pos[2] - TARGET[2])
        if d > 5:
            continue
        print(f"d={d:.1f} pos=({pos[0]:.3f}, {pos[1]:.3f}, {pos[2]:.3f})  {path.split('/')[-1]}")

    print("\n--- mission/scroll near GRC ---")
    for tid in tf_by_go:
        pos, path = build_world(tid)
        low = path.lower()
        if not any(k in low for k in ("mission", "scroll", "document", "book", "port dude", "portdude")):
            continue
        d = math.hypot(pos[0] - TARGET[0], pos[2] - TARGET[2])
        if d > 100:
            continue
        print(f"d={d:.1f} pos=({pos[0]:.3f}, {pos[1]:.3f}, {pos[2]:.3f})")
        print(f"  {path}\n")


if __name__ == "__main__":
    main()
