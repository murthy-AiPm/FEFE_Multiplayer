# FEFE_Multiplayer — Operating Document

## Project summary

FEFE_Multiplayer is the working repo for **7 Days Till Dawn** (7DTD) — a Unity 6
asymmetric multiplayer survival-defense game. A small group of defenders holds a
castle through a 7-day prep cycle culminating in a Day-7 orc siege. One player
is the dragon — a wildcard ally who can defend the castle or side with the orcs.

Target player count: 1–N defenders + 1 dragon. The game scales with player
count (Phasmophobia model); it does not change shape.

Tech stack: Unity 6, Unity Netcode for GameObjects (NGO), Mecanim for the
dragon (Animator Controllers + blend trees), Animancer for humans (rule-based
blend mixers), Cinemachine 3.x.

The project is in **Phase 1 testing**. Phase 0 playtesting completed with
positive feedback, which informed the pivot from the prior FEFE direction
(5-role asymmetric siege) to 7DTD's simpler equipment-based shape. Most of the
existing dragon, vitals, ballista, and networking code carries forward; the
5-role specialization does not — see `design/7DaysTillDawn_Design.md` for the
equipment-based replacement and `design/FEFE_Design.md` for historical context
on the prior direction.

Phase 1 implies real builds in real test cycles. Prefer stability-preserving
changes during active test windows; check with the user before refactors that
might invalidate an in-flight playtest build.

## Paths & conventions

- Project root: `Assets/FEFE` (this directory loads AGENTS.md every session).
- Layout:
  - `CharacterScripts/Scripts/Animal/Controller` — animal base classes (Animal* prefix).
  - `CharacterScripts/Scripts/Animal/Dragon` — dragon-specific (Dragon* prefix).
  - `CharacterScripts/Scripts/Animal/Horse` — mountable horse + mount system.
  - `CharacterScripts/Scripts/Human` — humanoid controllers, combat, animation, camera, UI.
  - `CharacterScripts/Scripts/Shared` — cross-character systems (respawn, burn, crit zones, ground fire pool).
  - `CharacterScripts/Scripts/Ballista` — ballista weapon + arrows.
  - `Sound` — sound database, per-character sound players, surface detection, ambient zones.
  - `Multiplayer/Scripts/Networking/{Client,Host,Server,Shared}` — split by ownership role.
- `design/ToDo.md` is an append-only session log. **Only update it when the user explicitly asks.**
  Tail-read it before appending. New entries go at the bottom of the file. Each entry: date header, root cause, files changed, fix.
- All other `.md` docs live under `design/` (ARCHITECTURE, FEFE_Design, FEFE_NPC_Architecture, ToDo).
  Only `AGENTS.md` stays at the `Assets/FEFE/` root so Codex auto-loads it each session.
  New `.md` files go in `design/` by default.
- For new files use the filesystem write tools. Unity-style "create asset" tools have failed silently in this project — do not rely on them.

## Architecture map

One line per class. Behavior detail lives in `design/ARCHITECTURE.md`.

### Dragon (Animal/Dragon)
- `DragonGroundController` — ground locomotion + flight/hit root-motion override on `OnAnimatorMove`.
- `DragonFlightController` — takeoff/flight/landing, writes Thrust/Yaw/Pitch/FlightMode to animator.
- `DragonSwimController` — swim locomotion and buoyancy.
- `DragonGroundingSystem` — dragon's ground/water raycast state.
- `DragonGroundAlignment` — slope alignment (subclass of `AnimalGroundAlignment`).
- `DragonAnimatorController` — flight/swim/combat NetworkVariable sync (extends base).
- `DragonCombatController` — attack modes (melee/firebreath), neck/spine/jaw procedural, roar.
- `DragonFireBreathDamage` — cone-overlap fire damage tick + BurnStatus.Ignite calls.
- `DragonHitboxManager` — paw trigger colliders, animation-window gated.
- `DragonDamageAnimator` — bridges DamageReceiver events to Mecanim; death-from-sky gravity.
- `DragonHitRootMotion` — StateMachineBehaviour, gates hit root-motion + alignment suspension.
- `DragonCinemachineModeSwithcer` — owner-only camera activation (NetworkBehaviour).
- `DragonSoundPlayer` — animation-event driven sounds, fire-breath crossfade, wing motion gate.
- `DragonUI` — local HUD bits for the dragon player.
- `GroundFireSpawner` — owner-driven raycast → ServerRpc → ClientRpc fan-out for ground patches.

