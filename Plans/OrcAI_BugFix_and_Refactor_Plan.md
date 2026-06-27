# OrcAI — Bug-Fix & De-Risk Plan (for Codex)

*Author: review pass on V0.7. Target files: `CharacterScripts/Scripts/Orc/OrcAI.cs` (3527 lines), `CharacterScripts/Scripts/Orc/OrcSquadController.cs`, `CharacterScripts/Scripts/Orc/OrcAnimationEventRelay.cs`.*

This plan is written for an automated coding agent ("Codex"). Execute it **phase by phase, in order**.

**Do NOT run `git` at all — no `git add`, no `git commit`, no `git stash`.** The human reviews, stages, and commits every change themselves in Sourcetree. Make your edits and leave the working tree dirty/unstaged. Keep each item's edits logically self-contained (don't intermix A2 and A5 in the same lines) so the human can review and stage them per-item, but the *act of committing* is theirs, not yours.

---

## 0. Context & hard guardrails (read first — do not skip)

This is a Unity 6 + Netcode for GameObjects (NGO) project in **Phase 1 playtesting**. `OrcAI` is the tactical-orc server-authoritative AI (a fork of `BearAI`). The project's operating rules (`Assets/FEFE/CLAUDE.md`) are binding here:

1. **Surgical edits only.** Targeted old→new replacements. Re-read the exact target lines immediately before editing — line numbers in this plan are *hints* and will drift as you edit. Match on method names and code text, not line numbers.
2. **Behavior-preserving unless a fix is explicitly a behavior change.** Each item below is tagged `[BUGFIX]` (must change observable behavior to be correct) or `[REFACTOR]` (must NOT change observable behavior). Keep each item's edits logically self-contained so the human can stage them separately — but do not commit (see above).
3. **Inspector-tweakable everything.** Any new tunable (duration, distance, speed, threshold) must be `[SerializeField]` with a `[Tooltip]`, grouped under an existing `[Header]`. **No new hardcoded magic numbers.** Where this plan says "serialize the existing magic number," preserve the current literal as the default so prefab feel does not change.
4. **Server-authoritative.** All AI mutation stays gated behind `IsServer`. Do not introduce client-side state writes. Do not convert NetworkAnimator-driven sync into NetworkVariables.
5. **Do not touch animator parameter string names** (`"Speed"`, `"CombatState"`, `"AttackIndex"`, `"Attack"`, `"Block"`, `"Parry"`, `"Stagger"`, `"Dead"`, and the serialized `leashThreat*` param strings). String parameter mismatches are silent failures in this project.
6. **Do not break the public API consumed by `OrcSquadController`** (`SetSquad`, `SetHomeAnchor`, `SetPatrolRoute`, `ReceiveSharedTarget`, `ReceiveSharedAlert`, `ReceiveCampAlertReturnHome`, `ReceiveCampAlertHold`, `IsAlive`, `HasCombatTarget`, `CurrentTarget`, `IsRangedInvestigationActive`, `Archetype`, gizmo setters) or the animation-event entry points consumed by `OrcAnimationEventRelay` (`HitboxEnable/Disable`, `MainHand*`, `OffHand*`, `BothHitboxes*`).
7. **Talk-before-code escalation:** items marked `[CONFIRM]` are decisions/possible-intent that need a human ruling. Do not silently pick a behavior — implement the recommended option but call it out clearly in the final handoff summary.
8. **Do not update `design/ToDo.md`.** It is a historical, read-only session log. Report root causes, files changed, fixes, and follow-up notes in the final handoff summary instead.

There is **no automated test suite** for this AI. "Acceptance" below is defined as: compiles clean, no new console errors/warnings in a play session, and a described manual repro that a human can run.

---

## 1. Summary of findings

The dominant defect class is an **implicit state machine smeared across ~13 parallel boolean flags** layered on top of the `OrcState`/`OrcSubState` enums. The combat/return/alert transition helpers each manually reset a *slightly different subset* of these flags, so flags drift out of sync. Several confirmed bugs are direct symptoms. The plan therefore:

- **Phase A** — fixes the highest-impact standalone bugs that don't need the refactor.
- **Phase B** — consolidates the duplicated flag-reset boilerplate into a single source of truth (kills the bug *class*).
- **Phase C** — optional structural cleanup (collapse the `Return` flag cluster into an enum; deduplicate scan loops). Lower priority, higher review cost.

Do A and B. Do C only if explicitly approved after A and B land.

---

## 2. Phase A — Standalone bug fixes

### A1. `[BUGFIX]` Harassment-aggro flag is never reset → permanent leash-ignore
**Location:** `ignoreHomePursueDistanceAfterLeashHarassment` — declared ~line 341, set `true` in `RegisterSuccessfulPlayerHit` (~line 2204), read in `ShouldIgnoreHomePursueDistance` (~2167) and `IsLeashHarassmentRevertedByLowHealth` (~2174).

**Symptom / root cause:** The flag is assigned `true` exactly once and is assigned `false` **nowhere** in the file (confirmed by full-file search). Per design (`design/OrcAI.md`, "Leash harassment update"), a hit during `LeashThreat` should let the orc ignore home-distance pursue checks *until its health drops to `lowHealthPursueRevertRatio`*. But because the flag is never cleared, an orc that was once harassed and then **regenerates health above the ratio** permanently ignores its home leash for every future, unrelated chase — it will chase a player across the map indefinitely. This is exploit-adjacent and breaks camp-leashing.

