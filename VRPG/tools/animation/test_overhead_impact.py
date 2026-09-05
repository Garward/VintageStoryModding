"""Regression checks for the single-sword low-impact revision."""

from pathlib import Path
import unittest

import numpy as np

from lib.rig import Rig, hand_mount, point
from overhead_candidate import SOURCE, ITEM, WEAPON, load

WORK = Path(__file__).resolve().parents[2] / "content/animation-workbench/overhead"


class ImpactTests(unittest.TestCase):
    def setUp(self) -> None:
        self.rig = Rig(load(SOURCE))
        self.mount = hand_mount(load(ITEM), self.rig.elements["ItemAnchor"]["attachmentpoints"][0])
        weapon = Rig(load(WEAPON))
        element = weapon.elements["tip"]
        self.tip = point(weapon.matrices({}, ["tip"])["tip"], (np.array(element["to"])-element["from"])/2)
        self.keys = load(WORK / "candidate-v2.patch.json")[0]["value"]["keyframes"]

    def measure(self, pose: dict) -> tuple[np.ndarray, float, np.ndarray]:
        matrices = self.rig.matrices(pose, ["ItemAnchor", "LowerArmL"])
        sword = matrices["ItemAnchor"] @ self.mount
        error = np.linalg.norm(point(matrices["LowerArmL"], [1.25,.8,1])-point(sword, [4.5,2,8]))
        return point(sword, self.tip), float(error), sword

    def test_preserved_first_half_and_endpoints(self) -> None:
        original = load(WORK / "candidate.patch.json")[0]["value"]["keyframes"]
        self.assertEqual(self.keys[:18], original[:18])
        self.assertEqual(self.keys[-1]["elements"], self.keys[0]["elements"])

    def test_accelerating_low_impact(self) -> None:
        heights = [self.measure(self.keys[f]["elements"])[0][1] for f in range(17,22)]
        self.assertLess(heights[-1], .8)
        speeds = -np.diff(heights)
        self.assertTrue(np.all(np.diff(speeds)>0), speeds)
        self.assertGreater(speeds[-1], 4)

    def test_subframe_grip_and_tip(self) -> None:
        maximum, minimum = 0., float("inf")
        for frame in range(35):
            for t in (0,.25,.5,.75,1):
                pose = {n: {f:v*(1-t)+self.keys[frame+1]["elements"][n][f]*t for f,v in tr.items()}
                        for n,tr in self.keys[frame]["elements"].items()}
                tip, error, _ = self.measure(pose)
                maximum, minimum = max(maximum,error), min(minimum,tip[1])
        self.assertLess(maximum,.25, f"Support grip drift: {maximum}")
        self.assertGreater(minimum,0, f"Tip below floor: {minimum}")

    def test_one_visible_sword(self) -> None:
        shape = load(WORK / "seraph-without-embedded-sword.json")
        def walk(element: dict) -> None:
            if element["name"] == "crossguard":
                self.assertTrue(all(not f.get("enabled",True) for f in element["faces"].values()))
            for child in element.get("children",[]):
                walk(child)
        for element in shape["elements"]:
            walk(element)

    def test_blade_roll_stays_in_cutting_plane(self) -> None:
        normal = self.measure(self.keys[17]["elements"])[2][:3,1]
        normal /= np.linalg.norm(normal)
        for frame in range(18,25):
            sword = self.measure(self.keys[frame]["elements"])[2]
            axis = sword[:3,0] / np.linalg.norm(sword[:3,0])
            reference = normal-axis*np.dot(normal,axis)
            reference /= np.linalg.norm(reference)
            actual = sword[:3,1] / np.linalg.norm(sword[:3,1])
            error = np.degrees(np.arccos(np.clip(reference@actual,-1,1)))
            self.assertLess(error,1, f"Frame {frame}: blade roll {error} degrees")


if __name__ == "__main__":
    unittest.main()
