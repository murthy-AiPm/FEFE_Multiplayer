# OrcAI — Archetype ScriptableObject Refactor (Spec for Codex)

*Goal: move the per-archetype **tuning** values out of `OrcAI`'s Inspector into an `OrcArchetypeDefinition` ScriptableObject, so each orc type (Grunt / Berserker / Skirmisher) is one small, tooltip-documented asset you tune in isolation. `OrcAI`'s Inspector shrinks to references + scene placement + one archetype slot. Behavior is identical; only the **source** of the values changes.*

*Target files: `CharacterScripts/Scripts/Orc/OrcAI.cs`, new `CharacterScripts/Scripts/Orc/OrcArchetypeDefinition.cs`, new asset files under `CharacterScripts/Data/OrcArchetypes/` (create the folder).*

---

## 0. Hard guardrails (binding — read first)

1. **No git. No `design/ToDo.md` edits.** Edit/create files, leave them unstaged for the human to review and commit in Sourcetree. Summarize in a final handoff.
2. **Behavior-preserving.** With the migrated `Berserker.asset` assigned to the existing `OrkBerseker.prefab`, the orc must play **identically** to today. This is a representation change, not a tuning change.
3. **Surgical edits to OrcAI.** Do not touch the AI logic, the disengage state machine, locomotion, detection, combat FSM, squad interface, networking, death, or the recent NavMesh/animation fixes. You are only changing where ~50 fields get their *values*, not how they're used.
4. **Phase 1 testing is live.** This is a big mechanical change to a recently-stabilized file — work carefully and keep the diff reviewable.
5. **Asset creation is via filesystem writes**, not Unity "create asset" tooling (it fails silently in this project). You must author the `.cs.meta` and `.asset` (+ `.asset.meta`) YAML by hand — see §5 for the exact GUID wiring. If the `.asset` field names don't match the SO's serialized field names exactly, Unity silently zeroes them, so copy names verbatim.
6. **Inspector-tweakable + documented.** Every field on the SO gets a `[Tooltip]` written for a *designer* (not a coder) and is grouped under a `[Header]`. Clear tooltips are the whole point — the user keeps forgetting what fields like `repositionDistance` mean.
7. **No automated tests.** Acceptance = compiles clean, no new Console errors, and the §6 checks pass in a play session run by the human.

---

## 1. The pattern (why this is low-risk)

For each field that moves to the SO:
- In `OrcAI`, change `[SerializeField] private float runSpeed = 6f;` → `private float runSpeed = 6f;` (drop `[SerializeField]`, **keep the initializer as a fallback default**).
- Add a `ScriptableObject` field: `[SerializeField] private OrcArchetypeDefinition archetype;` under a new top `[Header("Archetype")]` (replacing the existing lone `archetype` enum field — see §3).
- At the very top of `OnNetworkSpawn` (before any tuning field is read — note `agent.speed = walkSpeed` already runs in `OnNetworkSpawn`), call `ApplyArchetype()` which copies every SO value into the matching private field **if** an SO is assigned.

**Every existing line of AI code that reads `runSpeed` (or any moved field) stays byte-for-byte the same** — only the field's declaration and its initialization source change. Hot path untouched.

If no SO is assigned, the private initializers act as fallbacks (prefab still runs). The migrated assets (§5) carry the real values.

---

## 2. Create `OrcArchetypeDefinition.cs`

A `ScriptableObject` with `[CreateAssetMenu(menuName = "FEFE/Orc Archetype")]`. It holds **only** the moved tuning fields (list in §4), grouped with the SAME `[Header]`s used in OrcAI, each with a designer-facing `[Tooltip]`. Include the `OrcArchetype archetype` enum as the first field (the asset *is* the archetype identity).

Reuse the existing top-level serializable types: `OrcAttackOption[] attacks`, `OrcWeaponHitboxSelection`, `OrcArchetype` (all already defined in `OrcAI.cs` at top level — do not redefine them).

