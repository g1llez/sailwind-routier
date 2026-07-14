"""Scan Sailwind level assets for transforms near Gold Rock City (x~368, z~-33)."""
from __future__ import annotations

import math
import re
import struct
from pathlib import Path

import UnityPy

from sailwind_paths import GAME_DATA

TARGET_X = 368.4732
TARGET_Z = -33.092
RADIUS = 80.0  # meters around GRC dock area


def read_transform_local(obj) -> tuple[str, tuple[float, float, float], tuple[float, float, float, float]]:
    data = obj.read()
    name = getattr(data, "m_Name", "") or ""
    t = data.m_LocalPosition
    r = data.m_LocalRotation
    return name, (t.x, t.y, t.z), (r.x, r.y, r.z, r.w)


def euler_y_from_quat(q: tuple[float, float, float, float]) -> float:
    x, y, z, w = q
    siny_cosp = 2 * (w * y + x * z)
    cosy_cosp = 1 - 2 * (y * y + z * z)
    return math.degrees(math.atan2(siny_cosp, cosy_cosp))


def dist_xz(a: tuple[float, float, float], bx: float, bz: float) -> float:
    return math.hypot(a[0] - bx, a[2] - bz)


def scan_level(level_path: Path) -> list[dict]:
    hits: list[dict] = []
    env = UnityPy.load(str(level_path))
    transforms: dict[int, object] = {}
    for obj in env.objects:
        if obj.type.name == "Transform":
            transforms[obj.path_id] = obj

    for obj in env.objects:
        if obj.type.name != "Transform":
            continue
        name, pos, rot = read_transform_local(obj)
        if dist_xz(pos, TARGET_X, TARGET_Z) > RADIUS:
            continue
        lower = name.lower()
        interesting = any(
            k in lower
            for k in (
                "table",
                "mission",
                "scroll",
                "port",
                "dude",
                "market",
                "desk",
                "counter",
                "stall",
                "kiosk",
                "exchange",
                "gold",
            )
        ) or name == ""
        if not interesting and pos[1] < 0.5:
            continue
        hits.append(
            {
                "level": level_path.name,
                "name": name or "<unnamed>",
                "pos": pos,
                "euler_y": euler_y_from_quat(rot),
                "dist": dist_xz(pos, TARGET_X, TARGET_Z),
            }
        )
    return hits


def find_grc_level() -> str | None:
    """Find which level file contains coords near GRC."""
    for level in sorted(GAME_DATA.glob("level*")):
        if level.suffix:
            continue
        try:
            env = UnityPy.load(str(level))
        except Exception:
            continue
        count = 0
        for obj in env.objects:
            if obj.type.name != "Transform":
                continue
            _, pos, _ = read_transform_local(obj)
            if dist_xz(pos, TARGET_X, TARGET_Z) < 5:
                count += 1
        if count > 5:
            return level.name
    return None


def main() -> None:
    if not GAME_DATA.is_dir():
        print(f"Missing game data: {GAME_DATA}")
        return

    grc_level = find_grc_level()
    print(f"GRC level candidate: {grc_level}")

    all_hits: list[dict] = []
    levels = [GAME_DATA / grc_level] if grc_level else sorted(p for p in GAME_DATA.glob("level*") if not p.suffix)

    for level in levels:
        try:
            all_hits.extend(scan_level(level))
        except Exception as exc:
            print(f"skip {level.name}: {exc}")

    all_hits.sort(key=lambda h: (h["dist"], -h["pos"][1]))

    print(f"\nTransforms within {RADIUS}m of ({TARGET_X}, {TARGET_Z}) — sorted by distance:\n")
    print(f"{'level':<8} {'name':<40} {'x':>9} {'y':>8} {'z':>9} {'yaw':>7} {'dist':>6}")
    print("-" * 95)
    for h in all_hits[:80]:
        p = h["pos"]
        print(
            f"{h['level']:<8} {h['name'][:40]:<40} "
            f"{p[0]:9.3f} {p[1]:8.3f} {p[2]:9.3f} {h['euler_y']:7.1f} {h['dist']:6.1f}"
        )

    tables = [h for h in all_hits if "table" in h["name"].lower() or "mission" in h["name"].lower()]
    if tables:
        print("\n--- mission/table named transforms ---")
        for h in tables:
            p = h["pos"]
            print(f"{h['name']}: ({p[0]:.4f}, {p[1]:.4f}, {p[2]:.4f}) yaw={h['euler_y']:.1f}")


if __name__ == "__main__":
    main()
