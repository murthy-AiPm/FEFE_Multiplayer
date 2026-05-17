# FEFE_Multiplayer — Architecture

A living systems map for `Assets/FEFE`. Pair this with `CLAUDE.md` (which is the
short operating doc — conventions, paths, working principles). This document is
the long-form reference: how each subsystem is built, where its boundaries are,
how it talks to the network, and what is actually fragile about it today.

> **Scope.** Everything documented below lives under `Assets/FEFE/…`. Third-party
> packages (Animancer, Cinemachine, Netcode for GameObjects, StarterAssets) are
> assumed; only our usage of them is described.

---

## 0. Overview

The codebase is a **Unity 6 + Netcode for GameObjects (NGO)** prototype for a
small co-op session (~5 players) where humans and a dragon share the same map.
There is one git branch in flight at a time (currently `v0.66_Burn`); pre-alpha
Phase 0; no commitment to backwards compatibility.

The project is built on two **parallel character stacks** that share only a
handful of generic components:

| Layer            | Human                                          | Dragon (and other animals)                         |
|------------------|------------------------------------------------|----------------------------------------------------|
| Locomotion root  | `CharacterController`                          | `Rigidbody`                                        |
| Animation        | Animancer (rule-driven blend trees)            | Mecanim Animator + blend trees                     |
| Movement style   | Capsule + camera-relative                      | Root-motion-only on ground/flight                  |
| Network sync     | Snapshot-based via `ClientAuthoritativeAnimancerSync` | NetworkVariable mirroring in `*AnimatorController` |
| Combat anims     | Driven by `RuleAnimancerDriver` rules          | Driven by Mecanim parameters set by controllers    |

What both stacks share: `VitalManager`, `DamageReceiver`, `HitboxController`,
`BurnStatus`, the `Sound/` subsystem, `MountController`, and the network session
infrastructure.

**Design philosophy in practice (from observing the code, not docs):**

- **Owner-write for player state, server-write for shared truth.** Player
  motion, intent, and animation flags are owner-authoritative
  `NetworkVariable`s throttled at 20 Hz. Damage, vitals, kill events, and
  spawning are server-authoritative ServerRpc → ClientRpc.
- **Throttle + epsilon everywhere.** `NETWORK_UPDATE_INTERVAL = 0.05f`,
  `FLOAT_EPSILON = 0.01f`. Every sync class re-implements this constant rather
  than sharing one.
- **Reflection for puppeting.** `ClientAuthoritativeAnimancerSync` writes the
  owner's `InputSnapshot` into a non-owner's `InputController` via a cached
  `<Snapshot>k__BackingField` `FieldInfo`. This is the seam between "owner
  collected input" and "remote replays the same logic from the snapshot."
- **Late-join self-healing in `OnNetworkSpawn`.** A late client whose dragon is
  mid-flight does `animator.Play("BlendFly")` directly so the Animator catches
  up without a transition; combat states do the equivalent.
- **Pooled visuals + `NetworkObjectReference` for cross-client identity.** Ground
  fire, decals, hit FX are local pools driven by RPCs; only IDs cross the wire.

---

## 1. Application & Session Lifecycle

### Responsibility
Boot the game, authenticate, host or join a relay/LAN session, and load the
`Game` scene with a single networked `CharacterSelectManager` spawned and ready.

### Key classes

- `ApplicationController` (`Multiplayer/Scripts/Networking/ApplicaitonController.cs`)
  — boot. Decides headless vs. graphical. Instantiates `HostSingleton` and
  `ClientSingleton`, awaits `clientSingleton.CreateClient()`, then
  `GameManager.GoToMenu()`.
- `HostSingleton` / `ClientSingleton` — `DontDestroyOnLoad` persistent holders
  for the corresponding `*GameManager` instance.
- `HostGameManager` — Relay allocation + Lobby create + heartbeat coroutine +
  `NetworkServer` setup + scene load. Hardcoded `MaxConnections = 20`,
  `GameSceneName = "Game"`.
- `ClientGameManager` — `UnityServices.InitializeAsync()`, builds
  `NetworkClient`, runs `AuthenticationWrapper.DoAuth()`, `JoinAllocationAsync`
  on join.
- `NetworkServer` — owns `ConnectionApprovalCallback`. Stores
  `clientId → authId → UserData` so any system can resolve a player's UGS
  identity. **Sets `response.CreatePlayerObject = false`** because spawning is
  delegated to `CharacterSpawnHandler`.
- `NetworkClient` — disconnect handler that forces a `Menu` scene load (only in
  pure-client mode; a host disconnecting itself stays put).
- `AuthenticationWrapper` — UGS anonymous sign-in with retry/state machine
  (`AuthState` enum: `NotAuntheticated/Authenticating/Authenticated/Error/TimeOut`).
- `VersionChecker` — fetches `allowed_version` from UGS Remote Config; the
  hardcoded `buildVersion` field must match for hosting to be allowed. Also
  injects the version into the lobby `Data` payload so clients can filter.
- `LANManager` — alternative host/join path with `0.0.0.0` bind, no Relay,
  no Lobby. F5/F6 hotkeys. Reuses `NetworkServer` and `UserData`.
- `UserData` — three-field `[Serializable]` POCO (`userName`, `userAuthId`,
  `characterId`). Sent as JSON via `NetworkConfig.ConnectionData`.

### Data flow

```
ApplicationController.Start
  → HostSingleton.CreateHost()   (pre-builds GameManager)
  → ClientSingleton.CreateClient()
       → ClientGameManager.InitAsync()
            → UnityServices.InitializeAsync()
            → AuthenticationWrapper.DoAuth()  (anonymous sign-in)
       → returns true on success
  → ClientGameManager.GoToMenu()  (loads "Menu" scene)

User clicks Host:
  HostGameManager.StartHostAsync
    → RelayService.CreateAllocation + GetJoinCode
    → UnityTransport.SetRelayServerData
    → LobbyService.CreateLobbyAsync (with Version + JoinCode in Data)
    → start heartbeat coroutine (15 s interval)
    → new NetworkServer(NetworkManager.Singleton)
    → build host UserData → Encode as ConnectionData
    → networkServer.AddHostData(userData)   (clientId 0 = host)
    → NetworkManager.StartHost()
    → SceneManager.LoadScene("Game", Single)
    → OnGameSceneLoaded → spawn the (already-in-scene) CharacterSelectManager

User clicks Join:
  ClientGameManager.StartClientAsync(joinCode)
    → RelayService.JoinAllocationAsync
    → UnityTransport.SetRelayServerData
    → build UserData → Encode as ConnectionData
    → NetworkManager.StartClient()
    → server's ApprovalCheck stores their UserData by authId
```

### Network contract

- Connection approval is implicit: any payload that JSON-decodes into a
  `UserData` is accepted. There is no token check, no allowlist, no version
  check on the server side. (The version check is **client-side only**, gating
  the host button.)
- The host is special-cased as `clientId == 0` in `NetworkServer.AddHostData`
  rather than detected from `IsHost`.

### Extension points

- `LANManager` is parallel to `HostGameManager`; if you add a third transport
  (Steam, custom matchmaker), follow the same shape: create a `NetworkServer`,
  fill in `ConnectionData`, call `StartHost`/`StartClient`, then `LoadScene`.
- `UserData` can be widened (add fields), but every subscriber that reads it
  via `GetUserDataByClientID` will need updating.

### Gotchas

- File is misspelled `ApplicaitonController.cs` (and a few siblings:
  `OnwerOnlyCameraRig.cs`, `DontDestoryPlayer`, `AnimanerNetSync.cs`,
  `SignInANonymouslyAsynyc`). Renaming any of these breaks scene/prefab
  references — check meta files before touching.
- `HostGameManager` hardcodes `GameSceneName = "Game"` and `MaxConnections = 20`;
  there is no central config.
- `VersionChecker.HostingAllowed` failing closed (returns false on fetch error)
  means a Remote Config outage silently disables hosting. Fine in production,
  brittle while iterating.
- The `Build` button path is invisible from the code: read the calling site in
  `MainMenu` before assuming behaviour.

---

## 2. Character Selection & Spawning

### Responsibility
Let every connected client choose one of N character prefabs (with optional
"unique" enforcement), persist that selection on the server, and spawn the
right prefab at the right `SpawnPoint` for both initial join and late join.

### Key classes

- `CharacterSelectManager` (`Multiplayer/Scripts/CharacterSelection/CharacterSelectManager.cs`)
  — `NetworkBehaviour` singleton. Owns
  `NetworkList<CharacterSelection>` for live UI sync and a static
  `Dictionary<ulong, int> serverSelections` that **persists across scene loads
  on the server**.
- `CharacterSelection` — `INetworkSerializable, IEquatable` struct of
  `(ClientId, CharacterIndex, IsReady, FixedString32Bytes PlayerName)`.
- `CharacterSelectionSync` — a thin shim used elsewhere; calls
  `SetServerCharacterSelection` directly on the host or routes through a
  `ServerRpc` on a client.
- `CharacterDatabase` (ScriptableObject) — array of `CharacterData`.
- `CharacterData` (ScriptableObject) — `characterName`, `icon`,
  `characterColor`, `prefab`, `combatLoadout`.
- `CharacterSpawnHandler` (`Multiplayer/Scripts/Spawn/CharacterSpawnHandler.cs`)
  — server-side `NetworkBehaviour` singleton in the `Game` scene. Subscribes to
  `OnClientConnectedCallback` and spawns or queues each client.
- `SpawnPoint` — self-registering `MonoBehaviour`. Static `GetRandomSpawnPos()`
  / `GetAllSpawnPoints()`.

### Data flow

```
Menu scene                          CharacterSelect scene                     Game scene
─────────                          ─────────────────────                     ──────────
                                  CharacterSelectManager.OnNetworkSpawn
                                   → server adds entry per ConnectedClientId
                                   → fires OnSelectionsChanged
client clicks card → TrySelectCharacter
   → SelectCharacterServerRpc
       → IsCharacterTaken? reject ─→ RejectSelectionClientRpc
       → else: update NetworkList + serverSelections[clientId]

client clicks Ready → SetReadyServerRpc

client clicks Spawn → NotifyReadyToSpawnServerRpc
                                                                    CharacterSpawnHandler.TrySpawnPlayerCharacter
                                                                       → reads CharacterSelectManager.GetPersistedCharacterIndex
                                                                       → Instantiate(prefab) at GetNextSpawnPosition()
                                                                       → networkObject.SpawnAsPlayerObject(clientId)
                                                                       → NotifySpawnSuccessClientRpc (targeted)
```

Late join:
- A client connecting after the `Game` scene already loaded gets
  `CharacterIndex == -1` (no entry in `serverSelections`).
  `LateJoinCharacterSelectUI` calls `RequestTakenCharactersServerRpc`, and on
  selection calls `RequestLateJoinSpawnServerRpc(idx)`. The server validates
  uniqueness, despawns any existing player object, persists, and spawns.

### Network contract

- All selection writes are server-validated (`SelectCharacterServerRpc` checks
  `IsCharacterTaken` again on the server, not just on the client).
- `serverSelections` is `static`, so the server's selection state survives the
  scene transition from `CharacterSelect` → `Game`. Cleared in
  `OnNetworkSpawn` when the manager re-spawns in the new scene.
- Spawning uses `SpawnAsPlayerObject(clientId)`, which makes the prefab visible
  via `NetworkManager.ConnectedClients[id].PlayerObject`.

### Extension points

- Add a class via `CharacterDatabase` ScriptableObject + a new prefab; nothing
  in the lifecycle code needs to change.
- `CombatLoadout` on the data asset is the seam to give a class different
  weapons / shield / vitals overrides.

### Gotchas

- The static `serverSelections` is **never cleared on host shutdown**, only on
  the next `OnNetworkSpawn`. Hosting twice in one editor session without a
  domain reload would carry stale data; `OnNetworkSpawn` does clear it on the
  server side, but a non-host hosting after being a client may inherit stale
  entries.
- `IsCharacterTaken` is duplicated in `CharacterSelectManager` and
  `CharacterSpawnHandler` — they should not drift.
