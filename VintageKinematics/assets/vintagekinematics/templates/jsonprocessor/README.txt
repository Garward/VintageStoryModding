Generic JSON kinetic processor template

Copy these files into your own mod, preserving the same relative asset folders:

assets/yourmod/blocktypes/exampleprocessor.json
assets/yourmod/shapes/block/exampleprocessor.json
assets/yourmod/vkrecipe/process/exampleprocessor/log-to-plank.json
assets/yourmod/lang/en.json

Then rename "exampleprocessor" and edit the recipe outputs. The C# classes are provided by
Vintage Kinematics:

class: BlockKineticJsonProcessor
entityClass: KineticJsonProcessor

Default behavior:
- The template is a 1x2 multiblock with the shaft in the bottom/controller cell and a
  decorative top cell.
- Slot 0 is item input.
- Slots 64-72 are output-only by default. The generic processor reserves slots 0-63 for
  inputs and 64-127 for outputs so larger machines can expose bigger buffers with JSON.
- Set vkProcessor.inputSlots and vkProcessor.outputSlots to change the visible/active buffer.
- Storage interaction defaults to regular GUI slots. Set vkProcessor.storageStyle to "crate"
  for crate-style shift/right-click input and right-click output. You can also set
  inputStorageStyle or outputStorageStyle separately.
- Follow the VK modeling guide in the unrotated/base shape:
  - shaft runs north-south
  - west side is item input
  - top is also item input
  - east side is item output
  - bottom is also item output
- The template uses explicit vkProcessor.io entries:
  - left side of the controller cell accepts input
  - top face of the upper decorative cell accepts input
  - right side of the controller cell exposes output
  - bottom face of the controller cell exposes output
  Exact cells are controller-relative offsets in the base/model orientation, then rotate with the
  block's side variant. For this 1x2 template, { "x": 0, "y": 1, "z": 0 } means the block
  directly above the controller.
- KineticWorker controls speed/work cost through the blocktype entity behavior.
- KineticAnimator rotates the shaft, and KineticPiston moves the PressHead element while the
  machine has rotation power. Rename those element references if your shape uses different
  element names.

Overrides:
- Prefer vkProcessor.io for new machines. Each entry has:
  - type: "input" or "output"
  - face: "up", "down", "north", "east", "south", "west", or relative "front", "back",
    "left", "right"
  - slots: "inputs", "outputs", a single slot like "64", or a range like "64-72"
  - optional cell: controller-relative base-orientation offset, for example { "x": 0, "y": 1, "z": 0 }
  - optional cells: "face" to expose IO across every multiblock cell on that face, or an array
    of controller-relative offsets
  - optional rotateCell: false if the cell offset should stay in world coordinates instead of
    rotating with the block; normally leave this out
- Use inputFace "inputLipNorth", "inputLipEast", "inputLipSouth", or "inputLipWest" when your
  model follows a different local input side but still uses side variants and shape rotation.
- Use fixed inputFace "north", "east", "south", "west", "up", or "down" only when the IO face
  should not rotate with placement. inputFace is only used by the legacy ioLayout fallback.
- Legacy ioLayout options:
  - sideInputOppositeAndDownOutput: side/top input, opposite/bottom output
  - sideInputOppositeOutput: side input, opposite output
  - topInputDownOutput: top input, bottom output
  - leftInputRightAndDownOutput: placement-facing left/top input, right/bottom output

Multiblock simple machines:
- Add the vanilla Multiblock block behavior to the blocktype, just like normal Vintage Story
  multiblocks.
- Add the KineticMultiblock entity behavior if shaft connection cells are not only the controller.
- Declare shaft cells with attributes.kineticShaftCells or attributes.kineticShaftElements.
- For explicit vkProcessor.io entries, use cells: "face" when IO should map across every claimed
  cell on the relevant claim face. ioScope is still used by the legacy ioLayout fallback.
- Example retort-like IO:
  "vkProcessor": {
    "machineCode": "myretort",
    "inputSlots": 16,
    "outputSlots": 16,
    "ioScope": "multiblock",
    "io": [
      { "type": "input", "face": "left", "slots": "inputs", "cells": "face" },
      { "type": "input", "face": "up", "slots": "inputs", "cells": "face" },
      { "type": "output", "face": "right", "slots": "outputs", "cells": "face" },
      { "type": "output", "face": "down", "slots": "outputs", "cells": "face" }
    ]
  }
