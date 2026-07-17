# Combat Visuals Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the combat visuals framework from `docs/superpowers/specs/2026-07-16-combat-visuals-design.md`: server publishes status sync, ground areas, and combat visual events; the client owns all rendering through a `VisualDirector` feeding five renderers with budgets and options.

**Architecture:** Three server→client channels (entity WatchedAttributes status tree, ground-area upsert/remove packets, one generic `CombatVisualEventPacket`), all consumed by one client `VisualDirector` that dispatches to renderers (skill FX particles, ground telegraph meshes, nameplate status overlay, combat text, player-state HUD). Pure logic (sync format, text merging, budgets, area stores) lives in engine-light classes with unit tests.

**Tech Stack:** C# 12 / net10.0, Vintage Story 1.22 modding API (`VintagestoryAPI`), protobuf-net packets on the existing `vrpg` channel, Cairo for HUD drawing, xunit for tests.

## Global Constraints

- Server never spawns particles or draws; it publishes state and events only (spec: Approach A).
- Packets and synced state carry only codes; the client resolves visuals from its own asset registry. Unknown style codes fall back to a generic style from the definition color — new content is never invisible.
- Status sync writes happen only on apply/refresh/deepen/consume/expire, never per tick. Durations sync as remaining-time-at-write plus a revision counter (server and client clocks differ, so no absolute cross-clock timestamps).
- Priority classes P0–P3; P0 (telegraphs, threshold flashes, window pulses, current target's overlay, own buff row) never degrades.
- All new options are client-side, in a "Combat Visuals" Hub Options category; toggles disable presentation, never the underlying sync.
- Never add `Co-Authored-By` or "Generated with Claude Code" trailers to commits (user rule).
- Existing code style: no `var` for non-obvious types is NOT enforced, but the codebase uses explicit types, four-space indent, `sealed` classes, and `StringComparer.OrdinalIgnoreCase` dictionaries — match it.
- Build with `dotnet build VRPG/VRPG.csproj` from `/home/garward/Games/Games/VintageStory/Modding`; it deploys to the local Mods folder automatically. Tests: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj`.
- Engine API signatures (particle, shader, renderer) should be verified against the game source in `Sources/` or the installed game at `/home/garward/Games/Games/VintageStory/vintagestory` when a call does not compile; keep the behavior, adjust the member name.

## File Structure

New files:

| Path | Responsibility |
| --- | --- |
| `VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` | xunit test project referencing VRPG |
| `VRPG/tests/VRPG.Tests/*.cs` | Unit tests for pure logic |
| `VRPG/src/Data/Definitions/StatusVisualDefinition.cs` | JSON `visual` block for status effects |
| `VRPG/src/Modules/Rpg/StatusEffects/StatusSync.cs` | Status tree write/read format (universal, pure) |
| `VRPG/src/Client/Visuals/EntityStatusCache.cs` | Client per-entity status countdown cache (pure) |
| `VRPG/src/Network/CombatVisualPackets.cs` | Event packet, kinds, flags, damage-type ids |
| `VRPG/src/Modules/Rpg/Combat/CombatVisualBroadcaster.cs` | Server range broadcast of visual events |
| `VRPG/src/Client/Visuals/CombatVisualsConfig.cs` | Client-local visual settings |
| `VRPG/src/Client/Visuals/VisualStyleResolver.cs` | styleCode → color/particle params |
| `VRPG/src/Client/Visuals/VisualDirector.cs` | Single intake; routes events to renderers |
| `VRPG/src/Client/Visuals/SkillFxRenderer.cs` | Client-side burst/ray/circle particles |
| `VRPG/src/Network/GroundAreaPackets.cs` | Area upsert/remove packets, shape/state enums |
| `VRPG/src/Modules/Rpg/Combat/GroundAreaService.cs` | Server area registry + broadcast + expiry |
| `VRPG/src/Client/Visuals/GroundAreaStore.cs` | Client mirror of area records (pure) |
| `VRPG/src/Client/Visuals/GroundTelegraphRenderer.cs` | Flat disc/ring world renderer |
| `VRPG/src/Client/Visuals/CombatTextModel.cs` | Merge/cap/priority text model (pure) |
| `VRPG/src/Client/HudElementVRPGCombatText.cs` | Floating text HUD renderer |
| `VRPG/src/Client/Visuals/AuraFamilies.cs` | Aura family particle recipes |
| `VRPG/src/Client/Visuals/AuraEmitterSystem.cs` | Per-entity status aura emitters |
| `VRPG/src/Client/HudElementVRPGPlayerStatus.cs` | Own buff/debuff row HUD |
| `VRPG/src/Client/HudElementVRPGWindowPulse.cs` | Crosshair-adjacent window pulse |
| `VRPG/src/Client/Visuals/VisualBudget.cs` | Channel budgets + degradation policy (pure) |

Modified files (anchors given per task): `StatusEffectDefinition.cs`, `StatusEffectInstance.cs`, `StatusEffectTracker.cs`, `RpgModule.cs`, `VRPGNetwork.cs`, `VRPGModSystem.cs`, `SkillCastingService.cs`, `EvasiveStepService.cs`, `EntityVrpgSkillProjectile.cs`, `SkillLoadoutPacket.cs`, `HudElementVRPGEntityHealthBars.cs`, `GuiElementVrpgSkillHotbar.cs`, `GuiElementVrpgHub.cs`, `GuiDialogVRPGHub.cs`, `VRPGConfig.cs`, `RpgCommandSystem.cs` (or `VRPGModSystem.RegisterCommands`), plus status-effect JSON assets.

Phases: Tasks 1–5 plumbing and migration (playable, no regressions), 6–7 ground areas, 8–13 renderers, 14–16 budgets/options/tooling. Each task ends buildable; in-game acceptance steps are marked.

---

### Task 1: Test project and status visual definitions

**Files:**
- Create: `VRPG/tests/VRPG.Tests/VRPG.Tests.csproj`
- Create: `VRPG/tests/VRPG.Tests/StatusVisualDefinitionTests.cs`
- Create: `VRPG/src/Data/Definitions/StatusVisualDefinition.cs`
- Modify: `VRPG/src/Data/Definitions/StatusEffectDefinition.cs` (add `Visual` property)

**Interfaces:**
- Produces: `StatusVisualDefinition { string Icon, string Color, string Aura, float AuraIntensityPerStack, bool ShowStacks, StatusBuildupVisualDefinition? Buildup }`; `StatusBuildupVisualDefinition { bool ShowBar, float Threshold, bool FlashAtThreshold }`; `StatusEffectDefinition.Visual` (never null, defaults). Tasks 10 and 11 read these.

- [ ] **Step 1: Create the test project**

`VRPG/tests/VRPG.Tests/VRPG.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>12.0</LangVersion>
    <IsPackable>false</IsPackable>
    <PackageOnRelease>false</PackageOnRelease>
    <DisableSharedDeploy>true</DisableSharedDeploy>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../VRPG.csproj" />
  </ItemGroup>

</Project>
```

The repo-root `Directory.Build.props` will resolve Vintage Story dlls for the transitive reference. If the shared `Directory.Build.targets` tries to deploy or package this project, set the same opt-out properties `ActiveFarming` uses (see the root props file's condition block).

- [ ] **Step 2: Write the failing test**

`VRPG/tests/VRPG.Tests/StatusVisualDefinitionTests.cs`:

```csharp
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class StatusVisualDefinitionTests
{
    [Fact]
    public void StatusEffectDefinitionHasNonNullVisualDefaults()
    {
        var definition = new StatusEffectDefinition();
        Assert.NotNull(definition.Visual);
        Assert.Equal("", definition.Visual.Icon);
        Assert.Equal("", definition.Visual.Aura);
        Assert.True(definition.Visual.ShowStacks);
        Assert.Equal(1f, definition.Visual.AuraIntensityPerStack);
        Assert.Null(definition.Visual.Buildup);
    }

    [Fact]
    public void BuildupDefaultsSupportThresholdDisplay()
    {
        var buildup = new StatusBuildupVisualDefinition();
        Assert.True(buildup.ShowBar);
        Assert.Equal(100f, buildup.Threshold);
        Assert.True(buildup.FlashAtThreshold);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj`
Expected: compile FAIL — `StatusEffectDefinition` has no `Visual`, `StatusVisualDefinition` does not exist.

- [ ] **Step 4: Implement the definitions**

`VRPG/src/Data/Definitions/StatusVisualDefinition.cs`:

```csharp
namespace VRPG.Data.Definitions;

public sealed class StatusVisualDefinition
{
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";
    public string Aura { get; set; } = "";
    public float AuraIntensityPerStack { get; set; } = 1f;
    public bool ShowStacks { get; set; } = true;
    public StatusBuildupVisualDefinition? Buildup { get; set; }
}

public sealed class StatusBuildupVisualDefinition
{
    public bool ShowBar { get; set; } = true;
    public float Threshold { get; set; } = 100f;
    public bool FlashAtThreshold { get; set; } = true;
}
```

In `StatusEffectDefinition.cs`, after the `VisualHint` property add:

```csharp
    public StatusVisualDefinition Visual { get; set; } = new StatusVisualDefinition();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj`
Expected: PASS (2 tests). Also run `dotnet build VRPG/VRPG.csproj` — clean build.

- [ ] **Step 6: Commit**

```bash
git add VRPG/tests VRPG/src/Data/Definitions/StatusVisualDefinition.cs VRPG/src/Data/Definitions/StatusEffectDefinition.cs
git commit -m "Add VRPG test project and status effect visual definitions"
```

---

### Task 2: Status sync — server writer, client cache, sample assets

**Files:**
- Modify: `VRPG/src/Modules/Rpg/StatusEffects/StatusEffectInstance.cs` (magnitude, duration)
- Modify: `VRPG/src/Modules/Rpg/StatusEffects/StatusEffectTracker.cs` (change event, magnitude apply)
- Create: `VRPG/src/Modules/Rpg/StatusEffects/StatusSync.cs`
- Create: `VRPG/src/Client/Visuals/EntityStatusCache.cs`
- Modify: `VRPG/src/Modules/Rpg/RpgModule.cs` (wire tracker → WatchedAttributes)
- Modify: `VRPG/assets/vrpg/vrpg/statuseffects/burn.json`, `bleed.json`, `chill.json` (add `visual` blocks)
- Create: `VRPG/assets/vrpg/vrpg/statuseffects/corrosion.json`, `stagger.json`
- Test: `VRPG/tests/VRPG.Tests/StatusSyncTests.cs`

**Interfaces:**
- Consumes: `StatusEffectInstance`, `StatusEffectTracker` (existing), `StatusVisualDefinition` (Task 1).
- Produces:
  - `StatusEffectInstance.Magnitude` (float, additive via `AddMagnitude(float)`), `StatusEffectInstance.DurationSeconds` (float, total at last refresh).
  - `StatusEffectTracker.Changed` — `event Action<long>?` fired with the entity id on apply/refresh and once per entity whose effects expired during `Tick`.
  - `StatusEffectTracker.Apply(long targetEntityId, string effectCode, long sourceEntityId = 0, float durationSeconds = 0, int stacks = 1, float magnitude = 0f)`.
  - `StatusSync.TreeKey == "vrpgStatus"`; `StatusSync.Write(ITreeAttribute entityAttributes, IReadOnlyList<StatusEffectInstance> effects)`; `StatusSync.Read(ITreeAttribute? entityAttributes)` → `List<SyncedStatus>`; `SyncedStatus { string Code; int Stacks; float Magnitude; int RemainingMs; int DurationMs; int Rev }`.
  - `EntityStatusCache.Update(long entityId, ITreeAttribute? entityAttributes, long nowMs)` → `IReadOnlyList<ActiveStatus>`; `ActiveStatus { string Code; int Stacks; float Magnitude; long EndMs; int DurationMs; float RemainingSeconds(long nowMs) }`; `EntityStatusCache.Prune(long nowMs)`. Tasks 10, 11, 12 consume `ActiveStatus`.

- [ ] **Step 1: Write the failing tests**

`VRPG/tests/VRPG.Tests/StatusSyncTests.cs`:

```csharp
using System.Collections.Generic;
using VRPG.Client.Visuals;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.StatusEffects;
using Vintagestory.API.Datastructures;
using Xunit;

namespace VRPG.Tests;

public sealed class StatusSyncTests
{
    private static StatusEffectInstance Instance(string code, float remainingSeconds, int stacks, float magnitude)
    {
        var definition = new StatusEffectDefinition { Code = code, MaxStacks = 10 };
        var instance = new StatusEffectInstance(definition, 0, remainingSeconds, stacks);
        instance.AddMagnitude(magnitude);
        return instance;
    }

    [Fact]
    public void WriteThenReadRoundTripsEffects()
    {
        var attributes = new TreeAttribute();
        StatusSync.Write(attributes, new List<StatusEffectInstance>
        {
            Instance("vrpg:corrosion", 6.5f, 3, 0f),
            Instance("vrpg:stagger", 4f, 1, 42f)
        });

        List<SyncedStatus> read = StatusSync.Read(attributes);
        Assert.Equal(2, read.Count);
        SyncedStatus corrosion = read.Find(s => s.Code == "vrpg:corrosion")!;
        Assert.Equal(3, corrosion.Stacks);
        Assert.Equal(6500, corrosion.RemainingMs);
        SyncedStatus stagger = read.Find(s => s.Code == "vrpg:stagger")!;
        Assert.Equal(42f, stagger.Magnitude);
        Assert.Equal(1, stagger.Rev);
    }

    [Fact]
    public void EachWriteBumpsRevision()
    {
        var attributes = new TreeAttribute();
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:burn", 3f, 1, 0f) });
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:burn", 3f, 2, 0f) });
        Assert.Equal(2, StatusSync.Read(attributes)[0].Rev);
    }

    [Fact]
    public void WritingEmptyListClearsTheTree()
    {
        var attributes = new TreeAttribute();
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:burn", 3f, 1, 0f) });
        StatusSync.Write(attributes, new List<StatusEffectInstance>());
        Assert.Empty(StatusSync.Read(attributes));
    }

    [Fact]
    public void CacheCountsDownLocallyBetweenRevisions()
    {
        var attributes = new TreeAttribute();
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:bleed", 5f, 2, 0f) });

        var cache = new EntityStatusCache();
        IReadOnlyList<ActiveStatus> first = cache.Update(7, attributes, nowMs: 1000);
        Assert.Single(first);
        Assert.Equal(6000, first[0].EndMs);
        Assert.Equal(5f, first[0].RemainingSeconds(1000), 2);

        // No new write: two seconds later the same end time holds.
        IReadOnlyList<ActiveStatus> later = cache.Update(7, attributes, nowMs: 3000);
        Assert.Equal(6000, later[0].EndMs);
        Assert.Equal(3f, later[0].RemainingSeconds(3000), 2);

        // A refresh write resets the countdown from the new remaining time.
        StatusSync.Write(attributes, new List<StatusEffectInstance> { Instance("vrpg:bleed", 5f, 3, 0f) });
        IReadOnlyList<ActiveStatus> refreshed = cache.Update(7, attributes, nowMs: 4000);
        Assert.Equal(9000, refreshed[0].EndMs);
        Assert.Equal(3, refreshed[0].Stacks);
    }

    [Fact]
    public void TrackerFiresChangedOnApplyAndExpiry()
    {
        var data = new VRPG.Data.VRPGDataRegistry();
        var tracker = new StatusEffectTracker(data);
        // Registry is empty in tests; Apply with unknown code returns false and must not fire.
        var changed = new List<long>();
        tracker.Changed += id => changed.Add(id);
        Assert.False(tracker.Apply(5, "vrpg:missing"));
        Assert.Empty(changed);
    }
}
```

Note: the tracker's positive-path (apply/expiry firing) needs a definition in the registry; `VrpgDataStore` loads from assets only. Cover the positive path by extracting the tracker's list mutation? No — keep it simple: the tracker change is four lines and is exercised in game via Task 16's `/vrpg vfx status`. The unit tests above cover the sync format and cache, which hold the real logic.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj`
Expected: compile FAIL — `StatusSync`, `EntityStatusCache`, `AddMagnitude`, `Changed` missing.

- [ ] **Step 3: Extend StatusEffectInstance**

Replace the body of `StatusEffectInstance.cs` with:

```csharp
using VRPG.Data.Definitions;

namespace VRPG.Modules.Rpg.StatusEffects;

public sealed class StatusEffectInstance
{
    public StatusEffectInstance(StatusEffectDefinition definition, long sourceEntityId, float durationSeconds, int stacks)
    {
        Definition = definition;
        SourceEntityId = sourceEntityId;
        RemainingSeconds = durationSeconds;
        DurationSeconds = durationSeconds;
        Stacks = stacks;
    }

    public StatusEffectDefinition Definition { get; }
    public long SourceEntityId { get; }
    public float RemainingSeconds { get; private set; }
    public float DurationSeconds { get; private set; }
    public int Stacks { get; private set; }
    public float Magnitude { get; private set; }

    public void Tick(float dt)
    {
        RemainingSeconds -= dt;
    }

    public void Refresh(float durationSeconds, int stacks)
    {
        RemainingSeconds = System.Math.Max(RemainingSeconds, durationSeconds);
        DurationSeconds = System.Math.Max(DurationSeconds, durationSeconds);
        Stacks = System.Math.Min(System.Math.Max(1, Definition.MaxStacks), System.Math.Max(Stacks, stacks));
    }

    public void AddMagnitude(float amount)
    {
        Magnitude = System.Math.Clamp(Magnitude + amount, 0f, 10000f);
    }
}
```

- [ ] **Step 4: Extend StatusEffectTracker**

In `StatusEffectTracker.cs`:

1. Add below the fields: `public event System.Action<long>? Changed;`
2. Change the `Apply` signature to `public bool Apply(long targetEntityId, string effectCode, long sourceEntityId = 0, float durationSeconds = 0, int stacks = 1, float magnitude = 0f)`.
3. In the existing-instance branch, after `existing.Refresh(duration, clampedStacks);` insert `existing.AddMagnitude(magnitude); Changed?.Invoke(targetEntityId);` (before `return true;`).
4. After `effects.Add(new StatusEffectInstance(...));` insert:

```csharp
        if (magnitude != 0f)
        {
            effects[effects.Count - 1].AddMagnitude(magnitude);
        }

        Changed?.Invoke(targetEntityId);
```

5. In `Tick`, track removals: before the inner `for` loop add `bool removedAny = false;`, set `removedAny = true;` where `effects.RemoveAt(i);` happens, and after the inner loop (before the `effects.Count == 0` check) add:

```csharp
            if (removedAny)
            {
                Changed?.Invoke(entityId);
            }
```

- [ ] **Step 5: Implement StatusSync**

`VRPG/src/Modules/Rpg/StatusEffects/StatusSync.cs`:

```csharp
using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace VRPG.Modules.Rpg.StatusEffects;

/// <summary>
/// Wire format for the vrpgStatus WatchedAttributes tree. Durations sync as
/// remaining-time-at-write plus a revision counter because server and client
/// clocks are different domains; the client counts down locally between writes.
/// </summary>
public static class StatusSync
{
    public const string TreeKey = "vrpgStatus";

    public static void Write(ITreeAttribute entityAttributes, IReadOnlyList<StatusEffectInstance> effects)
    {
        int rev = (entityAttributes.GetTreeAttribute(TreeKey)?.GetInt("rev") ?? 0) + 1;
        var tree = new TreeAttribute();
        tree.SetInt("rev", rev);
        var list = new TreeAttribute();
        for (int i = 0; i < effects.Count; i++)
        {
            StatusEffectInstance effect = effects[i];
            var node = new TreeAttribute();
            node.SetInt("stacks", effect.Stacks);
            node.SetFloat("magnitude", effect.Magnitude);
            node.SetInt("remainingMs", (int)(System.Math.Max(0f, effect.RemainingSeconds) * 1000f));
            node.SetInt("durationMs", (int)(System.Math.Max(0f, effect.DurationSeconds) * 1000f));
            list[effect.Definition.Code] = node;
        }

        tree["effects"] = list;
        entityAttributes[TreeKey] = tree;
    }

    public static List<SyncedStatus> Read(ITreeAttribute? entityAttributes)
    {
        var result = new List<SyncedStatus>();
        ITreeAttribute? tree = entityAttributes?.GetTreeAttribute(TreeKey);
        ITreeAttribute? list = tree?.GetTreeAttribute("effects");
        if (tree == null || list == null)
        {
            return result;
        }

        int rev = tree.GetInt("rev");
        foreach (KeyValuePair<string, IAttribute> pair in list)
        {
            if (pair.Value is not ITreeAttribute node)
            {
                continue;
            }

            result.Add(new SyncedStatus
            {
                Code = pair.Key,
                Stacks = node.GetInt("stacks", 1),
                Magnitude = node.GetFloat("magnitude"),
                RemainingMs = node.GetInt("remainingMs"),
                DurationMs = node.GetInt("durationMs"),
                Rev = rev
            });
        }

        return result;
    }
}

public sealed class SyncedStatus
{
    public string Code = "";
    public int Stacks;
    public float Magnitude;
    public int RemainingMs;
    public int DurationMs;
    public int Rev;
}
```

- [ ] **Step 6: Implement EntityStatusCache**

`VRPG/src/Client/Visuals/EntityStatusCache.cs`:

```csharp
using System.Collections.Generic;
using VRPG.Modules.Rpg.StatusEffects;
using Vintagestory.API.Datastructures;

namespace VRPG.Client.Visuals;

/// <summary>
/// Client-side view of an entity's synced statuses. Recomputes local end times
/// only when the synced revision changes; otherwise counts down locally.
/// </summary>
public sealed class EntityStatusCache
{
    private sealed class EntityEntry
    {
        public int Rev = -1;
        public long LastSeenMs;
        public readonly List<ActiveStatus> Statuses = new List<ActiveStatus>();
    }

    private readonly Dictionary<long, EntityEntry> byEntityId = new Dictionary<long, EntityEntry>();

    public IReadOnlyList<ActiveStatus> Update(long entityId, ITreeAttribute? entityAttributes, long nowMs)
    {
        if (!byEntityId.TryGetValue(entityId, out EntityEntry? entry))
        {
            entry = new EntityEntry();
            byEntityId[entityId] = entry;
        }

        entry.LastSeenMs = nowMs;
        int rev = entityAttributes?.GetTreeAttribute(StatusSync.TreeKey)?.GetInt("rev") ?? 0;
        if (rev != entry.Rev)
        {
            entry.Rev = rev;
            entry.Statuses.Clear();
            foreach (SyncedStatus status in StatusSync.Read(entityAttributes))
            {
                entry.Statuses.Add(new ActiveStatus
                {
                    Code = status.Code,
                    Stacks = status.Stacks,
                    Magnitude = status.Magnitude,
                    DurationMs = status.DurationMs,
                    EndMs = nowMs + status.RemainingMs
                });
            }
        }

        entry.Statuses.RemoveAll(status => status.EndMs <= nowMs);
        return entry.Statuses;
    }

    public void Prune(long nowMs)
    {
        var stale = new List<long>();
        foreach (KeyValuePair<long, EntityEntry> pair in byEntityId)
        {
            if (nowMs - pair.Value.LastSeenMs > 10000)
            {
                stale.Add(pair.Key);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            byEntityId.Remove(stale[i]);
        }
    }
}

public sealed class ActiveStatus
{
    public string Code = "";
    public int Stacks;
    public float Magnitude;
    public long EndMs;
    public int DurationMs;

    public float RemainingSeconds(long nowMs)
    {
        return System.Math.Max(0f, (EndMs - nowMs) / 1000f);
    }
}
```

- [ ] **Step 7: Wire the tracker to WatchedAttributes in RpgModule**

In `RpgModule.StartServerSide`, after `serverApi = api;` add:

```csharp
        statusEffects.Changed += entityId =>
        {
            Vintagestory.API.Common.Entities.Entity? entity = serverApi?.World.GetEntityById(entityId);
            if (entity == null)
            {
                return;
            }

            StatusSync.Write(entity.WatchedAttributes, statusEffects.Get(entityId));
            entity.WatchedAttributes.MarkPathDirty(StatusSync.TreeKey);
        };
```

- [ ] **Step 8: Add visual blocks to status assets**

Add to `VRPG/assets/vrpg/vrpg/statuseffects/burn.json`, `bleed.json`, `chill.json` a top-level `visual` member (merge into the existing JSON object, keeping every existing field):

```json
"visual": { "icon": "burn", "color": "#f06a28", "aura": "embers" }
```

```json
"visual": { "icon": "bleed", "color": "#b81c2e", "aura": "drips" }
```

```json
"visual": { "icon": "chill", "color": "#78c9f0", "aura": "frost" }
```

Create `VRPG/assets/vrpg/vrpg/statuseffects/corrosion.json`:

```json
{
  "code": "vrpg:corrosion",
  "name": "Corrosion",
  "description": "Rust eats at the target, dealing damage over time.",
  "kind": "ailment",
  "polarity": "debuff",
  "stackMode": "refresh",
  "maxStacks": 10,
  "defaultDurationSeconds": 8.0,
  "tags": ["rust", "ailment", "dot"],
  "visual": { "icon": "rust", "color": "#c96a1e", "aura": "rustflakes", "auraIntensityPerStack": 0.6 }
}
```

Create `VRPG/assets/vrpg/vrpg/statuseffects/stagger.json`:

```json
{
  "code": "vrpg:stagger",
  "name": "Stagger",
  "description": "Posture damage builds toward a breaking point.",
  "kind": "buildup",
  "polarity": "debuff",
  "stackMode": "refresh",
  "maxStacks": 1,
  "defaultDurationSeconds": 6.0,
  "tags": ["physical", "control", "buildup"],
  "visual": {
    "icon": "fracture_pulse",
    "color": "#ffd166",
    "showStacks": false,
    "buildup": { "showBar": true, "threshold": 100.0, "flashAtThreshold": true }
  }
}
```

- [ ] **Step 9: Run tests and build**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — Expected: PASS.
Run: `dotnet build VRPG/VRPG.csproj` — Expected: clean build.

- [ ] **Step 10: Commit**

```bash
git add VRPG/src VRPG/tests VRPG/assets
git commit -m "Sync status effects to entity watched attributes with client countdown cache"
```

---

### Task 3: Combat visual event packet, damage-type ids, server broadcaster

**Files:**
- Create: `VRPG/src/Network/CombatVisualPackets.cs`
- Create: `VRPG/src/Modules/Rpg/Combat/CombatVisualBroadcaster.cs`
- Modify: `VRPG/src/Network/VRPGNetwork.cs` (register message type on both sides)
- Test: `VRPG/tests/VRPG.Tests/VisualDamageTypesTests.cs`

**Interfaces:**
- Produces:
  - `CombatVisualKind : byte { Impact, Burst, Ray, Damage, Heal, Shield, Break, Counter, Consume, WindowOpen, Mark }`
  - `[Flags] CombatVisualFlags { None = 0, Crit = 1, Threshold = 2 }`
  - `CombatVisualEventPacket { byte Kind; string StyleCode; long SourceEntityId; long TargetEntityId; double X, Y, Z; float Radius; float Magnitude; int Flags; byte DamageType; int FallbackColorRgba }`
  - `VisualDamageTypes.FromCode(string damageTypeCode)` → byte id (`Physical=0, Fire=1, Cold=2, Lightning=3, Rust=4, Bleed=5, Heal=6`); `VisualDamageTypes.ColorRgba(byte id)` → packed ARGB int.
  - `CombatVisualBroadcaster.Send(CombatVisualEventPacket packet)` — sends to every player within 80 blocks of (X, Y, Z).
- Tasks 4, 5, 9, 13, 16 consume all of these.

- [ ] **Step 1: Write the failing test**

`VRPG/tests/VRPG.Tests/VisualDamageTypesTests.cs`:

```csharp
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class VisualDamageTypesTests
{
    [Theory]
    [InlineData("vrpg:physical", 0)]
    [InlineData("physical", 0)]
    [InlineData("vrpg:fire", 1)]
    [InlineData("vrpg:cold", 2)]
    [InlineData("vrpg:lightning", 3)]
    [InlineData("vrpg:rust", 4)]
    [InlineData("vrpg:bleed", 5)]
    [InlineData("", 0)]
    [InlineData("vrpg:someday-new-type", 0)]
    public void MapsDamageCodesWithPhysicalFallback(string code, byte expected)
    {
        Assert.Equal(expected, VisualDamageTypes.FromCode(code));
    }

    [Fact]
    public void EveryIdHasAnOpaqueColor()
    {
        for (byte id = 0; id <= VisualDamageTypes.Heal; id++)
        {
            int color = VisualDamageTypes.ColorRgba(id);
            Assert.NotEqual(0, color & unchecked((int)0xff000000));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj`
Expected: compile FAIL — `VisualDamageTypes` missing.

- [ ] **Step 3: Implement the packet file**

`VRPG/src/Network/CombatVisualPackets.cs`:

```csharp
using System;
using ProtoBuf;

namespace VRPG.Network;

public enum CombatVisualKind : byte
{
    Impact = 0,
    Burst = 1,
    Ray = 2,
    Damage = 3,
    Heal = 4,
    Shield = 5,
    Break = 6,
    Counter = 7,
    Consume = 8,
    WindowOpen = 9,
    Mark = 10
}

[Flags]
public enum CombatVisualFlags
{
    None = 0,
    Crit = 1,
    Threshold = 2
}

[ProtoContract]
public sealed class CombatVisualEventPacket
{
    [ProtoMember(1)]
    public byte Kind { get; set; }

    [ProtoMember(2)]
    public string StyleCode { get; set; } = "";

    [ProtoMember(3)]
    public long SourceEntityId { get; set; }

    [ProtoMember(4)]
    public long TargetEntityId { get; set; }

    [ProtoMember(5)]
    public double X { get; set; }

    [ProtoMember(6)]
    public double Y { get; set; }

    [ProtoMember(7)]
    public double Z { get; set; }

    [ProtoMember(8)]
    public float Radius { get; set; }

    [ProtoMember(9)]
    public float Magnitude { get; set; }

    [ProtoMember(10)]
    public int Flags { get; set; }

    [ProtoMember(11)]
    public byte DamageType { get; set; }

    // Used only when StyleCode is empty (effects with no data definition, e.g. Evasive Step).
    [ProtoMember(12)]
    public int FallbackColorRgba { get; set; }
}

public static class VisualDamageTypes
{
    public const byte Physical = 0;
    public const byte Fire = 1;
    public const byte Cold = 2;
    public const byte Lightning = 3;
    public const byte Rust = 4;
    public const byte Bleed = 5;
    public const byte Heal = 6;

    public static byte FromCode(string damageTypeCode)
    {
        string path = damageTypeCode ?? "";
        int colon = path.IndexOf(':');
        if (colon >= 0)
        {
            path = path.Substring(colon + 1);
        }

        return path.ToLowerInvariant() switch
        {
            "fire" => Fire,
            "cold" => Cold,
            "lightning" => Lightning,
            "rust" => Rust,
            "bleed" => Bleed,
            "heal" => Heal,
            _ => Physical
        };
    }

    public static int ColorRgba(byte id)
    {
        return id switch
        {
            Fire => unchecked((int)0xfff06a28),
            Cold => unchecked((int)0xff78c9f0),
            Lightning => unchecked((int)0xffffe66d),
            Rust => unchecked((int)0xffc96a1e),
            Bleed => unchecked((int)0xffb81c2e),
            Heal => unchecked((int)0xff54d16a),
            _ => unchecked((int)0xfff2ede4)
        };
    }
}
```

- [ ] **Step 4: Implement the broadcaster**

`VRPG/src/Modules/Rpg/Combat/CombatVisualBroadcaster.cs`:

```csharp
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Combat;

public sealed class CombatVisualBroadcaster
{
    public const float BroadcastRange = 80f;

    private readonly ICoreServerAPI api;
    private readonly IServerNetworkChannel channel;

    public CombatVisualBroadcaster(ICoreServerAPI api, IServerNetworkChannel channel)
    {
        this.api = api;
        this.channel = channel;
    }

    public void Send(CombatVisualEventPacket packet)
    {
        double rangeSquared = BroadcastRange * BroadcastRange;
        foreach (IPlayer player in api.World.AllOnlinePlayers)
        {
            if (player is not IServerPlayer serverPlayer
                || serverPlayer.ConnectionState != EnumClientState.Playing
                || serverPlayer.Entity == null)
            {
                continue;
            }

            double dx = serverPlayer.Entity.Pos.X - packet.X;
            double dy = serverPlayer.Entity.Pos.Y - packet.Y;
            double dz = serverPlayer.Entity.Pos.Z - packet.Z;
            if (dx * dx + dy * dy + dz * dz <= rangeSquared)
            {
                channel.SendPacket(packet, serverPlayer);
            }
        }
    }
}
```

- [ ] **Step 5: Register the message type**

In `VRPGNetwork.cs`, append `.RegisterMessageType<CombatVisualEventPacket>()` to **both** the server and client chains (order must match between the two lists — add it at the end of each).

- [ ] **Step 6: Run tests and build**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — PASS.
Run: `dotnet build VRPG/VRPG.csproj` — clean.

- [ ] **Step 7: Commit**

```bash
git add VRPG/src VRPG/tests
git commit -m "Add combat visual event packet, damage type ids, and range broadcaster"
```

---

### Task 4: Client VisualDirector, style resolver, config, and skill FX renderer

**Files:**
- Create: `VRPG/src/Client/Visuals/CombatVisualsConfig.cs`
- Create: `VRPG/src/Client/Visuals/VisualStyleResolver.cs`
- Create: `VRPG/src/Client/Visuals/SkillFxRenderer.cs`
- Create: `VRPG/src/Client/Visuals/VisualDirector.cs`
- Modify: `VRPG/src/Config/VRPGConfig.cs` (add `CombatVisuals` to `RpgModuleConfig`)
- Modify: `VRPG/src/VRPGModSystem.cs` (create director, register handler, dispose)
- Test: `VRPG/tests/VRPG.Tests/VisualStyleResolverTests.cs`

**Interfaces:**
- Consumes: `CombatVisualEventPacket`, `CombatVisualKind`, `VisualDamageTypes` (Task 3); `SkillDefinition`/`StatusEffectDefinition`; `SkillDefinitionValidator.TryParseColor(string, out int)` (existing, in `VRPG.Data`).
- Produces:
  - `CombatVisualsConfig` — full settings class (all fields below; the Hub UI arrives in Task 15):
    `bool CombatTextEnabled=true, DamageNumbers=true, EventWords=true, MergeNumbers=true, StatusAuras=true; float TelegraphOpacity=1f; string DegradationPolicy="own-first"; float Intensity=1f`.
  - `VisualStyle { int ColorRgba; SkillParticleDefinition Particles; float Radius }`
  - `VisualStyleResolver(Func<string, SkillDefinition?> skills, Func<string, StatusEffectDefinition?> statuses)` with `VisualStyle Resolve(string styleCode, int fallbackColorRgba, float radius)`.
  - `SkillFxRenderer(ICoreClientAPI capi)` with `Burst(VisualStyle style, Vec3d center)`, `Ray(VisualStyle style, Vec3d start, Vec3d end)`, `Circle(VisualStyle style, Vec3d center)`, and `float QuantityScale` (set by the director; default 1).
  - `VisualDirector(ICoreClientAPI capi, VRPGDataRegistry data, CombatVisualsConfig config)` with `HandleEvent(CombatVisualEventPacket packet)` and `Dispose()`. Kinds it does not render yet (Damage, Heal, Shield, Break, Counter, Consume, WindowOpen, Mark) are silently ignored until Tasks 9 and 13 — route them through a single `switch` so later tasks only add cases.

- [ ] **Step 1: Write the failing test**

`VRPG/tests/VRPG.Tests/VisualStyleResolverTests.cs`:

```csharp
using VRPG.Client.Visuals;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class VisualStyleResolverTests
{
    [Fact]
    public void ResolvesSkillColorAndParticles()
    {
        var skill = new SkillDefinition { Code = "vrpg:cinder", Color = "#f06a28", Radius = 3.2f };
        var resolver = new VisualStyleResolver(
            code => code == "vrpg:cinder" ? skill : null,
            _ => null);

        VisualStyle style = resolver.Resolve("vrpg:cinder", 0, radius: 0f);
        Assert.Equal(unchecked((int)0xfff06a28), style.ColorRgba);
        Assert.Same(skill.Particles, style.Particles);
        Assert.Equal(3.2f, style.Radius);
    }

    [Fact]
    public void FallsBackToStatusColor()
    {
        var status = new StatusEffectDefinition { Code = "vrpg:burn" };
        status.Visual.Color = "#b81c2e";
        var resolver = new VisualStyleResolver(_ => null, code => code == "vrpg:burn" ? status : null);

        VisualStyle style = resolver.Resolve("vrpg:burn", 0, radius: 1f);
        Assert.Equal(unchecked((int)0xffb81c2e), style.ColorRgba);
        Assert.Equal(1f, style.Radius);
    }

    [Fact]
    public void UnknownCodeUsesFallbackColorAndDefaultParticles()
    {
        var resolver = new VisualStyleResolver(_ => null, _ => null);
        VisualStyle style = resolver.Resolve("vrpg:not-authored-yet", unchecked((int)0x8a7cff66), radius: 2f);
        Assert.Equal(unchecked((int)0x8a7cff66), style.ColorRgba);
        Assert.NotNull(style.Particles);
    }

    [Fact]
    public void EmptyFallbackColorYieldsNeutralDefault()
    {
        var resolver = new VisualStyleResolver(_ => null, _ => null);
        VisualStyle style = resolver.Resolve("", 0, radius: 0.5f);
        Assert.NotEqual(0, style.ColorRgba);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj`
Expected: compile FAIL.

- [ ] **Step 3: Implement config and resolver**

`VRPG/src/Client/Visuals/CombatVisualsConfig.cs`:

```csharp
namespace VRPG.Client.Visuals;

/// <summary>
/// Client-local combat visual settings, persisted with the client mod config
/// and edited from the Hub Options "Combat Visuals" category.
/// </summary>
public sealed class CombatVisualsConfig
{
    public bool CombatTextEnabled { get; set; } = true;
    public bool DamageNumbers { get; set; } = true;
    public bool EventWords { get; set; } = true;
    public bool MergeNumbers { get; set; } = true;
    public bool StatusAuras { get; set; } = true;
    public float TelegraphOpacity { get; set; } = 1f;
    public string DegradationPolicy { get; set; } = "own-first";
    public float Intensity { get; set; } = 1f;
}
```

`VRPG/src/Client/Visuals/VisualStyleResolver.cs`:

```csharp
using System;
using VRPG.Data;
using VRPG.Data.Definitions;

namespace VRPG.Client.Visuals;

public sealed class VisualStyle
{
    public int ColorRgba;
    public SkillParticleDefinition Particles = new SkillParticleDefinition();
    public float Radius;
}

public sealed class VisualStyleResolver
{
    private static readonly int NeutralColor = unchecked((int)0xccf2ede4);
    private readonly Func<string, SkillDefinition?> skills;
    private readonly Func<string, StatusEffectDefinition?> statuses;

    public VisualStyleResolver(Func<string, SkillDefinition?> skills, Func<string, StatusEffectDefinition?> statuses)
    {
        this.skills = skills;
        this.statuses = statuses;
    }

    public VisualStyle Resolve(string styleCode, int fallbackColorRgba, float radius)
    {
        SkillDefinition? skill = string.IsNullOrWhiteSpace(styleCode) ? null : skills(styleCode);
        if (skill != null && SkillDefinitionValidator.TryParseColor(skill.Color, out int skillColor))
        {
            return new VisualStyle
            {
                ColorRgba = skillColor,
                Particles = skill.Particles,
                Radius = radius > 0f ? radius : skill.Radius
            };
        }

        StatusEffectDefinition? status = string.IsNullOrWhiteSpace(styleCode) ? null : statuses(styleCode);
        if (status != null && SkillDefinitionValidator.TryParseColor(status.Visual.Color, out int statusColor))
        {
            return new VisualStyle { ColorRgba = statusColor, Radius = radius };
        }

        return new VisualStyle
        {
            ColorRgba = fallbackColorRgba != 0 ? fallbackColorRgba : NeutralColor,
            Radius = radius
        };
    }
}
```

Check `SkillDefinitionValidator.TryParseColor` accepts `#aarrggbb`/`#rrggbb` and yields an opaque int for 6-digit input; the tests above assume `#f06a28` → `0xfff06a28`. If the existing helper returns something else, adapt the expected constants in the test, not the helper.

- [ ] **Step 4: Implement SkillFxRenderer**

`VRPG/src/Client/Visuals/SkillFxRenderer.cs` — this is the client-side port of the three server spawners in `SkillCastingService` (`SpawnBurst`, `SpawnRay`, `SpawnCircle`). Keep the exact particle math; only the API object changes and quantities multiply by `QuantityScale`:

```csharp
using System;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;

namespace VRPG.Client.Visuals;

public sealed class SkillFxRenderer
{
    private readonly ICoreClientAPI capi;

    public SkillFxRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary>Set by the VisualDirector before each dispatch; 0..1.</summary>
    public float QuantityScale = 1f;

    public void Burst(VisualStyle style, Vec3d center)
    {
        SkillParticleDefinition particles = style.Particles;
        float quantity = particles.BurstQuantity * QuantityScale;
        if (quantity <= 0f)
        {
            return;
        }

        int samples = Math.Clamp((int)Math.Ceiling(quantity / 2f), 6, 18);
        float quantityPerSample = quantity / samples;
        double ringRadius = Math.Max(0.35, style.Radius * 0.72);
        float velocity = particles.Velocity;
        for (int i = 0; i < samples; i++)
        {
            double angle = Math.PI * 2 * i / samples;
            float velocityX = (float)Math.Cos(angle) * velocity * 0.55f;
            float velocityZ = (float)Math.Sin(angle) * velocity * 0.55f;
            var point = new Vec3d(
                center.X + Math.Cos(angle) * ringRadius,
                center.Y - Math.Min(0.12, style.Radius * 0.03),
                center.Z + Math.Sin(angle) * ringRadius);
            capi.World.SpawnParticles(
                quantityPerSample,
                style.ColorRgba,
                point.Clone().Add(-0.04, -0.03, -0.04),
                point.Clone().Add(0.04, 0.04, 0.04),
                new Vec3f(velocityX - 0.04f, 0.03f, velocityZ - 0.04f),
                new Vec3f(velocityX + 0.04f, Math.Max(0.08f, velocity * 0.22f), velocityZ + 0.04f),
                particles.LifetimeSeconds * 0.8f,
                particles.Gravity,
                particles.Scale * 0.72f,
                ParticleModel(particles.Model));
        }
    }

    public void Ray(VisualStyle style, Vec3d start, Vec3d end)
    {
        SkillParticleDefinition particles = style.Particles;
        float quantity = particles.TrailQuantity * QuantityScale;
        if (quantity <= 0f)
        {
            return;
        }

        const int segments = 9;
        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)(segments + 1);
            var point = new Vec3d(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t,
                start.Z + (end.Z - start.Z) * t);
            float life = Math.Max(0.06f, particles.TrailLifetimeSeconds * (0.45f + 0.55f * (float)t));
            capi.World.SpawnParticles(
                quantity,
                style.ColorRgba,
                point,
                point,
                new Vec3f(-0.05f, -0.05f, -0.05f),
                new Vec3f(0.05f, 0.05f, 0.05f),
                life,
                0f,
                particles.Scale * 0.62f,
                ParticleModel(particles.Model));
        }
    }

    public void Circle(VisualStyle style, Vec3d center)
    {
        SkillParticleDefinition particles = style.Particles;
        int segments = Math.Clamp((int)Math.Round(particles.BurstQuantity * QuantityScale), 8, 40);
        for (int i = 0; i < segments; i++)
        {
            double angle = Math.PI * 2 * i / segments;
            var point = new Vec3d(
                center.X + Math.Cos(angle) * style.Radius,
                center.Y - 0.15,
                center.Z + Math.Sin(angle) * style.Radius);
            capi.World.SpawnParticles(
                1f,
                style.ColorRgba,
                point,
                point.Clone().Add(0, 0.15, 0),
                new Vec3f(0f, 0.1f, 0f),
                new Vec3f(0f, 0.45f, 0f),
                particles.LifetimeSeconds,
                particles.Gravity,
                particles.Scale * 0.72f,
                ParticleModel(particles.Model));
        }
    }

    /// <summary>Origin for ray starts, matching the old server-side CastVisualOrigin.</summary>
    public static Vec3d CastVisualOrigin(Vintagestory.API.Common.Entities.Entity caster, SkillParticleDefinition particles)
    {
        Vec3d eye = new Vec3d(
            caster.Pos.X + caster.LocalEyePos.X,
            caster.Pos.InternalY + caster.LocalEyePos.Y,
            caster.Pos.Z + caster.LocalEyePos.Z);
        Vec3f view = caster.Pos.GetViewVector();
        double horizontalX = -Math.Cos(caster.Pos.Yaw) * particles.OriginHorizontalOffset;
        double horizontalZ = Math.Sin(caster.Pos.Yaw) * particles.OriginHorizontalOffset;
        return new Vec3d(
            eye.X + horizontalX + view.X * particles.OriginForwardOffset,
            eye.Y + particles.OriginVerticalOffset + view.Y * particles.OriginForwardOffset,
            eye.Z + horizontalZ + view.Z * particles.OriginForwardOffset);
    }

    private static EnumParticleModel ParticleModel(string model)
    {
        return string.Equals(model, "cube", StringComparison.OrdinalIgnoreCase)
            ? EnumParticleModel.Cube
            : EnumParticleModel.Quad;
    }
}
```

- [ ] **Step 5: Implement VisualDirector**

`VRPG/src/Client/Visuals/VisualDirector.cs`:

```csharp
using System;
using VRPG.Data;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>
/// Sole client-side consumer of combat visual channels. Owns budgets and
/// options; renderers never decide what to skip on their own.
/// </summary>
public sealed class VisualDirector : IDisposable
{
    private readonly ICoreClientAPI capi;
    private readonly VisualStyleResolver styles;
    private readonly SkillFxRenderer skillFx;

    public CombatVisualsConfig Config { get; }

    public VisualDirector(ICoreClientAPI capi, VRPGDataRegistry data, CombatVisualsConfig config)
    {
        this.capi = capi;
        Config = config;
        styles = new VisualStyleResolver(code => data.Skills.Get(code), code => data.StatusEffects.Get(code));
        skillFx = new SkillFxRenderer(capi);
    }

    public void HandleEvent(CombatVisualEventPacket packet)
    {
        VisualStyle style = styles.Resolve(packet.StyleCode, packet.FallbackColorRgba, packet.Radius);
        var position = new Vec3d(packet.X, packet.Y, packet.Z);
        skillFx.QuantityScale = Config.Intensity;

        switch ((CombatVisualKind)packet.Kind)
        {
            case CombatVisualKind.Impact:
            case CombatVisualKind.Burst:
                skillFx.Burst(style, position);
                break;
            case CombatVisualKind.Ray:
                skillFx.Ray(style, RayStart(packet, style), position);
                break;
            // Damage, Heal, Shield, Break, Counter, Consume, Mark: combat text (Task 9).
            // WindowOpen: crosshair pulse (Task 13).
            default:
                break;
        }
    }

    private Vec3d RayStart(CombatVisualEventPacket packet, VisualStyle style)
    {
        Entity? source = packet.SourceEntityId != 0 ? capi.World.GetEntityById(packet.SourceEntityId) : null;
        return source != null
            ? SkillFxRenderer.CastVisualOrigin(source, style.Particles)
            : new Vec3d(packet.X, packet.Y + 1.2, packet.Z);
    }

    public void Dispose()
    {
    }
}
```

- [ ] **Step 6: Wire into config and mod system**

In `VRPG/src/Config/VRPGConfig.cs`, inside `RpgModuleConfig` after the `Resources` property add:

```csharp
    public VRPG.Client.Visuals.CombatVisualsConfig CombatVisuals { get; set; } = new VRPG.Client.Visuals.CombatVisualsConfig();
```

(If keeping a client type out of `VRPG.Config` bothers the build later, move `CombatVisualsConfig` to `VRPG.Config` — but it holds only plain fields, so the namespace reference is fine.)

In `VRPGModSystem`:
1. Add field: `private VisualDirector? visualDirector;`
2. In `StartClientSide`, after the `clientChannel` handler chain, append to that chain:

```csharp
            .SetMessageHandler<CombatVisualEventPacket>(packet => visualDirector?.HandleEvent(packet))
```

3. Still in `StartClientSide`, after the hotkey registrations:

```csharp
        if (config.Modules.Rpg.Enabled)
        {
            visualDirector = new VisualDirector(api, DataRegistry, config.Modules.Rpg.CombatVisuals);
        }
```

(`DataRegistry` contents load during `AssetsFinalize`, which runs before packets can arrive; constructing the director with the registry instance in `StartClientSide` is safe because the resolver looks codes up lazily.)

4. In `Dispose`, add `visualDirector?.Dispose(); visualDirector = null;` beside the other client teardowns.
5. Add `using VRPG.Client.Visuals;` where needed.

- [ ] **Step 7: Run tests and build**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — PASS.
Run: `dotnet build VRPG/VRPG.csproj` — clean.

- [ ] **Step 8: Commit**

```bash
git add VRPG/src VRPG/tests
git commit -m "Add client visual director, style resolver, and skill FX renderer"
```

---

### Task 5: Migrate all server particle spawns to visual events

**Files:**
- Modify: `VRPG/src/Modules/Rpg/Skills/SkillCastingService.cs`
- Modify: `VRPG/src/Modules/Rpg/RpgModule.cs`
- Modify: `VRPG/src/Modules/Rpg/Combat/EvasiveStepService.cs`
- Modify: `VRPG/src/Modules/Rpg/Skills/EntityVrpgSkillProjectile.cs`

**Interfaces:**
- Consumes: `CombatVisualBroadcaster`, `CombatVisualEventPacket`, `CombatVisualKind`, `VisualDamageTypes` (Task 3).
- Produces: `SkillCastingService` constructor gains a trailing `CombatVisualBroadcaster visuals` parameter. Damage events now flow per hit (Task 9 renders them). No other public signature changes.

- [ ] **Step 1: Give SkillCastingService the broadcaster**

Add a `private readonly CombatVisualBroadcaster visuals;` field, a trailing constructor parameter `CombatVisualBroadcaster visuals`, assign it, and add `using VRPG.Modules.Rpg.Combat;`.

In `RpgModule.StartServerSide`, replace the `skills = new SkillCastingService(...)` line with:

```csharp
        var visualBroadcaster = new CombatVisualBroadcaster(api, channel);
        skills = new SkillCastingService(api, data, playerStore, resources, skillDamage, visualBroadcaster);
```

Also store it for later tasks: add field `private CombatVisualBroadcaster? visualBroadcaster;` to `RpgModule`, assign it here, and expose `public CombatVisualBroadcaster? VisualBroadcaster => visualBroadcaster;`.

- [ ] **Step 2: Replace the three spawners with events**

In `SkillCastingService`:

1. Delete `SpawnBurst`, `SpawnRay`, `SpawnCircle`, and `ParticleModel` (the private methods).
2. Add one helper:

```csharp
    private CombatVisualEventPacket Event(CombatVisualKind kind, SkillDefinition skill, Vec3d position)
    {
        return new CombatVisualEventPacket
        {
            Kind = (byte)kind,
            StyleCode = skill.Code,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Radius = skill.Radius,
            DamageType = VisualDamageTypes.FromCode(skill.Damage.Type)
        };
    }
```

3. In `CastRaycastArea`, replace the trailing `SpawnRay(...); SpawnBurst(...);` with:

```csharp
        CombatVisualEventPacket ray = Event(CombatVisualKind.Ray, skill, center);
        ray.SourceEntityId = player.Entity.EntityId;
        visuals.Send(ray);
        visuals.Send(Event(CombatVisualKind.Burst, skill, center));
```

4. In `CastCircle`, replace `SpawnCircle(skill, center);` with `visuals.Send(Event(CombatVisualKind.Burst, skill, center));` — the client's `Burst` reads `Radius`, so the caster-centered ring survives. (Task 6 adds the transient ground disc here.)
5. In `HandleProjectileImpact`, replace `SpawnBurst(skill, center);` with `visuals.Send(Event(CombatVisualKind.Burst, skill, center));`.
6. In `ApplyAreaDamage`, inside the `for` loop after `target.ReceiveDamage(...)`, add per-hit damage events:

```csharp
            Vec3d targetCenter = EntityCenter(target);
            CombatVisualEventPacket damageEvent = Event(CombatVisualKind.Damage, skill, targetCenter);
            damageEvent.SourceEntityId = player.Entity.EntityId;
            damageEvent.TargetEntityId = target.EntityId;
            damageEvent.Magnitude = damage;
            visuals.Send(damageEvent);
```

7. `CastVisualOrigin` is no longer used server-side — delete it (the client copy lives in `SkillFxRenderer`). Add `using VRPG.Network;`.

- [ ] **Step 3: Migrate the evasive step burst**

`EvasiveStepService` constructor already receives the api and channel. Replace the body of `SpawnBurst(EntityPlayer player)` with:

```csharp
        Vec3d center = player.Pos.XYZ.Clone().Add(0, 0.15, 0);
        new CombatVisualBroadcaster(api, channel).Send(new CombatVisualEventPacket
        {
            Kind = (byte)CombatVisualKind.Burst,
            StyleCode = "",
            FallbackColorRgba = unchecked((int)0x8a7cff66),
            X = center.X,
            Y = center.Y,
            Z = center.Z,
            Radius = 0.6f,
            SourceEntityId = player.EntityId
        });
```

(If constructing a broadcaster per call reads poorly, hold one in a field initialized in the constructor — the service already stores `api`; match whichever fields exist.) Add `using VRPG.Network;` and `using VRPG.Modules.Rpg.Combat;` — note the class already lives in `VRPG.Modules.Rpg.Combat`.

- [ ] **Step 4: Make the projectile trail client-side**

Open `EntityVrpgSkillProjectile.cs` and find the `World.SpawnParticles(...)` call (~line 303). It runs inside the entity's tick. Wrap or move it so it executes **only on the client side**:

```csharp
        if (World.Side == EnumAppSide.Client)
        {
            // existing trail SpawnParticles call, unchanged
        }
```

Entities tick on both sides in Vintage Story; the trail spawning locally per client removes it from server broadcast entirely. Verify the color/definition lookups it uses are available client-side (the skill registry is; `Configure(skill, level)` state may not survive on the client — if the trail code reads fields set only server-side, sync the needed color/scale via the entity's `WatchedAttributes` in `Configure`, reading them in the trail branch).

- [ ] **Step 5: Build and verify in game**

Run: `dotnet build VRPG/VRPG.csproj` — clean; the build deploys to the Mods folder.

In-game acceptance (single-player test world, admin):
1. `/vrpg` skill admin path: learn and equip a raycast skill, a projectile skill, and a circle skill (existing admin commands).
2. Cast each: burst ring, ray trail, caster circle, and projectile trail all still appear, visually equivalent to before.
3. Damage events flow (no visible change yet — combat text is Task 9; confirm no errors in client/server logs).
4. Trigger Evasive Step: the violet puff appears.

- [ ] **Step 6: Commit**

```bash
git add VRPG/src
git commit -m "Move all skill and combat particle rendering from server to client visual events"
```

---

### Task 6: Ground area registry — packets and server service

**Files:**
- Create: `VRPG/src/Network/GroundAreaPackets.cs`
- Create: `VRPG/src/Modules/Rpg/Combat/GroundAreaService.cs`
- Modify: `VRPG/src/Network/VRPGNetwork.cs` (register both packets)
- Modify: `VRPG/src/Modules/Rpg/RpgModule.cs` (create service, snapshot on join, dispose)
- Modify: `VRPG/src/Modules/Rpg/Skills/SkillCastingService.cs` (transient disc for circle skills)
- Test: `VRPG/tests/VRPG.Tests/GroundAreaTableTests.cs`

**Interfaces:**
- Consumes: `IServerNetworkChannel`, `CombatVisualBroadcaster` pattern (Task 3).
- Produces:
  - `GroundAreaShape : byte { Disc = 0, Ring = 1 }`; `GroundAreaState : byte { Armed = 0, Triggered = 1, Active = 2, Expiring = 3 }`
  - `GroundAreaUpsertPacket { long Id; string OwnerUid; string StyleCode; byte Shape; double X, Y, Z; long FollowEntityId; float Radius; byte State; int RemainingMs }`
  - `GroundAreaRemovePacket { long Id }`
  - `GroundAreaTable` (pure): `Upsert(GroundAreaRecord)`, `TickExpiry(long nowMs)` → `ExpiryTransitions { List<GroundAreaRecord> NowExpiring; List<long> Expired }`, `IReadOnlyCollection<GroundAreaRecord> All`.
  - `GroundAreaRecord { long Id; string OwnerUid; string StyleCode; GroundAreaShape Shape; double X, Y, Z; long FollowEntityId; float Radius; GroundAreaState State; long ExpiresAtMs }`
  - `GroundAreaService(ICoreServerAPI api, IServerNetworkChannel channel)`: `long Place(string ownerUid, string styleCode, GroundAreaShape shape, Vec3d center, float radius, GroundAreaState state, float durationSeconds, long followEntityId = 0)`, `void SetState(long id, GroundAreaState state)`, `void Remove(long id)`, `void SendSnapshot(IServerPlayer player)`, `void Dispose()`.
- Task 7 consumes the packets; Task 16 drives the service from admin commands.

- [ ] **Step 1: Write the failing test**

`VRPG/tests/VRPG.Tests/GroundAreaTableTests.cs`:

```csharp
using VRPG.Modules.Rpg.Combat;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class GroundAreaTableTests
{
    private static GroundAreaRecord Record(long id, long expiresAtMs, GroundAreaState state = GroundAreaState.Active)
    {
        return new GroundAreaRecord { Id = id, StyleCode = "vrpg:test", Radius = 2f, State = state, ExpiresAtMs = expiresAtMs };
    }

    [Fact]
    public void AreasNearExpiryTransitionToExpiringExactlyOnce()
    {
        var table = new GroundAreaTable();
        table.Upsert(Record(1, expiresAtMs: 2000));

        ExpiryTransitions first = table.TickExpiry(nowMs: 800);
        Assert.Single(first.NowExpiring);
        Assert.Equal(GroundAreaState.Expiring, first.NowExpiring[0].State);

        ExpiryTransitions second = table.TickExpiry(nowMs: 900);
        Assert.Empty(second.NowExpiring);
    }

    [Fact]
    public void ExpiredAreasAreRemovedAndReported()
    {
        var table = new GroundAreaTable();
        table.Upsert(Record(1, expiresAtMs: 1000));
        ExpiryTransitions transitions = table.TickExpiry(nowMs: 1500);
        Assert.Contains(1L, transitions.Expired);
        Assert.Empty(table.All);
    }

    [Fact]
    public void ArmedAreasDoNotEnterExpiringEarly()
    {
        var table = new GroundAreaTable();
        table.Upsert(Record(1, expiresAtMs: 60000, GroundAreaState.Armed));
        Assert.Empty(table.TickExpiry(nowMs: 58000).NowExpiring);   // 2000 ms left > 1500 ms lead
        Assert.Single(table.TickExpiry(nowMs: 58600).NowExpiring);  // 1400 ms left <= lead
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — compile FAIL.

- [ ] **Step 3: Implement the packets**

`VRPG/src/Network/GroundAreaPackets.cs`:

```csharp
using ProtoBuf;

namespace VRPG.Network;

public enum GroundAreaShape : byte
{
    Disc = 0,
    Ring = 1
}

public enum GroundAreaState : byte
{
    Armed = 0,
    Triggered = 1,
    Active = 2,
    Expiring = 3
}

[ProtoContract]
public sealed class GroundAreaUpsertPacket
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string OwnerUid { get; set; } = "";

    [ProtoMember(3)]
    public string StyleCode { get; set; } = "";

    [ProtoMember(4)]
    public byte Shape { get; set; }

    [ProtoMember(5)]
    public double X { get; set; }

    [ProtoMember(6)]
    public double Y { get; set; }

    [ProtoMember(7)]
    public double Z { get; set; }

    [ProtoMember(8)]
    public long FollowEntityId { get; set; }

    [ProtoMember(9)]
    public float Radius { get; set; }

    [ProtoMember(10)]
    public byte State { get; set; }

    [ProtoMember(11)]
    public int RemainingMs { get; set; }
}

[ProtoContract]
public sealed class GroundAreaRemovePacket
{
    [ProtoMember(1)]
    public long Id { get; set; }
}
```

Register both in `VRPGNetwork.cs` at the end of **both** chains.

- [ ] **Step 4: Implement table and service**

`VRPG/src/Modules/Rpg/Combat/GroundAreaService.cs`:

```csharp
using System;
using System.Collections.Generic;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Combat;

public sealed class GroundAreaRecord
{
    public long Id;
    public string OwnerUid = "";
    public string StyleCode = "";
    public GroundAreaShape Shape;
    public double X, Y, Z;
    public long FollowEntityId;
    public float Radius;
    public GroundAreaState State;
    public long ExpiresAtMs;
}

public sealed class ExpiryTransitions
{
    public readonly List<GroundAreaRecord> NowExpiring = new List<GroundAreaRecord>();
    public readonly List<long> Expired = new List<long>();
}

/// <summary>Pure area bookkeeping so expiry rules are unit-testable.</summary>
public sealed class GroundAreaTable
{
    public const int ExpiringLeadMs = 1500;

    private readonly Dictionary<long, GroundAreaRecord> areas = new Dictionary<long, GroundAreaRecord>();

    public IReadOnlyCollection<GroundAreaRecord> All => areas.Values;

    public void Upsert(GroundAreaRecord record)
    {
        areas[record.Id] = record;
    }

    public GroundAreaRecord? Get(long id)
    {
        return areas.TryGetValue(id, out GroundAreaRecord? record) ? record : null;
    }

    public bool Remove(long id)
    {
        return areas.Remove(id);
    }

    public ExpiryTransitions TickExpiry(long nowMs)
    {
        var transitions = new ExpiryTransitions();
        foreach (GroundAreaRecord record in areas.Values)
        {
            if (record.ExpiresAtMs <= nowMs)
            {
                transitions.Expired.Add(record.Id);
            }
            else if (record.State != GroundAreaState.Expiring && record.ExpiresAtMs - nowMs <= ExpiringLeadMs)
            {
                record.State = GroundAreaState.Expiring;
                transitions.NowExpiring.Add(record);
            }
        }

        for (int i = 0; i < transitions.Expired.Count; i++)
        {
            areas.Remove(transitions.Expired[i]);
        }

        return transitions;
    }
}

public sealed class GroundAreaService : IDisposable
{
    private readonly ICoreServerAPI api;
    private readonly IServerNetworkChannel channel;
    private readonly GroundAreaTable table = new GroundAreaTable();
    private readonly long tickListenerId;
    private long nextId = 1;

    public GroundAreaService(ICoreServerAPI api, IServerNetworkChannel channel)
    {
        this.api = api;
        this.channel = channel;
        tickListenerId = api.Event.RegisterGameTickListener(Tick, 250);
    }

    public long Place(string ownerUid, string styleCode, GroundAreaShape shape, Vec3d center, float radius, GroundAreaState state, float durationSeconds, long followEntityId = 0)
    {
        var record = new GroundAreaRecord
        {
            Id = nextId++,
            OwnerUid = ownerUid,
            StyleCode = styleCode,
            Shape = shape,
            X = center.X,
            Y = center.Y,
            Z = center.Z,
            FollowEntityId = followEntityId,
            Radius = radius,
            State = state,
            ExpiresAtMs = api.World.ElapsedMilliseconds + (long)(durationSeconds * 1000f)
        };
        table.Upsert(record);
        Broadcast(record);
        return record.Id;
    }

    public void SetState(long id, GroundAreaState state)
    {
        GroundAreaRecord? record = table.Get(id);
        if (record == null)
        {
            return;
        }

        record.State = state;
        Broadcast(record);
    }

    public void Remove(long id)
    {
        if (table.Remove(id))
        {
            BroadcastToAll(new GroundAreaRemovePacket { Id = id });
        }
    }

    public void SendSnapshot(IServerPlayer player)
    {
        foreach (GroundAreaRecord record in table.All)
        {
            channel.SendPacket(ToPacket(record), player);
        }
    }

    private void Tick(float dt)
    {
        ExpiryTransitions transitions = table.TickExpiry(api.World.ElapsedMilliseconds);
        for (int i = 0; i < transitions.NowExpiring.Count; i++)
        {
            Broadcast(transitions.NowExpiring[i]);
        }

        for (int i = 0; i < transitions.Expired.Count; i++)
        {
            BroadcastToAll(new GroundAreaRemovePacket { Id = transitions.Expired[i] });
        }
    }

    private GroundAreaUpsertPacket ToPacket(GroundAreaRecord record)
    {
        return new GroundAreaUpsertPacket
        {
            Id = record.Id,
            OwnerUid = record.OwnerUid,
            StyleCode = record.StyleCode,
            Shape = (byte)record.Shape,
            X = record.X,
            Y = record.Y,
            Z = record.Z,
            FollowEntityId = record.FollowEntityId,
            Radius = record.Radius,
            State = (byte)record.State,
            RemainingMs = (int)Math.Max(0, record.ExpiresAtMs - api.World.ElapsedMilliseconds)
        };
    }

    // Areas are few (wards, traps); broadcast to everyone and let the client range-filter.
    private void Broadcast(GroundAreaRecord record)
    {
        BroadcastToAll(ToPacket(record));
    }

    private void BroadcastToAll(object packet)
    {
        foreach (IPlayer player in api.World.AllOnlinePlayers)
        {
            if (player is IServerPlayer serverPlayer && serverPlayer.ConnectionState == EnumClientState.Playing)
            {
                if (packet is GroundAreaUpsertPacket upsert)
                {
                    channel.SendPacket(upsert, serverPlayer);
                }
                else if (packet is GroundAreaRemovePacket remove)
                {
                    channel.SendPacket(remove, serverPlayer);
                }
            }
        }
    }

    public void Dispose()
    {
        api.Event.UnregisterGameTickListener(tickListenerId);
    }
}
```

- [ ] **Step 5: Wire into RpgModule and circle skills**

In `RpgModule`:
1. Field: `private GroundAreaService? groundAreas;` and property `public GroundAreaService? GroundAreas => groundAreas;`
2. In `StartServerSide` (next to the broadcaster from Task 5): `groundAreas = new GroundAreaService(api, channel);`
3. Pass it into the casting service: extend `SkillCastingService`'s constructor with a trailing `GroundAreaService groundAreas` parameter and update the construction call.
4. Snapshot on join: `api.Event.PlayerNowPlaying += player => groundAreas?.SendSnapshot(player);` (store the delegate in a field so `Dispose` can unsubscribe, matching the existing `PlayerNowPlaying` teardown pattern).
5. In `Dispose`: `groundAreas?.Dispose(); groundAreas = null;`

In `SkillCastingService.CastCircle`, after the burst event line add the transient slam disc:

```csharp
        groundAreas.Place(player.PlayerUID, skill.Code, GroundAreaShape.Disc, center, skill.Radius, GroundAreaState.Active, durationSeconds: 0.8f);
```

- [ ] **Step 6: Run tests and build**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — PASS.
Run: `dotnet build VRPG/VRPG.csproj` — clean.

- [ ] **Step 7: Commit**

```bash
git add VRPG/src VRPG/tests
git commit -m "Add server ground area registry with expiry transitions and join snapshots"
```

---

### Task 7: Client ground area store and telegraph renderer

**Files:**
- Create: `VRPG/src/Client/Visuals/GroundAreaStore.cs`
- Create: `VRPG/src/Client/Visuals/GroundTelegraphRenderer.cs`
- Modify: `VRPG/src/VRPGModSystem.cs` (handlers, renderer registration, dispose)
- Test: `VRPG/tests/VRPG.Tests/GroundAreaStoreTests.cs`

**Interfaces:**
- Consumes: `GroundAreaUpsertPacket`, `GroundAreaRemovePacket`, enums (Task 6); `VisualStyleResolver` (Task 4); `CombatVisualsConfig.TelegraphOpacity`.
- Produces:
  - `GroundAreaStore` (pure): `Upsert(GroundAreaUpsertPacket packet, long nowMs)`, `Remove(long id)`, `Prune(long nowMs)` (drops locally expired areas), `IReadOnlyCollection<ClientGroundArea> All`.
  - `ClientGroundArea { long Id; string OwnerUid; string StyleCode; GroundAreaShape Shape; double X, Y, Z; long FollowEntityId; float Radius; GroundAreaState State; long LocalExpiresAtMs; long StateChangedAtMs }`
  - `GroundTelegraphRenderer : IRenderer` reading the store each frame.
- The `VisualDirector` gains `public GroundAreaStore Areas { get; }` so Task 16's stress checks and future systems can read it.

- [ ] **Step 1: Write the failing test**

`VRPG/tests/VRPG.Tests/GroundAreaStoreTests.cs`:

```csharp
using VRPG.Client.Visuals;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class GroundAreaStoreTests
{
    private static GroundAreaUpsertPacket Packet(long id, int remainingMs, GroundAreaState state = GroundAreaState.Active)
    {
        return new GroundAreaUpsertPacket { Id = id, StyleCode = "vrpg:test", Radius = 2f, State = (byte)state, RemainingMs = remainingMs };
    }

    [Fact]
    public void UpsertComputesLocalExpiryFromRemainingTime()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, remainingMs: 5000), nowMs: 1000);
        ClientGroundArea area = System.Linq.Enumerable.Single(store.All);
        Assert.Equal(6000, area.LocalExpiresAtMs);
    }

    [Fact]
    public void StateChangeStampsTransitionTimeForFlashAnimation()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, 60000, GroundAreaState.Armed), nowMs: 1000);
        store.Upsert(Packet(1, 59000, GroundAreaState.Triggered), nowMs: 2000);
        ClientGroundArea area = System.Linq.Enumerable.Single(store.All);
        Assert.Equal(GroundAreaState.Triggered, area.State);
        Assert.Equal(2000, area.StateChangedAtMs);
    }

    [Fact]
    public void SameStateUpsertKeepsOriginalTransitionStamp()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, 60000, GroundAreaState.Active), nowMs: 1000);
        store.Upsert(Packet(1, 59000, GroundAreaState.Active), nowMs: 5000);
        Assert.Equal(1000, System.Linq.Enumerable.Single(store.All).StateChangedAtMs);
    }

    [Fact]
    public void PruneDropsAreasPastLocalExpiryEvenWithoutRemovePacket()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, 1000), nowMs: 0);
        store.Prune(nowMs: 1500);
        Assert.Empty(store.All);
    }

    [Fact]
    public void RemoveDeletesTheArea()
    {
        var store = new GroundAreaStore();
        store.Upsert(Packet(1, 60000), nowMs: 0);
        store.Remove(1);
        Assert.Empty(store.All);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — compile FAIL.

- [ ] **Step 3: Implement the store**

`VRPG/src/Client/Visuals/GroundAreaStore.cs`:

```csharp
using System.Collections.Generic;
using VRPG.Network;

namespace VRPG.Client.Visuals;

public sealed class ClientGroundArea
{
    public long Id;
    public string OwnerUid = "";
    public string StyleCode = "";
    public GroundAreaShape Shape;
    public double X, Y, Z;
    public long FollowEntityId;
    public float Radius;
    public GroundAreaState State;
    public long LocalExpiresAtMs;
    public long StateChangedAtMs;
}

public sealed class GroundAreaStore
{
    private readonly Dictionary<long, ClientGroundArea> areas = new Dictionary<long, ClientGroundArea>();

    public IReadOnlyCollection<ClientGroundArea> All => areas.Values;

    public void Upsert(GroundAreaUpsertPacket packet, long nowMs)
    {
        if (!areas.TryGetValue(packet.Id, out ClientGroundArea? area))
        {
            area = new ClientGroundArea { Id = packet.Id, StateChangedAtMs = nowMs };
            areas[packet.Id] = area;
        }
        else if (area.State != (GroundAreaState)packet.State)
        {
            area.StateChangedAtMs = nowMs;
        }

        area.OwnerUid = packet.OwnerUid;
        area.StyleCode = packet.StyleCode;
        area.Shape = (GroundAreaShape)packet.Shape;
        area.X = packet.X;
        area.Y = packet.Y;
        area.Z = packet.Z;
        area.FollowEntityId = packet.FollowEntityId;
        area.Radius = packet.Radius;
        area.State = (GroundAreaState)packet.State;
        area.LocalExpiresAtMs = nowMs + packet.RemainingMs;
    }

    public void Remove(long id)
    {
        areas.Remove(id);
    }

    public void Prune(long nowMs)
    {
        var expired = new List<long>();
        foreach (ClientGroundArea area in areas.Values)
        {
            if (area.LocalExpiresAtMs <= nowMs)
            {
                expired.Add(area.Id);
            }
        }

        for (int i = 0; i < expired.Count; i++)
        {
            areas.Remove(expired[i]);
        }
    }
}
```

- [ ] **Step 4: Run store tests**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — PASS (renderer not yet written; only store tests exist).

- [ ] **Step 5: Implement the telegraph renderer**

`VRPG/src/Client/Visuals/GroundTelegraphRenderer.cs`. Flat meshes, not particles: one unit disc (triangle fan, 48 segments) and one unit ring (quad band, inner radius 0.92), uploaded once, tinted per area. This is the one engine-heavy class in the plan; the structure below is correct for VS 1.22, but verify member names against `Sources/` if a call fails to compile — keep the behavior:

```csharp
using System;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

public sealed class GroundTelegraphRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly GroundAreaStore store;
    private readonly VisualStyleResolver styles;
    private readonly CombatVisualsConfig config;
    private readonly MeshRef discMesh;
    private readonly MeshRef ringMesh;
    private readonly Matrixf modelMat = new Matrixf();

    public double RenderOrder => 0.5;
    public int RenderRange => 90;

    public GroundTelegraphRenderer(ICoreClientAPI capi, GroundAreaStore store, VisualStyleResolver styles, CombatVisualsConfig config)
    {
        this.capi = capi;
        this.store = store;
        this.styles = styles;
        this.config = config;
        discMesh = capi.Render.UploadMesh(BuildDisc(48));
        ringMesh = capi.Render.UploadMesh(BuildRing(48, 0.92f));
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        long nowMs = capi.ElapsedMilliseconds;
        store.Prune(nowMs);
        if (store.All.Count == 0 || config.TelegraphOpacity <= 0.01f)
        {
            return;
        }

        IRenderAPI rpi = capi.Render;
        Vec3d cameraPos = capi.World.Player.Entity.CameraPos;
        string ownUid = capi.World.Player.PlayerUID;

        rpi.GlDisableCullFace();
        rpi.GlToggleBlend(true);

        foreach (ClientGroundArea area in store.All)
        {
            Vec3d position = ResolvePosition(area);
            IStandardShaderProgram prog = rpi.PreparedStandardShader((int)position.X, (int)position.Y, (int)position.Z);
            VisualStyle style = styles.Resolve(area.StyleCode, 0, area.Radius);
            Vec4f tint = Tint(style.ColorRgba, Alpha(area, ownUid, nowMs));
            prog.RgbaTint = tint;
            prog.ExtraGlow = 40;
            prog.ModelMatrix = modelMat
                .Identity()
                .Translate(position.X - cameraPos.X, position.Y - cameraPos.Y + 0.06, position.Z - cameraPos.Z)
                .Scale(area.Radius, 1f, area.Radius)
                .Values;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            rpi.RenderMesh(area.Shape == GroundAreaShape.Ring ? ringMesh : discMesh);

            // Wards and owned discs also get a crisp boundary ring on top.
            if (area.Shape == GroundAreaShape.Disc && area.State != GroundAreaState.Triggered)
            {
                Vec4f edge = Tint(style.ColorRgba, Math.Min(1f, tint.A * 2.2f));
                prog.RgbaTint = edge;
                rpi.RenderMesh(ringMesh);
            }

            prog.Stop();
        }

        rpi.GlToggleBlend(false);
        rpi.GlEnableCullFace();
    }

    private Vec3d ResolvePosition(ClientGroundArea area)
    {
        if (area.FollowEntityId != 0)
        {
            Entity? followed = capi.World.GetEntityById(area.FollowEntityId);
            if (followed != null)
            {
                return new Vec3d(followed.Pos.X, followed.Pos.Y, followed.Pos.Z);
            }
        }

        return new Vec3d(area.X, area.Y, area.Z);
    }

    private float Alpha(ClientGroundArea area, string ownUid, long nowMs)
    {
        bool own = string.Equals(area.OwnerUid, ownUid, StringComparison.Ordinal);
        float sinceChange = (nowMs - area.StateChangedAtMs) / 1000f;
        float baseAlpha = area.State switch
        {
            GroundAreaState.Armed => own ? 0.26f : 0.10f,
            GroundAreaState.Triggered => Math.Max(0f, 0.8f - sinceChange * 1.6f),
            GroundAreaState.Expiring => 0.18f + 0.16f * (float)Math.Sin(nowMs / 90.0),
            _ => 0.32f
        };
        return baseAlpha * config.TelegraphOpacity;
    }

    private static Vec4f Tint(int colorRgba, float alpha)
    {
        return new Vec4f(
            ((colorRgba >> 16) & 0xff) / 255f,
            ((colorRgba >> 8) & 0xff) / 255f,
            (colorRgba & 0xff) / 255f,
            alpha);
    }

    private static MeshData BuildDisc(int segments)
    {
        var mesh = new MeshData(segments + 2, segments * 3, false, true, true, false);
        AddVertex(mesh, 0f, 0f, 0f);
        for (int i = 0; i <= segments; i++)
        {
            double angle = Math.PI * 2 * i / segments;
            AddVertex(mesh, (float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
        }

        for (int i = 1; i <= segments; i++)
        {
            mesh.AddIndices(new[] { 0, i, i + 1 });
        }

        return mesh;
    }

    private static MeshData BuildRing(int segments, float inner)
    {
        var mesh = new MeshData((segments + 1) * 2, segments * 6, false, true, true, false);
        for (int i = 0; i <= segments; i++)
        {
            double angle = Math.PI * 2 * i / segments;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            AddVertex(mesh, cos * inner, 0f, sin * inner);
            AddVertex(mesh, cos, 0f, sin);
        }

        for (int i = 0; i < segments; i++)
        {
            int baseIndex = i * 2;
            mesh.AddIndices(new[] { baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 1, baseIndex + 3, baseIndex + 2 });
        }

        return mesh;
    }

    private static void AddVertex(MeshData mesh, float x, float y, float z)
    {
        mesh.AddVertexWithFlags(x, y, z, 0.5f, 0.5f, unchecked((int)0xffffffff), 0);
    }

    public void Dispose()
    {
        discMesh.Dispose();
        ringMesh.Dispose();
    }
}
```

If `PreparedStandardShader` needs a bound texture, bind the engine's white texture via `prog.Tex2D = capi.Render.GetOrLoadTexture(new AssetLocation("game:textures/misc/white.png"));` (confirm the asset path in the game install's `assets/game/textures/` — there is a plain white utility texture; use whichever exists, e.g. `block/white.png`).

- [ ] **Step 6: Wire into the director and mod system**

In `VisualDirector` add:

```csharp
    public GroundAreaStore Areas { get; } = new GroundAreaStore();
    public VisualStyleResolver Styles => styles;

    public void HandleAreaUpsert(GroundAreaUpsertPacket packet)
    {
        Areas.Upsert(packet, capi.ElapsedMilliseconds);
    }

    public void HandleAreaRemove(GroundAreaRemovePacket packet)
    {
        Areas.Remove(packet.Id);
    }
```

In `VRPGModSystem.StartClientSide`, extend the handler chain:

```csharp
            .SetMessageHandler<GroundAreaUpsertPacket>(packet => visualDirector?.HandleAreaUpsert(packet))
            .SetMessageHandler<GroundAreaRemovePacket>(packet => visualDirector?.HandleAreaRemove(packet))
```

and after creating the director:

```csharp
        if (visualDirector != null)
        {
            telegraphRenderer = new GroundTelegraphRenderer(api, visualDirector.Areas, visualDirector.Styles, config.Modules.Rpg.CombatVisuals);
            api.Event.RegisterRenderer(telegraphRenderer, EnumRenderStage.AfterBlit, "vrpg-telegraphs");
        }
```

Add the `private GroundTelegraphRenderer? telegraphRenderer;` field and dispose/unregister it in `Dispose`. If `AfterBlit` draws over the UI, switch to `EnumRenderStage.OIT` (translucent world stage) — that is the intended stage for translucent world geometry; try `OIT` first.

- [ ] **Step 7: Build and verify in game**

Run: `dotnet test ... && dotnet build VRPG/VRPG.csproj` — PASS/clean.

In game: cast a circle-delivery skill — a translucent disc with a brighter rim appears under the caster for ~0.8 s and pulses out. No z-fighting with the ground (raise the 0.06 Y offset if it flickers).

- [ ] **Step 8: Commit**

```bash
git add VRPG/src VRPG/tests
git commit -m "Render ground telegraph discs and rings from the synced area registry"
```

---

### Task 8: Combat text model (pure merge/cap/priority logic)

**Files:**
- Create: `VRPG/src/Client/Visuals/CombatTextModel.cs`
- Test: `VRPG/tests/VRPG.Tests/CombatTextModelTests.cs`

**Interfaces:**
- Produces:
  - `CombatTextSettings { int MergeWindowMs = 500; int MaxEntries = 20; int NumberLifetimeMs = 1100; int WordLifetimeMs = 1400; bool MergeNumbers = true }`
  - `CombatTextKind { Number, Word }`
  - `CombatTextEntry { long TargetEntityId; CombatTextKind Kind; byte DamageType; float Amount; string Word; bool Crit; int MergeCount; long CreatedMs; long LastMergeMs; long ExpiresAtMs; int Priority; double AnchorX, AnchorY, AnchorZ }`
  - `CombatTextModel(CombatTextSettings settings)`: `AddNumber(long targetId, byte damageType, float amount, bool crit, double x, double y, double z, long nowMs)`, `AddWord(long targetId, string word, int priority, double x, double y, double z, long nowMs)`, `Tick(long nowMs)`, `IReadOnlyList<CombatTextEntry> Entries`.
- Rules (from the spec): per target at most one merged number **per damage type** (merging inside a rolling window measured from the last merge) plus **one word slot** (higher priority wins, ties refresh); global cap `MaxEntries` — adding beyond it evicts the lowest-priority, oldest entry; words carry priority 100+, crit numbers 10, plain numbers 1.
- Task 9 renders `Entries`; Task 9's director wiring calls the Add methods.

- [ ] **Step 1: Write the failing tests**

`VRPG/tests/VRPG.Tests/CombatTextModelTests.cs`:

```csharp
using System.Linq;
using VRPG.Client.Visuals;
using Xunit;

namespace VRPG.Tests;

public sealed class CombatTextModelTests
{
    private static CombatTextModel Model(int maxEntries = 20, bool merge = true)
    {
        return new CombatTextModel(new CombatTextSettings { MaxEntries = maxEntries, MergeNumbers = merge });
    }

    [Fact]
    public void RapidHitsOnSameTargetAndTypeMergeIntoOneEntry()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 12f, false, 0, 0, 0, nowMs: 1000);
        model.AddNumber(1, 0, 8f, false, 0, 0, 0, nowMs: 1300);
        CombatTextEntry entry = Assert.Single(model.Entries);
        Assert.Equal(20f, entry.Amount);
        Assert.Equal(2, entry.MergeCount);
        Assert.Equal(1300, entry.LastMergeMs);
    }

    [Fact]
    public void MergeWindowMeasuresFromLastMergeSoStreamsKeepAccumulating()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1400);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1800);
        Assert.Single(model.Entries);
        Assert.Equal(15f, model.Entries[0].Amount);
    }

    [Fact]
    public void HitsOutsideTheWindowStartANewNumber()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1600);
        Assert.Equal(2, model.Entries.Count);
    }

    [Fact]
    public void DifferentDamageTypesNeverMerge()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 1, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 4, 5f, false, 0, 0, 0, 1100);
        Assert.Equal(2, model.Entries.Count);
    }

    [Fact]
    public void MergingDisabledSpawnsIndividualNumbers()
    {
        CombatTextModel model = Model(merge: false);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1100);
        Assert.Equal(2, model.Entries.Count);
    }

    [Fact]
    public void CritMarksTheMergedEntry()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 30f, true, 0, 0, 0, 1200);
        Assert.True(model.Entries[0].Crit);
        Assert.Equal(10, model.Entries[0].Priority);
    }

    [Fact]
    public void OneWordSlotPerTargetHigherPriorityWins()
    {
        CombatTextModel model = Model();
        model.AddWord(1, "STAGGERED", 100, 0, 0, 0, 1000);
        model.AddWord(1, "BREAK", 110, 0, 0, 0, 1100);
        CombatTextEntry word = Assert.Single(model.Entries.Where(e => e.Kind == CombatTextKind.Word));
        Assert.Equal("BREAK", word.Word);
    }

    [Fact]
    public void LowerPriorityWordDoesNotReplaceLiveHigherOne()
    {
        CombatTextModel model = Model();
        model.AddWord(1, "BREAK", 110, 0, 0, 0, 1000);
        model.AddWord(1, "MARKED", 100, 0, 0, 0, 1100);
        Assert.Equal("BREAK", model.Entries.Single(e => e.Kind == CombatTextKind.Word).Word);
    }

    [Fact]
    public void ScreenCapEvictsLowestPriorityOldestFirst()
    {
        CombatTextModel model = Model(maxEntries: 3);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);   // plain, oldest
        model.AddNumber(2, 0, 5f, true, 0, 0, 0, 1100);    // crit
        model.AddWord(3, "BREAK", 110, 0, 0, 0, 1200);     // word
        model.AddNumber(4, 0, 5f, false, 0, 0, 0, 1300);   // forces eviction

        Assert.Equal(3, model.Entries.Count);
        Assert.DoesNotContain(model.Entries, e => e.TargetEntityId == 1);
        Assert.Contains(model.Entries, e => e.Kind == CombatTextKind.Word);
    }

    [Fact]
    public void TickRetiresExpiredEntries()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.Tick(nowMs: 3000);
        Assert.Empty(model.Entries);
    }

    [Fact]
    public void MergingRefreshesExpiry()
    {
        CombatTextModel model = Model();
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1000);
        model.AddNumber(1, 0, 5f, false, 0, 0, 0, 1400);
        model.Tick(nowMs: 2200);
        Assert.Single(model.Entries);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — compile FAIL.

- [ ] **Step 3: Implement the model**

`VRPG/src/Client/Visuals/CombatTextModel.cs`:

```csharp
using System.Collections.Generic;

namespace VRPG.Client.Visuals;

public sealed class CombatTextSettings
{
    public int MergeWindowMs { get; set; } = 500;
    public int MaxEntries { get; set; } = 20;
    public int NumberLifetimeMs { get; set; } = 1100;
    public int WordLifetimeMs { get; set; } = 1400;
    public bool MergeNumbers { get; set; } = true;
}

public enum CombatTextKind
{
    Number,
    Word
}

public sealed class CombatTextEntry
{
    public long TargetEntityId;
    public CombatTextKind Kind;
    public byte DamageType;
    public float Amount;
    public string Word = "";
    public bool Crit;
    public int MergeCount = 1;
    public long CreatedMs;
    public long LastMergeMs;
    public long ExpiresAtMs;
    public int Priority;
    public double AnchorX, AnchorY, AnchorZ;
}

/// <summary>
/// Merge, cap, and priority rules for floating combat text. Engine-free so the
/// spam behavior is unit-testable; the HUD renderer only reads Entries.
/// </summary>
public sealed class CombatTextModel
{
    private readonly CombatTextSettings settings;
    private readonly List<CombatTextEntry> entries = new List<CombatTextEntry>();

    public CombatTextModel(CombatTextSettings settings)
    {
        this.settings = settings;
    }

    public IReadOnlyList<CombatTextEntry> Entries => entries;

    public void AddNumber(long targetId, byte damageType, float amount, bool crit, double x, double y, double z, long nowMs)
    {
        if (settings.MergeNumbers)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                CombatTextEntry existing = entries[i];
                if (existing.Kind == CombatTextKind.Number
                    && existing.TargetEntityId == targetId
                    && existing.DamageType == damageType
                    && nowMs - existing.LastMergeMs <= settings.MergeWindowMs)
                {
                    existing.Amount += amount;
                    existing.MergeCount++;
                    existing.Crit |= crit;
                    existing.Priority = existing.Crit ? 10 : existing.Priority;
                    existing.LastMergeMs = nowMs;
                    existing.ExpiresAtMs = nowMs + settings.NumberLifetimeMs;
                    existing.AnchorX = x;
                    existing.AnchorY = y;
                    existing.AnchorZ = z;
                    return;
                }
            }
        }

        Insert(new CombatTextEntry
        {
            TargetEntityId = targetId,
            Kind = CombatTextKind.Number,
            DamageType = damageType,
            Amount = amount,
            Crit = crit,
            Priority = crit ? 10 : 1,
            CreatedMs = nowMs,
            LastMergeMs = nowMs,
            ExpiresAtMs = nowMs + settings.NumberLifetimeMs,
            AnchorX = x,
            AnchorY = y,
            AnchorZ = z
        }, nowMs);
    }

    public void AddWord(long targetId, string word, int priority, double x, double y, double z, long nowMs)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            CombatTextEntry existing = entries[i];
            if (existing.Kind == CombatTextKind.Word && existing.TargetEntityId == targetId)
            {
                if (priority < existing.Priority)
                {
                    return;
                }

                existing.Word = word;
                existing.Priority = priority;
                existing.CreatedMs = nowMs;
                existing.LastMergeMs = nowMs;
                existing.ExpiresAtMs = nowMs + settings.WordLifetimeMs;
                existing.AnchorX = x;
                existing.AnchorY = y;
                existing.AnchorZ = z;
                return;
            }
        }

        Insert(new CombatTextEntry
        {
            TargetEntityId = targetId,
            Kind = CombatTextKind.Word,
            Word = word,
            Priority = priority,
            CreatedMs = nowMs,
            LastMergeMs = nowMs,
            ExpiresAtMs = nowMs + settings.WordLifetimeMs,
            AnchorX = x,
            AnchorY = y,
            AnchorZ = z
        }, nowMs);
    }

    public void Tick(long nowMs)
    {
        entries.RemoveAll(entry => entry.ExpiresAtMs <= nowMs);
    }

    private void Insert(CombatTextEntry entry, long nowMs)
    {
        entries.Add(entry);
        while (entries.Count > settings.MaxEntries)
        {
            int victim = 0;
            for (int i = 1; i < entries.Count; i++)
            {
                CombatTextEntry candidate = entries[i];
                CombatTextEntry current = entries[victim];
                if (candidate.Priority < current.Priority
                    || (candidate.Priority == current.Priority && candidate.CreatedMs < current.CreatedMs))
                {
                    victim = i;
                }
            }

            entries.RemoveAt(victim);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — PASS.

- [ ] **Step 5: Commit**

```bash
git add VRPG/src/Client/Visuals/CombatTextModel.cs VRPG/tests
git commit -m "Add merge, cap, and priority model for floating combat text"
```

---

### Task 9: Combat text renderer and director routing

**Files:**
- Create: `VRPG/src/Client/HudElementVRPGCombatText.cs`
- Modify: `VRPG/src/Client/Visuals/VisualDirector.cs` (route text kinds)
- Modify: `VRPG/src/VRPGModSystem.cs` (open the HUD element)

**Interfaces:**
- Consumes: `CombatTextModel`/`CombatTextEntry` (Task 8), `VisualDamageTypes.ColorRgba` (Task 3), `CombatVisualsConfig` (Task 4).
- Produces: `VisualDirector.CombatText` (`CombatTextModel`, public get) — the HUD reads it; `HudElementVRPGCombatText(ICoreClientAPI capi, VisualDirector director)`.
- Event-word mapping used by the director (Task 16 exercises every kind): `Break → "BREAK"` (priority 110), `Counter → "COUNTER"` (105), `Consume → "CONSUMED"` (100), `Mark → "MARKED"` (95), `Shield → shield number in the shield color` (Heal-style number), `Heal → green number`.

- [ ] **Step 1: Route events in the director**

In `VisualDirector` add:

```csharp
    public CombatTextModel CombatText { get; } = new CombatTextModel(new CombatTextSettings());
```

Keep `CombatText`'s `MergeNumbers` in sync with config: set `CombatText` settings from `Config` where constructed — pass `new CombatTextSettings { MergeNumbers = config.MergeNumbers }`. Then extend the `switch` in `HandleEvent`:

```csharp
            case CombatVisualKind.Damage:
                if (Config.CombatTextEnabled && Config.DamageNumbers)
                {
                    CombatText.AddNumber(packet.TargetEntityId, packet.DamageType, packet.Magnitude,
                        (packet.Flags & (int)CombatVisualFlags.Crit) != 0,
                        packet.X, packet.Y, packet.Z, capi.ElapsedMilliseconds);
                }

                break;
            case CombatVisualKind.Heal:
                if (Config.CombatTextEnabled && Config.DamageNumbers)
                {
                    CombatText.AddNumber(packet.TargetEntityId, VisualDamageTypes.Heal, packet.Magnitude, false,
                        packet.X, packet.Y, packet.Z, capi.ElapsedMilliseconds);
                }

                break;
            case CombatVisualKind.Shield:
                if (Config.CombatTextEnabled && Config.DamageNumbers)
                {
                    CombatText.AddNumber(packet.TargetEntityId, VisualDamageTypes.Cold, packet.Magnitude, false,
                        packet.X, packet.Y, packet.Z, capi.ElapsedMilliseconds);
                }

                break;
            case CombatVisualKind.Break:
                AddWord(packet, "BREAK", 110);
                break;
            case CombatVisualKind.Counter:
                AddWord(packet, "COUNTER", 105);
                break;
            case CombatVisualKind.Consume:
                AddWord(packet, "CONSUMED", 100);
                skillFx.Burst(style, position);
                break;
            case CombatVisualKind.Mark:
                AddWord(packet, "MARKED", 95);
                break;
```

with the private helper:

```csharp
    private void AddWord(CombatVisualEventPacket packet, string word, int priority)
    {
        if (Config.CombatTextEnabled && Config.EventWords)
        {
            CombatText.AddWord(packet.TargetEntityId, word, priority, packet.X, packet.Y, packet.Z, capi.ElapsedMilliseconds);
        }
    }
```

- [ ] **Step 2: Implement the HUD renderer**

`VRPG/src/Client/HudElementVRPGCombatText.cs`, following the Cairo full-frame pattern of `HudElementVRPGEntityHealthBars` (redraw each frame, project world anchors to screen):

```csharp
using System;
using Cairo;
using VRPG.Client.Visuals;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client;

public sealed class HudElementVRPGCombatText : HudElement
{
    private readonly VisualDirector director;
    private LoadedTexture texture;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGCombatText(ICoreClientAPI capi, VisualDirector director) : base(capi)
    {
        this.director = director;
        texture = new LoadedTexture(capi);
        TryOpen();
    }

    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveRenderEvents() => true;
    public override bool TryClose() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        long nowMs = capi.ElapsedMilliseconds;
        director.CombatText.Tick(nowMs);
        if (!director.Config.CombatTextEnabled || director.CombatText.Entries.Count == 0)
        {
            return;
        }

        int frameWidth = Math.Max(1, capi.Render.FrameWidth);
        int frameHeight = Math.Max(1, capi.Render.FrameHeight);
        using var surface = new ImageSurface((Format)0, frameWidth, frameHeight);
        using var ctx = new Context(surface);
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        foreach (CombatTextEntry entry in director.CombatText.Entries)
        {
            DrawEntry(ctx, entry, nowMs, frameWidth, frameHeight);
        }

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
        if (texture.TextureId > 0)
        {
            capi.Render.Render2DLoadedTexture(texture, 0, 0, 2300f);
        }
    }

    private void DrawEntry(Context ctx, CombatTextEntry entry, long nowMs, int frameWidth, int frameHeight)
    {
        Vec3d anchor = ResolveAnchor(entry);
        Vec3d screen = MatrixToolsd.Project(anchor,
            capi.Render.PerspectiveProjectionMat, capi.Render.PerspectiveViewMat, frameWidth, frameHeight);
        if (screen.Z < 0)
        {
            return;
        }

        long lifetime = Math.Max(1, entry.ExpiresAtMs - entry.CreatedMs);
        float age = Math.Clamp((nowMs - entry.CreatedMs) / (float)lifetime, 0f, 1f);
        double rise = 34.0 * age;
        double alpha = age < 0.75 ? 0.96 : 0.96 * (1.0 - (age - 0.75) / 0.25);

        string text;
        double fontSize;
        int color;
        if (entry.Kind == CombatTextKind.Word)
        {
            text = entry.Word;
            fontSize = 21.0;
            color = unchecked((int)0xffffd166);
        }
        else
        {
            text = entry.Amount >= 100f ? entry.Amount.ToString("0") : entry.Amount.ToString("0.#");
            bool heal = entry.DamageType == VisualDamageTypes.Heal;
            if (heal)
            {
                text = "+" + text;
            }

            fontSize = entry.Crit ? 20.0 : 14.5;
            fontSize += Math.Min(4.0, (entry.MergeCount - 1) * 0.8);
            color = VisualDamageTypes.ColorRgba(entry.DamageType);
        }

        double x = screen.X;
        double y = frameHeight - screen.Y - rise;
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(fontSize);
        TextExtents ext = ctx.TextExtents(text);
        double tx = x - ext.Width / 2 - ext.XBearing;

        ctx.SetSourceRGBA(0, 0, 0, alpha * 0.85);
        ctx.MoveTo(tx + 1.5, y + 1.5);
        ctx.ShowText(text);

        ctx.SetSourceRGBA(
            ((color >> 16) & 0xff) / 255.0,
            ((color >> 8) & 0xff) / 255.0,
            (color & 0xff) / 255.0,
            alpha);
        ctx.MoveTo(tx, y);
        ctx.ShowText(text);
    }

    private Vec3d ResolveAnchor(CombatTextEntry entry)
    {
        Entity? target = entry.TargetEntityId != 0 ? capi.World.GetEntityById(entry.TargetEntityId) : null;
        if (target != null)
        {
            return new Vec3d(target.Pos.X, target.Pos.Y + Math.Max(1.2f, target.SelectionBox?.Y2 ?? 1.5f) + 0.35, target.Pos.Z);
        }

        return new Vec3d(entry.AnchorX, entry.AnchorY + 1.2, entry.AnchorZ);
    }

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }
}
```

- [ ] **Step 3: Open the HUD in the mod system**

In `VRPGModSystem.StartClientSide`, after the director is created:

```csharp
        if (visualDirector != null)
        {
            clientCombatText = new HudElementVRPGCombatText(api, visualDirector);
        }
```

Add the field and dispose it in `Dispose` like the other HUD elements.

- [ ] **Step 4: Build and verify in game**

`dotnet build VRPG/VRPG.csproj`, then in game: cast an area skill into several enemies — one merged number per enemy (colored by the skill's damage type) rises and fades; spamming casts grows the number instead of stacking columns.

- [ ] **Step 5: Commit**

```bash
git add VRPG/src
git commit -m "Render merged floating combat text from visual events"
```

---

### Task 10: Nameplate status overlay — icons, stacks, buildup bar, shield segment

**Files:**
- Modify: `VRPG/src/Client/HudElementVRPGEntityHealthBars.cs`
- Modify: `VRPG/src/VRPGModSystem.cs` (pass registry + cache into the HUD)

**Interfaces:**
- Consumes: `EntityStatusCache`/`ActiveStatus` (Task 2), `StatusEffectDefinition.Visual` (Task 1), `VrpgIconPainter.Draw(ctx, token, x, y, size, r, g, b, alpha)` (existing), `SkillDefinitionValidator.TryParseColor`.
- Produces: constructor becomes `HudElementVRPGEntityHealthBars(ICoreClientAPI capi, RpgEntityHudConfig config, VRPGDataRegistry data)`; it owns a private `EntityStatusCache`. Task 11 creates its own cache instance — the caches are cheap read-views, they do not need to be shared.
- Shield overlay reads `entity.WatchedAttributes` floats `"vrpgShieldCurrent"` / `"vrpgShieldMax"`. Nothing writes them for creatures yet — the overlay is data-driven and dormant until a mechanic does; the player's own shield already renders on the resource bars.

- [ ] **Step 1: Extend the constructor and fields**

Add `private readonly VRPGDataRegistry data;` and `private readonly EntityStatusCache statusCache = new EntityStatusCache();`, extend the constructor, add `using VRPG.Client.Visuals; using VRPG.Data; using VRPG.Data.Definitions;`, and update the construction call in `VRPGModSystem.StartClientSide` (both places the HUD is constructed) to pass `DataRegistry`.

- [ ] **Step 2: Draw the overlay in DrawEntry**

At the end of `DrawEntry`, after the `DrawHealthBar(...)` call, add:

```csharp
        DrawStatusOverlay(ctx, entity, x, y + barHeight + 3 * scale, width, scale);
```

(`y` at that point is the bar's top; reuse the local variables already in scope — bar height and scale.) Then add the methods:

```csharp
    private void DrawStatusOverlay(Context ctx, Entity entity, double x, double y, double width, double scale)
    {
        IReadOnlyList<Visuals.ActiveStatus> statuses = statusCache.Update(entity.EntityId, entity.WatchedAttributes, capi.ElapsedMilliseconds);
        DrawShieldOverlay(ctx, entity, x, y - 3 * scale, width, scale);

        double iconSize = 15 * scale;
        double iconX = x;
        foreach (Visuals.ActiveStatus status in statuses)
        {
            StatusEffectDefinition? definition = data.StatusEffects.Get(status.Code);
            if (definition == null)
            {
                continue;
            }

            if (definition.Visual.Buildup?.ShowBar == true)
            {
                DrawBuildupBar(ctx, definition, status, x, y, width, scale);
                y += 7 * scale;
                continue;
            }

            if (iconX + iconSize > x + width)
            {
                continue;
            }

            DrawStatusIcon(ctx, definition, status, iconX, y, iconSize, scale);
            iconX += iconSize + 3 * scale;
        }
    }

    private void DrawStatusIcon(Context ctx, StatusEffectDefinition definition, Visuals.ActiveStatus status, double x, double y, double size, double scale)
    {
        if (!Data.SkillDefinitionValidator.TryParseColor(definition.Visual.Color, out int color))
        {
            color = unchecked((int)0xfff2ede4);
        }

        double r = ((color >> 16) & 0xff) / 255.0;
        double g = ((color >> 8) & 0xff) / 255.0;
        double b = (color & 0xff) / 255.0;

        // Duration wipe: darken the spent fraction of a backing plate.
        double remaining = status.DurationMs <= 0 ? 1.0 : Math.Clamp(status.RemainingSeconds(capi.ElapsedMilliseconds) * 1000.0 / status.DurationMs, 0, 1);
        SetColor(ctx, 0, 0, 0, 0.6);
        ctx.Rectangle(x, y, size, size);
        ctx.Fill();
        SetColor(ctx, r * 0.35, g * 0.35, b * 0.35, 0.85);
        ctx.Rectangle(x, y + size * (1 - remaining), size, size * remaining);
        ctx.Fill();

        VrpgIconPainter.Draw(ctx, string.IsNullOrWhiteSpace(definition.Visual.Icon) ? definition.Code : definition.Visual.Icon, x, y, size, r, g, b);

        if (definition.Visual.ShowStacks && status.Stacks > 1)
        {
            ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(Math.Max(8.0, size * 0.52));
            string stacks = status.Stacks.ToString();
            TextExtents ext = ctx.TextExtents(stacks);
            SetColor(ctx, 0, 0, 0, 0.9);
            ctx.MoveTo(x + size - ext.Width + 0.5, y + size - 0.5);
            ctx.ShowText(stacks);
            SetColor(ctx, 1, 1, 1, 0.98);
            ctx.MoveTo(x + size - ext.Width - 0.5, y + size - 1.5);
            ctx.ShowText(stacks);
        }
    }

    private void DrawBuildupBar(Context ctx, StatusEffectDefinition definition, Visuals.ActiveStatus status, double x, double y, double width, double scale)
    {
        StatusBuildupVisualDefinition buildup = definition.Visual.Buildup!;
        double barHeight = 5 * scale;
        double fill = Math.Clamp(status.Magnitude / Math.Max(1f, buildup.Threshold), 0, 1);
        bool atThreshold = fill >= 1.0;

        if (!Data.SkillDefinitionValidator.TryParseColor(definition.Visual.Color, out int color))
        {
            color = unchecked((int)0xffffd166);
        }

        SetColor(ctx, 0, 0, 0, 0.72);
        ctx.Rectangle(x - 1, y - 1, width + 2, barHeight + 2);
        ctx.Fill();
        SetColor(ctx, 0.15, 0.12, 0.08, 0.9);
        ctx.Rectangle(x, y, width, barHeight);
        ctx.Fill();

        double flash = atThreshold && buildup.FlashAtThreshold
            ? 0.65 + 0.35 * Math.Sin(capi.ElapsedMilliseconds / 80.0)
            : 0.95;
        SetColor(ctx, ((color >> 16) & 0xff) / 255.0 * flash, ((color >> 8) & 0xff) / 255.0 * flash, (color & 0xff) / 255.0 * flash, 0.95);
        ctx.Rectangle(x, y, width * fill, barHeight);
        ctx.Fill();

        // Threshold notch.
        SetColor(ctx, 1, 1, 1, 0.85);
        ctx.Rectangle(x + width - 1.5, y - 1, 1.5, barHeight + 2);
        ctx.Fill();
    }

    private void DrawShieldOverlay(Context ctx, Entity entity, double barX, double barBottomY, double width, double scale)
    {
        float shield = entity.WatchedAttributes.GetFloat("vrpgShieldCurrent");
        float shieldMax = entity.WatchedAttributes.GetFloat("vrpgShieldMax");
        if (shield <= 0f || shieldMax <= 0f || !TryReadHealth(entity, out _, out float maxHealth) || maxHealth <= 0f)
        {
            return;
        }

        double fraction = Math.Min(1.0, shield / maxHealth);
        double overlayHeight = 3 * scale;
        SetColor(ctx, 0.47, 0.94, 1.0, 0.9);
        ctx.Rectangle(barX, barBottomY - overlayHeight, width * fraction, overlayHeight);
        ctx.Fill();
    }
```

Adjust the `DrawEntry` layout so the row leaves vertical room: the projected `y` already anchors above the head; the overlay grows downward toward the entity, which is fine. If the buildup bar collides with the health text in `readable` mode, offset it by the bar height once — check in game.

Also call `statusCache.Prune(capi.ElapsedMilliseconds);` once per `CollectEntries()` refresh.

- [ ] **Step 3: Build and verify in game**

Build, then in game (uses Task 16's command once it exists — for now use the existing `/vrpg` status admin command if present, or defer the visual check to Task 16):
- Apply `vrpg:corrosion` with 3 stacks to a looked-at creature → rust icon with "3" appears under its health bar and empties top-down as it expires.
- Apply `vrpg:stagger` with magnitude 60 → gold buildup bar at 60%; at 100+ it flashes.

- [ ] **Step 4: Commit**

```bash
git add VRPG/src
git commit -m "Show status icons, stacks, buildup bars, and shield overlays on entity nameplates"
```

---

### Task 11: Status aura emitters

**Files:**
- Create: `VRPG/src/Client/Visuals/AuraFamilies.cs`
- Create: `VRPG/src/Client/Visuals/AuraEmitterSystem.cs`
- Modify: `VRPG/src/VRPGModSystem.cs` (start/stop the system)

**Interfaces:**
- Consumes: `EntityStatusCache` (Task 2), `StatusEffectDefinition.Visual.Aura` / `AuraIntensityPerStack` (Task 1), `CombatVisualsConfig.StatusAuras` / `Intensity`.
- Produces: `AuraFamily { int ColorRgba; float RiseVelocity; float Gravity; float Scale; float QuantityPerTick; bool Cube }`; `AuraFamilies.Get(string name)` → `AuraFamily?` for `rustflakes|embers|drips|frost|sparks|mark`; `AuraEmitterSystem(ICoreClientAPI capi, VRPGDataRegistry data, CombatVisualsConfig config)` with `Start()`, `Dispose()`. `MaxAuraEntities = 12` (nearest first, current target always included).

- [ ] **Step 1: Implement aura families**

`VRPG/src/Client/Visuals/AuraFamilies.cs`:

```csharp
using System.Collections.Generic;

namespace VRPG.Client.Visuals;

public sealed class AuraFamily
{
    public int ColorRgba;
    public float RiseVelocity;
    public float Gravity;
    public float Scale;
    public float QuantityPerTick;
    public bool Cube;
}

public static class AuraFamilies
{
    private static readonly Dictionary<string, AuraFamily> byName = new Dictionary<string, AuraFamily>(System.StringComparer.OrdinalIgnoreCase)
    {
        ["rustflakes"] = new AuraFamily { ColorRgba = unchecked((int)0xd0c96a1e), RiseVelocity = -0.05f, Gravity = 0.35f, Scale = 0.22f, QuantityPerTick = 1.6f, Cube = true },
        ["embers"] = new AuraFamily { ColorRgba = unchecked((int)0xd0f06a28), RiseVelocity = 0.35f, Gravity = -0.02f, Scale = 0.2f, QuantityPerTick = 1.8f },
        ["drips"] = new AuraFamily { ColorRgba = unchecked((int)0xd0b81c2e), RiseVelocity = -0.15f, Gravity = 0.6f, Scale = 0.18f, QuantityPerTick = 1.2f },
        ["frost"] = new AuraFamily { ColorRgba = unchecked((int)0xc078c9f0), RiseVelocity = 0.06f, Gravity = 0.02f, Scale = 0.24f, QuantityPerTick = 1.2f },
        ["sparks"] = new AuraFamily { ColorRgba = unchecked((int)0xe0ffe66d), RiseVelocity = 0.25f, Gravity = 0.1f, Scale = 0.14f, QuantityPerTick = 1.5f },
        ["mark"] = new AuraFamily { ColorRgba = unchecked((int)0xc0ffd166), RiseVelocity = 0.5f, Gravity = 0f, Scale = 0.26f, QuantityPerTick = 0.8f }
    };

    public static AuraFamily? Get(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && byName.TryGetValue(name, out AuraFamily? family) ? family : null;
    }
}
```

- [ ] **Step 2: Implement the emitter system**

`VRPG/src/Client/Visuals/AuraEmitterSystem.cs`:

```csharp
using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>
/// Loops a subtle particle aura per synced status so primed targets read at a
/// glance. Caps at the nearest MaxAuraEntities entities; the current target is
/// always included.
/// </summary>
public sealed class AuraEmitterSystem : IDisposable
{
    public const int MaxAuraEntities = 12;
    public const float ScanRadius = 24f;

    private readonly ICoreClientAPI capi;
    private readonly VRPGDataRegistry data;
    private readonly CombatVisualsConfig config;
    private readonly EntityStatusCache cache = new EntityStatusCache();
    private long listenerId;

    public AuraEmitterSystem(ICoreClientAPI capi, VRPGDataRegistry data, CombatVisualsConfig config)
    {
        this.capi = capi;
        this.data = data;
        this.config = config;
    }

    public void Start()
    {
        listenerId = capi.Event.RegisterGameTickListener(Tick, 250);
    }

    private void Tick(float dt)
    {
        if (!config.StatusAuras || capi.World?.Player?.Entity == null)
        {
            return;
        }

        long nowMs = capi.ElapsedMilliseconds;
        cache.Prune(nowMs);
        EntityPlayer playerEntity = capi.World.Player.Entity;
        Entity? targeted = capi.World.Player.CurrentEntitySelection?.Entity;

        Entity[] nearby = capi.World.GetEntitiesAround(playerEntity.Pos.XYZ, ScanRadius, ScanRadius,
            entity => entity != playerEntity && entity.Alive && entity.WatchedAttributes.HasAttribute(Modules.Rpg.StatusEffects.StatusSync.TreeKey));

        Array.Sort(nearby, (left, right) =>
            left.Pos.SquareDistanceTo(playerEntity.Pos.XYZ).CompareTo(right.Pos.SquareDistanceTo(playerEntity.Pos.XYZ)));

        int emitted = 0;
        for (int i = 0; i < nearby.Length && emitted < MaxAuraEntities; i++)
        {
            if (EmitFor(nearby[i], nowMs))
            {
                emitted++;
            }
        }

        if (targeted != null && targeted != playerEntity && Array.IndexOf(nearby, targeted) >= MaxAuraEntities)
        {
            EmitFor(targeted, nowMs);
        }
    }

    private bool EmitFor(Entity entity, long nowMs)
    {
        IReadOnlyList<ActiveStatus> statuses = cache.Update(entity.EntityId, entity.WatchedAttributes, nowMs);
        bool any = false;
        foreach (ActiveStatus status in statuses)
        {
            StatusEffectDefinition? definition = data.StatusEffects.Get(status.Code);
            AuraFamily? family = definition == null ? null : AuraFamilies.Get(definition.Visual.Aura);
            if (family == null)
            {
                continue;
            }

            any = true;
            float quantity = family.QuantityPerTick
                * Math.Max(1f, status.Stacks * definition!.Visual.AuraIntensityPerStack)
                * config.Intensity;
            float half = Math.Max(0.25f, entity.SelectionBox.XSize * 0.5f);
            float height = Math.Max(0.8f, entity.SelectionBox.YSize);
            Vec3d center = entity.Pos.XYZ;
            capi.World.SpawnParticles(
                quantity,
                family.ColorRgba,
                center.AddCopy(-half, height * 0.2, -half),
                center.AddCopy(half, height * 0.9, half),
                new Vec3f(-0.05f, family.RiseVelocity - 0.03f, -0.05f),
                new Vec3f(0.05f, family.RiseVelocity + 0.08f, 0.05f),
                0.9f,
                family.Gravity,
                family.Scale,
                family.Cube ? EnumParticleModel.Cube : EnumParticleModel.Quad);
        }

        return any;
    }

    public void Dispose()
    {
        if (listenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(listenerId);
            listenerId = 0;
        }
    }
}
```

- [ ] **Step 3: Wire into the mod system**

In `VRPGModSystem.StartClientSide`, next to the director creation: create `auraEmitters = new AuraEmitterSystem(api, DataRegistry, config.Modules.Rpg.CombatVisuals); auraEmitters.Start();` with field + `Dispose()` teardown.

- [ ] **Step 4: Build and verify in game**

Build; in game apply `vrpg:corrosion` to a creature — falling rust-colored flakes loop around it, denser at higher stacks; toggling Status Auras off (config file until Task 15) stops them.

- [ ] **Step 5: Commit**

```bash
git add VRPG/src
git commit -m "Loop status aura particles around afflicted entities with a nearest-entity cap"
```

---

### Task 12: Player buff/debuff row HUD

**Files:**
- Create: `VRPG/src/Client/HudElementVRPGPlayerStatus.cs`
- Modify: `VRPG/src/VRPGModSystem.cs` (open/dispose)

**Interfaces:**
- Consumes: `EntityStatusCache` (own instance), `StatusEffectDefinition.Visual`, `VrpgIconPainter`. Statuses reach the player's own entity through the same `vrpgStatus` WatchedAttributes sync as everyone else — no new channel.
- Produces: `HudElementVRPGPlayerStatus(ICoreClientAPI capi, VRPGDataRegistry data)`. Magic Shield needs no work here: `RpgResourcePacket.MagicShield` already renders on the resource bars.

- [ ] **Step 1: Implement the HUD element**

`VRPG/src/Client/HudElementVRPGPlayerStatus.cs` — icon row (26 px icons) with stack count and remaining seconds, docked bottom-center above the hotbar area (fixed offset; a config position can come later if it collides with another HUD):

```csharp
using System;
using System.Collections.Generic;
using Cairo;
using VRPG.Client.UI;
using VRPG.Client.Visuals;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class HudElementVRPGPlayerStatus : HudElement
{
    private readonly VRPGDataRegistry data;
    private readonly EntityStatusCache cache = new EntityStatusCache();
    private LoadedTexture texture;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGPlayerStatus(ICoreClientAPI capi, VRPGDataRegistry data) : base(capi)
    {
        this.data = data;
        texture = new LoadedTexture(capi);
        TryOpen();
    }

    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveRenderEvents() => true;
    public override bool TryClose() => false;

    public override void OnRenderGUI(float deltaTime)
    {
        if (capi.World?.Player?.Entity == null)
        {
            return;
        }

        long nowMs = capi.ElapsedMilliseconds;
        IReadOnlyList<ActiveStatus> statuses = cache.Update(
            capi.World.Player.Entity.EntityId, capi.World.Player.Entity.WatchedAttributes, nowMs);
        if (statuses.Count == 0)
        {
            return;
        }

        const double iconSize = 26.0;
        const double gap = 6.0;
        int frameWidth = Math.Max(1, capi.Render.FrameWidth);
        int frameHeight = Math.Max(1, capi.Render.FrameHeight);
        double totalWidth = statuses.Count * (iconSize + gap) - gap;
        double x = (frameWidth - totalWidth) / 2.0;
        double y = frameHeight - 168.0;

        using var surface = new ImageSurface((Format)0, frameWidth, frameHeight);
        using var ctx = new Context(surface);
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;

        foreach (ActiveStatus status in statuses)
        {
            StatusEffectDefinition? definition = data.StatusEffects.Get(status.Code);
            if (definition == null)
            {
                continue;
            }

            if (!SkillDefinitionValidator.TryParseColor(definition.Visual.Color, out int color))
            {
                color = unchecked((int)0xfff2ede4);
            }

            double r = ((color >> 16) & 0xff) / 255.0;
            double g = ((color >> 8) & 0xff) / 255.0;
            double b = (color & 0xff) / 255.0;

            ctx.SetSourceRGBA(0, 0, 0, 0.62);
            ctx.Rectangle(x - 2, y - 2, iconSize + 4, iconSize + 4);
            ctx.Fill();
            VrpgIconPainter.Draw(ctx, string.IsNullOrWhiteSpace(definition.Visual.Icon) ? definition.Code : definition.Visual.Icon, x, y, iconSize, r, g, b);

            ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
            ctx.SetFontSize(10.5);
            string seconds = Math.Ceiling(status.RemainingSeconds(nowMs)).ToString("0");
            TextExtents ext = ctx.TextExtents(seconds);
            ctx.SetSourceRGBA(0, 0, 0, 0.9);
            ctx.MoveTo(x + (iconSize - ext.Width) / 2 + 1, y + iconSize + 12);
            ctx.ShowText(seconds);
            ctx.SetSourceRGBA(0.95, 0.93, 0.88, 0.96);
            ctx.MoveTo(x + (iconSize - ext.Width) / 2, y + iconSize + 11);
            ctx.ShowText(seconds);

            if (definition.Visual.ShowStacks && status.Stacks > 1)
            {
                string stacks = status.Stacks.ToString();
                TextExtents stackExt = ctx.TextExtents(stacks);
                ctx.SetSourceRGBA(0, 0, 0, 0.9);
                ctx.MoveTo(x + iconSize - stackExt.Width + 1, y + 11);
                ctx.ShowText(stacks);
                ctx.SetSourceRGBA(1, 1, 1, 0.98);
                ctx.MoveTo(x + iconSize - stackExt.Width, y + 10);
                ctx.ShowText(stacks);
            }

            x += iconSize + gap;
        }

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
        if (texture.TextureId > 0)
        {
            capi.Render.Render2DLoadedTexture(texture, 0, 0, 2250f);
        }
    }

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }
}
```

- [ ] **Step 2: Wire, build, verify**

Open it in `StartClientSide` (guarded by `config.Modules.Rpg.Enabled`), dispose in `Dispose`. Build; in game apply a status to yourself (Task 16 command, or temporarily via the server console once it exists) — icon with countdown appears above the hotbar. If it overlaps the vanilla or VRPG hotbar at some UI scales, adjust the `y` offset.

- [ ] **Step 3: Commit**

```bash
git add VRPG/src
git commit -m "Add player buff and debuff row HUD driven by synced statuses"
```

---

### Task 13: Hotbar empowered glow and crosshair window pulse

**Files:**
- Modify: `VRPG/src/Network/SkillLoadoutPacket.cs` (add `Empowered`)
- Modify: `VRPG/src/Modules/Rpg/Skills/SkillCastingService.cs` (empowered set + loadout flag)
- Modify: `VRPG/src/Modules/Rpg/RpgModule.cs` (public setter)
- Modify: `VRPG/src/Client/UI/GuiElementVrpgSkillHotbar.cs` (glow in `DrawSlot`)
- Create: `VRPG/src/Client/HudElementVRPGWindowPulse.cs`
- Modify: `VRPG/src/Client/Visuals/VisualDirector.cs` (WindowOpen routing)
- Modify: `VRPG/src/VRPGModSystem.cs` (pulse HUD wiring)

**Interfaces:**
- Consumes: `CombatVisualKind.WindowOpen` (Task 3), existing loadout broadcast (1 s tick + on-change sends).
- Produces:
  - `SkillLoadoutSlotPacket.Empowered` (`[ProtoMember(11)] bool`).
  - `SkillCastingService.SetEmpowered(string playerUid, string skillCode, bool on)`.
  - `RpgModule.SetSkillEmpowered(IServerPlayer player, string skillCode, bool on)` — sets and immediately resends the loadout. No gameplay computes empowerment yet (Stagger thresholds arrive with skill mechanics); this is the framework hook plus the Task 16 admin toggle.
  - `HudElementVRPGWindowPulse.Trigger(int colorRgba, float durationSeconds)`; `VisualDirector.WindowPulse` (settable property).

- [ ] **Step 1: Server side**

`SkillLoadoutPacket.cs` — add to `SkillLoadoutSlotPacket`:

```csharp
    [ProtoMember(11)]
    public bool Empowered { get; set; }
```

`SkillCastingService` — add:

```csharp
    private readonly Dictionary<string, HashSet<string>> empoweredSkills = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

    public void SetEmpowered(string playerUid, string skillCode, bool on)
    {
        if (!empoweredSkills.TryGetValue(playerUid, out HashSet<string>? codes))
        {
            codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            empoweredSkills[playerUid] = codes;
        }

        string normalized = NormalizeCode(skillCode);
        if (on)
        {
            codes.Add(normalized);
        }
        else
        {
            codes.Remove(normalized);
        }
    }
```

and in `BuildLoadout`, inside the slot construction add:

```csharp
                Empowered = skill != null
                    && empoweredSkills.TryGetValue(player.PlayerUID, out HashSet<string>? playerEmpowered)
                    && playerEmpowered.Contains(skill.Code),
```

`RpgModule` — add:

```csharp
    public void SetSkillEmpowered(IServerPlayer player, string skillCode, bool on)
    {
        skills?.SetEmpowered(player.PlayerUID, skillCode, on);
        SendLoadout(player);
    }
```

- [ ] **Step 2: Hotbar glow**

In `GuiElementVrpgSkillHotbar.DrawSlot`, after the slot background/border drawing and before the icon, add a gold empowered frame (match the file's local drawing helpers — it draws with raw Cairo calls):

```csharp
        if (entry?.Empowered == true)
        {
            ctx.SetSourceRGBA(1.0, 0.82, 0.4, 0.95);
            ctx.LineWidth = 3.0;
            ctx.Rectangle(x + 1.5, y + 1.5, size - 3.0, size - 3.0);
            ctx.Stroke();
            ctx.SetSourceRGBA(1.0, 0.82, 0.4, 0.28);
            ctx.Rectangle(x + 3.0, y + 3.0, size - 6.0, size - 6.0);
            ctx.Fill();
        }
```

The hotbar redraws on every snapshot (`SetSnapshot` → `Redraw`), and loadouts rebroadcast at 1 Hz plus immediately on `SetSkillEmpowered`, so the glow appears within a second without per-frame animation. (A pulsing glow would need drawing in `RenderInteractiveElements`; skip unless the static frame reads poorly in game.)

- [ ] **Step 3: Window pulse HUD**

`VRPG/src/Client/HudElementVRPGWindowPulse.cs`:

```csharp
using System;
using Cairo;
using Vintagestory.API.Client;

namespace VRPG.Client;

public sealed class HudElementVRPGWindowPulse : HudElement
{
    private LoadedTexture texture;
    private long startedMs = -1;
    private long endsMs;
    private int colorRgba;

    public override string? ToggleKeyCombinationCode => null;
    public override bool Focusable => false;

    public HudElementVRPGWindowPulse(ICoreClientAPI capi) : base(capi)
    {
        texture = new LoadedTexture(capi);
        TryOpen();
    }

    public override bool ShouldReceiveKeyboardEvents() => false;
    public override bool ShouldReceiveRenderEvents() => true;
    public override bool TryClose() => false;

    public void Trigger(int colorRgba, float durationSeconds)
    {
        this.colorRgba = colorRgba;
        startedMs = capi.ElapsedMilliseconds;
        endsMs = startedMs + (long)(Math.Max(0.4f, durationSeconds) * 1000f);
    }

    public override void OnRenderGUI(float deltaTime)
    {
        long nowMs = capi.ElapsedMilliseconds;
        if (startedMs < 0 || nowMs >= endsMs)
        {
            return;
        }

        int frameWidth = Math.Max(1, capi.Render.FrameWidth);
        int frameHeight = Math.Max(1, capi.Render.FrameHeight);
        double cx = frameWidth / 2.0;
        double cy = frameHeight / 2.0 + 70.0;

        // Repeating 600 ms expanding ring while the window is open.
        double phase = ((nowMs - startedMs) % 600) / 600.0;
        double radius = 10.0 + 22.0 * phase;
        double alpha = 0.85 * (1.0 - phase);

        using var surface = new ImageSurface((Format)0, frameWidth, frameHeight);
        using var ctx = new Context(surface);
        ctx.Operator = Operator.Clear;
        ctx.Paint();
        ctx.Operator = Operator.Over;
        ctx.SetSourceRGBA(
            ((colorRgba >> 16) & 0xff) / 255.0,
            ((colorRgba >> 8) & 0xff) / 255.0,
            (colorRgba & 0xff) / 255.0,
            alpha);
        ctx.LineWidth = 3.0;
        ctx.Arc(cx, cy, radius, 0, Math.PI * 2);
        ctx.Stroke();

        capi.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
        if (texture.TextureId > 0)
        {
            capi.Render.Render2DLoadedTexture(texture, 0, 0, 2260f);
        }
    }

    public override void Dispose()
    {
        texture.Dispose();
        base.Dispose();
    }
}
```

- [ ] **Step 4: Route WindowOpen**

`VisualDirector`: add `public HudElementVRPGWindowPulse? WindowPulse { get; set; }` (namespace `VRPG.Client`) and the switch case:

```csharp
            case CombatVisualKind.WindowOpen:
                if (packet.TargetEntityId == capi.World.Player?.Entity?.EntityId)
                {
                    WindowPulse?.Trigger(style.ColorRgba, packet.Magnitude);
                }

                break;
```

`VRPGModSystem.StartClientSide`, next to the combat text HUD:

```csharp
        if (visualDirector != null)
        {
            clientWindowPulse = new HudElementVRPGWindowPulse(api);
            visualDirector.WindowPulse = clientWindowPulse;
        }
```

Field + dispose as with the other HUD elements.

- [ ] **Step 5: Build and verify in game**

Build; verify with Task 16's commands once available (`/vrpg vfx empower 1 on` → slot 1 glows gold; `/vrpg vfx event windowopen` targeting yourself → repeating ring pulses under the crosshair). For now confirm clean build and no HUD regressions.

- [ ] **Step 6: Commit**

```bash
git add VRPG/src
git commit -m "Add empowered hotbar slot glow and crosshair window pulse"
```

---

### Task 14: Visual budget and degradation policy

**Files:**
- Create: `VRPG/src/Client/Visuals/VisualBudget.cs`
- Modify: `VRPG/src/Client/Visuals/VisualDirector.cs` (apply scales, record spend)
- Modify: `VRPG/src/Client/Visuals/AuraEmitterSystem.cs` (respect the budget)
- Test: `VRPG/tests/VRPG.Tests/VisualBudgetTests.cs`

**Interfaces:**
- Consumes: `CombatVisualsConfig.DegradationPolicy` / `Intensity`.
- Produces:
  - `VisualPriority { Critical = 0, Own = 1, Others = 2, Cosmetic = 3 }`
  - `VisualBudget(int particlesPerSecond = 900)` with `bool OwnFirst`, `float QuantityScale(VisualPriority priority, long nowMs)`, `void Record(float particleCost, long nowMs)`.
  - `VisualDirector.Budget` (public get) — Task 16's stress test observes degradation through it.
- Degradation curves (load = spend in the current 1 s window / cap): own-first → Cosmetic `1 − 2·load`, Others `1 − 2·max(0, load − 0.5)`, Own `1 − 5·max(0, load − 0.8)`; uniform → all three `1 − load`; every result clamped to [0, 1]; Critical is always exactly 1. P0 renderers (telegraphs, buildup flash, window pulse, buff row) never consult the budget at all — that is how "P0 never degrades" is enforced structurally.

- [ ] **Step 1: Write the failing tests**

`VRPG/tests/VRPG.Tests/VisualBudgetTests.cs`:

```csharp
using VRPG.Client.Visuals;
using Xunit;

namespace VRPG.Tests;

public sealed class VisualBudgetTests
{
    private static VisualBudget Loaded(float load, bool ownFirst = true)
    {
        var budget = new VisualBudget(particlesPerSecond: 1000) { OwnFirst = ownFirst };
        budget.Record(load * 1000f, nowMs: 500);
        return budget;
    }

    [Fact]
    public void NoLoadMeansFullFidelityForEveryone()
    {
        VisualBudget budget = Loaded(0f);
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Cosmetic, 500));
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Own, 500));
    }

    [Fact]
    public void OwnFirstDegradesCosmeticBeforeOthersBeforeOwn()
    {
        VisualBudget budget = Loaded(0.6f);
        Assert.Equal(0f, budget.QuantityScale(VisualPriority.Cosmetic, 500));
        Assert.Equal(0.8f, budget.QuantityScale(VisualPriority.Others, 500), 2);
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Own, 500));

        VisualBudget heavier = Loaded(0.9f);
        Assert.Equal(0.5f, heavier.QuantityScale(VisualPriority.Own, 500), 2);
    }

    [Fact]
    public void CriticalNeverDegrades()
    {
        VisualBudget budget = Loaded(5f);
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Critical, 500));
    }

    [Fact]
    public void UniformPolicyScalesAllNonCriticalEqually()
    {
        VisualBudget budget = Loaded(0.4f, ownFirst: false);
        Assert.Equal(0.6f, budget.QuantityScale(VisualPriority.Cosmetic, 500), 2);
        Assert.Equal(0.6f, budget.QuantityScale(VisualPriority.Others, 500), 2);
        Assert.Equal(0.6f, budget.QuantityScale(VisualPriority.Own, 500), 2);
    }

    [Fact]
    public void WindowRollsOverAndLoadResets()
    {
        var budget = new VisualBudget(1000) { OwnFirst = true };
        budget.Record(900f, nowMs: 100);
        Assert.True(budget.QuantityScale(VisualPriority.Cosmetic, 100) < 0.01f);
        Assert.Equal(1f, budget.QuantityScale(VisualPriority.Cosmetic, 1300));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj` — compile FAIL.

- [ ] **Step 3: Implement the budget**

`VRPG/src/Client/Visuals/VisualBudget.cs`:

```csharp
using System;

namespace VRPG.Client.Visuals;

public enum VisualPriority
{
    Critical = 0,
    Own = 1,
    Others = 2,
    Cosmetic = 3
}

/// <summary>
/// Sliding one-second particle budget. Non-critical priorities degrade in
/// order (cosmetic, then others, then own) under the own-first policy, or all
/// together under the uniform policy. Critical (P0) always returns 1 — and P0
/// renderers do not consult the budget at all.
/// </summary>
public sealed class VisualBudget
{
    private readonly int particlesPerSecond;
    private long windowStartMs;
    private float spent;

    public bool OwnFirst { get; set; } = true;

    public VisualBudget(int particlesPerSecond = 900)
    {
        this.particlesPerSecond = Math.Max(1, particlesPerSecond);
    }

    public void Record(float particleCost, long nowMs)
    {
        RollWindow(nowMs);
        spent += Math.Max(0f, particleCost);
    }

    public float QuantityScale(VisualPriority priority, long nowMs)
    {
        if (priority == VisualPriority.Critical)
        {
            return 1f;
        }

        RollWindow(nowMs);
        float load = spent / particlesPerSecond;
        if (!OwnFirst)
        {
            return Math.Clamp(1f - load, 0f, 1f);
        }

        return priority switch
        {
            VisualPriority.Cosmetic => Math.Clamp(1f - 2f * load, 0f, 1f),
            VisualPriority.Others => Math.Clamp(1f - 2f * Math.Max(0f, load - 0.5f), 0f, 1f),
            _ => Math.Clamp(1f - 5f * Math.Max(0f, load - 0.8f), 0f, 1f)
        };
    }

    private void RollWindow(long nowMs)
    {
        if (nowMs - windowStartMs >= 1000)
        {
            windowStartMs = nowMs;
            spent = 0f;
        }
    }
}
```

- [ ] **Step 4: Integrate into director and auras**

`VisualDirector`:
1. Add `public VisualBudget Budget { get; } = new VisualBudget();`
2. At the top of `HandleEvent`, replace `skillFx.QuantityScale = Config.Intensity;` with:

```csharp
        Budget.OwnFirst = !string.Equals(Config.DegradationPolicy, "uniform", StringComparison.OrdinalIgnoreCase);
        long nowMs = capi.ElapsedMilliseconds;
        VisualPriority priority = packet.SourceEntityId == capi.World.Player?.Entity?.EntityId
            ? VisualPriority.Own
            : VisualPriority.Others;
        skillFx.QuantityScale = Budget.QuantityScale(priority, nowMs) * Config.Intensity;
```

3. In the particle-spawning cases (`Impact`/`Burst`/`Ray`/`Consume`), after dispatching record the estimated spend:

```csharp
                Budget.Record(style.Particles.BurstQuantity * skillFx.QuantityScale, nowMs);
```

(for `Ray`, record `style.Particles.TrailQuantity * 9f * skillFx.QuantityScale`).

`AuraEmitterSystem`: give the constructor a trailing `VisualBudget budget` parameter (the director's instance, passed by `VRPGModSystem`); in `EmitFor`, multiply `quantity` by `budget.QuantityScale(VisualPriority.Others, nowMs)` and call `budget.Record(quantity, nowMs)` after spawning.

- [ ] **Step 5: Run tests and build**

`dotnet test` PASS; `dotnet build` clean.

- [ ] **Step 6: Commit**

```bash
git add VRPG/src VRPG/tests
git commit -m "Enforce particle budgets with configurable own-first degradation"
```

---

### Task 15: Hub Options "Combat Visuals" category

**Files:**
- Modify: `VRPG/src/Client/UI/GuiElementVrpgHub.cs`
- Modify: `VRPG/src/Client/GuiDialogVRPGHub.cs` (thread new ctor params)
- Modify: `VRPG/src/VRPGModSystem.cs` (pass config + persist callback)

**Interfaces:**
- Consumes: `CombatVisualsConfig` (Task 4), existing `DrawOptionRow`, `ClickKind`, `OptionsCategories` patterns in `GuiElementVrpgHub`.
- Produces: `GuiElementVrpgHub` and `GuiDialogVRPGHub` constructors gain trailing `CombatVisualsConfig combatVisuals, Action combatVisualsChanged` parameters. Toggling mutates the shared config object (the director and renderers read it live) and invokes the callback, which persists the client config.

- [ ] **Step 1: Extend the hub element**

In `GuiElementVrpgHub.cs`:

1. `OptionsCategories` becomes `{ "Notifications", "Combat Hotbar", "Combat Visuals" }`.
2. Add fields `private readonly VRPG.Client.Visuals.CombatVisualsConfig combatVisuals;` and `private readonly Action combatVisualsChanged;`, set from the new trailing ctor params.
3. Extend `ClickKind` with `ToggleCombatText, ToggleDamageNumbers, ToggleEventWords, ToggleMergeNumbers, ToggleStatusAuras, ToggleDegradationPolicy, CycleTelegraphOpacity, CycleIntensity`.
4. In `DrawOptions`'s category `switch`, add `case 2: DrawCombatVisualOptions(ctx, pageX, bodyY, pageW, bodyH); break;` and implement, following `DrawNotificationOptions` exactly:

```csharp
    private void DrawCombatVisualOptions(Context ctx, double x, double y, double width, double height)
    {
        DrawText(ctx, "Combat Visuals", x, y + scaled(31.0), scaled(20.0), bold: true, ColorGold(), maxWidth: width);
        DrawText(
            ctx,
            "Client-side presentation settings. Turning something off never hides gameplay-critical telegraphs or timing cues.",
            x, y + scaled(58.0), scaled(12.0), bold: false, ColorMuted(), maxWidth: width);

        double rowY = y + scaled(96.0);
        double step = scaled(84.0);
        DrawOptionRow(ctx, x, rowY, width, "Combat text",
            "Master switch for floating damage numbers and event words.",
            combatVisuals.CombatTextEnabled, ClickKind.ToggleCombatText);
        DrawOptionRow(ctx, x, rowY + step, width, "Damage numbers",
            "Show merged damage and healing numbers over targets.",
            combatVisuals.DamageNumbers, ClickKind.ToggleDamageNumbers);
        DrawOptionRow(ctx, x, rowY + step * 2, width, "Event words",
            "Show BREAK, COUNTER, and similar state words over targets.",
            combatVisuals.EventWords, ClickKind.ToggleEventWords);
        DrawOptionRow(ctx, x, rowY + step * 3, width, "Merge rapid hits",
            "Combine rapid hits on one target into a single growing number.",
            combatVisuals.MergeNumbers, ClickKind.ToggleMergeNumbers);
        DrawOptionRow(ctx, x, rowY + step * 4, width, "Status auras",
            "Loop subtle particles around enemies carrying statuses.",
            combatVisuals.StatusAuras, ClickKind.ToggleStatusAuras);
        DrawOptionRow(ctx, x, rowY + step * 5, width, "Protect my own effects: "
            + (IsOwnFirst() ? "on" : "off (uniform)"),
            "Under heavy load, degrade cosmetic and other players' effects before your own.",
            IsOwnFirst(), ClickKind.ToggleDegradationPolicy);
        DrawOptionRow(ctx, x, rowY + step * 6, width, "Telegraph opacity: "
            + (int)Math.Round(combatVisuals.TelegraphOpacity * 100) + "%",
            "Strength of ground discs and rings. Click to cycle 100 / 50 / 25%.",
            combatVisuals.TelegraphOpacity > 0.3f, ClickKind.CycleTelegraphOpacity);
        DrawOptionRow(ctx, x, rowY + step * 7, width, "Effect intensity: "
            + (int)Math.Round(combatVisuals.Intensity * 100) + "%",
            "Overall particle quantity. Click to cycle 100 / 75 / 50%.",
            combatVisuals.Intensity > 0.6f, ClickKind.CycleIntensity);
    }

    private bool IsOwnFirst()
    {
        return !string.Equals(combatVisuals.DegradationPolicy, "uniform", StringComparison.OrdinalIgnoreCase);
    }
```

If eight rows overflow the page height, reduce `step` or reuse the page's existing scroll mechanism if one exists — match whatever `DrawNotificationOptions` does when content exceeds the pane.

5. In the click-region `switch` (near the `ToggleCooldownNotifications` cases), add:

```csharp
                case ClickKind.ToggleCombatText:
                    combatVisuals.CombatTextEnabled = !combatVisuals.CombatTextEnabled;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleDamageNumbers:
                    combatVisuals.DamageNumbers = !combatVisuals.DamageNumbers;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleEventWords:
                    combatVisuals.EventWords = !combatVisuals.EventWords;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleMergeNumbers:
                    combatVisuals.MergeNumbers = !combatVisuals.MergeNumbers;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleStatusAuras:
                    combatVisuals.StatusAuras = !combatVisuals.StatusAuras;
                    combatVisualsChanged();
                    break;
                case ClickKind.ToggleDegradationPolicy:
                    combatVisuals.DegradationPolicy = IsOwnFirst() ? "uniform" : "own-first";
                    combatVisualsChanged();
                    break;
                case ClickKind.CycleTelegraphOpacity:
                    combatVisuals.TelegraphOpacity = combatVisuals.TelegraphOpacity > 0.75f ? 0.5f
                        : combatVisuals.TelegraphOpacity > 0.35f ? 0.25f : 1f;
                    combatVisualsChanged();
                    break;
                case ClickKind.CycleIntensity:
                    combatVisuals.Intensity = combatVisuals.Intensity > 0.9f ? 0.75f
                        : combatVisuals.Intensity > 0.6f ? 0.5f : 1f;
                    combatVisualsChanged();
                    break;
```

After each case the page must redraw the same way the existing toggles do (they fall through to the shared redraw at the end of the click handler — keep that behavior).

- [ ] **Step 2: Thread the parameters**

`GuiDialogVRPGHub` constructor gains trailing `VRPG.Client.Visuals.CombatVisualsConfig combatVisuals, Action combatVisualsChanged` and passes them into `GuiElementVrpgHub`'s construction. In `VRPGModSystem.OpenHubDialog`, pass `config.Modules.Rpg.CombatVisuals` and `() => PersistClientConfig(api)`.

The director's combat-text merge setting must follow the config live: in `VisualDirector.HandleEvent`, before the switch add `textSettings.MergeNumbers = Config.MergeNumbers;` where `textSettings` is the `CombatTextSettings` instance the director passed to `CombatText` (store it in a field when constructing the model).

- [ ] **Step 3: Build and verify in game**

Build; in game open Hub → Options → Combat Visuals. Toggle Damage numbers off, cast into an enemy: no numbers, but bursts and telegraphs remain. Toggle Status auras off: auras stop within a tick. Reconnect: settings persisted (they live in the client's `VRPG.json` mod config).

- [ ] **Step 4: Commit**

```bash
git add VRPG/src
git commit -m "Add Combat Visuals options category with live client settings"
```

---

### Task 16: `/vrpg vfx` admin commands and Gate A acceptance

**Files:**
- Modify: `VRPG/src/Modules/Rpg/RpgModule.cs` (`ApplyStatus` helper)
- Modify: `VRPG/src/VRPGModSystem.cs` (`vfx` subcommands under the existing `vrpg` command)

**Interfaces:**
- Consumes: `RpgModule.VisualBroadcaster` (Task 5), `RpgModule.GroundAreas` (Task 6), `RpgModule.SetSkillEmpowered` (Task 13), `StatusEffectTracker.Apply` (Task 2).
- Produces: `RpgModule.ApplyStatus(long entityId, string code, int stacks, float magnitude, float seconds)` → bool; admin subcommands `vfx event`, `vfx status`, `vfx area`, `vfx empower`, `vfx stress`.

- [ ] **Step 1: Add the RpgModule helper**

```csharp
    public bool ApplyStatus(long entityId, string code, int stacks, float magnitude, float seconds)
    {
        return statusEffects.Apply(entityId, code, 0, seconds, stacks, magnitude);
    }
```

- [ ] **Step 2: Register the commands**

In `VRPGModSystem.RegisterCommands`, add a `vfx` subcommand block to the `vrpg` command (privilege `controlserver`, `RequiresPlayer`):

```csharp
            .BeginSubCommand("vfx")
                .WithDescription("Fire synthetic combat visuals for testing.")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("event")
                    .WithDescription("Fire a combat visual event at the aimed point (kinds: impact, burst, ray, damage, heal, shield, break, counter, consume, windowopen, mark).")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.Word("kind"), api.ChatCommands.Parsers.OptionalWord("style"))
                    .HandleWith(args => HandleVfxEvent(api, args))
                .EndSubCommand()
                .BeginSubCommand("status")
                    .WithDescription("Apply a status effect to the looked-at entity (or yourself).")
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("code"),
                        api.ChatCommands.Parsers.OptionalInt("stacks", 1),
                        api.ChatCommands.Parsers.OptionalFloat("magnitude", 0f),
                        api.ChatCommands.Parsers.OptionalFloat("seconds", 8f))
                    .HandleWith(args => HandleVfxStatus(args))
                .EndSubCommand()
                .BeginSubCommand("area")
                    .WithDescription("Place a ground area at the aimed block (states: armed, triggered, active, expiring).")
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("style"),
                        api.ChatCommands.Parsers.OptionalWord("state"),
                        api.ChatCommands.Parsers.OptionalFloat("radius", 3f),
                        api.ChatCommands.Parsers.OptionalFloat("seconds", 20f))
                    .HandleWith(args => HandleVfxArea(args))
                .EndSubCommand()
                .BeginSubCommand("empower")
                    .WithDescription("Toggle the empowered glow on a loadout slot.")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.Int("slot"), api.ChatCommands.Parsers.Word("onoff"))
                    .HandleWith(args => HandleVfxEmpower(args))
                .EndSubCommand()
                .BeginSubCommand("stress")
                    .WithDescription("Spray synthetic damage events around you to verify budgets.")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.OptionalInt("eventsPerSecond", 60), api.ChatCommands.Parsers.OptionalInt("seconds", 10))
                    .HandleWith(args => HandleVfxStress(api, args))
                .EndSubCommand()
            .EndSubCommand()
```

with handlers (private methods in `VRPGModSystem`):

```csharp
    private TextCommandResult HandleVfxEvent(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule?.VisualBroadcaster == null)
        {
            return TextCommandResult.Error("The VRPG visual runtime is not available.");
        }

        if (!Enum.TryParse((string)args[0], true, out CombatVisualKind kind))
        {
            return TextCommandResult.Error("Unknown visual kind: " + args[0]);
        }

        Vintagestory.API.MathTools.Vec3d position =
            player.CurrentBlockSelection?.FullPosition
            ?? player.Entity.Pos.XYZ.AheadCopy(4, 0, player.Entity.Pos.Yaw);
        long targetId = player.CurrentEntitySelection?.Entity?.EntityId ?? player.Entity.EntityId;

        rpgModule.VisualBroadcaster.Send(new CombatVisualEventPacket
        {
            Kind = (byte)kind,
            StyleCode = (args.Parsers[1].IsMissing ? "" : (string)args[1]) ?? "",
            SourceEntityId = player.Entity.EntityId,
            TargetEntityId = targetId,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Radius = 2.5f,
            Magnitude = 17f,
            FallbackColorRgba = unchecked((int)0xccf2ede4)
        });
        return TextCommandResult.Success("Fired " + kind + ".");
    }

    private TextCommandResult HandleVfxStatus(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule == null)
        {
            return TextCommandResult.Error("The VRPG rpg runtime is not available.");
        }

        long entityId = player.CurrentEntitySelection?.Entity?.EntityId ?? player.Entity.EntityId;
        bool applied = rpgModule.ApplyStatus(entityId, (string)args[0], (int)args[1], (float)args[2], (float)args[3]);
        return applied
            ? TextCommandResult.Success("Applied " + args[0] + " to entity " + entityId + ".")
            : TextCommandResult.Error("Unknown status effect: " + args[0]);
    }

    private TextCommandResult HandleVfxArea(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule?.GroundAreas == null)
        {
            return TextCommandResult.Error("The VRPG area runtime is not available.");
        }

        Vintagestory.API.MathTools.Vec3d position =
            player.CurrentBlockSelection?.FullPosition ?? player.Entity.Pos.XYZ;
        string stateWord = args.Parsers[1].IsMissing ? "active" : (string)args[1];
        if (!Enum.TryParse(stateWord, true, out GroundAreaState state))
        {
            return TextCommandResult.Error("Unknown area state: " + stateWord);
        }

        long id = rpgModule.GroundAreas.Place(
            player.PlayerUID, (string)args[0], GroundAreaShape.Disc, position,
            (float)args[2], state, (float)args[3]);
        return TextCommandResult.Success("Placed area " + id + " (" + state + ").");
    }

    private TextCommandResult HandleVfxEmpower(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule == null)
        {
            return TextCommandResult.Error("The VRPG rpg runtime is not available.");
        }

        int slot = (int)args[0] - 1;
        SkillLoadoutPacket loadout = rpgModule.BuildSkillLoadout(player);
        if (slot < 0 || slot >= loadout.Slots.Length || string.IsNullOrEmpty(loadout.Slots[slot].Code))
        {
            return TextCommandResult.Error("Slot " + args[0] + " has no equipped skill.");
        }

        bool on = string.Equals((string)args[1], "on", StringComparison.OrdinalIgnoreCase);
        rpgModule.SetSkillEmpowered(player, loadout.Slots[slot].Code, on);
        return TextCommandResult.Success((on ? "Empowered " : "Cleared ") + loadout.Slots[slot].Name + ".");
    }

    private TextCommandResult HandleVfxStress(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule?.VisualBroadcaster == null)
        {
            return TextCommandResult.Error("The VRPG visual runtime is not available.");
        }

        int eventsPerSecond = Math.Clamp((int)args[0], 1, 500);
        int seconds = Math.Clamp((int)args[1], 1, 60);
        var random = new Random();
        long endAtMs = api.World.ElapsedMilliseconds + seconds * 1000L;
        long listenerId = 0;
        listenerId = api.Event.RegisterGameTickListener(_ =>
        {
            if (api.World.ElapsedMilliseconds >= endAtMs)
            {
                api.Event.UnregisterGameTickListener(listenerId);
                return;
            }

            var pos = player.Entity.Pos.XYZ;
            for (int i = 0; i < Math.Max(1, eventsPerSecond / 4); i++)
            {
                rpgModule!.VisualBroadcaster!.Send(new CombatVisualEventPacket
                {
                    Kind = (byte)(random.NextDouble() < 0.7 ? CombatVisualKind.Damage : CombatVisualKind.Burst),
                    StyleCode = "",
                    FallbackColorRgba = unchecked((int)0xccf2ede4),
                    DamageType = (byte)random.Next(0, 6),
                    Magnitude = random.Next(1, 40),
                    X = pos.X + random.NextDouble() * 12 - 6,
                    Y = pos.Y + 1,
                    Z = pos.Z + random.NextDouble() * 12 - 6,
                    Radius = 1.5f
                });
            }
        }, 250);
        return TextCommandResult.Success("Stressing " + eventsPerSecond + " events/s for " + seconds + "s.");
    }
```

Adjust parser accessor details (`args.Parsers[n].IsMissing`, argument indexing) to match how the existing commands in this file read optional arguments — follow `RpgCommandSystem` for a working optional-parser example.

- [ ] **Step 3: Build and run the full in-game acceptance pass**

`dotnet test` PASS, `dotnet build` clean, then in a test world:

1. `/vrpg vfx event burst vrpg:cinder_orb` — orange burst at the aimed point.
2. `/vrpg vfx event break` at a creature — "BREAK" word.
3. `/vrpg vfx status vrpg:corrosion 5 0 12` at a creature — icon + stacks + rust aura; watch the duration wipe empty.
4. `/vrpg vfx status vrpg:stagger 1 80 8` — buildup bar at 80%; repeat with 120 — threshold flash.
5. `/vrpg vfx status vrpg:burn 1 0 6` while aiming at nothing — own buff row shows the burn icon counting down.
6. `/vrpg vfx area vrpg:cinder_orb armed 3 30` — faint disc; `/vrpg vfx area vrpg:cinder_orb active 4 10` — solid disc with rim, pulsing in its last 1.5 s.
7. `/vrpg vfx empower 1 on` — slot 1 glows; `off` clears it.
8. `/vrpg vfx event windowopen` — crosshair pulse (the command targets you when no entity is selected).
9. `/vrpg vfx stress 200 10` — numbers merge instead of flooding, screen cap holds (~20 entries), FPS stays playable; switch degradation policy to uniform in Options and observe the difference.
10. Cast real skills during the stress — your own bursts stay visible while synthetic (source-less) events degrade first.
11. Two-player dedicated-server smoke test: both clients see each other's casts, statuses, and areas; a client joining mid-fight sees existing areas and statuses.

Record any failures as new checklist items in `docs/design/testable-version-todo.md` under Gate A ("hit feedback without chat or particle spam").

- [ ] **Step 4: Commit**

```bash
git add VRPG/src VRPG/docs
git commit -m "Add vfx admin commands and stress harness for combat visual acceptance"
```

---

## Plan Self-Review Notes

- **Spec coverage:** channels 1–3 → Tasks 2, 3, 6; migration debt → Task 5; five renderers → Tasks 4/5 (skill FX), 7 (telegraphs), 10+11 (status overlay + auras), 8+9 (combat text), 12+13 (player-state HUD); budgets/degradation → Task 14; Hub Options → Task 15; error handling (fallback styles Task 4, silent entity drops Tasks 9/11, local expiry Tasks 2/7); testing section → pure-class tests throughout + Task 16 commands and acceptance. The spec's "snapshot re-sent on approach" is simplified to broadcast-to-all + join snapshot (areas are few); revisit only if area counts grow.
- **Deviation from spec, intentional:** the spec says durations sync as "absolute end time"; server and client `ElapsedMilliseconds` are different clock domains, so the sync uses remaining-time-at-write + revision counter, which achieves the same goal (no per-tick sync). Noted in `StatusSync` doc comment.
- **Engine-API risk:** `GroundTelegraphRenderer` (shader/mesh members) and command parser accessors are the two places most likely to need name adjustments against the installed game API; behavior is fully specified.

## Execution Handoff

Plan complete. Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task with review between tasks (superpowers:subagent-driven-development).
2. **Inline Execution** — execute tasks in-session with checkpoints (superpowers:executing-plans).



