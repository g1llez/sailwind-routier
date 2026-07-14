"""Scan level2 transforms with full hierarchy names."""
from __future__ import annotations

import math

import UnityPy

from sailwind_paths import level

LEVEL = level("level2")
TARGET = (368.4732, 4.0679, -33.092)


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

    # attach GO names to transforms
    tf_by_go = {}
    for tid, td in transforms.items():
        go_id = td.m_GameObject.path_id if td.m_GameObject else 0
        go_name = ""
        if go_id in gameobjects:
            go_name = gameobjects[go_id].m_Name or ""
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
                    go_name or td.m_Name or "",
                )
            )
            cur = td.m_Father.path_id if td.m_Father else 0
        chain.reverse()
        return world_from_chain(chain)

    keywords = ("table", "mission", "scroll", "port", "dude", "market", "exchange", "desk", "counter", "clerk")
    hits = []
    for tid in tf_by_go:
        pos, path = build_world(tid)
        low = path.lower()
        if not any(k in low for k in keywords):
            continue
        d = math.hypot(pos[0] - TARGET[0], pos[2] - TARGET[2])
        if d > 120:
            continue
        hits.append((d, pos, path))

    hits.sort()
    print(f"Found {len(hits)} named transforms near GRC\n")
    for d, pos, path in hits:
        print(f"d={d:5.1f}m  pos=({pos[0]:7.3f}, {pos[1]:7.3f}, {pos[2]:7.3f})")
        print(f"  {path}\n")

    # Y histogram 30m radius
    print("Y levels within 30m of target XZ:")
    buckets: dict[float, list[str]] = {}
    for tid in tf_by_go:
        pos, path = build_world(tid)
        if math.hypot(pos[0] - TARGET[0], pos[2] - TARGET[2]) > 30:
            continue
        if pos[1] < -1 or pos[1] > 20:
            continue
        key = round(pos[1], 2)
        buckets.setdefault(key, []).append(path or "<unnamed>")
    for y in sorted(buckets):
        print(f"  Y={y:6.2f}  count={len(buckets[y])}")


if __name__ == "__main__":
    main()