- `GetPlayerName(clientId)` in the manager only resolves names through
  `HostSingleton.Instance.GameManager.networkServer`. On a LAN host that
  goes through `LANManager` instead, this resolves to a fallback
  `"Player {id}"` because `HostSingleton.GameManager` was never built.

---

## 3. Animal Locomotion (base layer)

### Responsibility
Generic NPC/animal locomotion components that the dragon and horse extend:
ground detection, paw alignment, swim hysteresis, shared root-motion plumbing
to a `Rigidbody`, and the Animator parameter cache (`IsGrounded`,
`ForwardSpeed`, `TurnSpeed`, `TurnAngle`, `GaitSpeed`).

### Key classes

- `AnimalGroundController` — base for all four-legged movement. Defines virtual
  hooks: `OnAnimatorMove`, `OnGroundUpdate`, `OnFixedGroundUpdate`,
  `OnTakeoffRequested`, `OnLanded`. Owner ServerRpc/ClientRpc for jumps so
  remotes hear the grunt.
- `AnimalAnimatorController` — base for all animal animator sync. Owns the
  ground-locomotion `NetworkVariable`s and the 20 Hz throttle
  (`NETWORK_UPDATE_INTERVAL`). All subclasses call `base.LateUpdate()` and
  `base.UpdateNetworkVariables()` first.
- `AnimalGroundingSystem` — four-paw `Physics.Raycast`s. The "grounded" rule is
  **at least 3 of 4 paws OR both front paws** — trips on slopes when one front
  paw lifts but the dragon shouldn't be considered airborne.
- `AnimalGroundAlignment` — averages paw normals + a `SuspendAlignment(seconds)`
  hook so a takeoff anim isn't fought by alignment.
- `AnimalSwimSystem` — water enter/exit hysteresis: enters at 0.3 m above
  water, exits at -0.5 m. Avoids flicker right at the surface.
- `HorseGroundController` — trivial subclass. `useRootMotion = true`, no
  overrides. The horse is "AnimalGroundController with mounting" — the
  interesting bits live in `MountableEntity` / `HorseSoundPlayer`.

### Data flow

```
FixedUpdate (Animator root motion):
  Animator.applyRootMotion=true → OnAnimatorMove (overridden by DragonGroundController)
                                  if IsOwner, applies deltaPosition / deltaRotation to Rigidbody
                                  HitRootMotionActive / FlightRootMotionActive branches

Update / LateUpdate (state):
  AnimalGroundingSystem.IsGrounded, paw normals
  AnimalGroundAlignment uses normals to keep model aligned to slope
  AnimalAnimatorController.UpdateNetworkVariables (every ~50 ms):
    isGrounded, forwardSpeed, turnSpeed, turnAngle, gaitSpeed → NetworkVariables
  AnimalAnimatorController.LateUpdate:
    on remotes, push NetworkVariable values into animator
```

### Network contract

- All ground locomotion `NetworkVariable`s are owner-write, everyone-read.
- 20 Hz throttle + `Mathf.Abs(...) > FLOAT_EPSILON` for floats; explicit
  zero-snap (`if (val == 0f && net.Value != 0f) net.Value = 0f`) is a recurring
  pattern to avoid epsilon drift leaving residual non-zero values.
- Jumps are edge-triggered by ServerRpc — never inferred from a flag.

### Extension points

- A new animal subclass overrides `OnAnimatorMove` if it needs custom root
  motion (dragon does this for flight; horse does not), and overrides
  `UpdateNetworkVariables` to add its own state (dragon adds flight, swim,
  combat; horse adds nothing).
- New ground bools/floats: add a `NetworkVariable`, hash an Animator parameter
  in `Awake`, push from `UpdateNetworkVariables` on the owner, pull in
  `LateUpdate` on remotes.

### Gotchas

- `AnimalGroundingSystem`'s "3-of-4 OR both front" rule is a hand-tuned
  heuristic — it works for the dragon and horse but is **not parameterised**.
- `AnimalGroundAlignment.SuspendAlignment(seconds)` is the only way to ask
  alignment to back off; if you forget to call it during a custom takeoff,
  the model fights you.

---

## 4. Dragon

The dragon is the most complex character in the codebase. Five tightly coupled
subsystems live on the same prefab and talk to each other through C# refs (no
events, no service locator).

### 4.1 Locomotion

#### Responsibility
Move the dragon on the ground (root-motion), in flight (root-motion-only,
6-DOF), and in water (Animancer/Mecanim swim blendtree). Hand off cleanly
between modes with shared takeoff/dive/landing animations.

#### Key classes
- `DragonGroundController` — extends `AnimalGroundController`. Branches in
  `OnAnimatorMove` for `FlightRootMotionActive` / `HitRootMotionActive` paths.
  `BeginTakeoff()` calls `flightController.EnterFlight()` after suspending
  alignment and pushing animator into the takeoff state.
- `DragonFlightController` — root-motion-only flight. Inputs: `FlightThrust`,
  `FlightYaw`, `FlightPitch`, `FlightRoll`. `EnterFlight()` / `ExitFlight()` to
  toggle `IsFlightMode`. `DiveCrashLand` ServerRpc for the kill-on-impact
  result; `EnforceMinAltitude` is a fail-safe so the rigidbody can't tunnel.
  Roll is dual-mode based on thrust: **above** `rollMotionMinThrust` Q/E is a
  one-shot discrete roll (snaps `_rmRoll = ±1`, locked for `rollDuration`,
  code drives forward/lateral/arc displacement); **at or below** that threshold
  Q/E is a smooth axis like Yaw (`MoveTowards` based on key hold, no code-
  driven displacement — visual roll only). Hover state is derived from thrust
  every frame (`isHoverMode = |thrust| < 0.05`, `isFlapping = thrust > 0.05`,
  `isGliding = thrust < -0.05`) — there is no toggle. While `thrust ≤ 0`,
  `hoverUpKey` (Space) / `hoverDownKey` (LeftControl) drive vertical motion at
  `hoverVerticalSpeed`; pressing them while flapping forward is a no-op.
- `DragonSwimController` — engages when `AnimalSwimSystem` reports submerged.
  `surfaceBuoyancy = 0` workaround keeps the dragon slightly under the
  waterline without bobbing oscillation.
- `DragonAnimatorController` — extends `AnimalAnimatorController`. Owns
  `netIsDiving`, `netFlightMode`, `netFlightThrust`, `netFlightYaw`,
  `netFlightPitch`, `netIsFalling`, `netIsRoar`, `netIsSwimming`,
  `netSwimSpeed`, `netSwimTurn`, `netSwimVertical`. Late-join fix in
  `OnNetworkSpawn` does `animator.Play("BlendFly")` or
  `animator.Play("SwimmingLocomotion")` for remotes mid-state.
- `DragonHitRootMotion` — `StateMachineBehaviour` on the hit-reaction state.
  Sets `HitRootMotionActive=true` in `OnStateEnter`, `false` in `OnStateExit`,
  plus an `HitAnimActive` edge so other systems can clean up.

#### Data flow (entering flight)

```
Owner presses jump while grounded + tap held:
  DragonGroundController.BeginTakeoff
    → AnimalGroundAlignment.SuspendAlignment(0.4s)
    → animator.SetBool("RequestTakeoff", true)
    → flightController.EnterFlight()   // sets IsFlightMode = true, Rigidbody.useGravity=false

  DragonAnimatorController.UpdateNetworkVariables (next tick)
    → netFlightMode.Value = true
    → netFlightThrust/Yaw/Pitch updates each frame from flightController

  Remote DragonAnimatorController.LateUpdate
    → animator.SetBool(flightModeHash, netFlightMode.Value)
    → animator.SetFloat(thrust/yaw/pitch hashes, …)
  Remote OnAnimatorMove
    → animator already in BlendFly thanks to FlightMode bool transition
    → root-motion drives Rigidbody on the OWNER only; remote relies on ClientNetworkTransform
```

#### Network contract
- `netFlightMode/Thrust/Yaw/Pitch/Roll` are owner-write.
- `netIsDiving` and `netIsFalling` are owner-write but explicitly **only
  pushed to the animator on remotes** (`if (!IsOwner)`) so the owner never
  fights its own state.
- Death is special: `DragonDamageAnimator` controls `FlightMode` while dead, so
  `DragonAnimatorController.LateUpdate` skips the FlightMode write
  (`if (!isDead)` guard at line 162).

#### Extension points
- A new flight mode (e.g. "soar") would add a NetworkVariable on
  `DragonAnimatorController`, a parameter on the Animator, and a transition
  driven by an `IsSoaring` bool from `DragonFlightController`.
- New emergency landing modes plug into `DragonFlightController.DiveCrashLand`
  pattern: a `ServerRpc` that toggles a state and disables input briefly.

#### Gotchas
- `DragonAnimatorController.LateUpdate` reads `ForwardSpeed` from
  `flightController.Velocity` when airborne and **overwrites the base
  ground-mode write**. If the base ever changes the ForwardSpeed semantics,
  the override silently rots.
- `OnAnimatorMove` deltas must only be applied on the owner — every subclass
  in the dragon chain enforces `if (!IsOwner) return;` in its branch.
- Late-join `animator.Play("BlendFly")` works only because the `BlendFly`
  state name matches the actual state in the controller asset; renaming the
  state without updating this code silently breaks late-join sync.
- The animator-side `Roll` parameter is a **Float**, not an Int — required by
  the smooth-axis branch which writes intermediate values via `MoveTowards`.
  The roll destination state is a single clip, not a direct blend tree;
  blending the roll motion with active flight blend weights (Pitch/Yaw/Thrust)
  every frame produced a visibly rough transition even in the animator preview.
- Roll exit blend (`rollExitBlendTime`) keeps code-driven forward speed
  (linearly tapered) running for ~0.3s after the discrete roll ends, to cover
  the animator's exit transition back into BlendFly. Without it the dragon
  visibly stalls — root motion is zero (roll clip is in-place) while BlendFly
  ramps in.
- Pause-menu freeze: `DragonFlightController.Update` early-returns on
  `PauseMenu.IsPaused`, but on the rising edge (entered pause this frame) it
  zeroes `_rmThrust/_rmPitch/_rmYaw` and the matching animator floats so the
  BlendFly tree eases into a glide/hover pose. Animator.speed is intentionally
  *not* zeroed — the blend tree must keep running to reach the rest pose.

### 4.2 Combat

#### Responsibility
Provide the dragon's offensive abilities: melee paw swipes, fire breath
(continuous cone-of-effect damage), procedural neck/head aim, jaw open,
roar, and ground fire patches as a lasting hazard.

#### Key classes
- `DragonCombatController` — the orchestration class. State: `IsAttacking`,
  `IsBreathingFire`, `IsRoaring`, `attackMode` (which paw), neck weights
  (5 segments at 0.05/0.10/0.20/0.30/0.35), jaw rotation
  range -106.534 → -125. Inputs: mouse for swipe, hold for fire breath,
  KeyCode.R for roar. `SetBreathingFireClientRpc` delegates to a shared
  `ApplyFireBreathVisualState(bool)`; `OnNetworkSpawn` calls the same helper
  on remotes when `netIsBreathingFire.Value` is already true so late joiners
  catch up to an in-progress breath (mirrors the `animator.Play("BlendFly")`
  late-join pattern in `DragonAnimatorController`).
- `FireBreathParticleHandler` — sits on every `ParticleSystem` in the fire-
  breath VFX whose **Collision module is enabled**. `OnParticleCollision`
  pulls events via `GetCollisionEvents` and forwards `(intersection, normal)`
  to the bound `DragonFireBreathDamage` and `GroundFireSpawner`. Only the
  owner binds receivers — remote clients still simulate VFX locally so they
  see flame, but their handlers stay unbound and inert (no duplicated damage /
  patch RPCs from each spectator).
- `DragonFireBreathDamage` — receives per-particle hits via
  `HandleParticleHit(other, point)`. Adds the hit `NetworkObjectId` to a
  per-tick `HashSet`, flushes one `ServerRpc` per `tickRate` with all unique
  receivers (dedupes 50 particles on one zombie down to one tick of damage).
  Damage is per-tick; **burn time is decoupled from tick interval** so a
  single tick can apply N seconds of burn and DOT continues after the breath
  stops.