**Write real tooltips.** Examples for the cryptic ones (match this clarity for all):
- `preferredCombatDistance` — "Ideal standoff distance the orc keeps from its target while fighting; reposition spots are placed around this radius."
- `repositionDistance` — "If the target gets closer than this, the orc sidesteps instead of standing still. Higher = repositions more eagerly."
- `repositionDuration` — "How long one reposition sidestep lasts before re-evaluating."
- `targetAttackReactDistance` — "Max distance at which the orc will react (block/parry) to the target's incoming swing."
- `frontalDefenseAngle` — "Total cone (degrees) in front of the orc within which it can block/parry. Hits from outside this cone can't be defended."
- `decisionInterval` — "Seconds between combat decisions (attack/block/reposition). Lower = more reactive, more CPU."
- `leashThreatMaxDuration` — "Max seconds the orc taunts a visible out-of-reach target at its leash boundary before returning home."
- `lowHealthPursueRevertRatio` — "At/below this health fraction, a harassed orc stops ignoring its home leash and retreats."
- `maxAttackersPerTarget` — "Max orcs in an attack slot on one target at once; extras hang back or pick a less-contested target."
- `berserkerLowHealthWeight` — "How strongly a berserker favors wounded targets when choosing who to hit (higher = more drawn to low HP)."
- `berserkerDogpilePenaltyWeight` — "Score penalty for targets already swarmed, so berserkers spread out."

Also create `OrcArchetypeDefinition.cs.meta` with a fixed GUID (see §5) so the assets can reference it deterministically.

---

## 3. Refactor `OrcAI.cs`

1. Replace the existing `[Header("Archetype")] [SerializeField] private OrcArchetype archetype = OrcArchetype.Grunt;` with `[SerializeField] private OrcArchetypeDefinition archetype;`. (The field name `archetype` now holds the SO.) Rename the existing enum-backed members so nothing breaks: keep a private `private OrcArchetype archetypeId = OrcArchetype.Grunt;` that `Archetype`/`IsGrunt`/`IsBerserker`/`IsSkirmisher` read from. `ApplyArchetype` sets `archetypeId = archetype.archetype` when an SO is assigned.
2. For every field in the §4 "MOVE" list: drop `[SerializeField]`, keep it `private` with its current initializer as the fallback. Leave all reads untouched.
3. Add `ApplyArchetype()` and call it as the **first statement** in `OnNetworkSpawn` (before `RememberOriginalPosition()` / the agent setup):
   ```csharp
   private void ApplyArchetype()
   {
       if (archetype == null) return;          // keep serialized fallbacks
       archetypeId = archetype.archetype;
       walkSpeed = archetype.walkSpeed;
       runSpeed = archetype.runSpeed;
       // ... copy EVERY moved field, 1:1 ...
       blockChance = archetype.blockChance;
       // attacks: DEEP COPY (see below)
       attacks = CloneAttacks(archetype.attacks);
   }
   ```
4. **Attacks must be deep-copied, not assigned by reference.** `OrcAttackOption` carries a runtime `[NonSerialized] cooldownTimer`. If every orc of a type shares the SO's array instances, they share cooldown state — a real bug. Add:
   ```csharp
   private static OrcAttackOption[] CloneAttacks(OrcAttackOption[] src)
   {
       if (src == null) return null;
       var copy = new OrcAttackOption[src.Length];
       for (int i = 0; i < src.Length; i++)
       {
           var a = src[i];
           copy[i] = a == null ? null : new OrcAttackOption {
               name = a.name, attackIndex = a.attackIndex, minRange = a.minRange,
               maxRange = a.maxRange, weight = a.weight, duration = a.duration,
               cooldown = a.cooldown, hitboxEnableDelay = a.hitboxEnableDelay,
               hitboxActiveTime = a.hitboxActiveTime, isHeavy = a.isHeavy,
               hitboxSelection = a.hitboxSelection
           }; // cooldownTimer stays default(0) per instance
       }
       return copy;
   }
   ```
   (If `OrcAttackOption` gains fields later, update this clone.)
