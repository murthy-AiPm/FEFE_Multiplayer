# Enemy Masses — Asset Notes

*Working notes on the candidate crowd-rendering asset for the Day-7 siege horde and any other large-scale enemy crowd in 7DTD.*
*Created: 2026-05-04. Asset not yet purchased; integration deferred until climax work begins.*

---

## What it is

**Enemy Masses Standard** — Unity Asset Store package for GPU-instanced massive crowds with combat, formation, and RTS-command behaviors. Vendor: Arawn Software Publishing.

- Asset Store: `Enemy Masses Standard - Massive Crowds, RTS, Formations for GPU Inst[ancing]` (search the Asset Store; URL not pinned here in case the listing moves).
- Documentation: <https://arawn-software-publishing.gitbook.io/enemy-masses>
- Status: actively developed (v1.2.0 / v2.0.4 releases as of January 2026).

Used in 7DTD to render the **crowd-tier orcs** for the Day-7 siege horde — the bulk of the attacking army that needs to look like 300–500+ orcs without each one being a `NetworkObject`. Tactical orcs (`OrcAI.md`) remain individual `NetworkObject`s for week encounters and Day-7 lieutenants/giants.

## Required dependencies

The asset has two paid prerequisites. Budget both.

1. **GPU Instancer Pro.** Required. The rendering backbone — Enemy Masses uses GPUI Pro for the actual instanced draw.
2. **GPU Instancer Pro — Crowd Animator Addon.** Required. The animation backbone — bakes Mecanim clips into a GPU-friendly format that the instanced renderer can play. Existing humanoid clips need to be run through this pipeline before they're usable on crowd agents.

**Game Creator 2** is *optional* — it's the documented "happy path" for visual scripting / non-coder workflows. The 7DTD project does not use GC2; we work with the asset's API directly. This is the less-documented path but supported.

## What the asset gives us

Confirmed from the doc export (`llms-full.txt`):

- **Render scale: 5,000+ animated units on screen.** Well above the 300–500 design target in `7DaysTillDawn_Design.md`. Plenty of headroom for the climax stretch tier if other constraints (client GPU, server CPU) hold.
- **Per-instance damage and death.** `ApplyDamageToAgent()`, `OnAgentDamaged` event, per-`CrowdAgent` `currentHealth`. Dead agents prune from the formation, play death animation, sink into ground, auto-despawn on configurable delay. Exactly the "dragon firebreath kills 30 specific instances" pattern.
- **Per-instance behavior override.** `SetMoveIntent(Vector3)`, `SetFollowIntent(Transform)`, `SetCombatTarget(int)`, `TeleportAgentToDestination`, agent-level skill queueing. Individual crowd agents can deviate from formation (engage a specific target, climb a wall, break off).
- **NavMesh is primary pathfinder.** Unity `NavMeshAgent` + formation logic + stuck-recovery (`SamplePosition` re-sync). FlowField fallback when NavMesh unavailable. `Flight3DNavigationAgent` for aerial agents (volumetric formations, altitude steering, boids-style avoidance).
- **Mecanim-compatible** with GPU baking via Crowd Animator. Per-crowd clip overrides + Mecanim parameter overrides — supports visual variety (axe orcs vs club orcs vs spear orcs as different crowds with shared rig).
- **Hit-react / impulse animations** via `PlayImpulseAnimation()`, `StartBlend()` (GPUI Crowd Utility). Supports the existing dragon firebreath / sword-impact reaction patterns.
- **Formation API.** `SetFormationType()`, `SetFormationSpacing()`, `CycleFormationType()`. Wave shapes can drive formation choice per wave.
- **Wall climbing IS supported** — per the author's YouTube demos (not in the gitbook docs at the time of this read). Validate the API path during eval; expect either NavMesh OffMeshLinks integration or a per-agent climb-skill primitive.

## What we have to build (BYO)

The asset's network model is **interface-driven, not transport-bundled.** It exposes:

- `INetworkSkillAuthority` — for skill execution flow (request → validate → confirm/reject).
- `INetworkDamageAuthority` — for damage routing.
- `INetworkCommandAuthority` — for RTS commands.
- `LocalNetworkAuthority` as a single-player fallback.
- Periodic state sync hooks: `OnResourceStateSync`, `OnCooldownStateSync`, `OnAgentStateSync`.

**No NGO or FishNet integration ships with the asset.** We implement those interfaces against NGO ourselves. Conceptually:

- `RequestSkillExecution(...)` on the client → server `ServerRpc` → asset's `ValidateSkillRequest()` → confirmation back via `ClientRpc`.
- Damage routes through `INetworkDamageAuthority` → server-authoritative `ApplyDamageToAgent` → client-side prune/anim via the existing event pipeline.
- RTS commands (move/attack/regroup) via `INetworkCommandAuthority` → `ServerRpc → ClientRpc` fan-out matching the `BallistaOperator` / `DragonCombatController` patterns already in the codebase.

This is **a real workstream, not free**. Estimated 1–2 weeks of focused work for a clean bridge implementation, more if validation is finicky. Architectural shape is correct — we're filling in a transport layer the asset deliberately leaves open.

### Animation pipeline

Existing humanoid Mecanim clips need to flow through the Crowd Animator Addon to be usable on crowd agents. Concrete steps (validate during eval):

1. Author / collect orc humanoid clips (idle, walk, run, attack styles, hit react, death).
2. Bake via Crowd Animator Addon → GPU-friendly format.
3. Wire crowd-level clip overrides for visual variants.
4. Hit-react via `PlayImpulseAnimation` triggered from damage events.

Tactical orc (`OrcAI.cs`) clips remain as standard Mecanim — they don't need GPU baking since each tactical orc is a regular `Animator`-driven `NetworkObject`.

## Integration plan with 7DTD

Where the asset sits in the orc architecture:

```
Tactical orc (OrcAI.cs)              Crowd orc (Enemy Masses CrowdAgent)
    └── individual NetworkObject         └── instanced via GPUI Pro
    └── HFSM + Utility AI                └── asset's formation + skill system
    └── Animancer/Mecanim hybrid         └── Crowd Animator addon (GPU-baked)
    └── full DamageReceiver              └── ApplyDamageToAgent / OnAgentDamaged
    └── NavMesh path planning            └── NavMesh path planning (shared)
    Used for: week encounters,           Used for: Day-7 horde bulk,
              Day-7 lieutenants,                   wave-shaped attacks
              giants
```

Both share NavMesh — the same baked NavMesh serves tactical agents and crowd agents. They coexist on the field at the climax (lieutenants embedded in crowd squads, giants standing apart, defender NPCs and dragon engaging both tiers).

The static `_attackerCounts` crowd-control dictionary in BearAI/OrcAI does NOT extend to crowd agents — crowd-vs-tactical interactions use the asset's own combat target system on one side and our `DamageReceiver` on the other. Bridge code for cross-tier damage:

- Tactical orc swings → standard `DamageReceiver` (already works).
- Crowd orc attacks defender NPC → asset's damage system, defender NPC's damage receiver wires the asset's event.
- Player swings against crowd → either asset's per-unit hit detection picks it up, or we route through `ApplyDamageToAgent` from our `HitboxController`.
- Dragon firebreath kills 30 crowd agents → one synced damage volume, asset's `OnAgentDamaged` fires per agent on each client locally.

## To validate during asset eval

Numbered prototype targets — first day with the asset answers most of these.