- `DragonHitboxManager` — animation-event-driven paw hitbox enable/disable
  (mirrors `HitboxController`'s pattern but specialised for paws so it can
  also hit dragon-only crit zones).
- `GroundFireSpawner` — receives per-particle hits via
  `HandleParticleHit(point, normal)`. Throttled by `spawnInterval`, fires a
  `ServerRpc` with `(spawnPos, normal)`, server fan-outs `ClientRpc` so every
  client spawns from its own local `GroundFirePool`. No raycast, no travel-
  delay math — particle position IS the spawn point.

#### Data flow
```
Owner holds left-mouse:
  DragonCombatController.Update
    → IsBreathingFire = true
    → DragonAnimatorController.UpdateNetworkVariables
        → netIsBreathingFire = true → animator Crossfade to fire breath state
    → SetBreathingFireClientRpc(true) on every client
        → ApplyFireBreathVisualState: animator bool + VFX prefab spawn + SFX
        → on owner only: bind FireBreathParticleHandler instances on each
          collision-enabled PS in the spawned VFX

  Per particle collision (owner's VFX only, every frame):
    FireBreathParticleHandler.OnParticleCollision
      → DragonFireBreathDamage.HandleParticleHit(other, point)
          accumulates unique NetworkObjectIds in per-tick HashSet
      → GroundFireSpawner.HandleParticleHit(point, normal)
          spawnInterval-throttled → ServerRpc(spawnPos, normal)
                → ClientRpc fanout → GroundFirePool.SpawnAt(...) on each client
                → patch.Init(config, sourceRef)

  Every tickRate seconds (DragonFireBreathDamage):
    flush HashSet → one ServerRpc with all NetObjectIds
      → server: ApplyProjectileDamage + BurnStatus.Ignite per receiver
```

#### Network contract
- `netIsBreathingFire`, `netIsRoar`, neck/jaw `NetworkVariable`s are
  owner-write so remote dragons mirror the head pose.
- Damage is server-authoritative through `DamageReceiver.ApplyProjectileDamage`
  (server-only API). The owner's VFX particles are the source of truth for
  hit candidates (`OnParticleCollision`). `DragonFireBreathDamage` collects
  unique `NetworkObjectId`s in a per-tick `HashSet` and flushes one
  `RequestFireDamageServerRpc` (`RequireOwnership = true`) every `tickRate`
  seconds with the full list. The ServerRpc body resolves each ID, calls
  `ApplyProjectileDamage` and `BurnStatus.Ignite` on the server. So a
  non-host dragon owner deals damage correctly — at the cost of one
  ServerRpc round-trip per tick (4 Hz today, so the latency is invisible).
- Ground fire patches are spawned on every client locally; only their identity
  (the dragon `NetworkObjectReference`) crosses the wire so self-immunity
  works.

#### Extension points
- New attack mode: extend `DragonCombatController` with another bool/state +
  add a `netAttackMode` value + Mecanim transition.
- New AoE hazards: copy the `GroundFirePool` + `GroundFirePatch` pattern
  (local pool + RPC fanout, NetworkObjectReference for source).

#### Gotchas
- The fire-breath VFX prefab particle systems must have **Collision module
  enabled, Type = World, "Send Collision Messages" on**, with the layer mask
  including ground + character/zombie layers and Collision Quality = High
  (Medium can miss thin colliders on small fast particles). If the VFX is
  rebuilt without these, damage and ground patches both silently stop.
- Range is now whatever `startSpeed * startLifetime` gives the particles —
  there is no `coneRange` knob. Increase either to extend reach; the
  collision footprint follows automatically.
- Only the **owner** binds `FireBreathParticleHandler` receivers. If you ever
  bind on remotes (e.g. by moving the bind into `ApplyFireBreathVisualState`
  unconditionally), every spectator will fire damage/patch RPCs — N-clients
  worth of duplicated damage.
- `GroundFirePatch` adds a kinematic Rigidbody in `Awake` if missing.
  `OnTriggerStay` requires at least one of the two parties to have a
  Rigidbody, and NavMesh-driven NPCs (zombies) don't have one. Removing the
  kinematic-RB add silently re-breaks zombie fire damage; player damage will
  keep working because CharacterController carries its own dispatch path.
- Roar is keyed to `KeyCode.R` (Old Input System) while combat is on `Input
  System` (`InputSnapshot`). This split is intentional but easy to miss.

### 4.3 Damage feedback

#### Responsibility
Wire the dragon to `DamageReceiver` events, play hit reactions, and run a
three-phase death sequence (hit → fall → impact) with the right Animator
parameters synced to remotes.

#### Key classes
- `DragonDamageAnimator` — subscribes to
  `DamageReceiver.OnPlayHitAnimation` / `OnPlayDeathAnimation`. Triggers
  `GotHit`, `HitFB`, `HitLR`, `IsDead`, `DeathLR`, `DeathImpact` Animator
  parameters. Owns the death-fall three-phase routine and the `HitAnimActive`
  edge cleanup so other systems know hit motion is over.

#### Network contract
- `OnPlayHitAnimation` / `OnPlayDeathAnimation` are `DamageReceiver` events
  fired *on every client* via `NotifyHitClientRpc` / `NotifyDeathClientRpc`,
  so the trigger is already broadcast — `DragonDamageAnimator` does not need
  its own NetworkVariables for hits.
- Death overrides flight: while `IsDead`, `DragonAnimatorController` skips
  `flightModeHash` writes so this class can free-control the parameter.

#### Gotchas
- The `IsDead` flag is queried by sibling controllers as
  `animator.GetBool(isDeadHash)`; flipping it via Animator parameter rather
  than a NetworkVariable means it's only consistent if everyone agrees on the
  Mecanim state. The death ClientRpc is the synchronisation point.

### 4.4 Sound

