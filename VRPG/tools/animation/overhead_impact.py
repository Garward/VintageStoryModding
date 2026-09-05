"""Refine the second half of V1 with a planted cutting plane and low tip impact."""

from __future__ import annotations

import copy
import json
import sys
from pathlib import Path

import numpy as np
from scipy.optimize import least_squares

from lib.rig import Rig, hand_mount, point
from overhead_candidate import SOURCE, ITEM, WEAPON, JOINTS, load

def author() -> dict:
    # Preserve the reviewed first half byte-for-byte; publish a separate candidate.
    work = Path(__file__).resolve().parents[2] / "content/animation-workbench/overhead"
    animation = copy.deepcopy(load(work / "candidate.patch.json")[0]["value"])
    rig = Rig(load(SOURCE))
    mount = hand_mount(load(ITEM), rig.elements["ItemAnchor"]["attachmentpoints"][0])
    weapon = Rig(load(WEAPON))
    tip_element = weapon.elements["tip"]
    tip_local = (np.array(tip_element["to"]) - tip_element["from"]) / 2
    tip = point(weapon.matrices({}, ["tip"])["tip"], tip_local)
    keys = animation["keyframes"]
    opening = keys[0]["elements"]
    start = keys[17]["elements"]

    def angles(pose: dict) -> np.ndarray:
        return np.array([pose[n]["rotation" + a] for n in JOINTS for a in "XYZ"])

    def measurements(pose: dict) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
        matrices = rig.matrices(pose, ["ItemAnchor", "LowerArmL"])
        sword = matrices["ItemAnchor"] @ mount
        return (point(sword, [8, 2, 8]), point(sword, tip),
                sword[:3, 1] / np.linalg.norm(sword[:3, 1]),
                point(matrices["LowerArmL"], [1.25, 0.8, 1]) - point(sword, [4.5, 2, 8]))

    start_grip, start_tip, normal, _ = measurements(start)
    end_grip, end_tip, end_normal, _ = measurements(opening)
    impact_grip = np.array([-5., 18., 7.])
    impact_tip = np.array([-30., .45, 7.])
    previous = angles(start)
    for frame in range(18, 35):
        if frame <= 21:
            t = ((frame - 17) / 4) ** 1.65
            grip = start_grip * (1-t) + impact_grip * t
            target_tip = start_tip * (1-t) + impact_tip * t
            target_normal = normal
            torso = start["UpperTorso"]["rotationZ"] * (1-t) + 16*t
        elif frame <= 24:
            grip, target_tip, target_normal, torso = impact_grip, impact_tip, normal, 16
        else:
            t = (frame - 24) / 11
            t = t*t*(3-2*t)
            grip = impact_grip * (1-t) + end_grip*t
            target_tip = impact_tip * (1-t) + end_tip*t
            target_normal = normal*(1-t) + end_normal*t
            torso = 16*(1-t)
        # Tip/grip define the cutting direction; constrain roll about that axis too.
        direction = target_tip - grip
        direction /= np.linalg.norm(direction)
        target_normal = target_normal - direction*np.dot(target_normal, direction)
        target_normal /= np.linalg.norm(target_normal)
        pose = copy.deepcopy(start)
        pose["UpperTorso"]["rotationZ"] = torso
        pose["Head"]["rotationZ"] = -torso*.6
        recovery = max(0., (frame-24)/11)
        recovery_angles = angles(opening)
        if frame > 24:
            blend = recovery*recovery*(3-2*recovery)
            recovery_angles = angles(keys[24]["elements"])*(1-blend) + angles(opening)*blend
            for name in pose:
                if name not in JOINTS and name not in ("UpperTorso", "Head"):
                    pose[name] = {f: v*(1-blend)+opening[name][f]*blend for f,v in pose[name].items()}

        def evaluate(values: np.ndarray) -> dict:
            candidate = copy.deepcopy(pose)
            for i, name in enumerate(JOINTS):
                for j, axis in enumerate("XYZ"):
                    candidate[name]["rotation"+axis] = float(values[i*3+j])
            return candidate

        def residual(values: np.ndarray) -> np.ndarray:
            candidate = evaluate(values)
            actual_grip, actual_tip, actual_normal, error = measurements(candidate)
            middle = {n: {f: (v+keys[frame-1]["elements"][n][f])/2 for f, v in tr.items()}
                      for n, tr in candidate.items()}
            _, middle_tip, _, middle_error = measurements(middle)
            return np.concatenate(((actual_grip-grip)*(0 if recovery else 1),
                (actual_tip-target_tip)*(0 if recovery else 20),
                (actual_normal-target_normal)*8*(1-recovery), error*300, middle_error*200,
                [min(0., actual_tip[1]-.1)*100, min(0., middle_tip[1]-.1)*100],
                (values-previous)*.025*(1-recovery),
                (values-recovery_angles)*(1 if recovery else .005)))

        solution = least_squares(residual, previous, bounds=(np.maximum(-179, previous-35),
                                    np.minimum(179, previous+35)), max_nfev=220)
        previous = solution.x
        candidate = evaluate(previous)
        keys[frame]["elements"] = candidate
    keys[-1]["elements"] = copy.deepcopy(opening)
    animation.update(code="vrpg-2h-overhead-v2", name="VRPG Two-Handed Overhead V2")
    return [{"op":"add", "path":"/animations/-", "value":animation}]


if __name__ == "__main__":
    json.dump(author(), sys.stdout, indent=2)
    sys.stdout.write("\n")
