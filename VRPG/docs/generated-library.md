# Generated Library Design

VRPG should treat the in-game library as generated documentation over live data.

The library should not be a hand-maintained wiki. Every data registry gets a small `ILibrarySource` implementation that contributes `LibraryEntry` records to `LibraryIndex`.

Current sources:

- Manual pages from `assets/*/vrpg/library/`.
- Stats from `assets/*/vrpg/stats/`.
- Stat families from `assets/*/vrpg/statfamilies/`.
- Gear rarities and affixes.
- Talent nodes.
- Dungeon themes.

Future sources should stay small:

- `SpellsLibrarySource`
- `UniquesLibrarySource`
- `MapsLibrarySource`
- `RecipesLibrarySource`
- `StatusEffectsLibrarySource`

The UI should render `LibraryIndex` rather than reading individual registries directly. That keeps search, categories, tooltips, and generated documentation consistent between commands and native dialogs.
