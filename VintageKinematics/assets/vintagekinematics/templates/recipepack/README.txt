VK recipe pack template

Use this folder when you want to make a mod that only adds recipes to Vintage Kinematics.
No C# is needed.

Copy the vkrecipe folder into your own mod:

assets/yourmod/vkrecipe/crusher/
assets/yourmod/vkrecipe/extractor/
assets/yourmod/vkrecipe/forgepress/
assets/yourmod/vkrecipe/mixer/
assets/yourmod/vkrecipe/process/
assets/yourmod/vkrecipe/sawmill/
assets/yourmod/vkrecipe/sieve/

Delete the example files you do not need, then edit item/block codes and quantities.
Recipe file names only need to be unique inside their folder.

Built-in machine recipe folders:

- crusher: one input stack to one or more output stacks.
- sieve: one input stack to a weighted output roll.
- extractor: one input stack to solid outputs, liquid output, or both.
- forgepress: one heated input stack, optional die, selected operation, outputs.
- mixer: unordered solid inputs, optional liquid input, outputs.
- sawmill: one input stack, selected sawmill mode, outputs.
- process/<machineCode>: generic JSON processors and JSON-backed VK machines.

Wildcard item codes use Vintage Story's normal * matching. For example:

game:ore-poor-nativecopper-*
game:ingot-*

Some machines can reuse wildcard captures in output codes. Crusher and forge press support
this for their built-in recipe types. Generic process recipes do not support wildcard outputs.

The charcoal retort uses the generic process folder:

assets/yourmod/vkrecipe/process/kineticcharcoalretort/my-recipe.json

Its built-in slot still accepts firewood-style input, so recipe packs should treat it as a
firewood-to-output machine unless VK exposes a broader retort input policy later.
