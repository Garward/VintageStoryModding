# Talent Tree Authoring Rules

Status: first construction standard for the scalable VRPG passive tree.

This document defines repeatable rules for layout, stat budgets, themes,
connections, gamechangers, naming, and production order. It is not a final list
of node values. Values must come from the resolved stat pipeline and in-game
balance tests.

## Confirmed Topology

The tree begins with **six global starting routes**.

- The six starting nodes sit in the central region of the full tree.
- Their centers form a wide, approximate hexagon around the tree center.
- They are far enough apart to leave useful authoring space between and around
  them.
- Starting nodes do not connect directly to one another.
- Selecting one starting node locks the other five for that tree life until a
  full reset.
- Each route grows outward from its hexagon point, away from the center.
- Later ordinary paths may connect route regions without passing through another
  locked starting node.

Count, placement, and core-stat pairings are settled. The six starts cover every
ordered dominant/secondary pairing of Strength, Dexterity, and Intelligence, so
each attribute dominates exactly two routes. Exact route themes remain a content
decision and are not silently inferred from the old three-foundation test tree.

## Working Route Identity Draft

These are mechanical working labels for the next design pass, not approved
player-facing names or final node lists. They cover the current class-candidate
pool without turning a talent route into a replacement for class identity.

| Route | Start package | Working promise | Early families | Defense or sustain | Lore bucket |
| --- | --- | --- | --- | --- | --- |
| Force | `+10 STR, +5 DEX` | Hit hard and remain in close danger. | Physical damage, heavy weapons, stagger | Health and armor | Plain trade/material |
| Precision | `+10 DEX, +5 STR` | Avoid the hit and make the opening count. | Projectiles, critical access, range, speed | Evasion and movement | Plain trade/material |
| Exposure | `+10 DEX, +5 INT` | Work with the symptoms of Rust bleed before they consume the fight. | Fire, Cold, Discharge, Rust, Bleed, area, status application | Status control and route-specific recovery | Rust-bleed |
| Tuning | `+10 INT, +5 DEX` | Keep power under control and make systems answer. | Mana, cast cadence, mechanisms, supported minions, Discharge | Magic Shield and Mana recovery | Clockwork/mechanism |
| Command | `+10 INT, +5 STR` | Make durable systems, allies, and controlled power answer together. | Supported minions, protection, control, Mana | Magic Shield and supported mitigation | Plain command/mechanism |
| Shelter | `+10 STR, +5 INT` | Stay standing and keep pressure away from others. | Recovery, protection, control, support, temporal stability | Health recovery and supported mitigation | Plain protection; Rust-bleed only where stability is explicit |

Each route's central start receives the listed equal-budget package. The
dominant stat sets tie affinity. Working route names and detailed themes may
change without changing the complete six-pair structure.

Before approval, each route must pass four checks:

1. It supports at least two class pairs rather than one intended class.
2. Its early defense or sustain works in solo play.
3. Its ordinary nodes do not require class-exclusive mechanics.
4. Its name communicates function before lore flavor.

## Goals

The tree must:

- remain readable after hundreds of nodes are added;
- give authors a repeatable way to extend one route or section;
- make early nodes dependable rather than traps;
- make Tier 2 a stronger, recognizable variation of Tier 1;
- reserve major losses and conversions for optional gamechangers;
- provide ordinary routes through the tree without requiring a gamechanger;
- keep nearby nodes mechanically related;
- expose only effects that execute through the same resolver used by combat;
- fit Vintage Story's plain voice without making the entire tree about the Rust
  World.

## Terms

### Starting node

One of the six mutually exclusive nodes in the central hexagon. A starting
node is a special power tier above ordinary Tier 1 and Tier 2. It grants only raw
Strength, Dexterity, and/or Intelligence, establishes affinity from its dominant
stat, and opens its route.

### Route

The broad path growing outward from one starting node. Each route owns an
approximately 60-degree wedge of the tree before later bridge regions begin.
A route may contain several themed sections, but those sections must remain
compatible with the route's declared identity.

### Section

A bounded group of nearby nodes with one mechanical theme. A section declares
its entry, allowed stat families, Tier-1 nodes, Tier-2 variations, ordinary
exits, and reserved gamechanger space.

### Tier 1

A broadly useful baseline node. Tier 1 introduces one stat or one inseparable
pair of stats. It has no explicit downside. Tier is a power-budget category, not
only distance from the center, so later sections may also contain Tier-1 nodes.

### Tier 2

A stronger or slightly narrower variation of a Tier-1 node. It normally gives
two to three times the Tier-1 stat budget. It still has no explicit downside and
does not rewrite a system rule.

### Bridge

An ordinary node or short chain connecting sections or route regions. Bridges
are infrastructure. They may not require a gamechanger.

### Gamechanger

An optional node that changes a rule, converts a resource, or accepts a large
loss for a large gain. `Keystone` is the current legacy data name. New schema
and player-facing language should use `gamechanger`.

## Central Hexagon Rules

Use the diameter of a normal node as layout unit `D`. Layout uses node-relative
units instead of pixels so later socket art and GUI scale do not change the
topology.

