# Orc AI — Design (work in progress)

*Created: 2026-05-03. Last updated: 2026-05-08.*

---

## Answered Framing Questions (2026-05-04)

1. **Typical week encounter.** Camps of 6–8 orcs are the common size. Design ceiling is a **20-orc squad fighting together** (numbers may flex up or down). The framing scenario is the player leading ~20 men against a 20-orc squad to save a village.

   **Updated scale (2026-05-04):** game design anticipates **4–5 players, each fighting ~20 orcs concurrently in different locations**, while each player commands ~10–20 NPCs. The relevant network metric is **entities visible per client**, not total entities in the world — interest management filters cross-zone traffic.

   **Typical week case (the common case):** 5 players spread across 5 zones, each zone has 1 player + 10–20 commanded NPCs + 20 orcs + ~5–10 civilians = **~40–50 entities visible per client**. NGO comfortable, no migration concern.

   **Climax case (Day-7 siege / convergence event):** all 5 players + dragon at one location, interest management collapses, attacking horde adds 100+ orcs. This is the only meaningful stress case for the network — and it has its own AI system (see "Two-tier orc AI" below).

   **Stress test plan (revised):**
   - Spread test: 5 clients × 5 non-overlapping zones × ~50 entities each. Should be trivially fine.
   - Convergence test: 5 clients in one zone at climax entity count. The only result that gates the NGO/FishNet decision.

   Optimizations in priority order:
   1. **Interest management** (NGO `NetworkObject.CheckObjectVisibility`) — typical-case savior, biggest win.
   2. **Custom anim sync** (byte-encoded, not NetworkAnimator) — climax-case relief.
   3. **Squad-level replication** — climax-case relief; orc v3 work.
   4. **Tick-rate scaling** — marginal, last priority.

   FishNet migration deferred. Decided at the convergence-test gate, not before.

### Two-tier orc AI (added 2026-05-04)

Climax (~100–200 orcs) and week encounters (~20 orcs) are different problems with different solutions. **Two distinct orc AI systems with a clean boundary:**

