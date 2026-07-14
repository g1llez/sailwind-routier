"""Find which level asset is nearest to GRC kiosk coords."""
from __future__ import annotations

import math

import UnityPy

from sailwind_paths import GAME_DATA

TARGET = (367.9850, 2.6459, 478.1103)
BASE = GAME_DATA


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


def scan_level(level: Path):
    env = UnityPy.load(str(level))
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

    best = 9999.0
    best_path = ""
    best_pos = None
    for tid in tf_by_go:
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
        pos, path = world_from_chain(chain)
        d = math.hypot(pos[0] - TARGET[0], pos[2] - TARGET[2])
        if d < best:
            best = d
            best_path = path
            best_pos = pos

    return best, best_pos, best_path


def main():
    for level in sorted(BASE.glob("level*")):
        if level.is_dir():
            continue
        try:
            best, pos, path = scan_level(level)
        except Exception:
            continue
        if best < 50:
            leaf = path.split("/")[-1] if path else ""
            print(f"{level.name}: d={best:.1f} pos={pos} leaf={leaf}")


if __name__ == "__main__":
    main()
