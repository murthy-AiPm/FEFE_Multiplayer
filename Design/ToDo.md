═══════════════════════════════════════════════════════════════
 MAINTENANCE CONVENTION — READ BEFORE APPENDING
═══════════════════════════════════════════════════════════════

 This file is an append-only session log. Newest entries go at
 the BOTTOM of the file. (Converted from ToDo.cs to ToDo.md on
 21st-April-2026 — no more block-comment wrapper to worry about;
 just plain markdown now.)

 BEFORE APPENDING:
  * Tail-read the last ~40 lines to match the style of the most
    recent entries and to avoid duplicating a change already logged.
  * One entry per logical change, not per file edited. A feature
    that touched 3 files is still ONE entry.

 ENTRY FORMAT:
  * Start with a date-stamped session header when opening a new
    session, using the existing separator style:

        ═══════════════════════════════════════════════════════════════
         DD-Month-YYYY — Short Session Topic
        ═══════════════════════════════════════════════════════════════

  * Each change inside the session uses an ALL-CAPS TITLE followed
    by a STATUS tag: DONE / WIP / BLOCKED / REVERTED.
        Example:  DRAGON ROAR — DONE:
  * Under the title, use " * " prefixed bullets. Cover, as applicable:
      - symptom (for bug fixes)
      - root cause
      - files changed
      - fix description
      - any Inspector fields added or renamed
  * Keep bullets terse but complete enough that a future session can
    reconstruct the "why" without reading the diff.

 DO NOT:
  * Rewrite, reflow, or delete old entries — even ones whose approach
    was later reverted. History is the value. If an approach is
    undone, add a new "TITLE — REVERTED:" entry that references the
    original by date and title.
  * Use this file for a feature backlog or TODO list. Backlog items
    live in CLAUDE.md ("on the horizon") or a separate BACKLOG.md.
  * Paste large code blocks. Describe the change; the diff is in git.

───────────────────────────────────────────────────────────────

 7th-March-2026
 * 1.  Setup horse height
 * 2.  Setup Arrow spwan point
 * 3.  Zoom out camera bit or add camera controls
 * 4.  Add higher resolutions - for screen settings
 * 5.  Disable controls on pause menu - done//
 * 6.  Hide player UI on pause menu - done
 * 7.  Setup Spawn points
 * 8.  Setup Death screen with spawn points
 * 9.  Setup loading screens to hide camera switching
 * 10. Add mouse sensitivity for Player, bow, and horse
 * 11. Tweak combat rotation speed
 * 12. Expose mouse sensitivity on settings
 * 13. Expose key bindings on settings
 * 14. Single space dodgestep, double space dodge
 * 15. Horse freeze all when idle - done
 * 16. Add character change option pause menu
 * 17. Player respwan option in pause menu
 * 18. Add offline mode
 * 19. Exit option in all screens
 * 20. Bow draw not syncing
 * 21. Need to understand blendtree sync better
 * 22. Horse not blending well when spam A/D - done//
 
Phase 1
* Build sound library
* Build Character library
* Build Castle/City/Battlegrounds
* Finish Dragon
* Buil AI foundation

═══════════════════════════════════════════════════════════════
 2nd-April-2026 — Dragon Combat + Flight System Session
═══════════════════════════════════════════════════════════════

COMBAT SYSTEM — COMPLETED:
 * Dual attack mode system — Press 1 = Melee, Press 2 = Fire Breath (mutually exclusive, toggle off by pressing same key again)
 * AttackMode changed from bool to int (0=none, 1=melee, 2=firebreath) — animator parameter updated accordingly
 * Melee mode: head yaw tracks camera while stationary, left-click triggers melee attack with spine twist
 * Fire Breath mode: head tracks camera in yaw AND pitch, hold left-click = continuous fire breath
 * Head yaw distributed across 5 neck bones (Neck→Neck4) with Inspector-tweakable weights for natural S-curve
 * Head pitch on Neck3 using yaw-rotated rb.rotation * Vector3.right axis — no twist artifacts
 * Head pitch offset (Inspector field) to correct for bone rest pose
 * Spine twist compensation — head yaw subtracts current spine twist so they don't double up
 * Procedural jaw open/close — jawBone Z rotation driven by code (closed: -106.534, open: -125), no animation needed
 * Fire breath VFX — prefab instantiated/destroyed on all clients via ClientRpc (fireBreathVFXPrefab)
 * Fire breath SFX — routed through DragonSoundPlayer (OnFireBreathStart/OnFireBreathEnd)
 * Melee VFX/SFX — spawned on all clients via MeleeAttackClientRpc, sound via DragonSoundPlayer.OnMeleeAttack
 * Right-click spine aim fully disabled for dragon
 * TurnAngle SmoothDamp clamped to ±1 in AnimalGroundController (overshoot fix)

FLIGHT SYSTEM — ROOT MOTION BLEND TREE:
 * Added useFlightRootMotion toggle on DragonFlightController — when ON, new blend tree system; when OFF, old mouse-heavy controls
 * Three animator parameters: Thrust, Yaw, Pitch (all -1 to 1 range, written directly to animator by flight controller)
 * Thrust: throttle-style — W taps increment by thrustIncrement (0.25 default), S decrements, value sticks on release, smooth lerp to target
 * Yaw: A/D driven, MoveTowards with dead zone snap to zero to prevent flicker
 * Pitch: driven from camera angle (cam.eulerAngles.x) — dragon follows where Cinemachine camera looks, normalized to -1..1 via flightPitchClamp
 * FlightMode (bool) animator parameter — master switch for flight state
 * Press C on ground → jump animation → FlightMode = true → enters flight
 * Press C while flying → ExitFlight() → FlightMode = false → all params reset to zero → gravity takes over → dragon falls and lands
 * FlightMode is the authority — grounded check does NOT exit flight, only C key press does
 * Dive functionality removed entirely (IsDiving always returns false)
 * Takeoff key hardcoded to C (removed from Inspector to avoid confusion with sprint/LeftShift)

FLIGHT ARCHITECTURE CHANGES:
 * AnimalGroundController.OnAnimatorMove changed from private to protected virtual
 * DragonGroundController overrides OnAnimatorMove — when FlightRootMotionActive is true, applies animator root motion directly (position via rb.linearVelocity + rotation via rb.MoveRotation), skips all ground logic
 * DragonGroundController.FlightRootMotionActive flag — set by flight controller during root motion flight
 * AnimalGroundAlignment.SuspendAlignment flag — when true, FixedUpdate bails out entirely (no rotation, no height adjustment)
 * Flight controller sets both flags on entry, clears both + calls SetYawImmediate on exit
 * AnimalGroundController.TurnAngle changed from private set to protected set
 * DragonGroundController.SetFlightAnimParams(thrust, yaw, pitch) — public method (currently unused since flight writes directly to animator)

