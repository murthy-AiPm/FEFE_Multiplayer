# OrcAI — Disengage Subsystem Rewrite (Spec for Codex)

*Scope: rewrite ONLY the "disengaged behavior" layer of `CharacterScripts/Scripts/Orc/OrcAI.cs` — the return-home / investigate / leash-return / camp-alert / low-health-retreat cluster. This replaces ~10 interacting boolean flags + an overloaded `Patrol/Return` substate with one explicit `ReturnActivity` state machine and a single transition method.*

*This is a representation refactor that is behavior-faithful to `design/OrcAI.md`, plus ONE intended behavior change (B-FIX-1 below). Everything else must look identical in play.*

---

## 0. Hard guardrails (binding — read first)

1. **No git. No `design/ToDo.md` edits.** Make edits, leave the working tree unstaged for the human to review and commit in Sourcetree. Report what changed in a final handoff summary only.
2. **Surgical, in-place edits.** Re-read each target method immediately before editing; match on method names/code text, not line numbers (they drift).
3. **Behavior-faithful.** Except for B-FIX-1, every observable behavior in `design/OrcAI.md` must be preserved exactly: leash threat/taunt, leash harassment aggro, low-health limp return, ranged-hit reaction (visible / in-range / out-of-range / bounded), out-of-view ranged-alert counter → alert-camp, camp Suspicious/Alerted holds, search-last-known, post-leash ranged-response window. If you cannot map an existing behavior cleanly, STOP and report — do not drop or "simplify" it.
4. **Do NOT touch** these systems: locomotion / `OnAnimatorMove`, the combat substates (`Approach`, `Reposition`, `Attack`, `Block`, `Parry`, `LeashThreat`), detection/LoS (`FindBestPerceivedTarget`, `CanPerceiveTarget`, `HasLineOfSight`), attack selection, hitbox code, crowd control (`_attackerCounts`), squad interface, networking, death, gizmos, the recent `SnapToNavMesh` / `RecoverAgentToNavMeshIfNeeded` / `OnAnimatorMove` guard work. Leave the temporary debug logging guard in place.
5. **Inspector-tweakable.** No new hardcoded magic numbers; reuse existing serialized fields. This refactor should add **no** new tunables (it only restructures state).
6. **Preserve the public API** consumed by `OrcSquadController` and `OrcAnimationEventRelay`: `SetSquad`, `SetHomeAnchor`, `SetPatrolRoute`, `ReceiveSharedTarget`, `ReceiveSharedAlert`, `ReceiveCampAlertReturnHome`, `ReceiveCampAlertHold`, `IsAlive`, `HasCombatTarget`, `CurrentTarget`, `IsRangedInvestigationActive`, `Archetype`, `IsRangedImpactInAwarenessRadius`, gizmo setters, and all `*Hitbox*` animation-event methods. Their signatures must not change.
7. **Server-authoritative** — no new client writes.
8. **No automated tests exist.** Acceptance = compiles clean, no new Console errors/warnings, and the §6 behavior matrix verified by the human in a play session.

---

## 1. The new state field

Add one enum and one field. Place the enum near `OrcPatrolMode`/`OrcArchetype`; place the field with the other private state.

```csharp
// What the orc is doing while it is NOT in active melee combat. Mutually exclusive
// by construction — replaces the old parallel-boolean disengage flags.
private enum ReturnActivity
{
    None,              // patrolling/idle at home, or in active combat — not disengaging
    Home,              // walking to OriginalPosition, then Idle
    LeashReturn,       // walking home after a LeashThreat (special re-acquire rule, B-FIX-1)
    LowHealthHome,     // limping to OriginalPosition; ignores ranged alerts; allows in-place melee defense
    MoveToInvestigate, // walking to a point of interest (last-known target pos, or a ranged source)
    SearchInvestigate, // standing at the point of interest, searching for searchDuration
    AlertReturnHome,   // running to homePosition to "alert the camp"
    RangedGuard,       // guard-block in place facing a ranged source (when investigate is disabled)
    CampAlertHold,     // squad-driven: hold position and face/guard a threat direction
}

private ReturnActivity returnActivity = ReturnActivity.None;
```

