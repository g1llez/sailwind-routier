"""Per-unit cargo weight/volume helpers for route planning."""

from __future__ import annotations

import re
from typing import Optional


def parse_volume_cuft(
    size_description: Optional[str],
    stored: Optional[float] = None,
) -> float:
    """
    Cubic feet per trade unit (matches Economy UI size line).

    Prefer `volume_cuft` from goods_catalog when present; otherwise parse
  `size_description` (e.g. "12 ft³", "2 x 3 x 4", or a lone number).
    """
    if stored is not None and stored > 0:
        return float(stored)
    if not size_description:
        return 1.0

    text = size_description.strip().lower()
    for pattern in (
        r"([\d.]+)\s*(?:ft³|ft3|cu\.?\s*ft|cubic\s*ft)",
        r"^([\d.]+)\s*(?:ft|')?\s*$",
    ):
        match = re.search(pattern, text)
        if match:
            value = float(match.group(1))
            if value > 0:
                return value

    nums = [float(n) for n in re.findall(r"[\d.]+", text)]
    if len(nums) >= 3:
        return nums[0] * nums[1] * nums[2]
    if len(nums) == 1 and nums[0] > 0:
        return nums[0]
    return 1.0