NETWORK SYNC — COMPLETED:
 * AttackMode (int) — owner writes NetworkVariable directly + ServerRpc/ClientRpc for animator
 * IsBreathingFire (bool) — owner writes NetworkVariable directly + ServerRpc/ClientRpc for animator/VFX/SFX
 * Head yaw, head pitch, spine twist — NetworkVariables at 20Hz
 * MeleeAttack — ServerRpc → ClientRpc trigger
 * VFX/SFX fire on ClientRpc so all clients see/hear effects
 * Jaw reads netIsBreathingFire on remote clients — FIXED: owner now writes NetworkVariable directly (Owner write permission was preventing ServerRpc from writing)
 * Flight params (FlightMode, Thrust, Yaw, Pitch) — synced via DragonAnimatorController NetworkVariables
   - Owner: flight controller writes to animator directly, DragonAnimatorController reads from animator and pushes to NetworkVariables at 20Hz
   - Remotes: DragonAnimatorController reads NetworkVariables and sets animator params
 * Flight state bools (IsHovering, IsFlying, IsGliding) — synced via existing NetworkVariables
 * Swim params (IsSwimming, SwimSpeed, SwimTurn, SwimVertical) — synced via existing NetworkVariables
 * CRITICAL FIX: NetworkVariables with Owner write permission cannot be written from ServerRpc (runs on server, not owner). Owner must write directly. Applied to netIsBreathingFire and netAttackMode.

CINEMACHINE / CAMERA — FIXED:
 * DragonCinemachineModeSwitcher changed from MonoBehaviour to NetworkBehaviour
 * Non-owner dragon cameras disabled (SetActive false, priority 0) via OnNetworkSpawn
 * Prevents client camera from switching to host's dragon
 * Same pattern as existing OwnerOnlyFreeLook for human characters

KEY FILES:
 * DragonCombatController.cs — combat modes, head tracking, spine twist, jaw, fire breath VFX/SFX
 * DragonFlightController.cs — flight modes, root motion blend tree params, takeoff/landing
 * DragonGroundController.cs — FlightRootMotionActive flag, OnAnimatorMove override, HasGroundBelow flight check, SetFlightAnimParams
 * AnimalGroundController.cs — base class, OnAnimatorMove now virtual, TurnAngle now protected set
 * AnimalGroundAlignment.cs — SuspendAlignment flag
 * DragonAnimatorController.cs — syncs all flight/swim/combat params over network, writes Pitch/Thrust/Yaw/FlightMode to animator for remotes
 * DragonCinemachineModeSwitcher.cs — now NetworkBehaviour, owner-only camera activation
 * DragonSoundPlayer.cs — fire breath start/loop/end sounds, melee attack sounds

BONE AXIS NOTES:
 * Neck1 rest Y is 270° world space when body faces 0° — never use bone.right for pitch
 * Pitch axis: rb.rotation * Vector3.right works twist-free, then rotate by head yaw for head-relative pitch
 * Yaw: world Vector3.up works across all neck bones
 * Jaw: local Z rotation (-106.534 closed, -125 open)

COMPLETED:
 * 6th-April-2026 — Directional dodge roll & dodge step via CombatLocomotionMixer blend trees (CartesianMixerState, 4 cardinal directions each). Dodge rules removed from RuleSet; mixer handles directionality. IsDodgeMixerActive guard added to CombatController to prevent re-entry while animation plays.

OUTSTANDING / NEXT:
 * Ground-to-flight transition: dragon clips into ground during jump→hover. Needs either better jump animation with root motion lift, or small takeoffLiftSpeed during jump timer
 * Flight blend tree tuning — need more animation clips mapped to blend tree positions
 * Fire breath while walking/flying — design question still open (see options A/B/C below)
 * TurnAngle animator parameter occasionally flickers (-2.8 seen) — may be blend tree internal damping
 * Fire breath animation mask — has neck pitch + jaw open baked together. Long term: create Avatar Mask that includes jaw but excludes neck bones, so animation opens mouth without fighting procedural head tracking

DRAGON TODO:
 * Death animations — directional death hits (front, back, left, right)
 * Smoother transition from ground to air and air to ground
 * Minimum flight height / terrain clipping guard — prevent dragon from flying through terrain
 * Cliff fall animation
 * Dragon sounds (footsteps, roars, wing flaps, impacts, ambient)

DESIGN QUESTIONS:
 * Fire breath while walking:
   A) Allow fire breath at walk only (not trot/sprint) — less conflict with blend tree
   B) Allow at all gaits — need to dampen neck yaw contribution so it doesn't fight locomotion
   C) Lock body rotation to camera while breathing fire during movement — dragon walks in aim direction

═══════════════════════════════════════════════════════════════
 7th-April-2026 — Dragon Hit/Death Animation System
═══════════════════════════════════════════════════════════════

COMPLETED:
 * DamageReceiver refactored to be character-agnostic — animation calls routed through events
   (OnPlayHitAnimation, OnPlayDeathAnimation) instead of hardcoded RuleAnimancerDriver calls
 * HumanDamageAnimator.cs created — bridges DamageReceiver events to RuleAnimancerDriver for human prefab
 * DragonDamageAnimator.cs created — bridges DamageReceiver events to Mecanim Animator for dragon prefab
 * Hit direction computed from attacker position in dragon-local space (FB/LR, snapped to dominant axis)
 * Death direction uses raw localDir.x for 1D blend tree interpolation across -1, -0.5, 0.5, 1
 * GotHit auto-resets after hitResetDelay (Inspector-tweakable)
 * ResetDeathState() method for respawn
 * Debug keypad testing added to DragonDamageAnimator (toggle via debugKeypadTesting bool)

KEY FILES:
 * DamageReceiver.cs — now character-agnostic, uses OnPlayHitAnimation / OnPlayDeathAnimation events
 * HumanDamageAnimator.cs — CharacterScripts/Scripts/Human/Combat/ — human bridge
 * DragonDamageAnimator.cs — CharacterScripts/Scripts/Animal/Dragon/ — dragon bridge