- Place the hexagon center at the logical center of the full tree.
- Place the six starting nodes at 60-degree intervals.
- Place starts at approximately `26D` to `28D` from tree center and at roughly
  `54%` to `58%` of the perimeter radius. This keeps them center-ish without
  compressing six route mouths into one central cluster.
- The hexagon may be widened or skewed for readability, but adjacent starting
  nodes should target `26D` to `28D` separation and must remain at least `25D`
  apart.
- A starting node may move at most `1D` from its template point without a layout
  review.
- Keep the space directly inside the hexagon clear during the first pass. Do
  not fill the center merely because it is empty.
- Give each start an outward axis from tree center through its node. Its route
  initially grows within a 60-degree wedge around that axis.
- No link may cross the central hexagon to another starting node.
- Route links and labels must not visually imply that an unchosen start can be
  entered from the center.

The first layout artifact should be generated from a center, radius, rotation,
and optional per-node offset. Six unrelated hand-authored coordinate pairs are
not a repeatable rule.

## Global Shape and Symmetry

The target shape is a **sixfold radial spiderweb with one perimeter ring**.

That phrase describes two scales at once:

- **Global:** a mostly circular tree divided into six rotationally repeated
  sectors, with routes running outward like spokes, cross-rungs joining nearby
  roads, and one clean continuous perimeter cycle.
- **Local:** small polygonal constellations made from triangles, diamonds,
  pentagons, short ladders, and loops, joined at visible junction nodes.

The radial bands are authoring zones, not additional circular roads. Local constellations may push
inward or outward to fit their theme, but the whole tree should still read as a
balanced disk when zoomed out.

### Radial bands

Author against five broad radial bands:

1. **Central hexagon:** the six raw-stat starts and protected empty center.
2. **Inner route band:** each route's first Tier-1 and Tier-2 sections.
3. **Middle web band:** outward lanes, specialization branches, and hybrid sections; it does
   not form a second continuous circle beside the perimeter.
4. **Outer specialization band:** deeper themed constellations and reserved
   gamechanger pockets.
5. **Perimeter express ring:** a continuous, deliberately sparse ordinary cycle
   around the outside of the full tree.

These bands may overlap slightly at their boundaries. Nodes must still have one
declared band for layout auditing.

### Symmetry standard

The tree uses approximate sixfold rotational symmetry, not identical copied
content.

Each of the six sectors should have comparable:

- radial depth;
- node and point-cost totals;
- number of local constellations;
- number of junctions and dead ends;
- access to outward web lanes and inner cross-rungs;
- access to the perimeter ring;
- reserved gamechanger space.

Mechanical themes and exact polygons may differ. A route may bend or widen for
its content, but it should not receive substantially more space, shortcuts, or
ordinary power than its rotated peers. Large asymmetry requires a gameplay
reason and a graph-budget review.

## Tree and Player Scale

The full tree targets approximately **400 authored nodes**. A normal complete
build has approximately **100 spendable talent points**. Late-game progression
may extend that budget to approximately **125 points**.

For planning and graph audits, `P` excludes the mandatory selected start. If the
implementation charges one point for that selection, audit ordinary allocation
against 99 and 124 points instead. The difference must not alter route design.

This scale means:

- a 100-point build can allocate about 25% of the whole tree;
- a 125-point late build can allocate about 31% of the whole tree;
- no build can collect most unrelated sections;
- an expensive breadth build remains possible without erasing specialization;
- meaningful alternatives must exist beyond the player's visible point horizon.

### Node-count planning target

The 400-node goal is a production target, not permission to fill empty space.
Use this approximate composition:

| Node group | Target count | Purpose |
| --- | ---: | --- |
| Central starts | 6 | Complete set of mutually exclusive ordered raw-stat pairs. |
| Local section nodes | 270–290 | Tier 1, Tier 2, and ordinary themed depth. |
| Bridges, junctions, and perimeter travel | 75–95 | Whole-tree reachability and sparse express travel. |
| Gamechangers | 20–35 | Optional transformations authored last. |

The midpoint of these ranges is approximately 400. Do not add low-value nodes
merely to hit the number.

Each of the six sectors should own approximately 67 nodes after shared bridge
and perimeter nodes are assigned to a home sector for auditing. A sector may
vary by about 10% when its geometry or mechanics require it. Larger differences
need an explicit balance review.

The graph audit must report:

- total authored and enabled nodes;
- node totals and total budget by sector;
- counts by start, Tier 1, Tier 2, bridge, perimeter, and gamechanger;
- reachable-node counts from every start at 100 and 125 points;
- shortest costs to each radial band;
- full perimeter and corner-to-corner costs;
- the percentage of the full tree collectable at each player budget.

### Local constellation grammar

- Use small closed or nearly closed polygons to group related choices.
- A constellation normally contains one Tier-1 idea, its Tier-2 variations, and
  one or two ordinary exits. A terminal specialization pod is the exception: it
  has one attachment and no transit exit.
- A visible junction connects constellations; do not hide a five-way travel hub
  inside what looks like a minor stat node.
- Prefer clean triangles, diamonds, pentagons, forks, and short loops over a
  rectangular grid.