`DragonSoundPlayer` — fire-breath crossfade between two AudioSources
(`fireBreathLoopMaxClipDuration = 3 s` because the loop point isn't seamless),
plays through `ProximitySoundManager`. The wing-flap sound gate consults
`DragonWingActivityTracker.IsFlapping` (see §4.5) so glide / dive frames don't
fire flap audio. See §11.

### 4.5 Vitals & Stamina

#### Responsibility
Cross-vital math for the dragon: drives stamina drain/regen during flight
and abilities, scales costs/regen by zone HP, exposes owner-readable gates
that downstream controllers (flight, combat) consult to enforce exhaustion
behavior. Sits on top of the cross-character vital pipeline in §10 — does
not own the vital pool; reads `Vital`s from `VitalManager`.

#### Key classes
- `DragonStaminaController` — `NetworkBehaviour`. Server-authoritative drain
  and regen accumulators flushed to `VitalManager` at `flushInterval` (50 ms).
  Public read API consumed by the rest of the dragon stack (callable on every
  peer; lockout/cap state is computed locally from synced `_stamina.Current`):
    - `CanFireBreath` — `!IsDepleted && !_fireBreathLockedOut`
    - `MeleeReducedDamage` — true at zero stamina
    - `WingsBroken` — true when wing zone HP at 0
    - `MaxFlightThrust` — smoothed cap, eases between `1f` and
      `highThrustThreshold` over `thrustCapEaseSeconds`. Owner's
      `DragonFlightController` clamps `_rmThrust` to this each frame.
    - `MaxClimbPitch` — smoothed cap, eases between `1f` and
      `exhaustedMaxClimbPitch` (~0.3) on the same window. Owner's flight
      controller clamps positive `_rmPitchTarget` to this; negative pitch
      (diving) is unaffected.
    - `StaminaNormalized` — for HUD readout.
- `DragonWingActivityTracker` — plain `MonoBehaviour` (not networked).
  Samples `wingBone.localRotation` in `LateUpdate` on every peer, reports
  smoothed angular velocity (`WingActivity`, deg/sec) and a binary
  `IsFlapping`. Consumed by:
    - `DragonStaminaController` — `flapEffort` term in the flight drain
      formula.
    - `DragonSoundPlayer` — gates the wing-flap sound during glides/dives.
  Defaults `flapThreshold = 80`, `smoothing = 8`. `IsFlapping` returns `true`
  when `wingBone` is null so consumers fall back to pre-tracker behavior if
  the Inspector reference is missing.

#### Drain formula (effort-based flight)
```
effort = clamp01(|thrust|) × invLerp(lazyFlapDegPerSec, hardFlapDegPerSec, wingActivity)
drain  = peakRate × effort × CostScale(wings)
```
"Work pays, gravity is free": hover (thrust ≈ 0) → no drain even with hard
flapping; tucked dive (flap ≈ 0) → no drain even at full thrust; climb at
moderate thrust + active flap → proportional drain. `peakRate` is
`highThrustDrainRate` (named for legacy reasons — now the rate at full effort).
Fire-breath drain is independent (`fireBreathDrainRate × CostScale(head)`,
no wing gate).

#### Regen
Server-side tick adds to `_regenAccum` whenever:
- `_regenCooldown` has elapsed (1 s grace after most recent drain frame), AND
- not firebreathing, not sprinting (`NetGaitSpeed > 0.9`), AND
- `Current < Max`.

Tier picked by grounded vs airborne only — there is no thrust gate. Drain
frames latch the cooldown open, so any time the dragon stops paying it
recovers automatically (mid-glide, mid-dive, hovering still, on the ground).

#### Firebreath lockout (anti-chatter)
Without hysteresis, a depleted dragon's regen tick raises stamina above 0
within 1–2 frames of being available, `CanFireBreath` flips true, breath
restarts, drain immediately depletes again — a sub-second restart loop heard
as continuous sound and a fluttering jaw. Fix: latch `_fireBreathLockedOut`
on `IsDepleted`, clear when `Current >= fireBreathRearmStamina` (~10% of
pool). Maintained on every peer so owner and server agree without a
NetworkVariable.

#### Cap smoothing (exhaustion ease)
`MaxFlightThrust` and `MaxClimbPitch` are not binary — both `MoveTowards`
their depletion targets at a rate of `(1 − target) / thrustCapEaseSeconds`
per second. Sharing the ease window means depletion reads as one coherent
bog-down event in the animator (Thrust + Pitch both walk through the blend
tree instead of stepping). Owner-only consumer (flight controller); remote
clients maintain their own smoothed values for free but nothing reads them.

#### Network contract
- Server runs `Update`; the per-peer block (lockout + cap smoothing) sits
  ABOVE the `IsServer` guard so every peer maintains the lockout and the
  smoothed caps from the synced `_stamina.Current`.
- Drain / regen accumulators are server-only. Flushed via `VitalManager`
  whose `_syncedValues` `NetworkList<float>` propagates to all clients.
- Zone HP regen pause (`_head.SetRegenPaused(zonesLocked)` etc.) is server-
  authoritative — `gateZoneRegen = true` ties zone recovery to the
  "grounded + idle + not firebreathing" state.

#### Gotchas
- `NetGaitSpeed` going stale at takeoff would freeze stamina regen with
  "still sprinting" — `AnimalAnimatorController.UpdateNetworkVariables`
  forces `netGaitSpeed = 0` while airborne (see §3 Gotchas).
- The per-peer Update block declares smoothing locals (`capEaseDt`); the
  server-only block below declares its own `dt = Time.deltaTime`. C# rejects
  re-declaring `dt` in a nested scope when an enclosing scope owns the
  same name (CS0136), so the smoothing block uses a different local name.
- `DragonWingActivityTracker` runs on every peer with no `IsOwner` guard
  because the server-authoritative drain reads `WingActivity` and needs a
  valid measurement on the server's animator-driven bone.

---

## 5. Horse & Mount

### Responsibility
A second animal type (the horse) plus the generic mounting system that lets a
human board a horse (and theoretically other mounts) and drive it.

### Key classes
- `HorseGroundController` — `AnimalGroundController` with `useRootMotion=true`
  and nothing else.
- `HorseSoundPlayer` — three footstep modes (`CadenceTimer`,
  `AnimationEvents`, `HoofCollision`); idle snorts (8–20 s); transition sounds
  (mount/dismount neighs).
- `HoofCollisionSoundPlayer` — `OnTriggerEnter` per hoof. Honors
  `HorseSoundPlayer.GetFootstepMode()` so it disables itself when another
  mode is active. Requires `MountableEntity.IsMoving`.
- `MountController` — on the player. `NetworkVariable<bool> netIsMounted`,
  `netIsTransitioning`. `DetectNearbyMount` (Physics-free,
  `FindObjectsByType`) within `interactionRadius`. E-hold to mount.
- `MountableEntity` — on the horse. Stores `riderId` as `clientId+1` so 0
  (host) is distinguishable from "no rider." `NetworkVariable<ulong> riderId`,
  ownership transfer to rider on mount, `ForceServerDismount` on rider death.
  `IsMoving` derived from rigidbody velocity threshold.
- `MountInputController` — polls `IsMounted`; when mounted, disables the
  player's `HumanoidController` and forwards inputs to the mount's
  `AnimalGroundController`.

### Data flow (mount)
```
Player presses E near a horse:
  MountController.Update (owner)
    → DetectNearbyMount
    → if found and held long enough: RequestMountServerRpc(mountNetObjId)
        → server: MountableEntity.SetRider(clientId+1)
                  ChangeOwnership(clientId)   // input now drives the horse
                  CompleteMountClientRpc → all clients animate sit-on-mount
  Owner now drives MountInputController
    → its inputs go into the horse's AnimalGroundController
    → ground locomotion NetworkVariables flow as normal

Death while mounted:
  DamageReceiver.HandleDeath (server)
    → MountController.IsMounted check → mount.ForceServerDismount()
        → MountableEntity transitions back to no-rider state
        → ownership returns to original (typically host)
```

### Network contract
- `netIsMounted` / `netIsTransitioning` are owner-write on the
  `MountController` so the host can see remote players boarding.
- `riderId+1` encoding: storing `clientId+1` so the default `0` value of a
  `NetworkVariable<ulong>` means "no rider," because `clientId 0` is a valid
  client (the host).
- Ownership transfer: the horse is owner-driven while mounted, server-owned
  while idle. This is why `MountableEntity` uses `IsServer`-gated logic for
  the mount/dismount transitions but lets owner-side movement happen directly.

### Extension points
- A new mountable: implement `MountableEntity` interface (the existing class
  is concrete; this would need refactoring to a base class). Today the only
  mount is the horse.
- A non-locomotion mount (e.g. siege engine): see `BallistaController`/
  `BallistaOperator`, which is structurally similar but does *not* go through
  `MountableEntity` — it's a separate stack.

### Gotchas
- `riderId+1` encoding is not a documented convention; readers expect
  `riderId == localClientId`. Always go through the helper accessor.
- `MountController.DetectNearbyMount` uses `FindObjectsByType` instead of
  `Physics.OverlapSphere`. Cheap with ~5 mounts; would scale poorly past
  dozens.
- Horse foot sounds depend on `MountableEntity.IsMoving` being true; if you
  rename or refactor that property, hooves go silent.

---

## 6. Human Locomotion

### Responsibility
First/third-person locomotion built on `CharacterController` and
`StarterAssets.ThirdPersonController`, extended by `HumanoidController` for
combat-aware behaviour (camera-relative strafe in combat mode, sprint stamina
drain, crouch-and-slide).

### Key classes
- `ThirdPersonController` — base. `LocomotionState` enum:
  `Grounded/Jumping/Falling/Hovering/Flying/Climbing`. Caches an
  `InputSnapshot` in `Update`. Movement uses `Atan2` + `SmoothDampAngle` for
  facing. Jump grace `jumpGraceTime = 0.12 s`; dodge/dodge-step suppress
  jumps mid-anim.
- `HumanoidController` — adds `CrouchAndSlide`, `SpeedLogic` with combat-mode
  override, `Walk` with strafe gated by
  `inCombat && (bowAimDraw || !sprinting)`. Strafe applies for sword/walk and
  whenever the bow is drawing or aiming; sprint in combat falls through to
  forward locomotion (regardless of weapon) so the rule system plays a
  forward run clip. `SprintStaminaDrain` via
  `combatController.ConsumeStaminaExternal`,
  `combatController.SetStaminaRegenPausedExternal`.
- `PlayerController` — trivial wiring: TPS + InputController references only.
- `InputController` — owns `InputSnapshot` (struct of all per-frame booleans
  and the move/look vectors). Handles double-tap space (0.3 s window) → evade,
  crouch toggle, hover toggle, weapon hotkeys (1/2/3), holster (H), action
  buttons. Has `ApplyHumanFromSnapshot` and `ApplyDragonFromSnapshot` to
  forward inputs into the right controller.

### Data flow
```
Owner Update:
  InputController.Update → produces InputSnapshot
    → ThirdPersonController.Update reads snapshot via InputController.Snapshot
    → HumanoidController extends/overrides movement based on combat state

Network sync (ClientAuthoritativeAnimancerSync):
  Owner Update samples Snapshot from InputController
    → 20 Hz throttle: pushes InputSnapshot into NetworkVariables
  Remote LateUpdate pulls NetworkVariables
    → reflection-writes <Snapshot>k__BackingField on the remote InputController
    → remote ThirdPersonController re-runs the same locomotion logic with the
      synced snapshot, so the CharacterController moves locally on each peer
      while ClientNetworkTransform corrects drift
```

### Network contract
- The human stack uses `ClientAuthoritativePlayerDriver` on the prefab to
  toggle `CharacterController`, input, and movement scripts per-ownership.
  Non-owners get the components disabled and rely on
  `ClientNetworkTransform` + `ClientNetworkAnimator` (both client-authoritative
  via the trivial overrides).

### Extension points
- Add a new locomotion mode: extend `LocomotionState`, add transitions in
  `ThirdPersonController`, and a NetworkVariable in
  `ClientAuthoritativeAnimancerSync` if the state must be visible to remotes.

### Gotchas
- The `InputSnapshot` autoproperty is set via reflection by the sync layer; if
  you rename `Snapshot` or change it from `{ get; private set; }` to a public
  setter, the cached `FieldInfo` lookup
  (`<Snapshot>k__BackingField`) silently breaks.
- `StarterAssetsInputs` is also referenced by the legacy `ClientPlayerMove`
  script (still in the project but largely superseded). Don't add new code
  paths to that file.

---

## 7. Human Animation (Rule-driven Animancer)

### Responsibility
Drive Animancer with declarative rules tied to inputs, weapon slots, and
combat state. Manage three Animancer layers (Base/Action/Attack), per-layer
locks, witcher-style combo chains, hit-reaction interrupts, and death freezing.

### Key classes
- `RuleAnimancerDriver` (~1310 lines, the single largest file in the human
  stack). Owns the three Animancer layers, the per-layer lock dictionary,
  the witcher attack profile + combo index + reset timer, the `OnAnimatorMove`
  delta application to `CharacterController`, the bow spine-aim IK on
  `Chest`/`UpperChest`, the rule evaluator (`Attack > Action > Base`), and the
  hit-reaction coroutine. `PlayHitReaction` cancels the current attack, fades
  Action+Attack, force-unlocks Base, plays the flinch with root motion, and
  lerps toward the attacker.
- `AnimationContext` — struct of refs (`tps`, `input`, `snapshot`,
  `mountController`, `combatController`, `weaponManager`). Key derived prop:
  `Moving` returns `mountController.CurrentMount.IsMoving` when mounted.
  Exposes `ActiveWeaponSlot`, `PendingWeaponSlot`, `Equipping`, `Holstering`,
  `ActionId`, `ActionStart`.
- `AnimationRuleSet` — contains `AnimLayer` enum (`Base=0, Action=1, Attack=2`),
  `TriggerMode`, `BoolParam`, `InputEdge`, `Direction4`, `RuleCondition` (with
  `useBool`/`useInput`/`useDirection`/`useActionId`), `AnimationRule`
  (`layer`, `priority`, `lockUntilEnd`, `maskOverride`, `animationKey`,
  `all: List<RuleCondition>`), `ComboProfile`/`ComboStep`.
- `AnimationSetBase` — `ClipEntry { key, ClipTransition, useRootMotion }` with
  dictionary lookup. `IsRootMotion(key)` returns a HashSet check.
- `CombatLocomotionMixer` — 2D Cartesian blend trees for in-combat
  locomotion. Two profile shapes:
    - `WeaponLocomotionProfile` (sword/fists/etc., one per non-bow slot):
      a single 8-direction *walk* mixer. Sprint is **not** handled here —
      it falls through to the rule system for a forward run clip.
    - `BowLocomotionProfile` (slot 2 only): two 8-direction mixers, `noAim`
      and `aim`. `SelectMixer` picks based on whether the bow is drawing or
      aiming, with a fallback to whichever variant is built.
  Dodge and dodge-step have separate cardinal-direction mixers.
  `WantsControl(activeWeaponSlot, isMoving, isDodging, isBlocking,
  isBowDrawing, isBowAiming, isMounted, isDodgeStep, isSprinting,
  useStrafeLocomotion)` short-circuits false when strafe locomotion is not
  requested or on `isSprinting` (any weapon) so sprint always falls through.
  `UpdateAndPlay(layer, moveInput, activeWeaponSlot, bowAimDraw)` smooth-damps
  the parameter and plays the selected mixer.
- `AnimationEventRelay` — relays Animation Events to subscribers
  (`HitboxEnable/Disable`, `EquipComplete/HolsterComplete`, `FootstepLeft/Right`,
  generic `OnCustomEvent` for custom keys).

### Data flow
```
RuleAnimancerDriver.LateUpdate:
  if dead → skip
  if hitReaction active → skip rule eval
  bow spine IK applied if slot==2 && (BowDrawing || BowAiming)
  evaluate rules per layer (highest priority wins) — Attack > Action > Base
  apply maskOverride if any
  set applyRootMotion from clip flag

OnAnimatorMove:
  read animator.deltaPosition / deltaRotation
  apply to CharacterController via cc.Move(...)

Witcher attack:
  TryQueueAttack(isHeavy)
    → pick combo step from WeaponProfile.combo
    → LockAttackUntilEnd(state, OnEnd: disable hitbox + fade out + DisableRootMotion)
    → SnapRotationToCamera(snapTime)
```

### Network contract
- The driver itself is *not* a `NetworkBehaviour`. Network sync of attacks,
  bow draws, equips, etc. lives in `ClientAuthoritativeAnimancerSync` (§9),
  which calls `PlayNetworkedAttack` / `PlayNetworkedEquip` /
  `PlayNetworkedBowDraw` on the remote driver. Edge events (start of an
  attack, start of bow draw) are detected on the owner and replicated via
  monotonically increasing seq counters.
- Hit/death reactions come in via `DamageReceiver` events and run locally on
  every client (the death/hit ClientRpc fires the event everywhere).

### Extension points
- New rule: edit a `AnimationRuleSet` ScriptableObject. No code changes if the
  existing `RuleCondition` building blocks suffice.
- New attack profile: add a `WeaponLocomotionProfile` for the new slot,
  reference it in `WeaponData`. Combat mixer and rules pick it up.

### Gotchas
- The driver is huge (~1310 lines). When you add behaviour, prefer extending
  the rule set or weapon profile data over adding more code paths in
  `LateUpdate`.
- The death path force-unlocks all three layers and freezes on the last
  frame; if you add a new layer, replicate that unlock or death will deadlock.
- `OnAnimatorMove` applying to `CharacterController` only when `applyRootMotion`
  is true on the current clip — if you forget to mark a clip in
  `AnimationSet`, the character slides without playing.

---

## 8. Human Combat

### Responsibility
Stamina-gated state machine for attacking, blocking, dodging, bow drawing/
firing, and dispatching hits to remote `DamageReceiver`s. Also manages weapon
slot equip/holster and hitbox lifecycles.

### Key classes
- `CombatController` — `CombatState { None, Dodging, Blocking, BowDrawing,
  BowAiming, Dead }`. Stamina-gated dodge (15 cost, 0.5 s duration, 0.25 s
  iframes). DodgeStep coroutine with camera-relative boost — gated on
  `!_input.modifiedHeld` (no dodge-step while sprinting) and on the bow not
  being equipped. Bow: `BeginBowDraw` snapshots `_bowAimStaminaAtStart`;
  `UpdateBowAim` drains per-second; `FireArrow` server-raycasts the camera
  ray to an aim point and sends `RequestFireArrowServerRpc`. Block angle
  120°, damage reduction 80%, stamina per hit. `SetRemoteCombatState` for
  puppets. `CanAttack` / `ConsumeAttackStamina` API consumed by the driver.
  `IsBowEquipped` helper checks `WeaponType.Bow` on the active weapon.
  `enableFistCombat` Inspector flag (default false): when off, unarmed
  primary press is a no-op — `HandleIdleInput` skips the fist-mode toggle
  and `CanAttack` returns false on slot 0. Used to soft-disable melee
  while client-side testing of locomotion changes is pending.
- `WeaponManager` — slots `0/1/2` (fists / primary melee / bow).
  `NetworkVariable<int> _activeSlot` server-write. `EquipState
  { Idle, Equipping, Holstering }` machine. `BeginTransition` holsters first
  if a weapon is equipped, then equips pending. `AutoCompleteTransition` via
  `animancerDriver.IsLocked` edge or `fallbackTransitionTime`.
  `OnAnimEvent_HolsterComplete` / `OnAnimEvent_EquipComplete` attach to the
  hand or holster (bow → `leftHandAttach`, others → `rightHandAttach`).
  `HandleShieldForSlot`, `InstantEquip` for ballista.
  `OnActiveSlotChanged` auto-moves visuals on puppets.
- `WeaponData` — ScriptableObject. `weaponType { None/OneHanded/TwoHanded/Bow/
  Shield }`, `weaponProfileName` links to `RuleAnimancerDriver` profile,
  `baseDamage`, `heavyDamageMultiplier`, `attackRange`, stamina costs,
  `arrowPrefab`, `arrowSpeed`, `drawTime`, `holsterLocalPos`/Rot, hitbox
  shape.
- `HitboxController` — `Physics.SphereCastNonAlloc` at offset in
  `castDirection`. `_alreadyHit` is a `HashSet<GameObject>` keyed by **root
  GameObject**, not per-collider. Skips self via `NetworkObject` identity.
  `HitInfo` struct passed to `OnHitDetected` event + `receiver.OnHitLocal`.
- `DamageReceiver` (`CharacterScripts/Scripts/Human/Combat/DamageReceiver.cs`)
  — owner detects hit (`OnHitLocal`), checks invincibility + block cone,
  calls `RequestDamageServerRpc(attackerNetId, rawDamage, hitPoint,
  wasBlocking)` with a server-side max-range gate (`maxRange = 5f` at line
  183). For projectiles: `ApplyProjectileDamage(damage, attackerPosition,
  critMultiplier)` is server-only and accumulates stagger
  (`staggerThreshold = 200f`, decay 50/s, delay 2 s). Death dismounts from
  ballista/horse, calls `RespawnController.StartCorpseTimer`, fires
  `NotifyDeathClientRpc` which disables `InputController`, `TPS`,
  `CombatController`, `AnimalGroundController`, `DragonFlightController`.
- `CombatLoadout` — primary/secondary/shield/fist + optional health/stamina
  overrides per character class.

### Data flow (melee swing)
```
Owner left-clicks (light):
  CombatController.CanAttack? + ConsumeAttackStamina
  RuleAnimancerDriver.TryQueueAttack(isHeavy=false)
    → picks combo step
    → LockAttackUntilEnd; OnEnd disables hitbox + fades out
  Animation event: HitboxEnable
    → HitboxController.Begin (cast direction + offset from WeaponData)
  Each FixedUpdate: SphereCastNonAlloc
    → for each unique root hit: receiver.OnHitLocal(HitInfo)
        → DamageReceiver.RequestDamageServerRpc
            → server range check + block path
            → vitalManager.ApplyDamage("health", finalDamage)
            → NotifyHitClientRpc → OnDamageReceived event + OnPlayHitAnimation
```

### Data flow (bow)
```
Owner holds RMB → CombatController.BeginBowDraw → CombatState=BowDrawing
  → AnimancerNetSync seq counter ticks → remote driver plays draw clip
After drawTime, state → BowAiming (visual aim, IK active)
Owner LMB → CombatController.FireArrow
  → owner raycasts camera ray to compute aim point
  → RequestFireArrowServerRpc(direction)
      → server spawns arrow NetworkObject (BallistaArrow's cousin) with shooter id
```

### Network contract
- Damage *application* is server-only. Owner produces hit candidates and asks
  the server to apply damage; server gates by max range (5 m for melee).
- Block / iframe / stamina checks happen on the **owner first** (locally
  authoritative for "did I block?") then revalidated server-side via
  `wasBlocking` flag; the server applies the reduced damage and consumes
  stamina.
- `NotifyHitClientRpc` is the single place hit reactions are broadcast — the
  damage receiver's `triggerHitAnimation` flag is the only way to suppress
  the flinch (used for projectile hits below the stagger threshold).

### Extension points
- New weapon: `WeaponData` SO + add a `WeaponLocomotionProfile` if you want
  unique strafe.
- New combat state: extend `CombatState`, add transitions, add a sync
  pathway in `ClientAuthoritativeAnimancerSync` if remotes need to see it.
- New hit zone: drop a `CritZoneMarker` on a child collider with a multiplier.

### Gotchas
- `HitboxController._alreadyHit` is keyed by *root GameObject*, so two hits
  to different colliders on the same character count as one. Intentional —
  prevents multi-collider double damage — but confusing if you split a
  rig and expect two hits.
- `DamageReceiver.RequestDamageServerRpc` enforces a hardcoded `maxRange = 5f`.
  Two-handed greatswords and any reach > 5 m will be silently rejected.
- `WeaponManager.AutoCompleteTransition`'s fallback timer is the only
  failsafe if an equip animation lacks the `EquipComplete` event; without it
  the player would lock indefinitely.

---

## 9. Network Synchronization Layer

### Responsibility
Everything that turns "owner inputs and intent" into "every other client sees
the same character moving and animating correctly" — without writing per-frame
animator parameters across the wire.

### Key classes
- `ClientAuthoritativePlayerDriver`
  (`Multiplayer/Scripts/NetworkPlayerMovement/New/`) — toggles `CharacterController`,
  `InputController`, `PlayerController`, `CombatController`, `HumanoidController`,
  `ThirdPersonController`, and any `ownerOnlyObjects` based on `IsOwner` in
  `OnNetworkSpawn`. The first line of defense against "remote characters
  fighting their own simulation."
- `ClientAuthoritativeAnimancerSync` (file: `AnimanerNetSync.cs`) — 20 Hz
  owner-write `NetworkVariable`s mirroring the InputSnapshot bools and
  derived flags; also exposes `RemoteAimPitch` for spine IK
  (`nvAimPitch`, 0.5° threshold). Reflection-cached
  `<Snapshot>k__BackingField` `FieldInfo` writes the snapshot into the
  remote `InputController` every receive. Edge-triggered seq counters:
  `nvAttackSeq`, `nvJumpSeq`, `nvBowDrawSeq`, `nvBowReleaseSeq`,
  `nvEquipSeq`, `nvActionSeq` (sampled every Update so a one-frame edge
  is never lost). On change, calls
  `RuleAnimancerDriver.PlayNetworkedAttack/Equip/BowDraw/etc.`.
  `NetworkedAttackState` struct: `(FixedString64Bytes attackKey,
  byte mode, bool isHeavy, int comboIndex)`.
- `DragonAnimatorController` — the dragon's parallel sync layer. NOT shared
  with the human stack; uses raw NetworkVariables on the animator parameters
  (the dragon is on Mecanim, not Animancer).
- `ClientNetworkTransform` / `ClientNetworkAnimator` — three-line overrides
  of NGO `NetworkTransform`/`NetworkAnimator` that flip
  `OnIsServerAuthoritative()` to `false`. Power the drift correction for
  human stacks.
- `OwnerOnlyFreeLook` (file: `OnwerOnlyCameraRig.cs`) — owner-only
  Cinemachine vcam priority + sensitivity (`PlayerPrefs`-backed `Gain_X`),
  FOV cycle on `V`. Owner-only `ownerOnlyObjects` toggle on
  `OnNetworkSpawn` / `OnGainedOwnership` / `OnLostOwnership`.

### Data flow (a single frame)
```
Owner Update:
  InputController.Update      → InputSnapshot
  CombatController.Update     → CombatState transitions
  WeaponManager.Update        → equip transitions
  AnimancerNetSync.Update:
    if Time.time - _lastSent > 0.05f:
      sample InputSnapshot, aimPitch, moveInput → write NetworkVariables
    sample edges (attack/jump/bow draw/release/equip/action) → bump seq
    write CombatState, ActiveSlot, EquipState

Remote receives NetworkVariable change:
  nvSnapshot.OnValueChanged → reflection-write Snapshot into InputController
  nvAimPitch.OnValueChanged → driver consumes for spine IK
  nvAttackSeq.OnValueChanged → if increased, call PlayNetworkedAttack(state)
  nvCombatState → SetRemoteCombatState
  nvActiveSlot → WeaponManager moves visuals to correct attach point
  nvMoveInput → CombatLocomotionMixer.SetRemoteParameter

Remote LateUpdate:
  RuleAnimancerDriver re-evaluates rules with synced snapshot/state
  CombatLocomotionMixer plays the right strafe blend
  ClientNetworkTransform corrects positional drift
```

### Network contract

| Channel              | Direction           | Frequency           |
|----------------------|---------------------|---------------------|
| InputSnapshot bools  | Owner → all         | 20 Hz throttled     |
| AimPitch             | Owner → all         | 20 Hz, 0.5° eps     |
| Move input           | Owner → all         | 20 Hz               |
| Attack/Jump/etc seq  | Owner → all         | every frame (so a one-frame edge is always sent within the throttle window) |
| ActiveSlot           | Server → all        | on transition       |
| Combat state         | Owner → all         | on transition       |
| Hit / death          | Server → all        | ClientRpc           |
| Position drift fix   | Owner → all         | ClientNetworkTransform internal |

### Extension points
- New per-frame state (e.g. a new locomotion mode): add a `NetworkVariable`
  to `AnimancerNetSync`, write on the owner under the throttle, react on
  remotes in an `OnValueChanged` callback that calls into the driver.
- New edge event: pick a new `nvFooSeq` `int` and bump it from the owner;
  detect change on remotes.

### Gotchas
- Re-implements `NETWORK_UPDATE_INTERVAL = 0.05f` and `FLOAT_EPSILON = 0.01f`
  in every sync class instead of sharing a constant. Changing one without the
  others creates phase mismatches.
- The reflection lookup of `<Snapshot>k__BackingField` is fragile: rename
  `Snapshot`, change its setter visibility, or compile with a different
  C# auto-property convention and the puppet stops receiving inputs
  silently.
- The dragon does **not** flow through `AnimancerNetSync` — it's on its own
  `NetworkVariable` mirror in `DragonAnimatorController`. Keep the two
  patterns separate; merging them prematurely will lose either type safety
  (Animator parameters) or extensibility (rule-based animancer).

---

## 10. Vitals & Status Effects

### Responsibility
Single source of truth for HP / stamina / future resources. Server-side regen,
client-side smoothed UI, depletion → death event. Status effects (burn) ride
on top with their own NetworkVariables and ticking damage.

### Key classes
- `VitalManager` — `NetworkList<float> _syncedValues` for server→client.
  Server ticks regen in `Update`, syncs on change. `ApplyDamage`,
  `RestoreVital`, `TryConsumeStamina` are server-only. `ResetAllVitals` on
  respawn. Events: `OnVitalChanged`, `OnVitalDepleted`, `OnDeath`.
  `killOnDepleted` (from `VitalDefinition`) triggers `OnDeath`.
- `Vital` — plain C# class. `Consume` sets regen cooldown, `Restore` clamps
  to `Max`, `TickRegen` gated by `regenEnabled` / `regenPaused` /
  `regenOnlyWhenGrounded`.
- `VitalDefinition` — ScriptableObject. `regenRate`, `regenDelay`,
  `regenOnlyWhenGrounded`, `clampAtZero`, `killOnDepleted`.
- `BurnStatus` — `NetworkVariable<bool> netIsBurning` and
  `NetworkVariable<float> netBurnTimeRemaining`, both server-write.
  `Ignite(time)` adds to remaining (capped at `maxBurnDuration = 10 s`),
  with self-immunity by NetworkObject identity. `HandleDeath` sets
  `_refreshLocked` so the current burn ticks out but no new ignites land.
  `ApplyTick` uses `damageReceiver.ApplyProjectileDamage` so the projectile
  hit-anim path is reused. `OnBurningChanged` spawns/despawns
  `fireVFXPrefab` on the pelvis bone.
- `GroundFirePool` — singleton, `poolSize = 64`. `SpawnAt(position, normal,
  config, sourceRef)` computes rotation from surface normal (works on
  ground/wall/ceiling). Uses a `LinkedList<GroundFirePatch>` for active
  + `Queue<GroundFirePatch>` for idle; oldest recycled when exhausted.
  Config struct: `GroundFirePatchConfig { lifetime, fadeDuration,
  damagePerTick, tickInterval, burnTimeOnContact }`.
- `GroundFirePatch` — `MonoBehaviour` (deliberately not a
  `NetworkBehaviour`). Same instance lives on host + every client; the
  server-side instance is gated by runtime `IsServer` flag inside
  `OnTriggerStay`. Decal projector fades during the last `fadeDuration`
  of `lifetime`. `OnTriggerStay` calls `BurnStatus.Ignite` with
  `burnTimeOnContact`.
- `CritZoneMarker` — drop on any collider; carries `damageMultiplier` and
  `zoneName` for projectile / future per-zone logic.

### Data flow
```
Server tick (VitalManager.Update):
  for each vital: TickRegen(deltaTime) if not paused
  if value changed beyond eps → write into NetworkList[index]
  client UI receives NetworkList change → smooths the bar

Damage:
  attacker → DamageReceiver.RequestDamageServerRpc OR ApplyProjectileDamage
    → vitalManager.ApplyDamage("health", final)
        → if newValue == 0 and killOnDepleted → OnDeath
            → DamageReceiver.HandleDeath → NotifyDeathClientRpc

Burn ignition:
  GroundFirePatch.OnTriggerStay (server) → target.BurnStatus.Ignite(burnTime)
    → netBurnTimeRemaining += time (clamped)
  Server tick: ApplyTick every tickInterval → ApplyProjectileDamage → vital
```

### Network contract
- All HP changes flow through `VitalManager` on the server. The
  `NetworkList<float>` index order is implicitly the order of vitals on the
  prefab — adding a vital is order-sensitive across versions.
- Burn is server-driven (IsServer-gated `Ignite` body); clients only see
  `netIsBurning` flip to enable the VFX.

### Extension points
- New status effect: copy `BurnStatus` shape (one bool + one float for
  duration, `Ignite` API, server-side tick). Hook to `VitalManager` via
  `ApplyDamage` or its own ticking.
- New crit zone behaviour: read `CritZoneMarker.ZoneName` in the projectile
  to route per-zone logic.

### Gotchas
- `NetworkList<float>` order matters — callers index by position
  (`_syncedValues[index]`), so reordering breaks clients silently.
- `BurnStatus._refreshLocked` after death lets the current burn finish but
  blocks new ignites; this is intentional for visual completeness but means
  a corpse can keep ticking burn damage on… nothing.
- `GroundFirePatch` is `MonoBehaviour`, not networked — every client must
  spawn it locally from the same RPC, and your local pool size must be at
  least as large as the worst-case visible patches or you'll see flicker
  from premature recycling.

---

## 11. Ballista

### Responsibility
A mountable siege weapon that the player operates with camera-driven yaw/pitch
and fires arrows that deal damage on impact (with crit-zone support). Parallel
to the mount system but uses its own component stack.

### Key classes
- `BallistaController` — `NetworkVariable<float> _netYaw`, `_netPitch`
  (owner-write), `NetworkVariable<ulong> _operatorId` (server-write),
  `NetworkVariable<bool> _isReloading`. `RequestMountServerRpc` changes
  ownership to the requesting client → `CompleteMountClientRpc`.
  `HandleRotationInput` follows camera yaw/pitch clamped. `RequestFireServerRpc`
  spawns the arrow + starts reload. `ForceDismount` for death.
- `BallistaOperator` — on the player. `CheckForBallista` is `Physics`-free
  (`FindObjectsByType` within `interactionRadius`). `CompleteMountClientRpc`
  disables TPS/combat, sets the vcam tracking to the ballista cube, and
  `InstantEquip(0)` to holster. `OnAnimatorIK` does spine head-look + hand
  grips on the ballista handles.
- `BallistaArrow` (`CharacterScripts/Scripts/Ballista/BallistaArrow.cs`) —
  `NetworkBehaviour`. Server-only `OnTriggerEnter`. Skips shooter by
  `NetworkObjectId`. On hit: disables collider, kinematic-locks rb, calls
  `damageReceiver.ApplyProjectileDamage(damage, position, critMultiplier)`
  with `CritZoneMarker.DamageMultiplier`, sticks via `NetworkObject.TrySetParent`,
  fans `StickArrowClientRpc` to freeze the rb on every client. Despawns
  after `stickDuration = 5 s`.
- `BallistaSoundPlayer` — Fire / Reload / Rotation creak sounds.
  Reload sound on `IsReloading` rising edge. Creak when rotation speed
  > `creakThreshold = 10 deg/s` and the ballista is occupied.

### Data flow
```
Player presses E near ballista:
  BallistaOperator.CheckForBallista finds nearest within interactionRadius
  → BallistaController.RequestMountServerRpc(playerNetId)
      → server changes ownership to client → operatorId=clientId
      → CompleteMountClientRpc → operator switches camera, holsters weapons

Player aims:
  BallistaController.Update (owner)
    → reads camera yaw/pitch → clamps → writes _netYaw / _netPitch

Player fires:
  BallistaController.RequestFireServerRpc
    → spawns BallistaArrow, sets shooter NetworkObjectId, _isReloading=true
    → reload coroutine sets _isReloading=false after delay

Arrow hits:
  BallistaArrow.OnTriggerEnter (server only)
    → ignore self/other arrows
    → damageReceiver.ApplyProjectileDamage with critMultiplier from CritZoneMarker
    → StickArrowClientRpc broadcasts the impact pose
    → TrySetParent(hitNetObj) so the arrow rides the target
    → Invoke(DespawnArrow, stickDuration)

Operator dies:
  DamageReceiver.HandleDeath
    → BallistaOperator.IsOperating ? BallistaController.ForceDismount(clientId)
```

### Network contract
- Yaw/pitch are owner-write so the rig animates smoothly on the operator's
  client; `_operatorId` is server-write so unmounting is authoritative.
- Arrow lifecycle is server-spawned, server-despawned. Stick pose is
  fan-out via ClientRpc; the server's `TrySetParent` replicates on its own.
- Self-skip is by `NetworkObjectId` (not `OwnerClientId`) so host- and
  client-fired arrows both correctly skip their shooter even when the host
  operates.

### Extension points
- A second siege weapon: copy the `Controller`/`Operator`/`Arrow` triplet.
  No shared base today.
- Per-target arrow behaviours (sticking into props, exploding on water):
  branch in `BallistaArrow.OnTriggerEnter`.

### Gotchas
- `BallistaArrow.OnTriggerEnter` runs on `IsServer` only; if you misconfigure
  the prefab to despawn early on a client, the impact never registers.
- Self-skip uses `NetworkObjectId` — if you spawn the arrow before calling
  `SetShooter`, the comparison against `ulong.MaxValue` lets the arrow
  immediately self-hit. Always call `SetShooter()` before `Spawn()`.
- `BallistaOperator.CheckForBallista` is `FindObjectsByType` per frame; cheap
  with one ballista, would scale poorly past dozens.

---

## 12. Camera & Owner Visibility

### Responsibility
Show the right Cinemachine vcam on the right machine, apply the player's
sensitivity and FOV preferences, hand off cameras when mounting/operating
ballista, and keep dragon vs human cameras separated.

### Key classes
- `OwnerOnlyFreeLook` (file `OnwerOnlyCameraRig.cs`) — see §9. The generic
  vcam owner-gate. Extra: `KeyCode.V` cycles `_fovSteps = { 30, 40, 50 }`.
- `VCam` — `CinemachineFreeLook`. Secondary attack swaps to a ZoomLook with
  alternate `VCameraObject` rig orbits. `OnEnable` reads `Gain_X` PlayerPrefs.
- `VCameraObject` — ScriptableObject describing `VerticalFOV` plus
  `TopRig`/`MiddleRig`/`BottomRig` `Height` and `Radius`.
- `DragonCinemachineModeSwithcer` — owner-only camera priority swap when
  flight engages. Same general pattern as `OwnerOnlyFreeLook`.
- `ClientPlayerMove` (legacy) — older owner-gate that toggled
  `PlayerInput`/`StarterAssetsInputs`/`ThirdPersonController`/`vcam.Priority`
  itself. Superseded by `ClientAuthoritativePlayerDriver` but still on
  some prefabs; do not extend.

### Network contract
- All camera classes are `NetworkBehaviour`s but only react to
  `IsOwner`/`OnGainedOwnership`/`OnLostOwnership`. They never write
  `NetworkVariable`s; the camera is local-only.

### Gotchas
- `ClientPlayerMove` and `ClientAuthoritativePlayerDriver` both gate input
  on ownership. If both are on the same prefab they can fight; only one
  should be enabled per character.
- Sensitivity is stored under the `Gain_X` key by `OwnerOnlyFreeLook`; if the
  settings menu writes a different key, sensitivity won't apply.

---

## 13. Proximity Sound

### Responsibility
A categorical-ranged 3D sound system that plays footsteps, weapon impacts,
combat vocals, ambient zones, and anything else through a pooled
`AudioSource` ring with mixer routing. Server-aware so other players hear
events without duplicating logic per actor.

### Key classes
- `SoundDatabase` — top-level `SoundEntry` (NOT nested), `SoundCategory
  { Quiet, Combat, Loud, Global, Ambient }`. Per-category `min/max`
  distances + rolloff, overridable per sound. `SurfaceFootstepSet` lookup
  pattern: `"CreatureType_SurfaceType"` →
  `"CreatureType_Default_footstep"` → `"SurfaceType"` →
  `"Default_footstep"`. `ImpactSoundEntry` matrix keyed by
  `"attackerMaterial|targetMaterial"` with `"Default|Default"` fallback.
- `ProximitySoundManager` — singleton, `DontDestroyOnLoad`. Pool of
  **32 always-active** `AudioSource`s so the mixer group sticks (prevents
  pop). `PlaySound` server/client paths, `PlayFootstepClientRpc` with
  `targetClientIds` excluding the sender (so an actor doesn't hear its own
  footstep twice). `PlayImpact` is categorical. `FadeOutAndReturn` coroutine
  over `clipFadeOutDuration` to prevent end-of-clip pop. `GetMixerGroup`
  maps category to `AudioMixerGroup`.
- `AnimalSoundPlayer` — server-only idle timer; `PlayAttack`/`Hit`/`Death`/
  `Sleep` API into `PlaySound`.
- `FootstepSoundPlayer` — `FootstepMode { CadenceTimer, AnimationEvents,
  Both }`. `driveFromServer` for AI. `SURFACE_CACHE_DURATION = 0.2s`.
  `creatureType` field for `"Bear_Snow"` style lookups via
  `TerrainSurfaceDetector`.
- `AmbientSoundZone` — trigger volume, *local only* play.
  `IsLocalPlayerOrMount` check: `ClientPlayerMove.IsOwner` OR
  `MountableEntity.IsMounted` and `RiderId == LocalClientId`.
- `HorseSoundPlayer` — three footstep modes (`CadenceTimer`,
  `AnimationEvents`, `HoofCollision`). FixedUpdate samples speed +
  smooths. Idle snort 8–20 s. Mount/dismount transitions server-driven
  (neigh + mount sound). `HorseFootStep` animation-event entry with
  dedup window.
- `CombatSoundPlayer` — subscribes to `DamageReceiver` (owner + remote),
  `CombatController` (owner only), `WeaponManager` (owner only with
  equip/unequip sounds), `HitboxController.OnHitboxEnabled` (swing start)
  + `OnHitDetected` (impact). Impact material resolved from the attacker's
  weapon `MaterialTag` or `weaponType → { Metal, Wood }`, target from
  `SurfaceTag` or `MaterialTag` or `"Flesh"` fallback.
  `IMPACT_DEDUP_WINDOW = 0.1 s`.
- `MaterialTag` / `SurfaceTag` — string tags consumed by `CombatSoundPlayer`
  + `TerrainSurfaceDetector` to look into the database.
- `TerrainSurfaceDetector` — static utility. Raycasts down 0.5 m above
  position, reads terrain splat map for the dominant layer, falls back to
  `SurfaceTag` on non-terrain colliders. Default surface from the database.
- `MinMaxRangeAttribute` — custom Inspector attribute for `Vector2`
  `[MinMaxRange(min,max)]` ranges.
- `VoiceChatStub` — placeholder API (`JoinChannel`/`LeaveChannel`/
  `SetProximityPosition`/`SetMuted`/`ToggleMute`/`SetVoiceVolume`) that
  logs only. Same shape Vivox/Agora will use; replace bodies later.

### Data flow
```
A footstep:
  Animation event (or cadence timer) on actor → FootstepSoundPlayer.PlayFootstep
    → owner: TerrainSurfaceDetector.GetSurfaceType(pos, db)
              → "Stone"
    → ProximitySoundManager.PlaySound("Bear_Stone_footstep", pos)
        if owner → also PlayFootstepClientRpc(targetClientIds=others)
        if remote → already heard via the rpc, skip

A melee hit:
  HitboxController.OnHitDetected → CombatSoundPlayer
    → resolve attacker MaterialTag (e.g. "Metal")
    → resolve target SurfaceTag (e.g. "Flesh")
    → ProximitySoundManager.PlayImpact("Metal", "Flesh", hitPoint)
        → SoundDatabase.GetImpactEntry("Metal|Flesh") or fallback
        → play through Combat mixer at impact volume
```

### Network contract
- `ProximitySoundManager.PlaySound` decides server vs client at call-site;
  the server fans out via ClientRpc with target exclusion so the originator
  doesn't double-hear.
- Ambient sound is *not* networked — it's a local effect for whoever is in
  the trigger volume.

### Extension points
- New sound: drop a `SoundEntry` into `SoundDatabase`, call by string key.
- New surface or material: add a string in the database matrix; add the
  `SurfaceTag`/`MaterialTag` to the relevant prefabs.
- Voice chat integration: keep `VoiceChatStub` API stable, swap the bodies
  for Vivox/Agora calls.

### Gotchas
- The 32-source pool is always active so the mixer group stays attached. If
  your scene has many simultaneous loud events you'll evict mid-clip.
  `FadeOutAndReturn` exists to soften this.
- String-keyed SoundDatabase has no compile-time check — typos surface only
  at runtime. Consider an editor warning if you grow this past ~50 entries.
- `AmbientSoundZone.IsLocalPlayerOrMount` knows about `ClientPlayerMove`
  (legacy), not the newer `ClientAuthoritativePlayerDriver`. Mounted humans
  on the new stack might bypass the ambient path.

---

## 14. Respawn & Death UI

### Responsibility
Show the player they died, offer spawn-point selection, request a server-side
respawn, and bring the player back online cleanly (vitals reset, burn cleared,
animator unfrozen, position teleported).

### Key classes
- `RespawnController` — server-side respawn entry point.
  `RequestRespawnServerRpc(spawnPointIndex)` resets vitals + burn, computes
  the spawn position, fires `NotifyRespawnClientRpc(pos, rot)`. The client
  RPC disables `CharacterController`/`Rigidbody`, teleports, re-enables,
  calls `animancerDriver.PlayRespawn`, calls
  `dragonDamageAnimator.ResetDeathState(yaw)`, hides `LoadingScreen`.
  Also `StartCorpseTimer` — if the player doesn't pick a spawn within N
  seconds, auto-respawn.
- `DeathScreen` — `Show(NetworkObject)` populates buttons from
  `SpawnPoint.GetAllSpawnPoints()` and listens for clicks → calls
  `respawnController.RequestRespawnServerRpc(index)`. Also calls
  `LoadingScreen.Instance.Show("Respawning…")`.
- `LoadingScreen` — global singleton; `Show("…")`/`Hide()`. Subscribes to
  NGO `SceneManager.OnLoadEventCompleted` to auto-hide after a scene swap.
- `PlayerHUD` — auto-creates Canvas + health/stamina bars; owner-only
  enable; smoothed fill.
- `WorldHealthBar` — world-space billboard for non-owner.
  `isPlayer` + `IsOwner` disables (you don't see your own).
  `showAfterDamageTime` grace before becoming visible.
- `TrainingDummy` — `autoReset` `respawnDelay` timer after death; subscribes
  to `VitalManager.OnDeath`.
- `PauseMenu` — `Resume` is called by `DeathScreen.Hide` so the cursor lock
  state is correct after a respawn.

### Data flow
```
On death:
  DamageReceiver.HandleDeath (server)
    → dismount/ballista cleanup
    → NotifyDeathClientRpc → disable controllers + OnPlayDeathAnimation +
                              DeathScreen.Show (owner only)
    → if isPlayer && IsServer → respawnController.StartCorpseTimer
  Owner: DeathScreen.Show
    → cursor unlock + spawn buttons populated
On respawn click:
  DeathScreen.OnSpawnPointSelected
    → LoadingScreen.Show("Respawning…")
    → DeathScreen.Hide → cursor lock
    → respawnController.RequestRespawnServerRpc(index)
        → server: ResetAllVitals + Burn clear
                  NotifyRespawnClientRpc(pos, rot)
                    → cc/rb disable → teleport → enable → driver.PlayRespawn
                    → LoadingScreen.Hide
```

### Network contract
- Death notification is a server-issued ClientRpc; respawn is a
  client-requested ServerRpc.
- The respawn ClientRpc disables `CharacterController` *before* moving, then
  re-enables — this avoids `CharacterController` snapping back to the
  pre-teleport position via internal collision.

### Extension points
- Add a new spawn rule: implement `SpawnPoint`'s static helpers, or extend
  `RespawnController.GetNextSpawnPosition` (currently in
  `CharacterSpawnHandler`'s sibling logic).
- Add a respawn cost (e.g. lose XP): hook into `RequestRespawnServerRpc`
  before the vital reset.

### Gotchas
- `DeathScreen.Show(NetworkObject)` requires the *owner's* NetworkObject;
  if you call it from the server you'll show the wrong UI on the wrong
  client.
- `LoadingScreen` is a `MonoBehaviour` singleton — there's no DontDestroy
  enforcement, so a scene that rebuilds the menu must re-find the
  instance.

---

## 15. Cross-cutting patterns

These are patterns that recur in 3+ subsystems. Recognising them when reading
new code saves time.

### Owner-write 20 Hz sync with epsilon
Every per-frame state that needs to reach remotes uses a NetworkVariable with
`NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner`,
written behind a `_lastSent + 0.05f` throttle, with floats gated by
`Mathf.Abs(net.Value - new) > 0.01f` and an explicit zero-snap. Re-implemented
in `AnimalAnimatorController`, `DragonAnimatorController`,
`AnimancerNetSync`, `BallistaController`, `MountController`, `BurnStatus`.

### Edge events via monotonic seq counters
Events that cannot be inferred from a flag (because the flag could flip back
before the network tick) use `NetworkVariable<int> nvFooSeq`. Owner increments
on the edge; remotes detect the change and react. Used by attack, jump, bow
draw/release, equip, action interaction. Implemented in `AnimancerNetSync`,
mirrored by `AnimalGroundController` (jump ServerRpc) for cross-client SFX.

### Late-join self-healing in `OnNetworkSpawn`
Whenever the Animator can be in a non-default state at spawn time (flight,
swimming, mounted, dying), `OnNetworkSpawn` checks the relevant
`NetworkVariable.Value` on remotes and calls `animator.Play("StateName")` to
force-jump into that state. Examples: `DragonAnimatorController` (BlendFly /
SwimmingLocomotion), `MountController` (mounted pose), and you should follow
the same pattern when adding new persistent states.

### Server-authoritative damage with owner intent
Owners detect candidate hits (raycasts, sphere casts, cone tests) and *request*
damage via a ServerRpc. Server gates by sanity (max range), looks up vitals,
applies, then fans out a ClientRpc for visual reactions. Used by
`DamageReceiver.RequestDamageServerRpc`, `BallistaArrow.OnTriggerEnter` (server
only, no owner stage), and `BurnStatus.ApplyTick` (server only, source already
trusted).

### Pooled local FX with `NetworkObjectReference` for source identity
Visual effects that exist on every client (ground fire patches, future decals
and impact VFX) are spawned from local pools keyed off a ClientRpc carrying a
`NetworkObjectReference` for the source actor. The reference resolves to a
`NetworkObject` on each client without sending the whole object, enabling
self-immunity checks (BurnStatus self-skip).

### ScriptableObject for tunables, NetworkVariable for state
Damage numbers, weapon stats, vital regen, sound clip mappings, character
prefabs are all SOs. Per-instance state (HP value, equip state, mount status)
is on NetworkVariables. Avoid the temptation to push tuning values into
NetworkVariables; they cost bandwidth and version-pin runtime to data.

---

## 16. Strengths

1. **Clear ownership model.** The owner-write / server-write split is
   consistent across the codebase. Once you internalise it, you can predict
   where to put a new bit of state without reading more than one file.
2. **Well-isolated subsystems on the dragon.** Locomotion, combat, damage
   feedback, sound, and animator sync are all separate `MonoBehaviour`s on
   the same prefab. They reference each other directly (no events) but each
   has a clear job.
3. **Animation as data, not code.** `AnimationRuleSet` + `WeaponData` +
   `AnimationSet` ScriptableObjects mean a designer can add a new attack
   without touching `RuleAnimancerDriver`. Combined with the witcher combo
   profile, this is the most extensible part of the project.
4. **Late-join handling is genuinely thought-through.** Every long-lived state
   has an `OnNetworkSpawn` puppet-fix path. This is the kind of thing
   prototypes routinely skip.
5. **Pooled FX with `NetworkObjectReference`.** Avoids spawning dozens of
   `NetworkObject`s for transient visuals while preserving source identity
   for game logic (self-immunity, attribution).

---

## 17. Weaknesses & risks

1. **Hardcoded melee max range = 5 m server-side.**
   `DamageReceiver.RequestDamageServerRpc` rejects any hit further than `5f`
   (`DamageReceiver.cs:183`). Two-handed weapons or any reach > 5 m fail
   silently. There is no per-weapon `serverHitMaxRange` and no log when the
   reject fires.
2. **Misspelled file/symbol names embedded in scene/prefab references.**
   `ApplicaitonController.cs`, `OnwerOnlyCameraRig.cs`, `AnimanerNetSync.cs`,
   `DontDestoryPlayer`, `SignInANonymouslyAsynyc`, `DragonCinemachineModeSwithcer`.
   Renaming any one will break references; until they are renamed
   (and the meta files migrated) every reader has to learn the typos.
3. **Reflection coupling between sync and InputController.**
   `AnimancerNetSync` writes the snapshot via a cached `FieldInfo` for
   `<Snapshot>k__BackingField`. Renaming the property, changing setter
   visibility, or compiling under a different C# auto-property scheme will
   silently break remote puppeting with no compile error.
4. **Two parallel character stacks share almost nothing.** Human and dragon
   each implement their own `*AnimatorController` sync, their own combat
   stack, their own damage feedback, their own input/snapshot wiring. Bug
   fixes routinely have to be made in both places (or one place quietly).
   Consolidating them is a Phase-1 task per `ToDo.md`.
5. **`NETWORK_UPDATE_INTERVAL` and `FLOAT_EPSILON` are duplicated.**
   Re-implemented as private constants in `AnimalAnimatorController`,
   `DragonAnimatorController` (line 71), `AnimancerNetSync`,
   `MountController`, `BurnStatus`, `BallistaController`. Tuning one without
   the others creates phase mismatches.
6. **`HitboxController._alreadyHit` keys by root GameObject.** Intentional, but
   if you split a character into multiple roots (e.g. ragdoll-on-death) the
   dedup breaks. Subtle and easy to regress.
7. **Damage-applied vs. visual flinch coupling lives in the `triggerHitAnimation`
   bool.** Default `true`; only `BurnStatus` and below-threshold projectiles
   pass `false`. Adding a new "silent damage" path is one boolean argument
   away from being forgotten.
8. **`CharacterSelectManager.serverSelections` is `static`.** Survives scene
   reloads (intentional) but also survives host-stop / host-start in the
   same editor session if the domain isn't reloaded. The `OnNetworkSpawn`
   clear protects only the host path, not late-joining LAN clients.
9. **`VersionChecker` fails closed silently.** Remote Config outage =
   nobody can host. Acceptable in production, painful in dev. There's no
   "force allow" override.

---

## 18. Open questions

1. **Damage attribution & friendly fire.** Does
   `ApplyProjectileDamage(damage, attackerPosition, critMultiplier)` need an
   `attackerNetObjId` so we can compute teams / lock out friendly fire?
   Currently the attacker is implicit (just a position) and a cooperative
   dragon could DOT a teammate forever via `BurnStatus`.
2. **Weapon hit ranges are inconsistent.** `WeaponData.attackRange` exists but
   `DamageReceiver.RequestDamageServerRpc` enforces a hardcoded 5 m server
   gate. Should the server gate read `WeaponData.attackRange + slack`?
3. **Static `serverSelections` lifecycle.** Should this move to a non-static
   field on `CharacterSelectManager` and rebuild from `NetworkList` on
   server start? Today it's a hidden cross-scene global.
4. **Where does the dragon's `IsDead` flag live authoritatively?** Today it's
   an Animator parameter (`isDeadHash`) checked via `animator.GetBool`. It
   syncs because the death ClientRpc transitions the state on every client,
   but it's not a NetworkVariable. A late-joining client whose dragon died
   *before* the join sees the corpse but `animator.GetBool(isDeadHash)`
   may be false until the next state evaluation. Worth verifying.
5. **The legacy `ClientPlayerMove` script.** Still in the project, still on
   some prefabs, but superseded by `ClientAuthoritativePlayerDriver`. Plan
   to remove? Otherwise a future contributor will copy from the wrong one.
6. **Voice chat integration timeline.** `VoiceChatStub` is a placeholder.
   Pinning a target SDK (Vivox, Agora, custom) early would shape the API.
7. **Mount system as a base class.** `MountableEntity` is concrete; only the
   horse uses it. Adding boats, dragons-as-mounts, or wagons would need
   either a refactor to a base class or a copy-paste of the entire stack.
8. **Sound database key explosion.** `SurfaceFootstepSet` already does
   four-level fallback. Past ~50 entries the string-keyed map will be the
   weakest point in maintenance. Editor lint?
9. **Crit zones: what carries the per-zone behaviour?** `CritZoneMarker.ZoneName`
   is exposed but unused. The TODO is implicit; explicit hooks (e.g.
   `ICritZoneEffect` component) would let zones do more than scale damage.

---

## 19. Further reading

- `Assets/FEFE/CLAUDE.md` — operating doc for AI agents (paths, conventions,
  working principles, gotchas summary).
- `Assets/FEFE/design/ToDo.md` — long-running session log of decisions, branches,
  and iteration notes. Useful for "why is this here" archaeology.
- `Assets/FEFE/CharacterScripts/Scripts/Animal/Dragon/DragonAnimatorController.cs`
  — the canonical "how do we sync a Mecanim animal across the network"
  reference.
- `Assets/FEFE/Multiplayer/Scripts/NetworkPlayerMovement/New/AnimanerNetSync.cs`
  — the canonical "how do we sync a rule-driven Animancer human across the
  network" reference.
- `Assets/FEFE/CharacterScripts/Scripts/Human/Combat/DamageReceiver.cs` —
  the canonical damage entry point. Read before changing anything in combat.

---

## 20. NPC AI

### Responsibility
Server-authoritative AI for non-player characters — currently a single shipping
implementation (`BearAI`, used as the zombie AI) and a planned, more complex
orc AI (see `design/OrcAI.md`).

### Key classes (existing)

- `BearAI` (`CharacterScripts/Scripts/Bear/BearAI.cs`) — complete NPC AI used
  for zombies. ~790 lines, `NetworkBehaviour`, `[RequireComponent(NavMeshAgent,
  NetworkObject)]`. Six-state flat enum FSM:
  `Idle → Wander → Chase → Attack → Return → Dead`. AI logic gated on
  `if (!IsServer) return;`.
- `EnemySurroundCoordinator` (`CharacterScripts/Scripts/Bear/`) — **stub**.
  File contents are a comment marker noting the class was replaced by the
  simpler crowd-wait approach inside `BearAI`. Safe to delete. Kept here so
  it isn't rediscovered as live code.

### How BearAI is wired

**Path planning vs locomotion are split.** This is the canonical pattern for
NPCs that need both pathfinding and root-motion-driven animation:

```csharp
// OnNetworkSpawn:
agent.updatePosition = false;
agent.updateRotation = false;            // agent plans only — does NOT move the transform
agent.avoidancePriority = Random.Range(20, 80);  // spread RVO priorities

// OnAnimatorMove (server only):
Vector3 rootPosition = animator.rootPosition;   // root motion drives position
// + manual gravity raycast (groundCheckDistance + 0.5f)
// + separation nudge (OverlapSphereNonAlloc on neighbours)
agent.nextPosition = rootPosition;       // tell agent where the body is
transform.position = rootPosition;       // apply
```

The Animator drives motion; NavMeshAgent only plans paths. The body's
authoritative position is fed back into the agent via `nextPosition` so the
next path query is correct. This keeps animations clean at the cost of
manual gravity / separation.

**Crowd control is a server-side static dictionary.** `BearAI._attackerCounts`
is `static Dictionary<Transform, int>` — process-wide, not per-NPC. Each
attacker registers when entering Attack state and unregisters on death /
target-loss. `IsCrowded()` blocks extras (default `maxAttackersPerTarget = 4`),
`FindLessContestedTarget()` retargets when overwhelmed. The static-ness means
the same dictionary works across mixed enemy types attacking the same target
(zombies + orcs counted together). It is server-only state, so no networking.

**Detection is throttled.** `_detectionInterval = 0.25s` — `FindNearestPlayer`
returns the cached `currentTarget` between intervals to avoid per-frame
`OverlapSphere` cost. Pure radius detection, no LoS raycast.

**Animation sync via NetworkAnimator.** Animator parameters are `Speed` (float,
smoothed in via `SetFloat(hash, value, 0.15f, dt)`), `Attack` (trigger), `Dead`
(bool). State-driven: `Speed` is mapped per-FSM-state (`Idle = 0`, `Wander = 0.5`,
`Chase = 1`, `Attack = 0`).

**Hitbox flow.** `HitboxController` on the bite child. `Invoke(nameof(EnableBiteHitbox), hitboxEnableDelay)`
opens it `hitboxEnableDelay` after the attack starts; `hitboxActiveDuration`
closes it. Damage routes through the standard `DamageReceiver` pipeline.

**Death flow.** `vitalManager.OnDeath` event → `SetState(Dead)` →
`agent.enabled = false`, `animator.SetBool(deadHash, true)`,
`DisableCollidersClientRpc()` so each client locally drops the capsule and
crit-zone colliders. No respawn.

### Net-visible state and ownership

- NetworkObject is server-owned (`Ownership = 1` on the prefab — server
  authority). All clients receive the standard `NetworkTransform` + the
  `NetworkAnimator` parameter snapshot. Client-side has no AI logic.
- For one-shot effects spawned by AI (sounds, VFX), the same ServerRpc →
  ClientRpc fan-out used elsewhere applies. `BearAI` uses
  `DisableCollidersClientRpc` for death-time collider drops; sounds run on
  the server's local `AnimalSoundPlayer` and propagate via animation events.

### Fragile / known-careful

- **`_attackerCounts` is process-wide static.** A bug that fails to
  unregister on an edge-case death path leaks entries forever. Always pair
  every `RegisterAttacker` with an `UnregisterAttacker` on every transition
  out of Attack/Chase that loses the target.
- **NavMeshAgent + `agent.speed = 100f`.** BearAI sets agent speed very high
  to prevent NavMesh from clamping root motion. Side effect: agent's own
  velocity-based avoidance is effectively disabled, leaving RVO + the manual
  separation nudge as the only avoidance. Don't expect agent.velocity to
  match the actual body velocity.
- **Manual gravity is one-way.** The downward raycast snaps to ground or
  accumulates `_verticalVelocity`. There is no upward force — if a knock-up
  effect is added later, this gravity model will fight it. The dragon's hit
  pipeline solved this with an SMB; orcs may need the same.
- **Detection is line-of-sight-blind.** `OverlapSphereNonAlloc` against
  `playerLayer`. Bears (and zombies, which inherit this AI) sense players
  through walls. Acceptable for zombies; the planned orc AI will need a LoS
  raycast added to detection.

### Planned extension — orc AI (`design/OrcAI.md`)

The orc AI is a **fork-and-extend** of `BearAI`, not a subclass. The shared
static `_attackerCounts` dictionary works correctly across the fork (mixed
zombie + orc encounters share crowd-control). Forking lets each AI evolve
independently without a coupling tax.

Forecast complexity (from `OrcAI.md`):
- Block, parry, multiple attack styles (driven by Mecanim sub-states + decision logic).
- Squad-level coordination (formation slots around target, casualty-driven flee).
- NavMesh OffMeshLinks for jump-over-obstacle.
- HFSM (top-level state) + utility-scored combat decisions (block / parry /
  attack / reposition) replacing BearAI's flat enum FSM.
- Squad coordinator entity (one per squad) holding slot assignments and
  morale state, networked at squad granularity not orc granularity (matching
  the squad-level replication principle in `FEFE_NPC_Architecture.md`).

Until the orc work begins, `BearAI` is the only NPC AI in the codebase.

---

## 21. Discovery summary

- **Subsystems documented:** 15 (Application & Session Lifecycle, Character
  Selection & Spawning, Animal Locomotion base, Dragon, Horse & Mount, Human
  Locomotion, Human Animation, Human Combat, Network Synchronization, Vitals
  & Status Effects, Ballista, Camera & Owner Visibility, Proximity Sound,
  Respawn & Death UI, NPC AI).
- **Total document length:** ~1100 lines.
- **Top 3 weaknesses to address first:**
  1. Hardcoded **5 m melee max range** in `DamageReceiver.RequestDamageServerRpc`
     ignores `WeaponData.attackRange` and silently rejects long-reach hits.
  2. **Reflection coupling** between `AnimancerNetSync` and `InputController.Snapshot`
     via `<Snapshot>k__BackingField` — renaming the property silently breaks
     remote puppeting.
  3. **Cooperative-DOT loophole** in `BurnStatus`: attacker is implicit
     (just a position passed to `ApplyProjectileDamage`), so a teammate's
     fire breath could DOT an ally indefinitely once friendly fire / teams
     are added.
- **Top 3 open questions:**
  1. Should `ApplyProjectileDamage` carry an `attackerNetObjId` so teams /
     friendly-fire can be enforced and the BurnStatus DOT loophole closed?
  2. Where should the dragon's authoritative `IsDead` state live —
     Animator parameter (today) or `NetworkVariable<bool>`?
  3. Are the two character stacks (human Animancer + dragon Mecanim) going
     to converge, and if so on what (a shared `*Sync` base class, a
     shared `INetworkedAnimator` interface, or just shared constants)?