| System | Used for | AI style | Networked? |
|---|---|---|---|
| **Tactical orc** | Week encounters, village raids, patrols, lieutenants | HFSM + Utility AI (this doc's design) | Yes — individual NetworkObjects |
| **Crowd orc** | Day-7 siege horde, large-scale convergence battles | GPU-instanced crowd asset (formation/wave system) | No — wave-state synced; instances local |

Candidate crowd asset (referenced for later evaluation): `Enemy Masses Standard` (Unity Asset Store, GPU-instanced massive crowds + RTS formations). See `Design/EnemyMasses_Asset.md` for asset notes — confirmed feature set, BYO networking workstream, eval prototype targets, paid-dependency budget. Final selection deferred to climax work.

Networking integration note: the asset's network model is **interface-driven, not transport-bundled**. We implement `INetworkSkillAuthority` / `INetworkDamageAuthority` / `INetworkCommandAuthority` against NGO ourselves (~1–2 weeks of focused bridge work).

Why this changes climax networking: with a crowd asset, climax stops being "100+ networked orcs" and becomes "~5–10 squad/wave entities + per-orc local rendering." Damage routes through localized events (RPC marks instances as dead; each client drops matching instance locally — same model as the cosmetic-civilian fire-death pattern in `FEFE_NPC_Architecture.md`). **Climax networked entity count drops from ~200 to ~30–50.** NGO comfortably holds.

Boundary questions to settle when climax work begins (not v1):
- [ ] Promotion / demotion between tiers — do tactical lieutenants live inside crowd squads, or are they separate?
- [ ] Damage receiver split — crowd kills are cheap (instant local), tactical kills are full DamageReceiver pipeline.
- [ ] Civilian interaction — crowd orcs likely don't interact with civilians (week-system concern only).
- [ ] Visual variation in crowd asset — runtime loadout / weapon swaps for crowd members.

2. **Threat shape of one orc.** *(Not yet answered. Implied by "save a village"-scale combat: a real fight, not a swat-aside nuisance — but pinning down whether 1v1 is decisive or only-dangerous-in-numbers will affect tuning, not architecture. Defer until single-orc combat is in playtest.)*

3. **AI tier.** *(Not yet answered. v1 starts with aggressive-rush — see "Phase 1" below — but full design will likely mix set-piece staging for camps with aggressive-rush in melee.)*

4. **What orcs do during the week.** **Spawn-on-arrival.** No off-screen abstract simulation — camps exist as data records (location, member count, alive/dead state) and instantiate when a player enters their zone. Saves us from building the active-zone abstract-tick system in `FEFE_NPC_Architecture.md` (that doc was scoped to the prior FEFE direction with civilian sims). 7DTD does not need it.

5. **Existing code.** **`CharacterScripts/Scripts/Bear/BearAI.cs` is a complete, shipping NPC AI** with NavMesh path planning, root-motion locomotion, server-authoritative FSM, crowd control, separation steering, and Mecanim/NetworkAnimator sync. The orc starts as a clone of BearAI retargeted to a humanoid rig + weapon hitbox. Architecture map missed this — `BearAI` and `EnemySurroundCoordinator` (now stubbed) live under `CharacterScripts/Scripts/Bear/`.

---

## Phase 1: Single Orc with Humanoid Movement and Combat

Goal: get one orc walking, chasing, and swinging a weapon at the player. No squad logic, no formations, no flanking. Proof of the AI loop before scaling to 20.

### Architecture — clone BearAI, retarget to humanoid orc

`CharacterScripts/Scripts/Bear/BearAI.cs` is a working, shipping NPC AI that already solves nearly every problem we'd face. It:

- Is server-authoritative (`NetworkBehaviour` + `if (!IsServer) return`) — correct NPC ownership.
- Uses **Mecanim + NetworkAnimator** for animation sync — matches the animation system decision.
- Splits **NavMeshAgent (path planning) from root motion (locomotion)**: `agent.updatePosition = false; agent.updateRotation = false;` and root motion drives position via `OnAnimatorMove`. This is the exact pattern designed for multi-agent scale.
- Has a 6-state FSM: Idle → Wander → Chase → Attack → Return → Dead. More complete than a 3-state v1 plan.
- Has **crowd control via static attacker count** (`_attackerCounts` dictionary, `maxAttackersPerTarget = 4`, `IsCrowded()` gate, `FindLessContestedTarget()` retarget). This is exactly what 20-orc-squad-vs-player needs.
- Has **separation steering** (`OverlapSphereNonAlloc` neighbour push) plus randomised `agent.avoidancePriority` for RVO — designed for many agents.
- Has **leash behavior** — returns home if pulled past `leashRadius`. Maps to camp-defending orcs.
- Throttles detection at `_detectionInterval = 0.25s` — answers the AI tick-rate question.
- Manually applies gravity via downward raycast in `OnAnimatorMove` (no rigidbody / CharacterController to fight).
- Wires VitalManager / DamageReceiver / HitboxController / AnimalSoundPlayer.

**Plan: clone BearAI.cs → OrcAI.cs as a near-copy.** Subclassing is tempting but the static `_attackerCounts` dictionary is process-wide, so crowd-control still works correctly across mixed Bear/Orc enemies on the same target whether we fork or subclass. Forking lets each AI evolve independently without coupling.

### What changes for an orc

| Bear | Orc |
|---|---|
| Quadruped rig, paw + bite hitboxes | Humanoid rig, weapon hitbox |
| `BearAnimations.controller` | `OrcAnimations.controller` (humanoid clips) |
| `biteHitbox` field | `weaponHitbox` field (rename for clarity) |
| Wander between idle bursts | Idle at camp / patrol — see "Squad scaling" below |
| Solo brawler | v1 still solo; v2 layers squad coordinator |

Animator parameters stay identical (`Speed`, `Attack`, `Dead`) so the script changes are minimal — mostly inspector field renames.

### Networking ownership

Same as BearAI — server-spawned NetworkObject, server-owned, AI logic gated on `IsServer`. NetworkAnimator handles animation sync. Don't replicate the player-humanoid pattern (Owner = client); for NPCs, "Owner" is the server.

### Weapon for v1

Inspector-wire a `HitboxController` on the orc's weapon (sword child). Set animation events on the attack clip to call `EnableHitbox()` / `DisableHitbox()`. BearAI uses an `Invoke(nameof(EnableBiteHitbox), hitboxEnableDelay)` pattern — works either way; animation events are tighter to the visual swing.

Defer:
- Orc-specific weapons (axe, club) — adds `WeaponData` assets, no new systems.
- Ranged orcs (bow) — out of scope for v1.

### Open v1 decisions (small, can defer)

- [ ] **Use `applyRootMotion` per-clip via SMB, or globally via the animator?** BearAI has root motion always-on; if some orc clips need code-driven movement (e.g. a parry recovery), gate per-clip with a StateMachineBehaviour that flips `applyRootMotion` on enter/exit.
- [ ] **Detection LoS?** BearAI uses pure radius (`OverlapSphereNonAlloc`). For 20 orcs in dense terrain (village walls), pure radius means orcs detect through walls. Adding one raycast per detected candidate is cheap at this count.
- [ ] **`useRootMotion` inspector toggle requested earlier:** with BearAI as the base, the toggle is a one-line `[SerializeField] bool useRootMotion = true;` checked at the top of `OnAnimatorMove` — falls back to NavMeshAgent's own `updatePosition`-driven movement when off. Useful for testing animation problems without the root-motion path masking them.

### Pathfinding — NavMesh, decided 2026-05-04

Decision: use Unity NavMesh. Designed for scale — 40 agents (20 orcs + 20 men) is well within NavMesh's ceiling, and authoring obstacle avoidance from scratch via raw steering would scale worse, not better. Flow fields are the only alternative that scales further but are massive overhead for this agent count.

**Architectural caveat — split path planning from locomotion.** `CharacterController` (used by `HumanoidController` for movement) and `NavMeshAgent` will fight each other if both write the transform. Pattern:

- `agent.updatePosition = false`, `agent.updateRotation = false` — agent plans only.
- AI driver reads `agent.desiredVelocity`, projects to the orc's local space, writes the result into `InputSnapshot.move`.
- AI driver faces the orc toward `agent.steeringTarget` (or directly toward player when in attack range).
- `CharacterController.Move()` in the existing humanoid pipeline does the actual locomotion. Animation, hitboxes, vitals all stay consistent with the player.

This keeps NavMesh as a pure pathfinder — same role it serves on most production shooters/RPGs — and lets us inherit every per-frame fix the player code already has (slope handling, ground-check, falling logic).

### Root motion toggle — `[SerializeField] bool useRootMotion`

Inspector toggle on the orc controller. When on, animation drives position (good for committed attack swings, lunge hits, recoveries). When off, code drives position via the NavMesh-fed input snapshot (good for chase locomotion, position-keeping).

Interaction with NavMesh: while a root-motion clip is playing the AI ignores `agent.desiredVelocity` (the clip wins). The agent's *path* is still planned in the background, so when the clip ends, chase resumes from wherever the orc landed without recomputing. Standard pattern — the dragon already does something similar with `DragonHitRootMotion` SMB suspending alignment during hit clips.

Likely use: root motion ON for attack/hit/death clips, OFF for locomotion. The toggle is per-orc-controller (so we can tune by archetype later) but `applyRootMotion` on the Animator may need per-clip control via SMB if mixed within one AnimatorController — defer that decision until clips are imported.

### Open v1 questions

- [x] Detection direction: use radius + line-of-sight raycast. Pure radius detects through walls and is too blunt for patrol/camp play. LoS should be implemented as a basic server-side visibility gate with Inspector-tweakable eye height, target height, obstacle mask, and detection tick interval.
- [ ] Tick rate: AI logic at every frame or throttled (10–20 Hz)? Per `FEFE_NPC_Architecture.md` recommendation, throttle distant agents. v1 single orc can run every frame; design the seam now.
- [ ] Animation set: do we use the same `HumanoidAnimationSet` and rule clips as the player, or author orc-specific clips? Reusing works — orc model retargets to the humanoid rig — but feel may suffer.
- [ ] NavMesh baking: static-mesh bake at build, or `NavMeshSurface` runtime bake to handle destructible terrain (castle walls collapsing, ground fire)? Static is cheaper; runtime is required if mid-fight terrain changes need to repath.

---

## Phase 2: Tactical Orc Basics, Archetypes, and Squad Seeds

Goal: turn the working single tactical orc into small, readable encounter groups without jumping to full commander AI. The immediate implementation focus is still basic movement/perception: line of sight, predefined patrol patterns, and simple archetype tuning.

### Tactical roster

Keep the tactical roster small:

1. **Grunt**
2. **Berserker**
3. **Skirmisher**
4. **Archer**
5. **Assassin**
6. **Giant**

Only Grunt, Berserker, and Skirmisher are in scope for the next implementation pass. Archer adds ranged combat, Assassin adds stealth/priority-target logic, and Giant is a boss-scale tactical unit; all three should wait until melee squads are stable.

### In-scope archetypes

**Grunt** is the baseline orc. It should use the current `OrcAI` tuning: medium speed, medium health, normal attack cadence, moderate block/parry, and straightforward approach behavior. Grunts fill the front line and teach the player what normal orcs do.

**Berserker** is pressure. It should still use the same core `OrcAI`, but with higher speed or aggression, lower defense, lower block/parry chance, shorter attack cooldowns, and dual-wield or chain-style attack options. Berserkers are allowed to feel reckless.

**Skirmisher** is movement. It should prefer side approaches, quick attacks, and disengage/reposition behavior over standing in the main dogpile. This is the first archetype that benefits from squad-assigned roles, but the first pass can be mostly tuning plus more frequent repositioning.

Implementation direction: start with prefab variants / serialized tuning on `OrcAI`. If variants become hard to maintain, add an `OrcArchetypeDefinition` ScriptableObject later to apply movement, defense, attack-table, and squad-role defaults. Do not split into separate AI scripts until an archetype needs behavior the base controller cannot express cleanly.

Implemented first step (2026-05-08): `OrcAI` now exposes a serialized `OrcArchetype` identity field with `Grunt`, `Berserker`, and `Skirmisher`, plus read-only helper properties for squad logic. This does **not** auto-apply tuning yet; prefab variants still own their Inspector values for speed, defense chance, attacks, hitboxes, and reposition settings.

### Leader as squad modifier

Add an orc leader concept, but do not add "Leader" as a seventh orc type. A leader is a squad modifier/role applied to an existing archetype, usually a Grunt at first.

Leader v1 behavior:
- Uses normal `OrcAI` combat.
- Acts as the squad's optional anchor for home/leash/patrol.
- Shares alert state with nearby squad members.
- Helps choose or broadcast the squad's main target.
- Has no special attacks in the first pass.

Leader death should weaken coordination, not break the squad. A simple first version can make skirmishers stop flanking, reduce alert sharing, or make damaged/low-confidence members hesitate or retreat later. If no leader is assigned, the squad still works from its spawn/home anchor.

### Line of sight

LoS is the next basic AI feature. It should replace pure radius detection as the normal target-acquisition gate while keeping radius as the cheap broad phase.

Server-side detection flow:
1. Use `OverlapSphereNonAlloc` on `playerLayer` as the broad phase.
2. Reject dead targets via existing `DamageReceiver.IsDead` / `VitalManager.IsDead` checks.
3. Raycast from an orc eye point to a target aim point.
4. Reject the target if the ray hits an obstacle before the target.
5. Keep the existing detection throttle; expose all distances, heights, masks, and intervals in the Inspector.

Implemented fields:
- `closeDetectionRadius`
- `viewDistance`
- `viewAngle`
- `detectionInterval`
- `detectionTime`
- `eyeHeight`
- `targetAimHeight`
- `visionObstacleMask`
- `loseSightGraceTime`
- `pursueRadiusFromHome`
- `searchDuration`

`loseSightGraceTime` matters because instant target drops feel twitchy around corners. A short grace window lets an alerted orc keep chasing briefly after the player breaks sight, while patrol acquisition still respects LoS.

### Ranged hit reaction

Arrow hits should create an alert-from-source reaction. The first implementation is intentionally simple:
- Ballista/human arrows pass the shooter NetworkObject when known, plus the hit point and source position.
- If the shooter is visible and inside both `investigateRadiusFromHome` and `pursueRadiusFromHome`, the orc immediately targets and pursues the shooter.
- If the shooter is visible inside `investigateRadiusFromHome` but outside `pursueRadiusFromHome`, the orc does not pursue; it runs home as the placeholder "alert the camp" behavior.
- If the source is within `investigateRadiusFromHome` but the shooter is not visible, the orc moves to the source position, searches for `searchDuration`, and then returns to patrol/home if it cannot reacquire.
- If the source is beyond investigate range, the orc moves toward the source but clamps its destination to the edge of `investigateRadiusFromHome`, searches there while facing the shot direction, and then returns to patrol/home.
- Ranged-source investigation uses run movement. If a long-range/bounded investigation spots a player outside `investigateRadiusFromHome`, the orc does not pursue; it runs back home as a placeholder "alert the camp" behavior. If it spots nobody, the return home remains a normal walk.
- Direct arrow hits temporarily override nearby missed-arrow impacts via `directRangedHitPriorityDuration`, so the orc prioritizes the direction/source of the arrow that actually hit it.
- Repeated ranged alerts from outside `viewDistance` increment an out-of-view counter. When `outOfViewRangedAlertReturnThreshold` is reached within `outOfViewRangedAlertWindow`, the orc runs home as the placeholder "alert the camp" behavior instead of continuing to investigate.
- While running home to alert camp, missed-arrow impacts are ignored so the orc does not oscillate between investigating and returning. A direct hit can only interrupt alert-return if the shooter is visible and inside `investigateRadiusFromHome`; otherwise the orc keeps running home.
- Alert-return can still reacquire normally visible players inside both `investigateRadiusFromHome` and `pursueRadiusFromHome`. Visible players outside either boundary keep the orc in alert-return instead of being pursued.
- If investigation is disabled by setting `investigateRadiusFromHome` to 0, the fallback is guard/block stance for `rangedHitGuardDuration`.
- While guarding or investigating from a ranged hit, the orc can still acquire a player who steps into normal FOV/LoS.
- Missed arrows that hit terrain/objects inside an orc's `closeDetectionRadius` also trigger the same source-aware investigation, unless the arrow directly hit an orc. This lets nearby misses feel heard/seen without double-processing direct hits.

Cover behavior is deferred. When cover points exist, this same ranged-hit branch should choose cover instead of only guarding for far/not-visible sources.

### Predefined patrol patterns

Predefined patrol routes are the other immediate basic. Random wander is useful for animals, but orc camps and village patrols need authored intent.

Patrol v1:
- `OrcAI` exposes `OrcPatrolMode`: `Wander`, `Loop`, `PingPong`, and `Idle`.
- `Idle` holds the orc at its placed/shared home position until combat, ranged alert, or squad alert behavior overrides it.
- `Wander` uses the existing random NavMesh point behavior around home.
- `Loop` follows authored `patrolPoints` in order and wraps back to the first valid point.
- `PingPong` follows authored `patrolPoints` forward, then reverses back through the same route.
- Authored routes use `patrolWaitTimeRange` at each point.
- `randomizePatrolStartPoint` can stagger orcs placed on the same route.
- If no valid points exist, route modes fall back to Wander.
- Preserve the current home/leash behavior so combat cannot drag an orc forever.

Prefer a small route component if multiple orcs should share one path. A squad can later assign staggered indices on the same route so the group does not stack on one waypoint.

### Squad behavior seed

Do not build full squad tactics yet. The first useful `OrcSquadController` can be tiny:
- Roster of `OrcAI` members.
- Optional leader reference.
- Shared home anchor.
- Shared alert state.
- Shared current target.
- Optional patrol route assignment.

Once that works, layer in engagement roles: grunts occupy main attack slots, berserkers chase/pressure, skirmishers flank. Keep all of this server-only; individual orcs already replicate through their NetworkObjects and animator sync.

Implemented first step (2026-05-08): `OrcSquadController` is a server-side scene component with serialized member entries, optional leader, shared home anchor, shared/member patrol routes, and target broadcast. Each member entry assigns one `OrcAI`, its calm-state `OrcPatrolMode`, whether it uses the shared patrol points or its own points, and whether route start is randomized. The controller draws patrol path gizmos for the shared route and member-specific routes. Context menu actions can rebuild the roster from child orcs or fill only missing child orcs; generated rows start as Wander, use shared patrol points, and randomize route starts. It polls member `OrcAI` instances for an active combat target and shares that target to nearby alive squadmates. If a leader is assigned and dies, alert sharing still works but uses `leaderlessAlertRadiusMultiplier` to weaken coordination. Individual orcs still keep their own LoS, leash, attacker-count, and less-contested-target behavior, so multiple visible players/NPCs can still split the fight instead of every orc blindly dogpiling the squad target.

---

## Out of scope for Phase 1 (for later)

- Full squad behavior (formation, target assignment, leader/follower, retreat-on-low-HP).
- Camp set-piece staging (ambush from trees, charge from gate).
- Archer, Assassin, and Giant archetypes.
- Active-zone abstract simulation — explicitly skipped via the spawn-on-arrival decision.
- 20v20 combat performance tuning — once one orc works, then 5, then 20.