PREFAB SETUP REQUIRED:
 * Add HumanDamageAnimator component to human prefab
 * Add DragonDamageAnimator component to dragon prefab
 * Dragon Animator Controller params: GotHit (Bool), HitFB (Float), HitLR (Float), IsDead (Bool), DeathLR (Float)
 * Transition: locomotion → hit blend tree (GotHit == true)
 * Transition: Any State → death blend tree (IsDead == true)

TODO — REMOVE BEFORE SHIPPING:
 * DragonDamageAnimator.debugKeypadTesting — keypad debug input for testing hit/death anims
   Hit: Numpad 8=front, 2=back, 4=left, 6=right (supports diagonals)
   Death: Numpad 7=LR-1, 1=LR-0.5, 3=LR+0.5, 9=LR+1
   Reset death: Numpad 5

DESIGN DECISIONS:
 * Root motion approach for hit rotation ABANDONED — hit clip root bone barely rotates,
   rotation is baked into pelvis/spine bones. deltaRotation was ~0.2°/frame, not usable.
 * Code-driven rotation adopted instead: rb.MoveRotation lerps toward attacker during hit,
   AnimalGroundAlignment.SuspendAlignment prevents fighting, SetYawImmediate syncs on end.
 * Hit clip import settings: Bake Into Pose CHECKED for Rotation, Pos Y, and Pos XZ.
 * Multiple projectiles: first hit wins for animation (cooldown via _isRotatingFromHit guard),
   damage still applies for all hits. Killing blow direction determines death animation.
 * Death direction: killing blow attacker position (Option A), not random.

BUGS FOUND THIS SESSION:
 * Dragon flight animations NOT syncing on remotes (pre-existing, NOT caused by hit/death changes).
   Remote sees dragon struggling between falling and flight. Ground anims sync fine.
   — FIXED 7th-April-2026, see session below.
 * Dragon respawn inversion on remotes — partially fixed (applyRootMotion owner-only on respawn),
   may still have edge cases. Loading screen added to respawn flow to mask sync delay.
   — FIXED 7th-April-2026: root cause was missing death→idle transition in Animator Controller.
     Bone rotations from death clip persisted on remotes because there was no transition back.

═══════════════════════════════════════════════════════════════
 7th-April-2026 — Flight Sync Fix + Respawn Inversion Fix
═══════════════════════════════════════════════════════════════

FLIGHT ANIMATION SYNC — FIXED:
 * Root cause: Two issues preventing flight animations from syncing to remotes.
 * Issue 1 — Zero-out block in DragonAnimatorController.LateUpdate() was clobbering
   Thrust/Yaw/Pitch to zero on the owner’s animator every frame during flight.
   UpdateNetworkVariables was reading from the animator (after clobber) instead of
   from the flight controller. Fix: removed zero-out block, added FlightThrust and
   FlightYaw public properties to DragonFlightController, UpdateNetworkVariables now
   reads directly from the flight controller (same pattern as ground reads from
   groundController.GaitSpeed/TurnAngle).
 * Issue 2 — On remotes, DragonFlightController.Update() early-returns (!IsOwner),
   so IsFlying/IsHoverMode/IsGliding/IsDiving are never set. DragonGroundController
   .HasGroundBelow() and OnFixedGroundUpdate() checked those flags to suppress falling.
   Result: remote sees IsFalling=true fighting FlightMode=true. Fix: added fallback
   check for animator.GetBool("FlightMode") (synced via NetworkVariable) in both methods.

KEY FILES CHANGED:
 * DragonFlightController.cs — added FlightThrust, FlightYaw public properties
 * DragonAnimatorController.cs — removed zero-out block, UpdateNetworkVariables reads
   from flight controller directly, owner LateUpdate only sets FlightMode (not Pitch)
 * DragonGroundController.cs — HasGroundBelow() and OnFixedGroundUpdate() now also
   check animator FlightMode param for remote clients

RESPAWN INVERSION — FIXED:
 * Root cause: Dragon Animator Controller had no transition from death state back to
   idle/locomotion. When IsDead was reset to false on respawn, the bone rotations from
   the death clip (pelvis/spine flip) persisted on remotes. Root transform was fine
   (confirmed via debug logs), inversion was purely at bone level.
 * Fix: Added death→idle transition in the Animator Controller (done by Murthy in editor).
 * Debug logs added to RespawnController for diagnosis, then removed after fix confirmed.

WATER ENTRY FLICKER — DEFERRED:
 * Issue: When dragon exits flight (C key) and free-falls into water, the IsFalling animator
   param flickers between true/false, causing free-fall and swim-enter anims to fight.
 * Root cause: OnTriggerExit in AnimalSwimSystem instantly nukes _isInWaterState when the
   dragon's collider briefly exits the water trigger volume (due to swim animation or buoyancy
   pushing the dragon above the trigger boundary). Dragon falls back in, re-enters trigger,
   but IsInWater waits for waterEntryDepthThreshold (0.3m) again. Cycle repeats = flicker.
 * Workaround: Set surfaceBuoyancy to 0 on DragonSwimController. Prevents the vertical push
   that causes the collider to exit the water trigger.
 * Proper fix: Add a grace period timer to AnimalSwimSystem.OnTriggerExit so brief exits
   don't immediately clear swim state. OnTriggerEnter cancels the pending exit if the dragon
   re-enters before the grace period expires (~0.3s). This way animation bobbing doesn't
   break swimming.

──────────────────────────────────────────────────────────────────────
9th-April-2026 — Death-from-Sky System & Late-Join Animation Sync
──────────────────────────────────────────────────────────────────────