- Avoid link crossings. A crossing that looks connected must be connected; an
  unconnected crossing should be rerouted.
- Repeat silhouette families for comparable decisions, but vary orientation and
  spacing enough that sections do not look stamped out.

### Base-stat trunk and specialization-pod grammar

Raw-attribute lines form readable travel trunks. Focused bonuses branch from
those trunks into compact terminal pods instead of being mixed into the route.

- A trunk node grants only a small `STR`, `DEX`, `INT`, or mixed raw-attribute
  package. The trunk must visibly continue past a specialization branch so the
  player can distinguish travel from investment.
- Main-trunk links and junctions receive stronger visual treatment than pod
  links. At overview zoom, the eye must follow the main road without inspecting
  node tooltips.
- Use a repeated route rhythm: **trunk → junction → optional pod or continue**.
  Farther outward, use fewer but larger pods instead of attaching mirrored blobs
  to both sides of every trunk node.
- Inner density comes from compact forks, short ordinary loops, nearby pods, and
  overlapping decision neighborhoods—not from compressing a long straight line
  of interchangeable stat nodes. The emphasized major road must remain visually
  traceable through the denser field.
- Each gap between neighboring starts uses an optional spiderweb: paired
  five-node outer and inner paths with at least three cross-rungs. Players may
  remain on either road or switch between them. No single internal web node may
  be required transit between the two junction endpoints.
- A terminal specialization pod has exactly one external attachment. A through
  cluster may have two attachments when it reconnects to a later junction on
  the same route and the ordinary trunk remains available beside it. Through
  clusters are optional loops, never mandatory transit.
- One pod covers one legible mechanical family, such as totems, critical hits,
  projectiles, bleeding, shields, or a named skill tag. Do not mix unrelated
  families to fill empty nodes.
- A small pod contains roughly **5–8 ordinary nodes**. A large late-route pod
  contains roughly **9–12**. Large pods are sparse focal landmarks rather than
  the default cluster size.
- Nodes nearest the attachment establish the mechanic with Tier-1 bonuses.
  Deeper nodes provide Tier-2 magnitude or variations of that same mechanic.
- Parallel arms should present a real emphasis choice, such as speed versus
  impact, area versus single target, or offense versus sustain. They must not be
  visually different paths containing functionally identical bonuses.
- Every ordinary specialization pod contains at least one split/rejoin choice.
  For `N` pod nodes, use between `N` and `N + 2` internal links: enough for an
  optional path, but not enough to become a tightly connected knot.
- Pod nodes remain at least **1.8D** apart in the capacity layout. Use the
  available web cells before compressing a silhouette below that spacing.
- The terminal focal node may use a larger socket treatment to communicate
  importance while remaining Tier 2. A large socket is not automatically a
  gamechanger; classification depends on mechanics and tradeoffs.
- Do not reserve gamechangers as the automatic cap of a specialization pod.
  Reserve them as one-edge leaves beside ordinary interior road, junction, or
  branch nodes, while ordinary Tier-1/Tier-2 progression visibly continues
  without them.
- A pod's attachment link, internal links, and focal node must remain visually
  readable at the normal tree overview zoom.

This produces a consistent visual sentence: **raw stats travel; compact pods
specialize; isolated side leaves transform rules**.

### Mechanic silhouette vocabulary

Mechanics should have recognizable topology before icons and color are added.
The same mechanic reuses its silhouette family across sectors unless a real
mechanical difference requires a variation.

| Mechanic family | Default silhouette | Visual promise |
| --- | --- | --- |
| Projectile | Forward fan or arrowhead | One source spreading into several trajectories. |
| Totem or placed skill | Rectangular frame | A constructed boundary with visible cross-links. |
| Defense | Shield-shaped closed loop | Protection wrapping around a focal node. |
| Critical | Diamond with a center line | Convergence on a precise payoff. |
| Resource or sustain | Large wheel | Several inputs supporting a central engine. |
| Outer mastery | Crown or branching landmark | A late, recognizable endpoint beyond the express ring. |

Authors may add silhouettes, but may not assign different shapes arbitrarily to
otherwise identical mechanics. Shape is navigation language, not decoration.

### Sparse perimeter grammar

The perimeter express ring trades specialization for efficient travel. It is a
major circular route, not necessarily the outermost occupied coordinate.

- Use **42 allocatable nodes** for one complete perimeter circuit: seven raw-stat
  nodes per sector. This nearly matches the 43-node reference while preserving
  exact sixfold ownership and point-cost symmetry.
- Perimeter nodes are normally `5D` to `9D` apart, substantially farther apart
  than nodes inside local constellations.
- Long perimeter links are intentional. They let one point cross more visual
  and angular distance than an inner route node.
- The six sector-facing perimeter anchors act as broad corners. Traveling from
  corner to corner along the edge should usually cost fewer points than crawling
  between the same sectors through dense inner constellations.
- Every node used to travel around the perimeter is a small Tier-1 raw-attribute
  node containing only `STR`, `DEX`, `INT`, or a small mixture of those
  attributes. The circuit contains no specialized multipliers, derived stats,
  conditions, conversions, downsides, or gamechangers.