### Humans (Human/*)
- `HumanoidController` — top-level humanoid orchestration.
- `HumanoidInputController` / `InputController` — input gathering.
- `HumanoidCombatController` / `CombatController` — combat state machine.
- `ThirdPersonController` / `PlayerController` — camera-driven movement.
- `HumanoidAnimationSet` / `DragonAnimationSet` / `AnimationSetBase` — Animancer rule sets.
- `AnimationContext` / `AnimationRuleSet` / `RuleAnimancerDriver` — rule evaluation + Animancer driver.
- `CombatLocomotionMixer` — Cartesian mixer states (dodge directionality lives here).
- `HumanDamageAnimator` — bridges DamageReceiver events to RuleAnimancerDriver.
- `VitalManager` / `Vital` / `VitalDefinition` — HP/stamina vital pipeline.
- `WeaponManager` / `WeaponData` / `CombatLoadout` — weapon equip + loadout data.
- `HitboxController` — sword/melee swing detection (SphereCastNonAlloc).
- `AnimationEventRelay` — relays animation events to combat/weapon code.
- `DamageReceiver` — character-agnostic damage entry, fires `OnPlayHitAnimation`/`OnPlayDeathAnimation`.
- `Human_VCam` / `VCam` / `VCameraObject` — Cinemachine wrappers.
- `PlayerHUD` / `WorldHealthBar` — health UI.
- `TrainingDummy` / `BowAimDebug` — test scaffolding.

### Animal base + horse
- `AnimalGroundController` — base ground locomotion. `OnAnimatorMove` is `protected virtual`; `TurnAngle` setter is `protected`. Input is gated by `protected virtual bool IsInputSuppressed()`: if a `MountableEntity` is on the same GameObject, suppression follows `IsMounted` + ownership; otherwise the legacy `_ignoreInput` flag (`protected`). Camera-relative rotation lazy-resolves `Camera.main` every `HandleGroundMovement`/`HandleAirSteering` call. Fake-gravity branch does a predict-cast (`fakeGravityLandHeight`, `fakeGravityGroundMask`) that clamps the fall step so the body lands flush.
- `AnimalAnimatorController` — base ground param sync (20 Hz throttled, change-detected).
- `AnimalGroundAlignment` — slope alignment with `SuspendAlignment` flag (FixedUpdate bails when true).
- `AnimalGroundingSystem` / `AnimalSwimSystem` / `AnimalFootIKController` / `AnimalFlightStateInspector` — base systems.
- `HorseGroundController` — thin override: forces `useRootMotion = true`, disables animator `applyRootMotion`, routes jump to `HorseSoundPlayer`. All input gating lives in the base.
- `MountController` / `MountableEntity` / `MountPromptUI` / `MountDetectionDebug` / `RandomIdleAnimator` — horse mount system. `MountableEntity.SetMounted` always uses `FreezeRotation` (never `FreezeAll`).
- `MountInputController` — legacy stub kept for prefab references; input gating moved to `AnimalGroundController.IsInputSuppressed`.

### Shared
- `RespawnController` — death/respawn flow (loading screen mask + bone reset).
- `BurnStatus` — DOT burn state, refresh-on-contact, NetworkObject-identity self-immunity.
- `CritZoneMarker` — collider tag for crit zones (head/wing). `damageMultiplier`, `zoneName`.
- `GroundFirePool` / `GroundFirePatch` — local prewarmed pool + per-patch fade/decal/trigger.

### Ballista
- `BallistaController` — turret aim + fire.
- `BallistaOperator` — player mount/dismount on the ballista.
- `BallistaArrow` — kinematic-RB trigger arrow; checks `CritZoneMarker`, calls `DamageReceiver`.

### Networking (Multiplayer/Scripts)
- `Networking/ApplicaitonController` — app entry, role decision.
- `Networking/Client/{ClientSingleton, ClientGameManager, NetworkClient}` — client lifecycle.
- `Networking/Host/{HostSingleton, HostGameManager}` — host lifecycle.
- `Networking/Server/NetworkServer` — server lifecycle.
- `Networking/Shared/UserData` — serializable user payload.
- `Networking/{AuthenticationWrapper, LANManager, VersionChecker}` — auth/transport/version.
- `Spawn/{CharacterSpawnHandler, SpawnPoint}` — spawn pipeline.
- `CharacterSelection/*` — selection UI, sync, late-join, character DB.
- `NetworkPlayerMovement/{ClientNetworkTransform, ClientNetworkAnimator, ClientPlayerMove, DontDestroyPlayer}` — movement/anim sync helpers.
- `NetworkPlayerMovement/New/{ClientAuthoritativePlayerDriver, OnwerOnlyCameraRig, AnimanerNetSync}` — newer client-auth driver.
- `PlayerDisplayNames/{PlayerDisplayName, HumanoidPlayer}` — display name sync.
- `NetworkMenus/{MainMenu, LobbiesList, LobbyItem, NameSelector, LoadingScreen}` and `Menus/{PauseMenu, DeathScreen, SettingMenuUI, QuitGameButton}` — menu UIs.