DEATH-FROM-SKY SYSTEM — IMPLEMENTED:
 Three-phase death when killed during flight: hit in sky → fall → hit ground.

 Phase 1 — HandleDeathAnimation (DragonDamageAnimator.cs):
 * Calls flightController.ExitFlight() to stop flight processing
 * Immediately re-sets FlightMode=true on animator so ground death anim doesn't fight
   flight death anim (ExitFlight clears it, we put it back)
 * Sets _deathFalling=true to start gravity + ground detection
 * Disables root motion, zeros velocity

 Phase 2 — Death fall gravity (DragonDamageAnimator.cs):
 * ApplyDeathFallGravity() — uses rb.linearVelocity = Vector3.down * velocity
   (not rb.MovePosition which clipped through terrain)
 * Lives in DragonDamageAnimator because DragonGroundController is disabled during death
 * Inspector fields: deathFallGravity (20), deathFallMaxSpeed (40)

 Phase 3 — Ground detection (DragonDamageAnimator.cs):
 * CheckDeathFallGround() — raycasts Vector3.down from deathFallRayOrigin
 * When ground detected within deathFallGroundDistance (3m), fires DeathImpact trigger
   via ServerRpc/ClientRpc
 * OnDrawGizmos draws red ray + sphere always visible in Scene view

 FlightMode during death:
 * DragonAnimatorController.LateUpdate() now skips FlightMode write when IsDead=true
   (both owner and remotes) so DragonDamageAnimator has full control
 * FlightMode stays true entire death sequence, only cleared in ResetDeathState (respawn)

 DragonFlightController death guard:
 * Added IsDead check to !isActive branch in Update() — skips crash detection,
   C-key re-entry, and _diveCrashTriggered reset when dead

 ExitFlight() made public for DragonDamageAnimator access.

 KEY FILES CHANGED:
 * DragonDamageAnimator.cs — death fall gravity, ground detection, DeathImpact RPC,
   FlightMode management, OnDrawGizmos, VitalManager ref, DebugInstantKill (Numpad 5),
   ResetDeathState moved to Numpad 0
 * DragonFlightController.cs — ExitFlight() public, IsDead guard in !isActive branch,
   C-key exit sets _diveCrashTriggered=true to suppress false crash detection
 * DragonAnimatorController.cs — IsDead guard on FlightMode write in LateUpdate
 * DragonGroundController.cs — comment update in OnAnimatorMove

 ANIMATOR SETUP NEEDED:
 * Add DeathImpact Trigger parameter
 * Create transition: flight death fall state → fly dead ground state,
   condition: DeathImpact trigger, no exit time

LATE-JOIN ANIMATION SYNC — IMPLEMENTED:
 * Problem: When client joins and host dragon is already flying, client sees idle animation.
   Syncs only after a state transition occurs.
 * Root cause: Unity Animator starts in default state (Idle). NetworkVariables have correct
   values but Animator needs animator.Play() to jump directly to the correct state.
 * Fix: Override OnNetworkSpawn in DragonAnimatorController. When a late-joining remote
   client spawns and netFlightMode.Value is true, calls animator.Play("BlendFly") and sets
   all flight params (Thrust, Yaw, Pitch). Same for swimming → animator.Play("SwimmingLocomotion").
 * KEY FILE: DragonAnimatorController.cs — OnNetworkSpawn override with late-join state forcing

 DRAGON COMBAT — HITBOXES, FIRE DAMAGE & FIRE PROPAGATION (TODO):

 1. Dragon Melee Hitbox Colliders on Paws:
    * Add trigger colliders to each paw bone (front-left, front-right) on the dragon rig
    * Colliders should be enabled only during melee attack animation window
      (use animation events or AnimatorStateInfo tag check to toggle)
    * On trigger enter, check for IDamageable interface on the hit object
    * Apply melee damage amount (Inspector-tweakable) and knockback direction
    * Network: damage dealt server-side, VFX/SFX via ClientRpc
    * Consider adding colliders to tail and jaw for future bite/tail-swipe attacks

 2. Fire Breath Collision & Damage:
    * Fire breath particles need a collision callback to detect what they hit
      Options: ParticleSystem collision module (OnParticleCollision), or a
      cone-shaped trigger collider attached to the mouth that activates while
      IsBreathingFire is true
    * Cone collider approach is simpler for networking — server checks overlap,
      applies damage-over-time (DOT) to everything inside the cone each tick
    * Damage amount per tick and tick rate should be Inspector-tweakable
    * Fire damage should stack or refresh a burn timer on the target
    * VFX: hit targets should show fire/scorch particle effect on contact point
    * Network: server owns damage calculation, clients see VFX/SFX via ClientRpc

 3. Setting Things on Fire (Fire Propagation System):
    * Burnable interface (IBurnable) for anything that can catch fire:
      - Players (human characters): catch fire, take DOT, can spread to nearby players
      - NPCs: catch fire, panic state change, take DOT, eventually die
      - Animals (horses, other dragons?): catch fire, flee behavior, take DOT
      - GameObjects & buildings: catch fire with visual stages
        (intact → burning → charred/destroyed), using pre-authored states
    * Each burnable object tracks: isBurning, burnTimer, burnDamagePerTick
    * Fire spread: burning objects can ignite nearby burnables within a radius
      (proximity check on a timer, not every frame)
    * Visual: fire VFX prefab instantiated/parented to burning object,
      scaled to object size, destroyed when burn ends
    * Audio: looping fire crackle sound on burning objects via proximity sound system
    * Buildings/structures: use pre-authored destruction states
      (intact → burning → collapsed), trigger NavMesh Obstacle carving for rubble
    * Network: burn state is a NetworkVariable (bool + timer), server authoritative,
      VFX/SFX spawned via ClientRpc
    * Extinguishing: fire stops after burnDuration expires, or if entering water

 LESSON LEARNED — FLIGHT HEAD YAW COMPENSATION:
 * Problem: During flight fire breath, the turn animation rotates the dragon's head
   in the turn direction. Procedural head tracking adds MORE rotation on top,
   causing the head to overshoot past where the camera is looking.
 * What DIDN'T work: Complex yaw-scaling systems that tried to detect same/opposite
   direction of turn vs look, lerping clamps based on FlightYaw, etc. These failed
   because the camera yaw and body yaw are tightly coupled during flight (body chases
   camera), so DeltaAngle between them doesn't cleanly separate "where I want to look"
   from "where the body is turning."
 * What WORKED: A simple fixed offset that counteracts the animation's head turn.
   flightYawAnimCompensation (Inspector-tweakable, default 20°) is multiplied by
   -FlightYaw and added to the delta. FlightYaw +1 (right turn) → offset -20°.
   FlightYaw -1 (left turn) → offset +20°. Straight flight → offset 0°.
   One line of math: animCompensation = FlightYaw * -flightYawAnimCompensation
 * Takeaway: When procedural bone manipulation fights baked animation, the fix is
   usually a simple offset to counteract the animation — not a complex system to
   detect and avoid the conflict. Try the simple thing first.

 DRAGON FIRE VFX — BURN STATUS & GROUND FIRE (TODO):

 1. Characters Ablaze (BurnStatus component):
    * New script: BurnStatus.cs — attach to any character/animal that can burn
    * Flow: DragonFireBreathDamage hits target → checks for BurnStatus → calls Ignite()
    * Ignite() starts burn: spawns fire VFX parented to target, starts DOT timer
    * Burn continues AFTER leaving fire cone (independent timer + DOT)
    * Re-entering fire cone refreshes the burn timer
    * When timer expires: fire VFX destroyed, DOT stops
    * Network: burn state is NetworkVariable<bool>, server applies DOT,
      fire VFX spawned/destroyed via ClientRpc
    * Fire VFX prefab: character-scale fire particles (needs art setup in Unity)
    * Modify DragonFireBreathDamage.cs to call BurnStatus.Ignite() on hit targets

 2. Ground Fire (fire streak → burnt patch):
    * New script: GroundFireSpawner.cs — on the dragon, spawns fire pools on ground hits
    * DragonFireBreathDamage does a raycast to find ground hit points
    * At each ground hit: spawn a "fire pool" prefab
    * Fire pool prefab contains: fire particle system (plays for X seconds, then stops)
      + a scorch decal/projector underneath (persists longer, the burnt patch)
    * Fire pools can optionally damage players who walk through them (overlap + DOT)
    * Spawn rate limited to avoid creating hundreds of fire pools per second
    * Self-destructs after burn duration + scorch duration
    * Fire pool prefab needs art setup in Unity (particle system + decal)

 FILES TO CREATE:
 * BurnStatus.cs — CharacterScripts/Scripts/Shared/ or similar shared location
 * GroundFireSpawner.cs — CharacterScripts/Scripts/Animal/Dragon/
 * Fire pool prefab — Prefabs/VFX/
 * Character fire VFX prefab — Prefabs/VFX/

 FILES TO MODIFY:
 * DragonFireBreathDamage.cs — add BurnStatus.Ignite() call on hit,
   add ground raycast for fire pool spawning

 GROUND FIRE BREATH WOBBLE (TODO):
 * Issue: On the ground, fire breath VFX is positioned at the mouth bone and
   aimed along mouth bone forward. Because the head/neck bones are jittered
   each frame by procedural neck rotations (head tracking), the mouth bone's
   forward vector wobbles, causing the fire VFX to visibly shake.
 * Already tried: Quaternion.Slerp smoothing toward LookRotation(spawnRef.forward, Vector3.up)
   in UpdateFireBreathVFX. Reduces but does not eliminate wobble.
 * Possible fixes to try:
   - Use the body's forward (rb.rotation * Vector3.forward) combined with the
     procedural head yaw/pitch values directly, instead of reading the bone transform
   - Cache the spawn rotation at fire breath start and only update it when the
     camera moves significantly (deadzone-based update)
   - Run fire VFX update in LateUpdate AFTER all bone manipulation is finished,
     and apply heavy smoothing (Slerp factor ~3-5 instead of 10)
   - Position fire VFX at mouth bone but compute rotation purely from camera +
     body forward (independent of bone transforms entirely)