1. **Frame rate at concurrent count on target hardware.** Spawn 500 instances, march at the castle, measure FPS on the chosen hardware floor (RTX 3060-class? lower?). Determines whether 300–500 / 1,000+ tier is realistic.
2. **Wall climbing API path.** Per the author's YouTube demos. Confirm the API surface and whether it integrates with NavMesh OffMeshLinks or is its own primitive. Determines whether the Day-7 wall-scale design holds as written or needs the "wait for breach" fallback.
3. **Burst / Jobs usage.** Not documented. Affects server CPU ceiling for the simulation side. Profile during the eval prototype; if managed C# only, server-side simulation cost may set the practical concurrent ceiling lower than rendering does.
4. **Formation + NavMesh composition.** Both tactical and crowd orcs use NavMesh; how does the asset's formation logic compose with multiple squads sharing NavMesh paths near a chokepoint? Probably fine; needs a chokepoint test (gate/breach).
5. **Corpse persistence cost.** Corpses sink and auto-despawn, but a 500-kill wave at climax could stack hundreds of corpses simultaneously. Profile a worst-case kill burst (dragon firebreath sweeps 50 instances, then another 50 a second later, etc.).
6. **Crowd Animator pipeline cost.** How long does it take to bake a humanoid clip via the addon? Affects animation iteration speed.
7. **Networking bridge prototype.** Implement a minimal `INetworkCommandAuthority` against NGO and confirm a "move squad" command syncs from one client to another at expected latency.
8. **Existing humanoid clip compatibility.** Run an existing FEFE humanoid clip through Crowd Animator. Verify it bakes cleanly; verify visual fidelity matches the standard Mecanim playback.
9. **Mixed-tier interaction.** Spawn 10 tactical orcs (regular `NetworkObject` + Mecanim) and 100 crowd orcs in the same scene. Confirm they coexist, share NavMesh, and damage routes both directions cleanly.
10. **GC2 dependency check.** Confirm we can use the asset without Game Creator 2 — most of the API surface is documented for direct use, but worth a test that no critical feature is GC2-gated.

## Procurement

- Asset is paid (current Asset Store pricing not pinned here — check at purchase time).
- GPU Instancer Pro is paid.
- Crowd Animator Addon may be bundled with GPUI Pro or a separate purchase — verify before checkout.
- Buy timing: when climax / Day-7 siege work begins. No value in owning the asset before tactical orc v1–v2 ships.

## API quick reference (extracted from doc export)

Useful for next session when integration starts.

**Agent control**
- `SetMoveIntent(Vector3)`, `SetFollowIntent(Transform)`, `SetCombatTarget(int)`
- `TeleportAgentToDestination(Vector3)`, `SetAgentFlying()`, `SetAgentLanded()`, `ForceAgentLanding()`
- `ForceAnimationRefresh()`, cast-state interruption per agent

**Skill execution**
- `RequestSkillExecution(caster, skillId, target)` — network-aware entry
- `ExecuteSkill()`, `CompleteCast()`, `InterruptCast()`
- Custom `SkillEffect` subclasses override `Execute(context)`; access `context.Caster`, `context.DamageableTarget`, `context.Targets`

**Damage / state**
- `ApplyDamageToAgent()`, `ApplyStatusEffect()`
- `OnAgentDamaged` event: `Action<CrowdAgent, float, int>`
- `OnAgentDied`

**Formation**
- `SetFormationType()`, `SetFormationSpacing()`, `CycleFormationType()`

**Animation**
- `PlayImpulseAnimation()` for hit react / knockback
- Per-crowd `Animator clip overrides`, Mecanim parameter overrides
- Three flight states: `FlightTakeOff`, `Flying`, `FlightLanding`

**Selection / RTS**
- `OnSelectionChanged` event on the RTS controller
- Move/Attack orders supported via `INetworkCommandAuthority`

**Network authority hooks (BYO transport)**
- `INetworkSkillAuthority`, `INetworkDamageAuthority`, `INetworkCommandAuthority`
- `LocalNetworkAuthority` (single-player fallback)
- `OnResourceStateSync`, `OnCooldownStateSync`, `OnAgentStateSync` — periodic sync events

## Cross-references

- `Design/OrcAI.md` — tactical orc AI design; crowd orc tier described in "Two-tier orc AI".
- `Design/7DaysTillDawn_Design.md` — Day-7 Siege scope (wave shape, concurrent counts, behaviors).
- `Design/FEFE_NPC_Architecture.md` — squad-replication / damage-volume / interest-management patterns this builds on.
- `Design/ARCHITECTURE.md` § 20 (NPC AI) — current NPC AI in the codebase (BearAI, planned OrcAI extension).
