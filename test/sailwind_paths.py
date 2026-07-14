"""Shared paths for local Sailwind asset-inspection scripts."""

import os
from pathlib import Path


SAILWIND_DIR = Path(
    os.environ.get(
        "SAILWIND_DIR",
        r"C:\Program Files (x86)\Steam\steamapps\common\Sailwind",
    )
)
GAME_DATA = SAILWIND_DIR / "Sailwind_Data"


def level(name: str) -> Path:
    return GAME_DATA / name