### Sound
- `SoundDatabase` — ScriptableObject lookup by string name.
- `SoundEntry` — **top-level type**, NOT nested under SoundDatabase.
- `DragonSoundPlayer` / `HorseSoundPlayer` / `AnimalSoundPlayer` / `CombatSoundPlayer` / `BallistaSoundPlayer` — per-character sound dispatch, animation-event driven.
- `FootstepSoundPlayer` / `HoofCollisionSoundPlayer` / `ProximitySoundManager` / `AmbientSoundZone` — environmental + footstep audio.
- `TerrainSurfaceDetector` / `SurfaceTag` / `MaterialTag` — surface-aware sound selection.
- `VoiceChatStub` / `MinMaxRangeAttribute` — voice + Inspector helpers.

## Working principles (hard rules)

- **Talk before code.** Discuss the design and confirm the plan before writing it. Bypassing this step has consistently produced rework.
- **Surgical edits only.** Use targeted old/new replacements. Re-read the target section *immediately* before editing — stale context causes mismatches and silent breakage.
- **Inspector-tweakable everything.** Serialize tunable values (`[SerializeField]`). No hardcoded thresholds, durations, speeds, distances.
- **Base classes stay clean.** Dragon/horse-specific behavior belongs in `DragonXController` / `HorseXController`, never in `AnimalXController`. When the base needs an extension point, add a `protected virtual` hook (see `OnAnimatorMove` and `TurnAngle` protections in `AnimalGroundController`).
- **Follow existing patterns.** Before inventing a new approach, find how the codebase already does it. The bullet-decal ground-fire fix (10th-April) is the canonical example: matching the familiar pattern beat the invented one.
- **Git as safety net.** Expect to revert to clean commits when an approach fails. Keep changes scoped so a revert doesn't take unrelated work with it.
- **Do not update `design/ToDo.md` by default.** Append to it only when the user explicitly asks for a ToDo/session-log entry. Root cause, files changed, fix description. Future sessions read this for context.

## Networking patterns

The canonical pipeline used everywhere in `DragonAnimatorController` / `AnimalAnimatorController`:

```csharp
// 1) Declare with Owner write permission (default for this codebase).
private NetworkVariable<bool> netIsRoar = new NetworkVariable<bool>(
    default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

// 2) Owner pushes source-of-truth → NetworkVariable in UpdateNetworkVariables (called at 20 Hz).
protected override void UpdateNetworkVariables()
{
    base.UpdateNetworkVariables();
    if (combatController != null)
    {
        bool roaring = combatController.IsRoaring;
        if (netIsRoar.Value != roaring)
            netIsRoar.Value = roaring;          // change-detected to avoid traffic
    }
}

// 3) ALL clients (owner included) read NetworkVariable → Animator in LateUpdate.
protected override void LateUpdate()
{
    base.LateUpdate();
    animator.SetBool(isRoarHash, netIsRoar.Value);
}
```

For one-shot effects (sounds/VFX trigger), use ServerRpc → ClientRpc fan-out. Example from `DragonCombatController`:

```csharp
[ServerRpc] void RoarSoundServerRpc()    => RoarSoundClientRpc();
[ClientRpc] void RoarSoundClientRpc()    => soundPlayer?.OnRoar();   // runs on every client
```

Same pattern is used for `MeleeAttack` VFX/SFX broadcast.

**CRITICAL RULE:** A NetworkVariable with `Owner` write permission **cannot** be written from inside a `[ServerRpc]` (the RPC body runs on the server, which is not the owner). The owner must write directly. RPCs are reserved for *broadcasting* a one-shot side effect to every client. This bit `netIsBreathingFire` and `netAttackMode` (see design/ToDo.md 2nd-April-2026 — "CRITICAL FIX").

Late-join: a remote that spawns into an in-progress flight sees the correct NetworkVariable values but the Animator sits in its default Idle state because no transition fires. `DragonAnimatorController.OnNetworkSpawn` calls `animator.Play("BlendFly")` (or `"SwimmingLocomotion"`) on remote clients when the relevant NetVar is already set. Same pattern applies to any future late-join state forcing.

## Project-specific gotchas