5. Confirm `ApplyArchetype` copies **every** field in §4. A missing field silently falls back to its initializer → behavior drift. Cross-check the list.

---

## 4. The field split (definitive)

**MOVE to `OrcArchetypeDefinition` (the "feel" of the archetype):**

- Identity: `archetype` (enum)
- Detection: `closeDetectionRadius`, `viewDistance`, `viewAngle`, `detectionInterval`, `detectionTime`, `loseSightGraceTime`, `pursueRadiusFromHome`, `searchDuration`, `eyeHeight`, `targetAimHeight`
- Ranged Alert: `investigateRadiusFromHome`, `rangedHitGuardDuration`, `reactToNearbyRangedImpacts`, `directRangedHitPriorityDuration`, `outOfViewRangedAlertReturnThreshold`, `outOfViewRangedAlertWindow`, `rangedReactionStaggerSuppressTime`
- Movement: `useLocomotionRootMotion`, `useCombatRootMotion`, `walkSpeed`, `damagedWalkSpeed`, `runSpeed`, `rotationSpeed`, `preferredCombatDistance`, `repositionDistance`, `repositionDuration`, `repositionSideBias`
- Wander/idle: `wanderRadius`, `idleMinTime`, `idleMaxTime`, `patrolWaitTimeRange`
- Utility: `decisionInterval`, `targetAttackReactDistance`, `frontalDefenseAngle`
- Leash: `holdVisibleThreatAtLeash`, `leashThreatFaceSpeedMultiplier`, `leashThreatMaxDuration`, `lowHealthPursueRevertRatio`, `leashThreatTransitionDuration`
- Attacks: `attacks[]`, `attackCancelDistanceBuffer`, `attackCancelReengageDelay`, `allowAnimationEventAttackCancelBeforeHitbox`, `allowAnimationEventAttackCancelAfterHitbox`, `attackCancelFade`, `blockDuringAttackCooldown`
- Block: `blockChance`, `blockDuration`, `blockCooldown`
- Parry: `parryChance`, `parryDuration`, `parryActiveWindow`, `parryCooldown`, `parryDamage`
- Crowd Control: `maxAttackersPerTarget`
- Berserker: `berserkerTargetScanInterval`, `berserkerRetargetCooldown`, `berserkerSwitchScoreMargin`, `berserkerDistanceWeight`, `berserkerLowHealthWeight`, `berserkerDogpilePenaltyWeight`, `berserkerCurrentTargetStickiness`
- Separation: `separationRadius`, `separationStrength`

**STAY on `OrcAI` (per-instance / scene / refs / tech):**

- References: `vitalManager`, `damageReceiver`, `animator`, `agent`, `animalSoundPlayer`, `weaponHitbox`, `offHandWeaponHitbox`, `weaponData`
- Scene placement: `patrolMode`, `patrolPoints`, `randomizePatrolStartPoint`, `arrivalThreshold`, `patrolPointArrivalThreshold` (squad assigns these per-instance)
- Layer masks: `playerLayer`, `visionObstacleMask`, `groundLayer`
- Animator-controller-specific strings: `leashThreatBoolParameter`, `leashThreatIndexParameter`, `leashThreatStatePath`, `leashThreatVariantCount`, `locomotionStatePath`
- Hitbox wiring: `useAnimationEventsForHitboxes`, `animationEventAttackTimeout`
- NavMesh tech: `chaseRepathInterval`, `chaseRepathDistance`, `navMeshDestinationSampleHeightOffset`, `navMeshDestinationSampleRadius`, `requireCompleteNavMeshPath`, `agentOffMeshRecoverRadius`, `homeSnapSampleRadius`, `rootMotionAgentCatchupSpeed`
- Physics: `gravityStrength`, `groundCheckDistance`
- Death: `collidersToDisableOnDeath`
- Debug: all `Debug` header fields

**Ambiguity rule:** if unsure whether a field belongs in the SO, **leave it on the component** (lower risk). The split above is the source of truth.

