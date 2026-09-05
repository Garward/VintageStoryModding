"""Structural and sampled-geometry checks for the overhead authoring candidate."""

from __future__ import annotations

import json
from pathlib import Path
import unittest

import numpy as np

from lib.rig import Rig, hand_mount, point
from overhead_candidate import SOURCE, ITEM, load

WORK = Path(__file__).resolve().parents[2] / "content/animation-workbench/overhead"


class OverheadCandidateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.shape = load(SOURCE)
        cls.rig = Rig(cls.shape)
        cls.mount = hand_mount(load(ITEM), cls.rig.elements["ItemAnchor"]["attachmentpoints"][0])
        cls.animation = load(WORK / "candidate.patch.json")[0]["value"]

    def test_complete_nonwrapping_frames_and_no_detached_limbs(self) -> None:
        keys = self.animation["keyframes"]
        self.assertEqual([k["frame"] for k in keys], list(range(36)))
        self.assertEqual(keys[0]["elements"], keys[-1]["elements"])
        self.assertEqual(self.animation["onAnimationEnd"], "Hold")
        for key in keys:
            for name, transform in key["elements"].items():
                self.assertIn(name, self.rig.elements)
                for axis in "XYZ":
                    self.assertEqual(transform["offset" + axis], 0)
                    self.assertTrue(np.isfinite(transform["rotation" + axis]))

    def test_grip_and_floor_clearance_between_authored_frames(self) -> None:
        keys = self.animation["keyframes"]
        maximum = 0.0
        minimum_tip = float("inf")
        for index in range(35):
            for t in (0, 0.25, 0.5, 0.75, 1):
                pose = {name: {field: value * (1-t) + keys[index+1]["elements"][name][field] * t
                               for field, value in transform.items()}
                        for name, transform in keys[index]["elements"].items()}
                matrices = self.rig.matrices(pose, ["ItemAnchor", "LowerArmL"])
                sword = matrices["ItemAnchor"] @ self.mount
                secondary = point(sword, [4.5, 2, 8])
                hand = point(matrices["LowerArmL"], [1.25, 0.8, 1])
                maximum = max(maximum, float(np.linalg.norm(hand-secondary)))
                minimum_tip = min(minimum_tip, point(sword, [53.5, 2, 8])[1])
        self.assertLess(maximum, 0.25, f"Support-hand drift: {maximum} model pixels")
        self.assertGreater(minimum_tip, 0, f"Blade tip below floor: {minimum_tip}")

    def test_preparation_and_downward_impact(self) -> None:
        def weapon(frame: int) -> np.ndarray:
            return self.rig.matrices(self.animation["keyframes"][frame]["elements"], ["ItemAnchor"])["ItemAnchor"] @ self.mount
        high, impact = weapon(12), weapon(18)
        self.assertGreater(point(high, [8, 2, 8])[1], 29)
        self.assertGreater(high[1, 0], 0)
        self.assertLess(impact[1, 0], 0)
        self.assertLess(point(impact, [8, 2, 8])[1], 21)


if __name__ == "__main__":
    unittest.main()