**Overlay fields that stay separate** (they are genuinely orthogonal modifiers, NOT activities — keep them, do not fold into the enum):
- `investigateFromRanged` (bool) — `MoveToInvestigate`/`SearchInvestigate` originated from a ranged hit (drives run speed + bounded handoff). Replaces the role of `rangedInvestigationActive`.
- `investigateBounded` (bool) — the ranged source was outside `investigateRadiusFromHome`, so the destination was clamped to the boundary. Replaces `boundedRangedInvestigationActive`.
- `postLeashReturnActive` + `postLeashReturnTimer` — the 8s window after a leash-return during which a ranged hit can re-pursue. KEEP AS-IS (it overlays `LeashReturn`/`Home`).
- `postLeashRangedChargeActive` — KEEP AS-IS.
- `ignoreHomePursueDistanceAfterLeashHarassment` — KEEP AS-IS (harassment aggro modifier).
- `lastKnownTargetPosition`, `rangedThreatPosition`, `campAlertHoldTimer`, `campAlertLookPosition` — KEEP (data the activities read).

**Delete these now-redundant booleans** (their meaning moves into `returnActivity`): `movingToLastKnownPosition`, `searchingLastKnownPosition`, `rangedHitGuardActive`, `rangedInvestigationActive`, `boundedRangedInvestigationActive`, `alertReturnHomeActive`, `campAlertHoldActive`, `lowHealthReturnHomeActive`, `leashReturnHomeActive`. Replace every read of them with the equivalent `returnActivity` check (mapping in §3).

---

## 2. The single transition method (kills the bug class)

Replace the ~8 duplicated reset blocks (currently in `BeginSearchLastKnownPosition`, `HandleInvalidCombatTarget`, `ResumeLowHealthReturnHome`, `BeginPursueRangedAttacker`, `BeginRangedHitGuard`, `BeginInvestigateRangedSource`, `BeginAlertReturnHome`, `BeginLeashThreat`, `ReceiveCampAlertHold`) with ONE method:

```csharp
// Single source of truth for entering a disengage activity. Resets all combat scratch
// + animator triggers to a known baseline, then sets the requested activity. Every
// Begin*/Handle*/Resume* transition routes through this — no more per-method subsets.
private void EnterReturnActivity(ReturnActivity activity)
{
    CancelInvoke(nameof(EnableSelectedWeaponHitbox));
    DisableWeaponHitbox();
    UnregisterAttacker();

    activeAttack = null;
    attackHitboxWindowStarted = false;
    postAttackBlockActive = false;
    nextBlockDuration = -1f;

    // animator reset (the block currently duplicated everywhere)
    if (animator != null)
    {
        animator.ResetTrigger(attackHash);
        animator.SetBool(blockHash, false);
        if (SubState == OrcSubState.Attack && !string.IsNullOrEmpty(locomotionStatePath))
            animator.CrossFade(locomotionStatePath, attackCancelFade, 0);
    }

    // clear the investigate overlays by default; callers re-set them right after
    investigateFromRanged = false;
    investigateBounded = false;

    returnActivity = activity;
}
```