---

## 5. Create the archetype assets

Create folder `CharacterScripts/Data/OrcArchetypes/`. Author these files by hand (filesystem writes):

1. `OrcArchetypeDefinition.cs.meta` — pick a fixed 32-hex GUID, e.g. `7c0a1b2c3d4e4f5a6b7c8d9e0f1a2b3c` (verify it doesn't collide with any existing `.meta` in the project; change one digit if it does). Standard MonoScript `.meta` shape.
2. `Berserker.asset` (+ `.asset.meta`), `Grunt.asset`, `Skirmisher.asset`. Each `.asset` is a `MonoBehaviour` referencing the SO script:
   ```yaml
   MonoBehaviour:
     m_Script: {fileID: 11500000, guid: 7c0a1b2c3d4e4f5a6b7c8d9e0f1a2b3c, type: 3}
     m_Name: Berserker
     # ... every SO field by exact serialized name ...
   ```
   Give each `.asset.meta` its own unique GUID.

**Values:**
- **Berserker.asset** — *migrate from the live prefab so nothing is lost.* Read the `OrcAI` MonoBehaviour block in `Prefabs/Enemies/OrkBerseker.prefab` and copy each §4-MOVE field's current value into the asset by matching field name (including the full `attacks` array, nested verbatim). This must reproduce today's Berserker exactly.
- **Grunt.asset** and **Skirmisher.asset** — start from the Berserker values, then apply these deltas (leave everything else equal to Berserker unless noted):

| Field | Grunt | Skirmisher |
|---|---|---|
| `archetype` | Grunt | Skirmisher |
| `runSpeed` | 5 | 7.5 |
| `rotationSpeed` | 6 | 10 |
| `preferredCombatDistance` | 2.2 | 2.8 |
| `repositionDistance` | 1.4 | 2.2 |
| `repositionDuration` | 0.7 | 0.45 |
| `maxAttackersPerTarget` | 4 | 3 |
| `leashThreatMaxDuration` | 5 | 4 |
| `blockChance` | 0.35 | 0.20 |
| `parryChance` | 0.20 | 0.15 |
| `blockCooldown` | 1.4 | 1.6 |
| `parryCooldown` | 2.0 | 2.0 |
| attack `cooldown` (each entry) | 1.2 | 0.9 |
| attack `duration` (each entry) | 1.1 | 0.8 |

(Health is on `VitalManager`, not the SO — the human sets that per-prefab: Grunt ~100, Skirmisher ~65.)

After this, assign `Berserker.asset` to the `Archetype` slot on `OrkBerseker.prefab`. (Grunt/Skirmisher prefabs are a later step the human does — out of scope here beyond creating the assets.)

---

## 6. Verification (human, play session)

1. **Compiles**, no Console errors; `OrkBerseker.prefab` Inspector now shows the `Archetype` slot + references/placement, not the wall of tuning fields.
2. **Berserker plays identically to pre-refactor** — spawn, patrol, chase, attack cadence, block/parry frequency, leash taunt, return home, low-health retreat all feel the same. (This is the behavior-preserving gate; if anything feels off, a field is missing from `ApplyArchetype` or mis-migrated in the asset.)
3. **Two orcs of the same archetype have independent attack cooldowns** (confirms the attacks deep-copy) — they shouldn't fire in lockstep.
4. Open `Berserker.asset` — every field populated (no zeroed values from a name mismatch).
5. Assign `Grunt.asset` / `Skirmisher.asset` to test instances → they exhibit the slower/defensive (Grunt) vs faster/mobile (Skirmisher) feel.

If an SO is left unassigned, the orc still runs on the fallback initializers (no crash).

---

## 7. Out of scope
- Creating Grunt/Skirmisher **prefab variants** (human does this after the assets exist).
- Weapon/loadout changes, animation/controller work, health balancing.
- Any AI logic change. This is purely "where do the numbers come from."
- Moving the per-instance/scene/tech fields (they stay on the component by design).