──────────────────────────────────────────────────────────────────────
10th-April-2026 — Burn System Phase 2 (Ground Fire Pool/Spawner)
──────────────────────────────────────────────────────────────────────

PHASE 2 — IMPLEMENTED:
 * GroundFirePool.cs (Shared/) — singleton-per-scene local pool. Prewarms patches in Awake,
   recycles oldest on overflow. SpawnAt(pos, normal, config, sourceOwnerId) is the only API.
   Holds GroundFirePatchConfig struct so all tuning is owned by the spawner (Inspector).
 * GroundFirePatch.cs (Shared/) — single patch behavior. Holds DecalProjector ref, fades
   fadeFactor from baseline -> 0 over the last fadeDuration seconds of lifetime. OnTriggerStay
   calls BurnStatus.Ignite() on entering targets, server-only (gated by runtime _isServer flag
   set from NetworkManager.Singleton.IsServer in Activate()). Patches do NOT stack DPS — they
   refresh BurnStatus burn timer, BurnStatus owns the actual damage (matches design decision).
 * GroundFireSpawner.cs (Animal/Dragon/) — owner-driven, polls combatController.IsBreathingFire
   (same pattern as DragonFireBreathDamage). Every spawnInterval: projects forwardProjection
   meters along breath forward + random scatter, raycasts down with separate groundMask, sends
   RequestSpawnGroundFireServerRpc. ServerRpc spawns locally on host then ClientRpc fans to
   non-host clients. Zero NGO churn — pure local pools + RPC fan-out. Late joiners miss
   in-flight patches; acceptable for ~3s decals.

ARCHITECTURE NOTES:
 * Same prefab on host and clients. GroundFirePatch.Activate() reads NetworkManager.IsServer
   to decide whether to enable damage logic. Visual fade runs everywhere.
 * Damage path: GroundFirePatch.OnTriggerStay -> BurnStatus.Ignite(burnTimeOnContact, ownerId).
   BurnStatus is the sole DOT source; patches just keep targets ignited.
 * sourceOwnerId is propagated through ServerRpc/ClientRpc so BurnStatus self-immunity works.
 * No edits made to BurnStatus.cs (already supports refresh-without-stack via maxBurnDuration
   clamp) or DragonFireBreathDamage.cs (independent component, parallel polling).

PREFAB / INSPECTOR SETUP REQUIRED (next session):
 * Create GroundFirePatch prefab: empty GO + GroundFirePatch component + URP DecalProjector
   child + trigger collider (sphere or box, isTrigger=true). Optional particle VFX child wired
   to vfxRoot field.
 * Create scene-level GroundFirePool GameObject in main gameplay scene. Add GroundFirePool
   component, assign patchPrefab, set poolSize (default 64).
 * Add GroundFireSpawner component to dragon prefab. Wire combatController, flightController,
   fireOrigin (mouth bone), cam.
 * CRITICAL: Set groundMask explicitly in Inspector. Do NOT leave at default (0) or Default-only.
   Same mistake as the flight controller groundCheckMask — needs to include terrain + ground
   layers used in the city/battleground scenes.
 * Tune: spawnInterval (0.05s), forwardProjection (8m), scatterRadius (1.5m), maxGroundDistance
   (50m), patchLifetime (3s), patchFadeDuration (1.2s), patchDamagePerTick (5), patchTickInterval
   (0.5s), patchBurnTimeOnContact (1.5s).
 * Phase 1 prefab setup still pending: BurnStatus components on human/dragon/NPC prefabs,
   character fire VFX prefab assigned, pelvisBone wired.

