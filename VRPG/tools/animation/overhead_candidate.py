"""Author a two-handed overhead candidate and its correctly mounted preview sword.

Requires NumPy/SciPy for offline pose solving. Emits JSON on stdout; never edits inputs.
"""

from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import sys

import numpy as np
from scipy.optimize import least_squares

from lib.rig import Rig, hand_mount, point

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "ActionCombat/assets/actioncombat/shapes/entity/humanoid/seraph-faceless.json"
WEAPON = ROOT / "ActionCombat/assets/actioncombat/shapes/item/tool/blade/claymore.json"
ITEM = ROOT / "ActionCombat/assets/actioncombat/itemtypes/weapons/claymore.json"
CODE = "vrpg-2h-overhead-v1"
JOINTS = ["UpperArmR", "LowerArmR", "UpperArmL", "LowerArmL", "ItemAnchor"]


def load(path: Path) -> dict:
    with path.open() as handle:
        return json.load(handle)


def preview_weapon(shape: dict, mount: np.ndarray, item: dict) -> dict:
    result = copy.deepcopy(shape)
    scale = item["tpHandTransform"]["scale"]

    def visit(element: dict) -> None:
        for key in ("from", "to", "rotationOrigin"):
            if key in element:
                element[key] = [round(v * scale, 6) for v in element[key]]
        for child in element.get("children", []):
            visit(child)

    for element in result["elements"]:
        visit(element)
    position = mount[:3, 3].tolist()
    result["elements"] = [{"name": "tp-mount", "from": position, "to": position,
        "rotationOrigin": position, "rotationX": 129, "rotationY": -181, "rotationZ": 4,
        "children": result["elements"]}]
    result["textures"] = {"handle": "game:block/wood/debarked/oak", "wood": "game:block/wood/debarked/oak",
                          "metal": "game:block/metal/ingot/iron", "blackbronze": "game:block/metal/ingot/iron"}
    return result


