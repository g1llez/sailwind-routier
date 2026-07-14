"""Deep scan of GRC level (level2) for port/mission table world positions."""
from __future__ import annotations

import math

import UnityPy

from sailwind_paths import GAME_DATA

LEVEL = GAME_DATA / "level2"
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
    vx, vy, vz = v
    # q * v * q^-1
    qv = quat_mul((vx, vy, vz, 0), ( -x, -y, -z, w))
    return quat_mul((x, y, z, w), qv)[:3]


def world_pos(transforms: dict, tid: int) -> tuple[tuple[float, float, float], str]:
    names: list[str] = []
    pos = (0.0, 0.0, 0.0)
    rot = (0.0, 0.0, 0.0, 1.0)
    cur = tid
    chain: list[tuple] = []
    while cur and cur in transforms:
        t = transforms[cur]
        data = t.read()
        chain.append(
            (
                data.m_LocalPosition.x,
                data.m_LocalPosition.y,
                data.m_LocalPosition.z,
                data.m_LocalRotation.x,
                data.m_LocalRotation.y,
                data.m_LocalRotation.z,
                data.m_LocalRotation.w,
                data.m_Name or "",
            )
        )
        cur = data.m_Father.path_id if data.m_Father else 0
    chain.reverse()
    for lx, ly, lz, qx, qy, qz, qw, name in chain:
        if name:
            names.append(name)
        rx, ry, rz = quat_rotate(rot, (lx, ly, lz))
        pos = (pos[0] + rx, pos[1] + ry, pos[2] + rz)
        rot = quat_mul(rot, (qx, qy, qz, qw))
    path = "/".join(names) if names else "<root>"
    return pos, path


def euler_y(q):
    x, y, z, w = q
    return math.degrees(math.atan2(2 * (w * y + x * z), 1 - 2 * (y * y + z * z)))


def main():
    env = UnityPy.load(str(LEVEL))
    transforms: dict = {}
    mono_names: dict[int, str] = {}

    for obj in env.objects:
        if obj.type.name == "Transform":
            transforms[obj.path_id] = obj
        elif obj.type.name == "MonoBehaviour":
            data = obj.read()
            tname = getattr(data, "m_Script", None)
            script = ""
            try:
                script = tname.name if tname else ""
            except Exception:
                pass
            if script in ("PortDude", "GPButtonPortMissions", "Port", "IslandMarket"):
                mono_names[obj.path_id] = script

    print(f"MonoBehaviours of interest: {len(mono_names)}")
    for mid, script in mono_names.items():
        mb = env.objects[mid].read()
        go = mb.m_GameObject
        if not go:
            continue
        go_data = env.objects[go.path_id].read()
        # find transform
        for tid, tobj in transforms.items():
            td = tobj.read()
            if td.m_GameObject.path_id == go.path_id:
                pos, path = world_pos(transforms, tid)
                print(f"\n[{script}] {go_data.m_Name}")
                print(f"  path: {path}")
                print(f"  world: ({pos[0]:.4f}, {pos[1]:.4f}, {pos[2]:.4f})")
                print(f"  dist to target: {math.hypot(pos[0]-TARGET[0], pos[2]-TARGET[2]):.2f}m")

    # All transforms with 'table' or 'mission' or 'scroll' in hierarchy path
    print("\n--- Hierarchy matches (table/mission/scroll/port/dude) ---")
    hits = []
    for tid in transforms:
        pos, path = world_pos(transforms, tid)
        low = path.lower()
        if not any(k in low for k in ("table", "mission", "scroll", "portdude", "port dude", "market", "exchange")):
            continue
        d = math.hypot(pos[0] - TARGET[0], pos[2] - TARGET[2])
        if d > 100:
            continue
        hits.append((d, pos, path))
    hits.sort()
    for d, pos, path in hits[:40]:
        print(f"  d={d:5.1f}  y={pos[1]:6.3f}  ({pos[0]:7.3f}, {pos[1]:7.3f}, {pos[2]:7.3f})  {path}")

    # Y-level histogram near target xz
    print("\n--- Ground/table Y levels near XZ (within 30m) ---")
    ys: dict[float, int] = {}
    for tid in transforms:
        pos, path = world_pos(transforms, tid)
        if math.hypot(pos[0] - TARGET[0], pos[2] - TARGET[2]) > 30:
            continue
        if pos[1] < 0 or pos[1] > 15:
            continue
        key = round(pos[1], 1)
        ys[key] = ys.get(key, 0) + 1
    for y in sorted(ys):
        print(f"  Y={y:5.1f}  ({ys[y]} transforms)")


if __name__ == "__main__":
    main()