**Rules for `EnterReturnActivity`:**
- It does NOT clear `awarenessTarget`/`targetAwareness` — preserve the exact current per-method choices for those (some methods cleared them, some didn't). Re-set them in the specific caller exactly as the current code does. (Check each caller; do not assume.)
- It does NOT touch `postLeashReturnActive/Timer`, `postLeashRangedChargeActive`, or `ignoreHomePursueDistanceAfterLeashHarassment` — those are cross-transition lifetimes; the specific callers manage them exactly as today.
- It does NOT call `SetState` — the caller still calls `SetState(...)` (e.g. `Patrol/Return`, `Patrol/Idle`, `Combat/Block`) exactly as it does now.

After calling `EnterReturnActivity(x)`, the caller sets only the activity-specific data (target/destination/threat position/timers) and then `SetState(...)`.

---

## 3. Mapping table — rewrite each transition

Translate each existing transition method to: `EnterReturnActivity(<activity>)` → set activity data → `SetState(...)`. Behavior must be identical (except B-FIX-1).

| Existing method | New activity | Activity data to set | SetState target |
|---|---|---|---|
| `BeginSearchLastKnownPosition` (lost sight) | `MoveToInvestigate`, `investigateFromRanged=false` | `lastKnownTargetPosition` already set by caller; `awarenessTarget=null; targetAwareness=0` (as today) | `Patrol/Return` |
| `HandleInvalidCombatTarget`, leash exit (`SubState==LeashThreat`) | `LeashReturn` | then `BeginPostLeashReturnWindow()` | `Patrol/Return` |
| `HandleInvalidCombatTarget`, health ≤ ratio | `LowHealthHome` | — | `Patrol/Return` |
| `HandleInvalidCombatTarget`, otherwise | `Home` | — | `Patrol/Return` |
| `ResumeLowHealthReturnHome` | `LowHealthHome` | `currentTarget=null` | `Patrol/Return` |
| `BeginRangedHitGuard(source)` | `RangedGuard` | `rangedThreatPosition=source; currentTarget=null`; `RotateToward(source)`; then `BeginBlock(rangedHitGuardDuration, false)` | (Block sets `Combat/Block`) |
| `BeginInvestigateRangedSource(source)` | `MoveToInvestigate`, `investigateFromRanged=true`, `investigateBounded=IsRangedSourceOutsideInvestigationRadius(source)` | `rangedThreatPosition=source; lastKnownTargetPosition=GetRangedInvestigationDestination(source)`; `RotateToward(source)` | `Patrol/Return` |
| `BeginAlertReturnHome(spotted)` | `AlertReturnHome` | `rangedThreatPosition=spotted`; reset out-of-view counters (as today) | `Patrol/Return` |
| `ReceiveCampAlertHold(threat,dur,face)` | `CampAlertHold` | `campAlertHoldTimer=Max(0.1,dur)`; compute `campAlertLookPosition` (as today); `RotateToward(...)` | `Patrol/Idle` |

`BeginPursueRangedAttacker` and `BeginLeashThreat` end in **combat** states (`Combat/Approach`, `Combat/LeashThreat`) → they set `returnActivity = ReturnActivity.None` (via `EnterReturnActivity(None)`) since the orc is re-engaging, then set their combat data. Keep their current target/awareness assignments.

**Derived reads to rewrite (search-and-replace each flag):**

- `GetCurrentReturnDestination()`:
  - `LowHealthHome` → `OriginalPosition`
  - `MoveToInvestigate` → `lastKnownTargetPosition`
  - `AlertReturnHome` → `homePosition`
  - `Home` / `LeashReturn` / else → `OriginalPosition`
- `IsRangedInvestigationActive` (public) → `(returnActivity == MoveToInvestigate || returnActivity == SearchInvestigate) && investigateFromRanged) || returnActivity == AlertReturnHome`. (Matches today's `rangedInvestigationActive || boundedRangedInvestigationActive || alertReturnHomeActive`.)
- `IsReturningHomeAfterLeashThreat()` → `returnActivity == LeashReturn && State == Patrol && SubState == Return`.
- `RefreshLowHealthReturnHomeMode()` → guard `returnActivity != LowHealthHome` instead of `!lowHealthReturnHomeActive`; on trigger, `EnterReturnActivity(LowHealthHome)` semantics (clear target etc. as today) but WITHOUT re-running the full SetState (it currently just flips flags mid-Return). Preserve current effect: stays in `Patrol/Return`, switches the active return to LowHealthHome.
- `UpdateAnimatorSpeed()` Return branch:
  - damaged-walk when `returnActivity == LowHealthHome && IsHealthAtOrBelowPursueRevertRatio()`
  - run when `returnActivity == AlertReturnHome`, OR `returnActivity == LowHealthHome` (above ratio), OR (`returnActivity == MoveToInvestigate && investigateFromRanged`)
  - else walk
  (Exactly mirrors today's `shouldDamagedWalk` / `shouldRunPatrolMovement`.)
- `UpdateCombat()` ranged-guard intercept: `returnActivity == RangedGuard && currentTarget == null` (was `rangedHitGuardActive && currentTarget == null`). On its block-timeout in `UpdateBlock`, clear to `None` and `SetState(Patrol, Return)` with `returnActivity = Home` — match current `rangedHitGuardActive` exit (it sets `blockCooldownTimer` then `Patrol/Return`).

**Return-arrival branches** (`UpdatePatrol`, `OrcSubState.Return`, inside the `physicallyAtReturnDestination || completedAcceptedReturnPath` block): rewrite the three sub-branches by `returnActivity`:
- `LowHealthHome` → clear to `None`; `SetState(Patrol, Idle)`.
- `MoveToInvestigate` → switch to `SearchInvestigate`; `SetState(Patrol, Idle)`; `stateTimer = searchDuration`.
- `AlertReturnHome` → record `completedAlertReturnHome=true`; reset out-of-view counters; if `!IsAtOriginalPosition()` switch to `Home` and `SetState(Patrol, Return); return;`; else fall through to clear.
- `Home` / `LeashReturn` (else) → clear `returnActivity = None`, clear `ignoreHomePursueDistanceAfterLeashHarassment`, `postLeash*`, etc. exactly as the current `else` branch (A1 fix preserved); `SetState(Patrol, Idle)`.

**Idle branch** (`UpdatePatrol`, `OrcSubState.Idle`):
- `SearchInvestigate` → rotate toward `rangedThreatPosition`; on `stateTimer <= 0` end search → `EnterReturnActivity(Home)` → `SetState(Patrol, Return)` (mirrors today's `searchingLastKnownPosition` end).
- `CampAlertHold` → countdown `campAlertHoldTimer`, face `campAlertLookPosition`; on expire clear to `None`, `stateTimer = 0` (resume patrol next tick), exactly as today.

---

## 4. B-FIX-1 — the leash-return re-acquire fix (the only intended behavior change)

**Today:** `canAcquireTarget` in `UpdatePatrol` is gated by `!IsReturningHomeAfterLeashThreat()`, so while walking home after a taunt the orc is blind to ALL targets. Combined with him sometimes never cleanly arriving, he can stay blind — the reported bug.

**New behavior (confirmed):** during `LeashReturn`, the orc may re-acquire a target that is **inside his pursue radius**, but still must NOT re-chase the far target that triggered the taunt (preserves the no-loop fix).

**Implementation:** in `UpdatePatrol`, replace the blanket `!IsReturningHomeAfterLeashThreat()` gate so that during `LeashReturn` acquisition is allowed, but immediately after `FindNearestDetectedTarget()` returns a target, if `returnActivity == LeashReturn` reject the candidate when it is NOT inside the pursue radius (`!IsTargetInsidePursueRadius(target)` and `!IsSelfInsidePursueRadius()`), i.e. keep walking home; accept it (drop to normal acquisition → `Combat/Approach`) when it IS inside the pursue radius. Reuse the existing `IsTargetInsidePursueRadius` / `IsSelfInsidePursueRadius` helpers. Do not add new fields.

Acceptance for B-FIX-1: taunt at boundary → he walks home → run up close to him (inside pursue radius) → he chases/reacts (sword hit then staggers AND re-acquires). Stand far at the boundary while he returns → he does NOT ping-pong back into a taunt.

---

## 5. Execution order (one logical change, but stage it so the human can review in slices)

1. Add the enum + field + overlay fields; delete the old booleans but temporarily leave compile errors as your worklist.
2. Add `EnterReturnActivity`. Route every transition through it (§3 table).
3. Rewrite the derived reads (`GetCurrentReturnDestination`, `IsRangedInvestigationActive`, `IsReturningHomeAfterLeashThreat`, `RefreshLowHealthReturnHomeMode`, `UpdateAnimatorSpeed`, `UpdateCombat` guard intercept, arrival branches, Idle branch).
4. Apply B-FIX-1.
5. Compile clean; self-check the §3 mapping line-by-line against the pre-edit behavior (diff each transition's old flags-left-set vs new activity+overlays — they must match, except B-FIX-1).
6. Hand back for the human to run §6.

Do NOT commit. Do NOT edit ToDo.md.

---

## 6. Behavior verification matrix (human runs in a play session)

Each must look identical to pre-rewrite, except B-FIX-1:
1. Patrol (Wander/Loop/PingPong/Idle) → see player in cone → chase → attack/block/parry → kill → walk home → resume patrol.
2. Chase past pursue radius → LeashThreat taunt → walk home; **B-FIX-1:** approach him during the walk-home → he chases; stay far at boundary → no ping-pong.
3. Hit him during taunt (harassment) → ignores home distance → at low health limps home (damaged walk) → arrives, resumes.
4. Lose sight mid-chase → walk to last-known → search → walk home.
5. Ranged hit, attacker visible in investigate radius → pursue. Not visible, in radius → run to source, search, return. Source outside radius → bounded run to boundary, search facing shot, return.
6. Repeated out-of-view ranged alerts ≥ threshold → run home to alert camp.
7. `investigateRadiusFromHome == 0` → ranged hit → guard-block in place for `rangedHitGuardDuration`.
8. Squad: ranged disturbance → Suspicious (investigators move, others hold & face) → 2nd disturbance → Alerted → visual confirm → Combat; all clear → Calm.
9. Death mid-each-activity → no errors, corpse settles.

If any path diverges from pre-rewrite (other than #2's B-FIX-1), it's a mapping error in §3 — fix before sign-off.

---

## 7. Out of scope
- The 3527-line god-class file split, the 3 duplicate scan-loop dedup, `Animator.parameters` caching (separate Phase C items — not now).
- Combat FSM, locomotion, detection, squad internals, networking — untouched.
- Any new tunables or balance changes.
