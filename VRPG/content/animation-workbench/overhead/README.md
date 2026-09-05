# Two-handed overhead candidate

This is an animation authoring fixture, not a live skill or combat-owner change.
`candidate.patch.json` adds `vrpg-2h-overhead-v1` to a supplied seraph shape through
the sampler. Nothing in this directory is packaged as a runtime asset.

## Findings

- ActionCombat currently maps two-handed overhead attacks to `axechop` in
  `AttackRegistry.Get2HAnimationCodeForDirection`. Editing the authored
  `2h-overhead` alone would not fix that playback route.
- The original authored overhead has four keys over 25 frames. With the actual
  third-person weapon mount applied, the blade remains raised rather than making
  a clear downward cut. The original supporting-arm pose and independently
  rotating item anchor also need correction.
- The previous generic sampling fixture used a zero-offset mount. That fixture
  was useful for exercising the bridge but was not sufficient for judging grip.

## Candidate contract

36 frames at playback speed 1: preparation through frame 10, hold to frame 12,
downward cut through frames 15–18, follow-through to frame 21, recovery to frame
35. The last frame is explicitly keyed and matches the first. Suggested impact
is frame 18, or 600 ms at speed 1. These are authoring markers, not server hit or
damage events. A future VRPG ability must map its authoritative timeline to them.

The offline solver uses the existing ready pose as its initial posture, keeps
limb translations at zero, and solves a shared blade trajectory and supporting
hand grip. It checks the midpoint between successive keys as well as the keys
themselves, since independently interpolated joints can separate between otherwise
valid poses. Angular changes are bounded during solving. No root displacement,
camera impulse, damage, inventory or starter transaction changes are included.

`claymore-tp-preview.json` bakes the existing item's 0.75 scale and the third-person
mount formula from `EntityShapeRenderer.RenderItem`, including the RightHand
attachment's -180-degree Y rotation. It is a preview copy; the original sword is
unchanged. This improves geometric inspection but still needs comparison against
the current game's blended third-person and first-person playback. The contact
points are explicit geometric proxies, not finger collision or an anatomical rig.

## Repeat inspection

From the Modding workspace, using the installed modeler runner:

```sh
xvfb-run -a python3 ../VSModelCreatorRedux/VSModelCreatorRedux/vsmodelcreator/tools/sample_animation.py \
  VRPG/content/animation-workbench/overhead/candidate.job.json \
  --launch ../VSModelCreatorRedux/vsmodelcreator-compose/run-compose.sh \
  --output tmp/vrpg-overhead-review
```

Use `before.job.json` to inspect the original authored clip with the same corrected
mount. The jobs sample front, side and three-quarter views. Each job fits its own
camera bounds; compare pose and trajectory rather than apparent image scale.
`playback.job.json` samples all 36 frames from side and three-quarter views for
30 fps playback. For example, inside its output directory, encode with
`ffmpeg -framerate 30 -i side-%04d.png -c:v libx264 -pix_fmt yuv420p side.mp4`.

## Authoring and validation

`VRPG/tools/animation/overhead_candidate.py` requires NumPy and SciPy for offline
authoring (tested with NumPy 2.5.1 and SciPy 1.18.0). Its `patch`, `weapon` and
`report` modes emit JSON to stdout without editing source files. The reusable
transform calculations live in `lib/rig.py`.

Run `python3 VRPG/tools/animation/test_overhead.py` from Modding. Checks cover
complete transform groups, finite rotations, fixed limb origins, explicit matching
endpoints, a raised preparation and downward impact, supporting-hand error below
0.25 model pixels, and blade-centerline floor clearance. Geometry is sampled at
quarter-frame intervals. These checks do not prove full-body collision clearance,
runtime animation blending or perceived impact; use the rendered samples and an
in-game test before adopting this candidate for a skill.