- Attribute packages should follow the neighboring sector's theme and rotate
  gradually at sector boundaries. This makes edge travel productive without
  competing with a focused inner constellation.
- Outer-section constellations may branch inward, outward, or diagonally from
  declared perimeter anchors. Nodes beyond the ring must remain terminal
  specialization pods or optional gamechanger leaves; they cannot create a
  second cheaper circumference that makes the express ring obsolete.
- Preserve deliberate whitespace outside the ring. Its purpose is to support
  irregular section silhouettes and late specialization, not to fill every
  corner of the authoring bounds.

## Tier-1 Placement Rules

Tier-1 nodes begin beyond each special starting node and are placed inside their
route or section using the same spacing rules.

- A route's first ordinary Tier-1 nodes sit `4D` to `7D` outward from its start.
- Tier-1 nodes in the same band remain at least `3D` apart.
- A Tier-1 node connects to its section entry or an ordinary approach node.
- Peer Tier-1 nodes do not connect laterally merely to make a web. Their Tier-2
  branches or later bridge nodes create route choices.
- A Tier-1 position must reserve at least one clear outward pocket for its
  Tier-2 variation.
- Keep at least one additional clear side pocket near mechanically important
  Tier-1 nodes for a possible gamechanger authored later.
- Do not bend a route into a neighboring wedge until both routes have completed
  their first ordinary sections.

## Tier-1 Content Rules

A Tier-1 node must:

- contain one primary stat line, or at most two inseparable stat lines;
- use flat or additive increased/reduced operations supported by the resolver;
- provide approximately one local budget unit;
- be broadly useful to a build entering the section for its stated theme;
- have no negative stat, disabled mechanic, resource loss, conversion, or hidden
  obligation;
- avoid rare conditions that leave it inactive during ordinary play;
- use no more than two numeric effect lines;
- put each numeric effect on its own tooltip line;
- cost one point unless the whole tree's point economy is revised deliberately.

The point and path are already opportunity costs. Tier 1 does not need another
punishment.

### Modifier Operation Vocabulary

Talent modifiers use three canonical operations:

- `add` displays as **Flat** and adds a fixed value to the base.
- `increased` displays as **Increased %**. All Increased and Reduced values for
  the same stat combine additively into one percentage.
- `more` displays as **More %**. Each More or Less value is a separate
  multiplier applied after the combined Increased total.

The resolved order is:

```text
(base + total flat)
    × (1 + total increased / 100)
    × product(1 + each more / 100)
```

Use Increased for ordinary repeatable percentage nodes. Reserve More for scarce,
high-impact effects whose multiplicative behavior is part of their budget and
identity. Legacy `percent` content resolves as Increased. Negative Increased is
displayed as Reduced; negative More is displayed as Less.

## Starting-Node Content Rules

Starting nodes are deliberately simpler and stronger than ordinary nodes.

Each starting node must:

- grant only raw Strength, Dexterity, and/or Intelligence;
- use a distinct package related to the mechanical themes placed nearest it;
- have one clearly dominant stat when it grants more than one core stat;
- set `StartingAttributeAffinity` to that dominant stat;
- target approximately twice the budget of a representative adjacent Tier-2
  node;
- have the same total budget as the other five starts within normal rounding;
- contain no derived stat, conditional effect, conversion, downside, class
  mechanic, or gamechanger;
- state that selecting it locks the other five starts until a full reset.

The first-pass package is `+10` to a dominant core stat and `+5` to a secondary
core stat, for 15 raw-stat points total. The six starts exhaust the ordered
pairs: STR/DEX, STR/INT, DEX/STR, DEX/INT, INT/STR, and INT/DEX. A balance pass
may revise the shared `10/5` magnitude, but no single start may use a different
budget or remove one of the six pairings.

Because a start targets roughly twice Tier 2, a 15-point raw package implies a
nearby Tier-2 target of about 7 to 8 raw-stat-equivalent budget. Since Tier 2 is
itself two to three times Tier 1, a comparable Tier-1 target falls around 3
budget units. These are initial ratios, not final balance values.

## Tier-2 Placement Rules

- Place Tier-2 nodes `2.5D` to `4D` beyond or to the side of their Tier-1 parent.
- The link from Tier 1 to Tier 2 must remain visually inside the same section.
- Tier-2 siblings remain at least `2D` apart.
- A Tier-2 node may begin an ordinary exit or bridge toward the next section.
- An interior Tier-2 node may connect to a reserved gamechanger pocket later
  only when ordinary progression visibly continues past or around that node;
  the pocket stays empty during ordinary-tree production.
- Do not place an unrelated Tier-2 effect in unused space beside a Tier-1 node.

## Tier-2 Content Rules

A Tier-2 node must:

- inherit its primary mechanical family from its Tier-1 parent;
- provide between two and three local budget units;
- place at least half its budget in the parent's primary stat family;
- be a stronger focus, a contextual variation, or a small hybrid within the
  same section theme;
- use no more than three numeric effect lines;
- have no explicit downside;
- avoid conversions, immunities, resource replacement, skill replacement, or
  multiplicative rule changes reserved for gamechangers;