STILL DEFERRED:
 * Ground fire breath VFX wobble fix (logged in earlier session, not touched this pass)
 * Flight remote-client animation sync bug (logged separately)
 * Building/structure burnable states with pre-authored destruction stages
 * Fire propagation between burnables (proximity ignition)

──────────────────────────────────────────────────────────────────────
10th-April-2026 (cont.) — Burn System Testing, Bug Fixes, Ground Patch Behavior
──────────────────────────────────────────────────────────────────────

PHASE 1 BUGS FIXED DURING TESTING:

 1. NPC IGNITION BROKEN — self-immunity check used OwnerClientId comparison.
    Symptom: zombies took fire breath damage and died, but never caught fire.
    Root cause: BurnStatus.Ignite() compared sourceOwnerId == OwnerClientId. Host-controlled
    dragon has OwnerClientId=0, server-spawned NPCs also have OwnerClientId=0, so the
    self-immunity check matched for ALL server-owned NPCs and Ignite() returned immediately.
    Fix: Signature changed to (float, NetworkObjectReference). Self-immunity compares
    NetworkObject identity via sourceRef.TryGet() == NetworkObject. Works for both
    player-owned and server-owned characters. Future-proof for controllable NPCs.
    Files: BurnStatus.cs, DragonFireBreathDamage.cs, GroundFirePool.cs, GroundFirePatch.cs,
    GroundFireSpawner.cs (all propagate NetworkObjectReference).

 2. BURN DURATION NOT ACCUMULATING — burn ended instantly when fire breath stopped.
    Root cause: DragonFireBreathDamage passed _tickInterval (0.25s) as burn duration per
    Ignite() call. Each tick added 0.25s, BurnStatus.Update() decremented Time.deltaTime
    every frame → net accumulation ~0.
    Fix: Added burnTimePerTick serialized field (default 1.5s) on DragonFireBreathDamage,
    decoupled from _tickInterval. ~2s of breathing now hits maxBurnDuration cap.
    Files: DragonFireBreathDamage.cs only.

GROUND FIRE PATCHES — NOW WORKING ON ALL SURFACES (BULLET-DECAL PATTERN):
 * Rewrote GroundFireSpawner.TrySpawnPatch() to bullet-decal pattern: single forward raycast
   from fireOrigin along breath direction, spawn at hit.point with hit.normal. No aim-angle
   gating, no downward fallback, no projection math. Hit = spawn, miss = skip. Empty sky
   breathes produce no patches because the ray hits nothing.
 * Scatter applied tangent to surface via Vector3.ProjectOnPlane(scatter, hit.normal) so
   walls and slopes get clean scatter.
 * GroundFirePool.SpawnAt() builds orthonormal rotation from hit.normal (robust for ground,
   walls, ceilings, slopes). Patch's local up-axis = surface normal.
 * DecalProjector child on patch prefab needs -90° X rotation so its local -Z (projection
   direction) points along patch up-axis (into the surface). Prefab-only fix.
 * Verified working: flat ground, slopes, vertical walls, ceilings. Flight horizontal into
   sky correctly produces no patches.