def author(shape: dict, mount: np.ndarray) -> tuple[dict, dict]:
    rig = Rig(shape)
    base = copy.deepcopy(next(a for a in shape["animations"] if a["code"] == "2h-idle-ready")["keyframes"][0]["elements"])
    for transform in base.values():
        for axis in "XYZ":
            transform["offset" + axis] = 0.0
    initial = np.array([base[n]["rotation" + axis] for n in JOINTS for axis in "XYZ"])
    rest = rig.matrices(base, ["ItemAnchor"])["ItemAnchor"] @ mount
    start_grip = point(rest, [8, 2, 8])
    start_direction = rest[:3, 0] / np.linalg.norm(rest[:3, 0])
    # Grip and blade trajectories in unshifted model pixels: forward is negative X.
    guides = [
        (0, start_grip, start_direction, 0),
        (6, [3, 28, 7], [0.25, 0.97, 0], -4),
        (10, [6, 31, 7], [0.80, 0.60, 0], -7),
        (12, [6, 31, 7], [0.80, 0.60, 0], -7),
        (15, [-1, 27, 7], [-0.85, 0.52, 0], 2),
        (18, [-5, 19, 7], [-0.90, -0.44, 0], 12),
        (21, [-4, 19, 7], [-0.89, -0.46, 0], 14),
        (27, [-1, 21, 6], [-0.50, 0.87, 0], 4),
        (35, start_grip, start_direction, 0),
    ]
    previous = initial.copy()
    return_pose = initial.copy()
    keys = []
    errors = []
    for frame in range(36):
        left = max((g for g in guides if g[0] <= frame), key=lambda g: g[0])
        right = next((g for g in guides if g[0] > frame), left)
        t = 0 if right[0] == left[0] else (frame - left[0]) / (right[0] - left[0])
        t = t * t * (3 - 2 * t)
        grip = np.array(left[1]) * (1 - t) + np.array(right[1]) * t
        direction = np.array(left[2]) * (1 - t) + np.array(right[2]) * t
        direction /= np.linalg.norm(direction)
        torso = left[3] * (1 - t) + right[3] * t
        pose = copy.deepcopy(base)
        pose["UpperTorso"]["rotationZ"] = torso
        # Keep the head steady while the trunk contributes to the cut.
        pose["Head"]["rotationZ"] = -torso * 0.6

        def evaluate(angles: np.ndarray) -> tuple[dict, np.ndarray, np.ndarray, np.ndarray]:
            candidate = copy.deepcopy(pose)
            for index, name in enumerate(JOINTS):
                for axis_index, axis in enumerate("XYZ"):
                    candidate[name]["rotation" + axis] = float(angles[index * 3 + axis_index])
            matrices = rig.matrices(candidate, ["ItemAnchor", "LowerArmL"])
            sword = matrices["ItemAnchor"] @ mount
            primary = point(sword, [8, 2, 8])
            secondary = point(sword, [4.5, 2, 8])
            hand = point(matrices["LowerArmL"], [1.25, 0.8, 1])
            blade = sword[:3, 0] / np.linalg.norm(sword[:3, 0])
            return candidate, primary, blade, hand - secondary

        def residual(angles: np.ndarray) -> np.ndarray:
            candidate, primary, blade, grip_error = evaluate(angles)
            midpoint_error = np.zeros(3)
            if keys:
                midpoint = {name: {field: (value + keys[-1]["elements"][name][field]) / 2
                                   for field, value in transform.items()}
                            for name, transform in candidate.items()}
                middle = rig.matrices(midpoint, ["ItemAnchor", "LowerArmL"])
                midpoint_error = point(middle["LowerArmL"], [1.25, 0.8, 1]) - point(middle["ItemAnchor"] @ mount, [4.5, 2, 8])
            recovery = max(0, (frame - 22) / 13)
            return np.concatenate(((primary - grip) * 4, (blade - direction) * 12, grip_error * 40,
                                   midpoint_error * 40,
                                   (angles - previous) * 0.008 * (1 - recovery),
                                   (angles - initial) * 0.002 * (1 - recovery),
                                   (angles - return_pose) * 0.15 * recovery * recovery))

        lower = np.maximum(-179, previous - 22)
        upper = np.minimum(179, previous + 22)
        solution = least_squares(residual, previous, bounds=(lower, upper), max_nfev=180,
                                 ftol=1e-7, xtol=1e-7, gtol=1e-7)
        previous = solution.x
        if frame == 0:
            return_pose = previous.copy()
        elif frame == 35:
            previous = return_pose.copy()
        candidate, actual, blade, grip_error = evaluate(previous)
        for transform in candidate.values():
            for field, value in transform.items():
                transform[field] = round(value, 5)
        keys.append({"frame": frame, "elements": candidate})
        errors.append({"frame": frame, "gripErrorPixels": float(np.linalg.norm(grip_error)),
                       "targetErrorPixels": float(np.linalg.norm(actual - grip)), "bladeDirection": blade.tolist()})
    # Explicit final keyed pose prevents interpolation back through an unkeyed tail.
    animation = {"name": "VRPG Two-Handed Overhead V1", "code": CODE, "quantityframes": 36,
                 "onActivityStopped": "EaseOut", "onAnimationEnd": "Hold", "keyframes": keys}
    return animation, {"frames": errors, "coordinateSpace": "model pixels, before viewport offset",
                       "activeFrames": [15, 21], "impactFrame": 18, "fpsAtSpeedOne": 30}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["patch", "weapon", "report"])
    args = parser.parse_args()
    shape, item = load(SOURCE), load(ITEM)
    rig = Rig(shape)
    mount = hand_mount(item, rig.elements["ItemAnchor"]["attachmentpoints"][0])
    if args.mode == "weapon":
        result = preview_weapon(load(WEAPON), mount, item)
    else:
        animation, report = author(shape, mount)
        result = report if args.mode == "report" else [{"op": "add", "file": "game:shapes/entity/humanoid/seraph-faceless.json",
                                                       "path": "/animations/-", "value": animation}]
    json.dump(result, sys.stdout, indent=2)
    sys.stdout.write("\n")


if __name__ == "__main__":
    main()