- remain understandable without an advanced formula view.

Good Tier-2 variations include:

- more of the same stat for a narrower weapon, element, or skill tag;
- the same output plus one closely related tempo or sustain line;
- a dependable condition such as `while Magic Shield remains` or
  `against a slowed target`, provided the route supplies that condition;
- a choice between area and single-target emphasis without making either branch
  a trap.

If taking a node changes how a resource is paid, removes a defense, prevents a
skill family, or demands a large loss, it is not Tier 2. It is a gamechanger.

## Stat Budget Rules

Every executable stat family needs a budget table defining how much of that stat
equals one local unit in the intended level band.

- Tier 1 targets `1.0` budget unit with a tolerance of `0.15`.
- Tier 2 targets `2.0` to `3.0` budget units.
- A starting node targets approximately twice its route's representative Tier-2
  budget and must remain equal to the other five starts.
- Flat values may scale by level or area only when the resolver supports it.
- Additive percent and multiplicative values do not silently scale with level.
- Multiplicative `more` modifiers are excluded from Tier 1 and normally excluded
  from Tier 2.
- Conditional value is discounted using measured uptime, not author intuition.
- Unsupported mechanics have no budget because they may not appear in the
  player-facing tree.

The first budget matrix should cover only executable Gate A stats: health,
health recovery, armor, evasion, Magic Shield, Mana, resource recovery, physical
damage, projectile damage, supported magic and Rust-bleed damage, critical
access, and supported tempo stats. Summons, ailments, threat, healing, and
class-specific resources wait for their runtimes.

For critical access, ordinary nodes grant Additional Critical Chance. These
sources add together and multiply the default `5%` base; `100% Additional`
therefore resolves to `10%` final chance before other layers. Flat Critical
Chance is deliberately absent from ordinary talent nodes because rare gear and
gear upgrades own that premium scaling lever. More Critical Chance is reserved
for explicit gamechangers. A complete critical section should contribute
several separate Additional sources rather than hiding the whole viable budget
in one mandatory notable.

## Section Theme Rules

Every section declares:

- one short mechanical theme;
- one entry and one or more ordinary exits;
- allowed primary stat families;
- allowed secondary stat families;
- forbidden effects;
- the baseline Tier-1 ideas it supports;
- which Tier-2 variations are valid extensions.

Nearby nodes must solve related problems. Do not put bow precision beside summon
durability because both happened to need an empty location. Hybrid mechanics
belong at route borders or in an explicitly hybrid section.

Within a section, use a consistent set of node roles where the theme supports
them:

| Role | Typical content |
| --- | --- |
| Output | Damage, healing, minion output, or the section's primary result. |
| Tempo | Speed, cooldown, uptime, movement, or action cadence. |
| Sustain | Resource maximum, recovery, efficiency, or continuation. |
| Defense | The route's matching survival layer. |
| Technique | Critical access, area, range, control, or status application. |

A section does not need all five roles. It does need enough repeated node types
to read as a coherent place rather than a loose pile of stats. If a role is not
supported by executable mechanics, leave room instead of inventing a fake node.

