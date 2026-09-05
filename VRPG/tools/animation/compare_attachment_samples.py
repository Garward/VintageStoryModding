"""Compare sampled sword geometry in its own coordinate frame."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np


def local_geometry(path: Path) -> dict[str, np.ndarray]:
    with path.open() as handle:
        elements = json.load(handle)["elements"]
    root = next(e for e in elements if e["name"].split("/")[-1] == "tp-mount")
    inverse = np.linalg.inv(np.array(root["worldMatrix"]).reshape(4, 4, order="F"))
    by_id = {e["id"]: e for e in elements}
    result = {}
    for element in elements:
        ancestor = element
        while ancestor["id"] != root["id"] and "parentId" in ancestor:
            ancestor = by_id[ancestor["parentId"]]
        if ancestor["id"] != root["id"] or not element["corners"]:
            continue
        corners = np.array([[p["x"], p["y"], p["z"], 1] for p in element["corners"]])
        result[element["name"].split("/")[-1]] = (inverse @ corners.T).T[:, :3]
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("samples", nargs="+", type=Path)
    args = parser.parse_args()
    reference = local_geometry(args.samples[0])
    for path in args.samples[1:]:
        sample = local_geometry(path)
        if sample.keys() != reference.keys():
            raise ValueError("Sword element sets differ")
        errors = {name: float(np.max(np.linalg.norm(sample[name] - points, axis=1)))
                  for name, points in reference.items()}
        worst = max(errors, key=errors.get)
        print(json.dumps({"sample": str(path), "parts": len(errors),
                          "maxCornerErrorPixels": errors[worst], "worstPart": worst}))


if __name__ == "__main__":
    main()