LESSONS LEARNED:

 * NetworkObject IDENTITY vs OwnerClientId: OwnerClientId is a property OF an entity, not an
   identifier FOR one. Server-owned objects all share OwnerClientId 0 — any "is this the
   same thing" check using OwnerClientId silently fails for NPC-vs-NPC or host-vs-NPC. Use
   NetworkObject identity (via NetworkObjectReference over the wire) for equality.

 * REFRESH-ON-CONTACT TIMERS need refresh value > time between refreshes, or net accumulation
   is zero and the system is a no-op. Applies to status effect refreshes, fall damage immunity
   windows, stagger refreshes — anything time-gated where additions race against decay.

 * MATCH FAMILIAR GAME PATTERNS DIRECTLY: When user references a familiar pattern ("like
   bullet marks in shooters"), implement THAT pattern. Don't invent a new one with projection
   math, fallback raycasts, aim-angle gates. First ground-fire attempt had all three; second
   attempt was 8 lines of raycast-forward-spawn-at-hit and worked for every case.

PHASE 1 + PHASE 2 NOW VERIFIED IN-EDITOR (host only):
 * Dragon breathes fire on zombie → zombie takes damage AND catches fire (BurnStatus VFX visible)
 * Burn persists several seconds after dragon stops breathing; DOT continues ticking
 * Ground patches spawn at hit points on ground, walls, slopes, ceilings via bullet-decal raycast
 * URP Decal placeholder (M_GroundFire_Placeholder, Shader Graphs/Decal shader) renders correctly

NEXT SESSION — START HERE:
 * Commit current state before any new work. Suggested msg: "Burn system Phase 1+2 working:
   NPC ignition, burn accumulation, bullet-decal ground patches on all surfaces"
 * Particle system Simulation Space check on walls — Local vs World. Local = flames follow
   surface normal (correct on walls, may look odd on slopes). Prefab setting, no code.
 * Multiplayer testing: host dragon igniting remote client's NPCs, ground patches on remotes,
   BurnStatus VFX syncing across clients.
 * Human character burn: add BurnStatus component to human prefab, wire fireVFXPrefab,
   pelvisBone, vitalManager. Test dragon-vs-human fire breath.
 * Corpse burn persistence test (_refreshLocked lets current burn finish after death).
 * Low-pri deferred: dwell-timer delayed spawn. Design: track last hit point, increment
   dwell timer while within "same location" radius (~1m), spawn only after threshold (~0.5s),
   reset when aim moves. Separate concern from current code structure.

──────────────────────────────────────────────────────────────────────
11th-April-2026 — Deferred Item Triage & Lifetime Tuning Note
──────────────────────────────────────────────────────────────────────

TRIAGE OF OPEN ITEMS:

 FIXED / DONE:
  * Dragon melee hitbox colliders on paws — DONE.
  * Particle Simulation Space (Local vs World) on ground patch prefab — DONE.

 STILL IN TESTING:
  * Flight remote-client animation sync — host-side looked okay in today's pass,
    needs dedicated client-side testing to confirm fully resolved.
  * Human burn prefab wiring (BurnStatus on human prefab, fireVFXPrefab, pelvisBone,
    vitalManager refs) — in progress, dragon-vs-human fire breath not yet verified.
  * Corpse burn persistence (_refreshLocked lets active burn play out after death) —
    still being validated.

 DEFERRED TO NEXT RELEASE / VERSION:
  * Ground fire breath VFX wobble on the ground (mouth-bone jitter from procedural
    neck tracking shakes VFX forward vector). Partial smoothing already in place.
  * Building/structure burnable states with pre-authored destruction stages
    (intact → burning → charred/collapsed, NavMesh Obstacle carving on collapse).
    May run experiments before committing to approach.
  * Fire propagation between burnables (proximity-ignition loop, timer-based).
  * Dwell-timer delayed patch spawn (see earlier design note). Defer to next release.

PATCH & BURN LIFETIME — NEEDS TUNING (next session, Inspector only):
 * User feedback: ground fire patches and character burns disappear too quickly.
 * No code change — pure Inspector tuning pass.
 * GroundFireSpawner (patch config):
   - patchLifetime: currently 3s → try 6–10s for longer-lingering scorch.
   - patchFadeDuration: currently 1.2s → keep short relative to lifetime so the
     decal stays visually solid and doesn't ghost for most of its life.
 * BurnStatus:
   - maxBurnDuration: raise so sustained fire breath produces a longer visible burn
     on characters/NPCs.
 * DragonFireBreathDamage:
   - burnTimePerTick: currently 1.5s. Raising this extends how long each hit keeps
     the target ignited after the cone leaves them.
 * Tune values live in Play Mode, then bake into prefab defaults.

DRAGON HIT ROOT MOTION — DONE:
 * Switched dragon hit reactions from code-driven rotation to full root motion.
 * New DragonHitRootMotion StateMachineBehaviour on the hit blend tree state
   flips HitRootMotionActive (DragonGroundController) and HitAnimActive
   (DragonDamageAnimator) on enter/exit.
 * DragonGroundController.OnAnimatorMove gained a hit-root-motion branch that
   mirrors the flight branch (owner-only, applies animator deltaPosition/
   deltaRotation; remotes sync via NetworkTransform).
 * DragonDamageAnimator: removed hitTurnSpeed/hitTurnOvershoot/hitRotationDuration
   and the timer-driven rotation logic. Cleanup (alignment yaw sync +
   SyncYawAfterHit) now runs on the true→false edge of HitAnimActive.
 * Unity-side: Bake Into Pose UNCHECKED on Root Transform Rotation and
   Position XZ for all 6 hit clips (Position Y stays baked).

RIGIDBODY INTERPOLATION → EXTRAPOLATE DURING FLIGHT — TODO:
 * Issue: rigidbody Interpolate mode causes visible lag during flight where
   root motion drives position; Extrapolate gives smoother visuals.
 * Plan: have DragonFlightController set rb.interpolation = Extrapolate when
   entering flight and restore Interpolate (or whatever the ground default is)
   on exit. Cache the original mode in OnEnable/Awake so we restore correctly.
 * Apply on owner only — remotes use NetworkTransform interpolation, the
   rigidbody mode is irrelevant there.
 * Watch for: extrapolation can overshoot on sudden direction changes; if
   visible during sharp yaw/pitch reversals, may need to clamp or revert.

GROUND FIRE SPAWN TRAVEL DELAY — DONE:
 * Fixed timing mismatch where quick fire breath taps spawned ground fire
   patches before the breath VFX was visible (raycast was instant, VFX had
   startup ramp).
 * GroundFireSpawner.TrySpawnPatch now delays the ServerRpc by
   hit.distance / flameStreamSpeed via a fire-and-forget coroutine.
 * New serialized field: flameStreamSpeed (default 30 m/s, Inspector tweakable).
 * Coroutine guards: aborts spawn if !IsSpawned or !IsOwner at completion.
 * In-flight flames still land even if breath stops mid-travel (intentional).

CRIT ZONE HIT REACTIONS — DONE:
 * Hit reaction animations now only fire on critical hits (projectile hits a
   CritZoneMarker collider). Damage always applies regardless.
 * New file: Shared/CritZoneMarker.cs — MonoBehaviour marker with future-ready
   damageMultiplier and zoneName fields. Drag onto head/wing/etc colliders.
 * DamageReceiver.ApplyProjectileDamage gains triggerHitAnimation param
   (default false). NotifyHitClientRpc gates OnPlayHitAnimation on this flag.
 * BallistaArrow checks other.GetComponent<CritZoneMarker>() on hit and passes
   isCritical through. Debug.Log includes crit status.
 * Existing melee/RequestDamageServerRpc path unchanged (always triggers anim
   via default param = true).
 * Setup: drag CritZoneMarker onto dragon head collider (or any crit zone).
   Same pattern applies to humans/NPCs later.

STAGGER THRESHOLD SYSTEM — DONE:
 * Replaced instant crit-hit-animation with stagger accumulation.
 * DamageReceiver tracks _staggerAccumulated (server only). Crit-zone hits
   add finalDamage (base damage * critMultiplier) to the accumulator. When it
   crosses staggerThreshold (default 200), hit reaction fires and resets to 0.
 * Crit multiplier now scales both health damage AND stagger accumulation.
   Head (mult 5, base 50) = 250 damage + 250 stagger → instant stagger.
   Chest (mult 1, base 50) = 50 damage + 50 stagger → 4 hits to stagger.
 * Stagger decays after staggerDecayDelay (default 2s) at staggerDecayRate
   (default 50/sec). Prevents infinite chip-away.
 * Non-crit-zone hits: base damage only, no stagger, no anim.
 * All new fields Inspector-tweakable on DamageReceiver.

DRAGON SOUND SYSTEM — DONE:
 * PlaySound(string) added to DragonSoundPlayer — called from animation events
   (Function: PlaySound, String: e.g. "WingFlap", "DragonWalk").
 * Removed timer-driven wing flap cadence (was doubling with anim events).
 * Fixed IsFlying() to use flightController.IsFlightMode (was broken).
 * Wing flap motion gate: tracks wingBone localRotation delta per frame,
   smooths into _wingActivity (deg/sec). PlaySound suppresses wingFlapSoundName
   when _wingActivity < wingMotionThreshold.
 * Solves Unity gotcha: animation events on flap clip still fire during
   crossfade to glide (clip timeline advances even as blend weight drops).
 * Tuned thresholds for default dragon rig: glide peaks ~30 deg/sec, flap
   bottoms ~160 deg/sec. Threshold 80 sits in the gap.
 * Setup: drag wing bone (shoulder-adjacent) into Wing Bone slot on
   DragonSoundPlayer. Leave empty to disable gating.

FIRE BREATH LOOP CROSSFADE — DONE:
 * Replaced single looping AudioSource with two alternating AudioSources.
 * When active source has <fireBreathLoopOverlap seconds of clip left, the
   other source starts a new random clip from the Fire Breath Loop entry.
 * Incoming clip's start masks outgoing clip's quiet tail — no perceived gap
   between chained non-seamless clips.
 * Both sources are created at runtime in StartFireBreathLoop under a child
   GameObject parented to dragon. Destroyed in StopFireBreathLoop.
 * New Inspector field: fireBreathLoopOverlap (default 0.5s).
 * Known limitation: if source clips contain internal quiet sections
   (dynamic one-shot recordings rather than sustained flame loops), user
   will still hear those quiet sections when both random clips coincide in
   quiet portions. Fix is to curate Fire Breath Loop entry to only include
   clips with consistent sustained sound — not a code problem.

FIRE BREATH LOOP SILENCE AFTER FIRST CLIP — DONE:
 * Symptom: after first random clip played, loop went silent. Raising
   fireBreathLoopOverlap to 100s did not help.
 * Root cause: crossfade trigger used full clip.length. If loop clips had
   trailing silence / fade-out (common in non-seamless dragon breath
   recordings), the overlap window would pass the audible portion; the
   "other" source would then be marked isPlaying (still in its own silent
   tail) and the early-return blocked the next start — dead air until the
   unreliable "not playing" safety branch restarted it.
 * Fix in DragonSoundPlayer.UpdateFireBreathCrossfade:
   - New Inspector field fireBreathLoopMaxClipDuration (default 3s).
   - effectiveLength = Min(clip.length, maxDuration).
   - Crossfade now triggers on effectiveLength - time, not clip.length.
   - Before starting next clip on "other", force-stop it if its own
     time >= its effectiveLength (kills silent-tail zombies).
 * Inspector-tunable per project — raise if any clip has real content
   beyond 3s. 999 effectively disables capping.

DRAGON ROAR — DONE:
 * Input: KeyCode roarKey (default R) on DragonCombatController.
 * State: _roarEndTime timer, public IsRoaring => Time.time < _roarEndTime.
 * roarDuration (default 2s) Inspector-tunable; holds IsRoar bool high
   long enough for the AnyState->Roar transition to fire.
 * Re-press blocked while IsRoaring; blocked when animator IsDead.
 * Sound: RoarSoundServerRpc -> RoarSoundClientRpc -> soundPlayer.OnRoar()
   on ALL clients (mirrors MeleeAttack RPC pattern). Sound clip string
   stays "Dragon_Roar" in SoundDatabase.
 * Animator sync in DragonAnimatorController mirrors IsDiving pattern:
   - New [SerializeField] DragonCombatController combatController (auto-
     wired from parent in Awake).
   - netIsRoar NetworkVariable, Owner write permission.
   - LateUpdate writes netIsRoar.Value -> animator IsRoar bool on all clients.
   - UpdateNetworkVariables writes combatController.IsRoaring -> netIsRoar
     on owner.
 * Animator setup: AnyState -> Roar with condition IsRoar == true;
   Roar -> Idle via Has Exit Time. No animation events needed.

21st-April-2026 — DRAGON FIRE BREATH: PARTICLE-COLLISION DRIVEN:
 * Root cause: previous architecture used three independent range knobs
   (DragonFireBreathDamage.coneRange, GroundFireSpawner.maxGroundDistance,
   GroundFireSpawner.forwardProjection [dead]) plus a separate VFX particle
   visual length, all tuned by hand. Symptoms:
   - Stray ground fire patches where the visible flame wasn't landing
     (single forward raycast doesn't model particle gravity, cone spread,
     emitter-velocity inheritance, or smoothed-vs-raw aim direction).
   - Inconsistent zombie damage (4 Hz overlap-sphere + cone-angle gate on
     a smoothed forward vector dropped edge-of-cone hits during sweeps).
 * Fix: VFX particles ARE the source of truth.
   - New FireBreathParticleHandler.cs sits on every PS in the VFX whose
     Collision module is enabled. OnParticleCollision pulls events via
     GetCollisionEvents and forwards (intersection, normal) to:
       * DragonFireBreathDamage.HandleParticleHit(other, point) — adds
         hit NetObjectId to a per-tick HashSet, flushes one ServerRpc
         per tickRate with all unique receivers (dedupes 50-particles-
         on-one-zombie down to one tick of damage).
       * GroundFireSpawner.HandleParticleHit(point, normal) — throttled
         by spawnInterval, ServerRpc -> ClientRpc fan-out spawns the
         patch from the local GroundFirePool (unchanged pattern).
   - Only the OWNER binds receivers. Remote clients still simulate VFX
     locally so they see flame, but their handlers are unbound and inert
     so we don't get N-clients of duplicated damage/patch RPCs.
   - DragonCombatController.SetBreathingFireClientRpc auto-attaches the
     handler to every collision-enabled PS in the spawned VFX and calls
     Bind() on the owner only.
 * Files changed:
   - NEW: CharacterScripts/Scripts/Animal/Dragon/FireBreathParticleHandler.cs
   - REWRITE: DragonFireBreathDamage.cs — removed coneRange/coneAngle/
     hitLayers/maxTargetsPerTick/cam/flightController/Update raycast loop.
     Kept damagePerSecond/tickRate/burnTimePerTick. Added HandleParticleHit.
   - REWRITE: GroundFireSpawner.cs — removed forwardProjection (dead),
     maxGroundDistance, groundMask, flameStreamSpeed, cam, flightController,
     Update raycast. Kept spawnInterval/scatterRadius/patch tuning.
     Added HandleParticleHit.
   - DragonCombatController.cs — added [SerializeField] fireBreathDamage
     and groundFireSpawner refs (auto-found from siblings in Awake).
     SetBreathingFireClientRpc now wires handlers after instantiating VFX.
 * Required Inspector setup on the fire breath VFX prefab particle system:
   - Collision module enabled, Type = World, "Send Collision Messages" on
   - Collision layer mask = ground + character/zombie layers
   - Collision Quality = High (recommended for tight detection on small
     fast particles; Medium can miss thin colliders)
   - Range is now whatever startSpeed * startLifetime gives — increase
     either to extend reach; collision footprint follows automatically.