The reusable talent-combo catalog and class-style coverage audit live in
[`initial-class-skill-roster.md`](initial-class-skill-roster.md#talent-combos).
Treat those combos as content-planning packages, not player-facing recipes or
exclusive class lanes. Reuse a broad mechanic pod wherever its explicit stats,
tags, statuses, positions, or events apply. Do not duplicate an equivalent node
only to put a class or intended-style name on it.

## Gamechanger Rules

Gamechangers are authored last. A candidate may be created only after its
section's Tier-1, Tier-2, and ordinary bridge paths are complete and playable.

High-level coverage targets may be recorded earlier to reserve conceptual and
layout space, as in the
[`initial class skill roster`](initial-class-skill-roster.md#gamechanger-coverage-targets).
A coverage target is not an authored candidate: it receives no final name,
value, node definition, required build-guide status, or guaranteed placement
until the matching ordinary section passes playtesting.

Every gamechanger must:

- create a mechanical identity felt during play;
- state a major gain and a meaningful loss or constraint on separate lines;
- use mechanics already implemented and inspectable by the stat breakdown;
- attach beside the ordinary interior road, junction, or branch theme it
  transforms rather than capping an ordinary specialization pod;
- normally be the **10th–20th allocation** from the matching selected start
  along its cheapest valid ordinary route, counting both the selected start and
  the gamechanger itself;
- have an attachment node at least **three ordinary graph links** from every
  other gamechanger attachment node;
- avoid touching another gamechanger visually, while accepting that several
  may occupy the same broad neighborhood when their tradeoffs make taking all
  of them undesirable;
- be optional for every build and every route;
- be removable without disconnecting an ordinary node from all six starts;
- never be the shortest mandatory bridge to another section;
- normally be a leaf; an optional loop is allowed only when a complete ordinary
  route already exists;
- avoid being merely `more damage for less defense` unless the exchange changes
  preparation or moment-to-moment play.

Valid shapes include converting Mana costs to Blood, removing Magic Shield to
make another defense scale differently, or changing projectile placement rules.
Exact gamechangers wait until those systems and their tradeoffs are proven.

### Mandatory articulation test

For each gamechanger, remove it and all incident links from a temporary graph.
Every ordinary node reachable before removal must remain reachable from at least
one starting node through ordinary nodes. Startup validation must fail when this
is not true.

Visual review is not enough once the tree grows.

## Ordinary Bridge Rules

- The first section of each route is complete before cross-route bridges are
  authored.
- Bridges begin at Tier 2 or later, never at a central starting node.
- A bridge may approach another route region but may not enter through its
  locked start.
- Major route regions should eventually have at least two ordinary approaches.
- Bridges use broadly compatible stats or a clearly declared hybrid theme.
- A bridge may be less efficient than deep specialization, but it may not be a
  deliberate trap.
- No gamechanger is bridge infrastructure.
- Gamechanger spacing is a minimum readability constraint, not a reason to
  distribute them evenly. Massive tradeoffs, not long travel tax, must be the
  primary reason a build does not collect every nearby gamechanger.

## Whole-Tree Reachability and Travel Budget

Every ordinary node must eventually be reachable from any selected start. The
five unchosen starting nodes remain locked, but their downstream regions must be
enterable through ordinary web paths that bypass those starts.

### Required travel network

- The inner band contains two cross-linked ordinary routes between neighboring
  starts that bypass the locked starts themselves. Removing any one internal
  web node must leave those junction endpoints connected.
- Every gap extends outward through a web lane connected to its inner routes and
  a distinct perimeter entrance. It does not need a giant direct chord to a
  major road; those regions already reconnect through the inner web and ring.
- Specialization pods branch from several depths and sides of the outward web;
  they may not all be stacked beside one major road while other web cells remain
  empty.
- Every sector has at least two independent ordinary approaches to the perimeter
  ring.
- The perimeter ring is one continuous cycle of ordinary nodes and links.
- Perimeter-to-perimeter steps are deliberately the longest ordinary links in
  the tree. Every non-perimeter link must be strictly shorter than the shortest
  express-ring step, otherwise the ring loses its visual travel identity.
- A player can enter the perimeter, travel the entire circumference, and return
  to the entry without taking a gamechanger.
- Removing any one gamechanger leaves the radial web and perimeter cycle
  unchanged.
- Radial web paths remain available for direct specialization, but
  edge travel usually crosses sectors with fewer allocated nodes once the player
  has paid the cost to reach the perimeter.
- The express ring may be the cheapest long-distance route, but it must never be
  the only way to move between sectors. Removing the ring temporarily must leave
  every route region connected through inner and outward web travel.

### Point-budget targets

Use `P = 100` for the normal complete-build audit and `P = 125` for the late-game
audit. As defined above, `P` excludes the mandatory selected start.

Because the graph has a fixed point cost, its travel costs must satisfy both
budgets rather than scaling with the player's current cap. The overlapping
acceptance ranges give these concrete topology targets:

- nearest perimeter entry: roughly **19–25 points** from a selected start;
- full perimeter cycle: **42 points**;
- cheapest entry plus the full cycle: roughly **61–67 points**;
- points left after that traversal: roughly **33–39** at 100 points and
  **58–64** at 125 points.

- Reaching the nearest perimeter entry should cost about `15%` to `25%` of `P`.
- A perimeter corner-to-corner arc should use roughly `25%` to `50%` fewer
  points than a dense inner path connecting comparable sector anchors.
- The perimeter cycle alone consumes `42%` of the normal 100-point budget and
  about `34%` of the 125-point late-game budget.
- The cheapest approach plus one fully allocated perimeter circuit should cost
  about `50%` to `70%` of `P` in total.
- A completed outer-ring traversal should leave about `30%` to `50%` of `P` for
  specialization away from the ring.
- A direct middle path may still be cheaper when the desired node is nearby and
  the player has not already reached the edge. The perimeter becomes efficient
  over long angular distances.
- Travel nodes still grant modest, theme-appropriate raw attributes. The
  expense comes from breadth and the entry commitment, not blank point taxes.

These ratios are acceptance targets rather than fixed node counts. The graph
audit should report shortest-path costs, perimeter-cycle cost, remaining points,
and sector totals at both 100 and 125 points whenever topology changes.

## Lore and Naming Rules

Talent names and descriptions follow the writing style guide and lore index.
Mechanical meaning wins every tie.

- Use short sentences and plain words.
- Name the effect before reaching for flavor.
- Use trade/material language for ordinary physical, armor, tool, and weapon
  sections.
- Use Rust-bleed language only for Fire, Cold, Discharge, Rust, Bleed, temporal
  stability, and explicitly Rust-touched mechanics.
- Use clockwork language for mechanisms, fittings, tuning, cogs, and related
  systems.
- Use provenance language for seraph, ruin, relic, and past-life sections.
- Do not make health, evasion, ordinary weapons, or every gamechanger a symptom
  of the Rust World.
- Do not invent magic schools, named historical ages, or a seventh element.
- Avoid marketing voice, exclamation points, em dashes, hyphen chains, and
  fantasy-name-generator compounds.
- Gamechanger copy states the gain and loss plainly. Flavor may not hide either.

Current prototype names are not precedent. Names such as `Arcane Shelter`,
`Temporal Aegis`, and `Living Bulwark` need a legibility and voice review when
their nodes are migrated.

## Required Data Contract

The current node record has coordinates, links, modifiers, `starter`,
`foundation`, and legacy `keystone` state. A six-route scalable tree needs:

- `route`: owning global route code;
- `section`: owning section code;
- `tier`: `start`, `tier1`, `tier2`, `bridge`, or `gamechanger`;
- `role`: `output`, `tempo`, `sustain`, `defense`, `technique`, or `bridge`;
- `topologyRole`: `trunk`, `podEntry`, `podMember`, `podFocal`, `bridge`, or
  `perimeter`;
- optional `specializationPod`: owning terminal pod code; absent on travel
  trunks and ordinary bridges;
- optional `podMode`: `terminal` or `through`;
- optional `podSize`: `small` or `large`;
- optional `mechanic`: stable mechanical-family code used to validate
  silhouette consistency;
- `budgetUnits`: author-declared balance budget for linting;
- `gamechanger`: explicit replacement for player-facing `keystone` language;
- `startingNode`: marks one of exactly six global starts;
- `primaryAffinity`: derived from the starting package's dominant raw stat and
  persisted explicitly for stable tie behavior;
- optional layout band and order metadata for deterministic generation or audit;
- authoring layout metadata containing template, instance, slot, generated
  coordinates, and persistent manual offsets. Runtime may consume only the
  resulting final coordinates.

A route registry should define its start angle, center radius, outward axis,
theme, allowed stat families, and initial sections. A section registry should
define local bounds, theme, allowed stats, and exits. Coordinates may remain on
nodes, but they should be generated or checked against templates instead of
being unconstrained hand placement.

A specialization-pod registry should define its attachment node, mechanical
family, permitted stats, focal node, mode, size, and silhouette. Gamechanger
pockets belong to the surrounding road or branch registry rather than to the
pod's automatic cap. This makes cluster topology machine-checkable instead of
relying on visual inspection.

## Layout Generation and Manual Editing

Talent layout uses a deterministic development-time generator with explicit
manual offsets. Runtime never procedurally changes node positions.

- A checked-in layout manifest owns world size, center, band radii, route axes,
  perimeter count, template instances, and manual offsets.
- The generator owns default node positions and template-internal links for the
  central hexagon, perimeter, raw-stat trunks, and specialization pods.
- Designers own node mechanics, cross-template links, template selection,
  gamechanger placement, and manual offsets.
- Generated nodes retain `generatedX` and `generatedY`. Their displayed position
  is `generated + manual offset`.
- Manual dragging writes offsets rather than replacing generated coordinates.
  Regeneration therefore preserves deliberate visual polishing.
- A node may opt into an explicit fully manual position only when ordinary
  templates and offsets cannot express its layout. The exception needs an
  authoring note.
- Generated preview and runtime assets must be deterministic. A check command
  rejects stale generated output.
- The workbench must support pan, zoom, guide overlays, node dragging, resetting
  one node or the whole layout, and exporting manual overrides.
- Player and admin clients render the server's active tree from a standalone,
  schema-versioned and content-hashed snapshot. A custom server tree must not
  require clients to download or install generated mod assets.
- The native player tree and admin editor share one graph component for fit,
  pan, zoom, hit testing, edge/socket rendering, and selection. Authoring
  controls remain outside the graph component.
- Admin changes live in an isolated draft. Moving nodes or changing fields does
  not broadcast gameplay state. Pressing Save validates and commits exactly one
  new revision.
- A changed saved revision broadcasts the tree once, reconciles removed or
  disconnected allocations with refunds, recalculates derived resources, and
  refreshes online players without restarting. An unchanged Save is a no-op.
- The workbench must eventually import the same compact saved tree/overlay and
  flattened runtime snapshot exported by the mod. Import preserves stable node
  codes and manual offsets and rejects incompatible schemas before mutation.

The current scaffold uses a `5600 × 5600` authoring world, a start radius of
`1200`, and a perimeter radius of `2150`. With `D = 44`, neighboring starts are
about `27.3D` apart and the starts sit at about `56%` of the perimeter radius.
These are first-pass acceptance values, not immutable pixel requirements.

The capacity preview contains 426 ordinary scaffold nodes: six starts, a
42-node express ring, 60 major-road junction nodes, 60 nodes in six optional
inner spiderwebs, 30 outward-web nodes, and 228 nodes in 30 specialization pods.
Six large mastery pods and six alternating small mechanic
pods extend beyond the express ring. This preview proves spatial capacity; it
is not permission to replace deliberate
whitespace with low-value final nodes.

Startup validation must reject:

- a starting-node count other than six;
- direct links between starting nodes;
- central starts outside hexagon spacing tolerance;
- anything other than the complete six ordered dominant-secondary core-stat
  pairings or a dominant-attribute count other than two per core attribute;
- a starting node containing anything other than raw STR/DEX/INT;
- unequal starting-node budgets or a package without a dominant affinity;
- a start whose budget is not approximately twice its route's representative
  Tier-2 budget;
- missing route or section ownership;
- a `trunk`, `junction`, `branch`, or `perimeter` node containing anything except raw
  STR/DEX/INT;
- a small specialization pod outside five-to-eight nodes or a large pod outside
  nine-to-twelve nodes without an explicit authored exception;
- a specialization pod whose ordinary nodes are internally disconnected;
- a terminal pod with anything other than one ordinary external attachment;
- a through cluster with anything other than two ordinary external attachments,
  or whose ordinary trunk alternative is missing;
- one mechanic family using unrelated silhouette templates without an authored
  mechanical reason;
- a pod node using a stat outside its declared mechanical family;
- a pod focal node that cannot be reached through its declared Tier-1 and
  Tier-2 progression;
- Tier-1 or Tier-2 negative modifiers;
- Tier-2 nodes outside the two-to-three-unit budget;
- node stats forbidden by their section;
- unreachable ordinary nodes;
- gamechangers that are articulation points;
- a gamechanger pocket closer than 10 or farther than 20 ordinary allocations
  from its matching start;
- gamechanger attachment nodes fewer than three ordinary graph links apart, or
  gamechanger sockets that touch visually;
- an inner spiderweb without exactly two junction endpoints, two optional
  routes, cross-rungs, or resilience after any one internal node is removed;
- an outward web lane that links circumferentially into a second circle, lacks
  an inner-web or perimeter connection, or fails to distribute specialization
  pods across two outward junctions plus at least one inner-web junction;
- a non-perimeter link as long as or longer than the shortest express-ring step;
- a specialization pod with no optional internal path, more than `N + 2`
  internal links, or less than `1.8D` node spacing;
- effects not registered with the executable stat resolver;
- overlapping nodes, unrelated link crossings, or an unrelated link entering
  any node's protected socket radius anywhere in the authored world.

## Production Plan

### Phase 0: choose the six route identities

For each route, decide:

- player-facing name and one-sentence promise;
- primary mechanical family;
- defense or sustain relationship;
- allowed and forbidden stats;
- raw starting-stat package and dominant tie affinity;
- first section theme;
- how it differs from class identity, since classes remain a separate dual-class
  system.

Do not author final nodes until all six route cards are distinct and together
cover the intended build space.

### Phase 1: add schema and structural validation

- Add route, section, tier, role, budget, starting-node, and gamechanger fields.
- Persist six-route selection separately while deriving core-stat affinity from
  the selected start's dominant raw stat.
- Generate the central hexagon from a template.
- Add spacing, theme, reachability, budget, and articulation validation.
- Treat the current nine-node graph as a disposable runtime fixture.

### Phase 2: freeze the executable stat budget

- Finish the resolved stat pipeline for every stat used by the first sections.
- Establish one-unit values for the intended early and midgame bands.
- Add stat, talent, and skill-hit breakdown tools.
- Do not load player-facing nodes for unresolved defenses or modifiers.

### Phase 3: build the six starts and first sections

- Author the six central raw-stat starting nodes as a special tier.
- Give each route enough Tier-1 nodes to establish its repeated node types.
- Give each Tier-1 node one Tier-2 variation before adding a second variation.
- Reserve ordinary exits and empty gamechanger pockets beside interior road or
  branch nodes at the required 10–20-point depth.
- Complete and test one route at a time while keeping all six start positions
  visible in the layout.
- Maintain the approximate 67-node final capacity of each sector even while only
  its first sections are populated.

### Phase 4: add ordinary depth and bridges

- Add second Tier-2 variations only where they create real choices.
- Add further themed sections outward within each route wedge.
- Connect mature route regions with ordinary bridges that bypass locked starts.
- Run reachability and visual audits after every section.

### Phase 5: author gamechangers last

- Identify transformations suggested by ordinary-node playtests.
- Place them only in reserved pockets beside the relevant theme.
- Implement and test the complete gain and loss before exposing the node.
- Run the articulation test and verify no route guide quietly assumes them.

### Phase 6: acceptance

- Compare at least two builds from every starting route.
- Verify Tier 2 is visibly stronger without making Tier 1 feel wasted.
- Verify all displayed values match the server resolver.
- Test selection, pan, zoom, allocation, reset, reconnect, and old-save migration.
- Have a tester identify a section's theme without reading every tooltip.
- Reject any region that reads as a loose stat pile.

## First-Pass Definition of Done

The first scalable pass is complete when:

- six mutually exclusive raw-stat starts form the central hexagon;
- the complete authored tree is approximately 400 meaningful nodes;
- normal and late-game graph audits use approximately 100 and 125 spendable
  points respectively;
- every start is approximately twice an adjacent Tier-2 budget and all six
  starts are equal in total measured value;
- each start opens a distinct, themed, executable route;
- every first-section Tier-1 node has at least one Tier-2 variation;
- every Tier-2 node measures at two to three times its parent budget;
- ordinary route depth and bridges do not require a gamechanger;
- gamechanger pockets are reserved but empty;
- startup validation catches topology, budget, and unsupported-effect errors;
- the Hub reports the same effects the server applies;
- node names pass the writing-guide review.