- **`SoundEntry` is top-level**, not nested under `SoundDatabase`. `SoundDatabase.SoundEntry` does not compile.
- **String parameter names must match exactly.** Animator params and PlaySound names are case- and space-sensitive: `"WingFlap"` vs `"Wing Flap"` is a silent failure. Verify the exact string in the database/animator before referencing it from code.
- **Pool AudioSources stay always active.** Unity drops `outputAudioMixerGroup` on `SetActive(true)` reactivation. Pooled AudioSources for proximity/footstep audio must be created enabled and kept enabled — gate playback via `Stop()`/`Play()`, not GameObject activity.
- **Arrows: kinematic Rigidbody + trigger collider.** Zombie/NPC capsule colliders are non-trigger so `Physics.SphereCastNonAlloc` (which ignores triggers) detects them for sword hits, while arrows still fire `OnTriggerEnter` against the capsule. Don't flip a capsule to trigger to "fix" arrow detection — it breaks sword hits.
- **Dragon hit-animation rotation lives in spine/pelvis bones, not root.** `Animator.deltaRotation` was ~0.2°/frame on imported hit clips, useless for code-driven rotation. The working solution: `rb.MoveRotation` lerps toward attacker, `AnimalGroundAlignment.SuspendAlignment = true` prevents alignment fighting, `SetYawImmediate` on exit. (Hit clips later switched to full root motion via `DragonHitRootMotion` SMB — see design/ToDo.md 11th-April. Either pattern is in the codebase; check the SMB before assuming.)
- **NetworkObject identity vs OwnerClientId.** `OwnerClientId` is a property of an entity, not an identifier for one. All server-owned NPCs share `OwnerClientId == 0`, so equality checks for "is this the same thing" silently match every server-owned object. Use `NetworkObjectReference` over the wire and compare `NetworkObject` identity (see `BurnStatus` self-immunity).
- **Refresh-on-contact timers** must use a refresh value > the time between refreshes, or the burn/stagger/etc. accumulates to zero. Burn DOT was a no-op until `burnTimePerTick` was decoupled from `_tickInterval`.
- **Horse prefabs are inconsistent.** As of 28th-April-2026, `HorseFEFEBlack` and `HorseFEFEPalomino` carry `HorseGroundController`; `HorseFEFE`, `HorseFEFEBrown`, `HorseFEFEGray`, `HorseFEFEWhite` carry `DragonGroundController`. Anything that is supposed to apply to "all horses" must live in `AnimalGroundController` (the common base) or be replicated. A `HorseGroundController`-only feature is dead code on most prefabs. Long-term cleanup is to standardize all horse prefabs on `HorseGroundController`.
- **Scene-loaded NetworkObjects spawn before the player's MainCamera.** Horses are scene objects, so any `cam = Camera.main?.transform` in `Awake` resolves to `null` permanently — the player's `MainCamera.prefab` (under `Multiplayer/Prefabs/Player/`) doesn't exist yet. Lazy-resolve in the consumer (`HandleGroundMovement` / `HandleAirSteering` re-fetch `Camera.main` on every call). This is the same trap any future scene-NPC will hit.
- **Input gating: `MountableEntity.IsMounted` is the source of truth, not flags.** A flag-based "riderless" gate (`_ignoreInput` toggled by `MountInputController.Update`) raced with `OnBecameGrounded` resetting the flag, opening a one-tick window per frame where input leaked into every horse on the map. The current pattern (`AnimalGroundController.IsInputSuppressed` consults `MountableEntity` directly each call) has no flag to keep in sync. Don't reintroduce a state flag.
- **Rigidbody constraints + dismount.** `MountableEntity.SetMounted(false)` used to set `RigidbodyConstraints.FreezeAll` to keep the riderless horse from drifting. That blocked decay (instant stop instead of glide-down) and trapped the horse mid-air on a mid-jump dismount. The fix is to leave it on `FreezeRotation` and rely on the controller's no-input branch + `OnAnimatorMove`'s X/Z-zero idle behavior. Don't reintroduce `FreezeAll`.
- **Root-motion + gravity conflict.** `OnAnimatorMove` for `useRootMotion = true` animals (horse) sets `rb.linearVelocity = animator.deltaPosition / Time.deltaTime`, which kills Y velocity each frame. The idle branch was changed to zero only X/Z, preserving Y so gravity (or fake gravity's `MovePosition`) can land the body. If a future horse animation has Y root motion (e.g. a falling clip), it will fight fake gravity — flatten it or gate `OnAnimatorMove`'s Y write.

## Further reading

- **Active design** — `design/7DaysTillDawn_Design.md`. 7DTD pitch, core loop, equipment, dragon loyalty axis, win/lose. **This is the source of truth for current direction.**
- For deep system design, call graphs, and per-subsystem data flow of existing code: see `design/ARCHITECTURE.md`.
- **Prior design (historical/reference)** — `design/FEFE_Design.md`. The 5-role siege framing the project pivoted away from after Phase 0. Most code built for FEFE carries forward; the role structure does not.
- For forward-looking NPC & scale architecture (active-zone streaming, agent ceilings, NGO-safe NPC patterns): see `design/FEFE_NPC_Architecture.md`.
- For session history, design rationale, and the trail of "why is it like this": see `design/ToDo.md`.