**Fix:** Reset `ignoreHomePursueDistanceAfterLeashHarassment = false` when the orc fully disengages and returns to calm/home. Concretely, clear it at the point the orc completes its home return and re-enters `Idle` (the arrival branches in `UpdatePatrol`'s `OrcSubState.Return` case where it transitions to `SetState(OrcState.Patrol, OrcSubState.Idle)`), and in `SetState`'s `Dead` case. The cleanest implementation is to fold this flag into the centralized reset added in Phase B — but if A ships before B, add the explicit clears now. Do **not** clear it on every `Begin*` transition (that would defeat the design intent of persistent harassment aggro during an ongoing engagement).

**Acceptance:** Manual repro — bait an orc to the leash boundary, hit it once (it should now ignore home distance and pursue), let it kill nothing and return home; once home and calm, re-bait it to the boundary without hitting it → it must now taunt/return at the boundary (LeashThreat), not chase indefinitely. Before the fix it chases.

---

### A2. `[BUGFIX]` `OnAnimatorMove` writes to a disabled NavMeshAgent during death
**Location:** `OnAnimatorMove` (~633–684); `SetState` `Dead` case sets `agent.enabled = false` (~1552); `ShouldUseRootMotionForState` returns `useCombatRootMotion` for `OrcState.Dead` (~2465).

**Symptom / root cause:** `useCombatRootMotion` defaults `true`, so for the `Dead` state `ShouldUseRootMotionForCurrentState()` is `true`. The death clip keeps firing `OnAnimatorMove`, whose guard (`if (!IsServer || !ShouldUseRootMotionForCurrentState() || animator == null || agent == null) return;`) checks `agent == null` but **not `agent.enabled`**. After death the agent is disabled, so `agent.speed = 100f` and especially `agent.nextPosition = rootPosition` execute on a disabled/off-navmesh agent → NGO/NavMesh logs an error **every frame** of the death animation ("...can only be called on an active agent that is on a NavMesh"), and corpse positioning goes through an unintended path.

**Fix:** Add **only** `!agent.enabled` to the `OnAnimatorMove` early-return guard:
`if (!IsServer || !ShouldUseRootMotionForCurrentState() || animator == null || agent == null || !agent.enabled) return;`
**Do NOT add `!agent.isOnNavMesh` to the early-return** (a first attempt did, and it broke root-motion locomotion — see correction below). The disabled-agent check alone removes the death-time error spam, because the agent is disabled only in the `Dead` case. The `agent.nextPosition` write further down is what re-syncs an enabled-but-momentarily-off-mesh agent back onto the navmesh; bailing on `isOnNavMesh` skips both the root-motion `transform.position` write (orc animates in place) and that re-sync (agent stuck off-mesh forever).

**CORRECTION (post-test regression):** The original fix gated the whole method on `!agent.isOnNavMesh` as well. For orcs using `useLocomotionRootMotion = true`, after a state churn (e.g. leash-taunt → return) the agent momentarily reports off-mesh, so `OnAnimatorMove` bailed entirely — the orc played its move clip but never translated, and the agent never recovered. Reverted to the `!agent.enabled`-only guard above. If death-clip corpse displacement ever needs suppressing, do it by *not* writing `transform.position` in the `Dead` branch specifically, not by gating on `isOnNavMesh`.

**Acceptance:** Kill an orc in a play session → Console shows **zero** NavMeshAgent errors during and after the death animation. Corpse settles without sliding/jitter.

---

### A3. `[BUGFIX]` Self-separation: orc pushes away from its own child colliders
**Location:** `OnAnimatorMove` separation loop (~659–681).

**Symptom / root cause:** The neighbour filter skips a collider only if `col.transform == transform` or if it has no `OrcAI`/`BearAI` in its parents. An orc whose body/weapon colliders live on **child** transforms (common) will not be skipped by `col.transform == transform`, and `col.GetComponentInParent<OrcAI>()` resolves to *this* orc (non-null) → the orc applies separation force away from its own colliders, causing drift/jitter. Note the LoS code (`HasLineOfSight`, ~3069) already correctly excludes self via `GetComponentInParent<OrcAI>() == this`; the separation loop is inconsistent with it.

**Fix:** In the loop, after the existing null/trigger checks, skip self by component identity:
`if (col.GetComponentInParent<OrcAI>() == this) continue;`
Place it before the existing `OrcAI == null && BearAI == null` filter (or fold the self-check in). Keep the random-jitter fallback for the genuine `dist < 0.001f` two-different-orcs-overlapping case.

**Acceptance:** A lone idle orc (no neighbours) holds position with no positional drift/jitter. Two overlapping orcs still push apart.

---

### A4. `[BUGFIX]` `DisableCollidersClientRpc` NREs on unassigned array
**Location:** `DisableCollidersClientRpc` (~3448–3456) iterating `collidersToDisableOnDeath`.

**Symptom / root cause:** `foreach (var col in collidersToDisableOnDeath)` throws `NullReferenceException` on every client if the serialized array is left unassigned on a prefab/variant (entirely plausible across the inconsistent orc prefabs). One NRE per death, on all clients.

**Fix:** Guard the array: `if (collidersToDisableOnDeath == null) return;` at the top of the RPC body (the per-element `if (col != null)` check already exists and stays).

**Acceptance:** An orc prefab with an empty `Colliders To Disable On Death` list dies with no NRE on host or client.

---

### A5. `[BUGFIX]` `UpdateStagger` calls `agent.ResetPath()` unguarded every frame
**Location:** `UpdateStagger` (~1167–1174).

**Symptom / root cause:** `agent.ResetPath()` is called each frame with no `agent != null && agent.enabled && agent.isOnNavMesh` guard. If an orc is staggered while its agent is briefly off the NavMesh (edge/ledge), this logs errors. Also it is redundant to reset the path every frame.

**Fix:** Guard it, and only reset once on entry rather than per-frame. Preferred: remove the per-frame `agent.ResetPath()` from `UpdateStagger` entirely — `SetState`'s `Stagger` case already calls `StopAgent()` (which resets the path) on entry. If a per-frame stop is genuinely needed, wrap it: `if (agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();`. `[CONFIRM]` not required; entry-only stop is behavior-equivalent for a stationary stagger.

**Acceptance:** Stagger an orc near a NavMesh edge → no errors; orc still halts during stagger.

---

### A6. `[BUGFIX/EDITOR]` Static collections survive "Enter Play Mode without Domain Reload"
**Location:** `static Dictionary<Transform,int> _attackerCounts`, `static List<OrcAI> _serverOrcs`, `static int _rangedImpactAlertSequence` (~272–274).

**Symptom / root cause:** If the project has Fast Enter Play Mode (domain reload disabled) — common for iteration speed — these statics retain entries across play sessions: phantom attacker counts (orcs think targets are already max-contested and refuse to attack / mis-route to "less contested" targets) and dead orcs lingering in `_serverOrcs`. Produces confusing, non-reproducible test behavior.

**Fix:** Add a reset hook:
```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetStatics()
{
    _attackerCounts.Clear();
    _serverOrcs.Clear();
    _rangedImpactAlertSequence = 0;
}
```
`SubsystemRegistration` runs before scene load on every play-mode entry regardless of domain-reload setting. (BearAI likely shares this latent issue — report it in the final handoff as a follow-up, but do not edit BearAI in this pass unless asked.)

**Acceptance:** With domain reload disabled, enter play mode twice in a row → orc attacker-contention behavior is identical both runs.

---

### A7. `[CONFIRM]` Single-hit consumption of the damage-suppression window
**Location:** `HandleDamageReceived` (~3284–3296).
```csharp
if (suppressDamageReceivedStateChangeTimer > 0f)
{
    suppressDamageReceivedStateChangeTimer = 0f;   // consumes on first hit
    return;
}
```
**Concern:** The suppression window (set to `0.25f` in several ranged-alert transitions to avoid a stagger cancelling the just-started reaction) is zeroed after suppressing a *single* melee hit. A second melee hit landing within the same window is **not** suppressed → an unintended stagger can still interrupt the ranged reaction. May be intentional (suppress exactly one hit) or a bug (suppress for the duration).

**Action:** Do not change behavior blindly. Flag to the human. **Recommended** if confirmed a bug: drop the `= 0f` line and let `TickTimers` expire the window naturally so it suppresses for its full duration. Also serialize the repeated `0.25f` literal (see A8) so the window is tunable.

---

### A8. `[REFACTOR]` Serialize the load-bearing magic numbers (no behavior change)
**Locations:** `suppressDamageReceivedStateChangeTimer = 0.25f` (repeated ~6×), `OnAnimatorMove` `agent.speed = 100f` (~637), `Stagger` `stateTimer = 0.45f` (~1541), reposition side offset `0.6f` (~1332).

**Fix:** Introduce `[SerializeField]` fields with `[Tooltip]` under appropriate headers, **defaulting to the current literals** so feel is unchanged:
- `rangedReactionStaggerSuppressTime = 0.25f` (Header: Ranged Alert)
- `rootMotionAgentCatchupSpeed = 100f` (Header: Movement) — replaces the `100f` literal; document that it keeps the disabled-by-default agent's internal position in sync while root motion drives the transform.
- `staggerDuration = 0.45f` (Header: a new/existing combat header)
- `repositionSideBias = 0.6f` (Header: Movement)

Replace every literal usage with the field. This is preparation for A7 and general tunability. Keep it a pure mechanical extraction in its own commit.

**Acceptance:** Inspector shows the new fields with the old values; play feel unchanged.

---

## 3. Phase B — Kill the bug class: centralize transient-state resets

This is the highest-value structural fix. **Do it as one focused `[REFACTOR]` commit, behavior-preserving.**

### B1. Inventory the transient flags
The following fields form the "transient sub-mode" set that gets manually reset across many methods:
`movingToLastKnownPosition`, `searchingLastKnownPosition`, `rangedHitGuardActive`, `rangedInvestigationActive`, `boundedRangedInvestigationActive`, `alertReturnHomeActive`, `campAlertHoldActive` (+ `campAlertHoldTimer`), `lowHealthReturnHomeActive`, `postLeashReturnActive` (+ `postLeashReturnTimer`), `leashReturnHomeActive`, `postLeashRangedChargeActive`, `ignoreHomePursueDistanceAfterLeashHarassment` (see A1), plus the combat scratch fields `activeAttack`, `attackHitboxWindowStarted`, `postAttackBlockActive`, `nextBlockDuration`, `awarenessTarget`, `targetAwareness`, and the out-of-view counters `outOfViewRangedAlertCount`/`outOfViewRangedAlertTimer`.

### B2. The problem to fix
These blocks are copy-pasted (with divergent subsets) across at least: `BeginLeashThreat`, `BeginSearchLastKnownPosition`, `HandleInvalidCombatTarget`, `ResumeLowHealthReturnHome`, `BeginPursueRangedAttacker`, `BeginRangedHitGuard`, `BeginInvestigateRangedSource`, `BeginAlertReturnHome`, `ReceiveCampAlertHold`. Divergences already cause latent bugs — e.g. `BeginRangedHitGuard` does not clear `postLeashReturnActive/Timer`/`postLeashRangedChargeActive`; several methods don't clear `outOfViewRangedAlert*`; `ignoreHomePursueDistanceAfterLeashHarassment` (A1) is cleared nowhere.

### B3. The fix
Introduce one private method that establishes a known-clean baseline, with parameters for the few fields a given transition wants to *keep set*:

```csharp
// Clears every transient sub-mode flag + combat scratch state to a known baseline.
// Each Begin*/Handle* transition calls this, then sets ONLY the flags it owns.
private void ResetTransientState(bool cancelHitbox = true)
{
    if (cancelHitbox)
    {
        CancelInvoke(nameof(EnableSelectedWeaponHitbox));
        DisableWeaponHitbox();
    }
    UnregisterAttacker();

    activeAttack = null;
    attackHitboxWindowStarted = false;
    postAttackBlockActive = false;
    nextBlockDuration = -1f;
    awarenessTarget = null;
    targetAwareness = 0f;

    movingToLastKnownPosition = false;
    searchingLastKnownPosition = false;
    rangedHitGuardActive = false;
    rangedInvestigationActive = false;
    boundedRangedInvestigationActive = false;
    alertReturnHomeActive = false;
    campAlertHoldActive = false;
    campAlertHoldTimer = 0f;
    lowHealthReturnHomeActive = false;
    postLeashReturnActive = false;
    postLeashReturnTimer = 0f;
    leashReturnHomeActive = false;
    postLeashRangedChargeActive = false;
    // NOTE: outOfViewRangedAlertCount/Timer and ignoreHomePursueDistanceAfterLeashHarassment
    // are intentionally NOT cleared here — they have cross-transition lifetimes.
    // Clear them explicitly in the transitions that own them (see below).
}
```

Then rewrite each `Begin*`/`Handle*`/`Resume*` method to: (1) call `ResetTransientState()`, (2) set **only** the handful of flags that transition actually wants `true` plus its target/timers, (3) do its animator crossfade, (4) `SetState(...)`. The animator reset boilerplate (`ResetTrigger(attackHash)`, `SetBool(blockHash,false)`, the `Attack`-state crossfade to `locomotionStatePath`) is itself duplicated — extract a `ResetAttackBlockAnimators()` helper and call it from the same sites.

**Critical correctness rules while refactoring:**
- This must be **behavior-preserving for the currently-correct paths.** For each method, diff the *old* set of flags-left-true against the new (baseline-then-set-specific) result. They must match for every field **except** the previously-divergent ones, which now become consistent (that's the intended bug fix — call each one out in the commit message).
- `outOfViewRangedAlert*` lifetime: it is cleared today in `BeginAlertReturnHome`, on target acquisition in `UpdatePatrol`, and on its own timer in `TickTimers`. Preserve exactly those clear points; do **not** move them into `ResetTransientState`.
- `ignoreHomePursueDistanceAfterLeashHarassment`: do the A1 clears (on home arrival → Idle, and Dead). Do **not** put it in `ResetTransientState` (it must survive `Begin*` transitions mid-engagement).
- Keep `RememberOriginalPosition`/home-anchor logic untouched.

### B4. Acceptance for Phase B
This is the risky one. The human verifies the full behavior matrix manually in a play session (they control git/Sourcetree, so they can compare against the pre-refactor build themselves):
1. Patrol (each of Wander/Loop/PingPong/Idle) → acquire player in cone → chase → attack/block/parry → kill → return home → resume patrol.
2. Chase past pursue radius → LeashThreat taunt at boundary → return after `leashThreatMaxDuration`.
3. Hit orc during LeashThreat → harassment pursue (A1) → low health → revert to leash return.
4. Ranged hit from visible attacker inside investigate radius → pursue. From not-visible inside radius → investigate source → search → return. From outside radius → bounded investigate to boundary → search → return.
5. Repeated out-of-view ranged alerts reaching `outOfViewRangedAlertReturnThreshold` → alert-return-home.
6. Squad: direct ranged hit escalates Calm→Suspicious→Alerted→Combat correctly; investigators move, non-investigators hold.

Each path must look identical to pre-refactor, **except** the A1 leash-aggro reset.

---

## 4. Phase C — Optional structural cleanup (only if A+B approved and time allows)

`[REFACTOR]` Behavior-preserving. Higher review cost; do not start without sign-off.

- **C1. Collapse the `Return`/investigation flag cluster into a `ReturnMode` enum.** Replace the mutually-related booleans (`movingToLastKnownPosition`, `searchingLastKnownPosition`, `alertReturnHomeActive`, `lowHealthReturnHomeActive`, `rangedInvestigationActive`/`boundedRangedInvestigationActive`, `leashReturnHomeActive`, `postLeashReturnActive`) with a single `enum ReturnMode { None, ToHome, ToLastKnown, Searching, AlertHome, LowHealthHome, RangedInvestigate, BoundedRangedInvestigate, PostLeash }` driving `GetCurrentReturnDestination` and the `Return` arrival branches. This makes `OrcSubState.Return` (currently overloaded six ways) legible and makes illegal combinations unrepresentable. Large diff — gate behind explicit approval.
- **C2. Deduplicate the three target-scan loops** (`FindBestPerceivedTarget`, `FindLessContestedVisibleTarget`, `FindBestBerserkerTarget`) into one `EnumeratePerceivedTargets(...)` helper that yields `(Transform candidate, int attackerCount, bool closeDetected)`; each caller keeps its own scoring. Removes ~80 lines of near-duplicate physics-query + filter code.
- **C3. Split the 3527-line god class into `partial class` files** by concern (`OrcAI.Perception.cs`, `OrcAI.Combat.cs`, `OrcAI.Locomotion.cs`, `OrcAI.AlertResponse.cs`, `OrcAI.cs` core). Pure file reorganization, zero logic change. Lowest risk of the three but largest churn; do last.
- **C4. Cache `Animator.parameters`** lookups: `TryGetAnimatorParameter` scans `animator.parameters` (which can allocate) on each call. Since all needed params are known at `OnNetworkSpawn`, validate presence once and store `bool`s. Minor GC win.

---

## 5. Squad controller (`OrcSquadController.cs`) — lower-priority notes

Not the focus of this pass, but surface in the final handoff for follow-up:
- `campState` is `[SerializeField]` under "Runtime State" — designers can accidentally edit a runtime-driven value in the Inspector. Consider making it a non-serialized property exposed read-only via a debug inspector, or clearly label it.
- Member registration (`Start` → `TryRegisterMembers`) races orc `OnNetworkSpawn`; current code tolerates it via `hasExternalHomeAnchor`, but `SetPatrolRoute` calling `InitializePatrolRoute` before spawn means `OnNetworkSpawn` re-randomizes the start point. Confirm intended; if not, guard re-init.
- No `OnDestroy` cleanup; members keep a stale `squad` reference if the squad is destroyed first. Edge case.

Do **not** modify squad behavior in this pass unless A1/B reveal a coupling that requires it.

---

## 6. Execution order

**No git from the agent — the human reviews and commits each item in Sourcetree (see section 0).** Order the *edits* so the human can stage them per-item:

1. `A8` (serialize magic numbers) — mechanical, unblocks A7.
2. `A2`, `A3`, `A4`, `A5`, `A6` — independent standalone bug fixes.
3. `A1` — leash-aggro reset.
4. `A7` — only after human `[CONFIRM]`.
5. `B` — centralize resets. **Heaviest manual verification.** Do this as its own batch and hand it back for review before starting C.
6. `C` — only after explicit approval.
7. Provide a consolidated final handoff summary; do not update `design/ToDo.md` and do not run git.

After each item: project compiles and has no new Console errors/warnings. The human runs the manual repro and commits. If any item can't be verified, stop and report rather than proceeding to the next.

## 7. Out of scope
- Rewriting to a true HFSM/Utility-AI framework (design doc's long-term goal) — not now.
- Crowd-tier orcs / Enemy Masses integration.
- `BearAI` edits (note shared issues in the final handoff only).
- Balance/feel tuning (e.g. always-block-during-cooldown behavior) — that's a design decision, not a bug.
