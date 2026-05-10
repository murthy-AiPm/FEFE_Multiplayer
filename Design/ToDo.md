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

26th-April-2026 — DRAGON ROLL: DUAL-MODE INPUT + LATERAL SLIDE TRAJECTORY:
 * Roll behaviour rewritten in DragonFlightController. Q/E now branches on
   flight thrust:
     - thrust >  rollMotionMinThrust (default 0.4): one-shot discrete roll.
       KeyDown snaps _rmRoll to ±1, locked for rollDuration. Code drives the
       displacement (forward speed + lateral slide + arc bump).
     - thrust ≤ rollMotionMinThrust: smooth axis like Yaw. Key hold drives
       _rmRoll via MoveTowards(target, rollSmoothing*dt). No code-driven
       displacement — the roll plays visually only.
 * _rmRoll changed from int → float. animator.SetInteger → SetFloat.
   FlightRoll public property is now float. Required after switching the
   animator's Roll parameter from Int to Float so the smooth-axis branch
   can write intermediate values.
 * Trajectory rewrite: previous "circular barrel roll" (rollArcRadius)
   returned to the original lateral line at end of roll, so the dragon
   never actually slid sideways. Replaced with two independent components
   — rollLateralDistance (net sideways displacement, eased via
   (1-cos(π*t))/2) and rollArcHeight (single up-bump, returns to baseline
   via (1-cos(2π*t))/2). Both velocity profiles are zero at the endpoints
   so there's no velocity flick when the roll starts/ends.
 * Roll exit blend (rollExitBlendTime, default 0.3s): code keeps applying
   forward speed (linearly tapered) for this long after _rollTimeRemaining
   hits zero, to cover the animator's exit transition back into BlendFly.
   Without this, root motion was at zero (roll clip is in-place) while
   BlendFly ramped in, producing a visible "speed = 0 for a second" stall.
 * Animator-side smoothness fix (user-side): the BlendFly→Roll destination
   was a direct blend tree, which mixed the roll motion with active flight
   blend weights (Pitch/Yaw/Thrust) every frame and produced a rough
   transition even in the animator preview. Switched to a simple state
   playing a single roll clip — preview is clean.
 * Removed: rollArcRadius (replaced by rollLateralDistance/rollArcHeight),
   rollMinThrust trigger gate (roll input now triggers at any thrust;
   motion is gated separately by rollMotionMinThrust).
 * Files changed:
   - CharacterScripts/Scripts/Animal/Dragon/DragonFlightController.cs —
     fields and roll input/motion logic.
   - Animations/DragonAnimations 2.controller — roll destination state
     swapped from blend tree to simple state; Roll parameter Int → Float.
 * Inspector defaults on DragonFlightController:
   - rollForwardSpeed: 15 m/s
   - rollDuration: 0.6 s
   - rollCooldown: 0.5 s
   - rollMotionMinThrust: 0.4
   - rollSmoothing: 3 (≈0.33s to reach ±1 from rest)
   - rollExitBlendTime: 0.3 s (match to the animator's Roll→BlendFly
     transition duration)
   - rollLateralDistance: 4 m
   - rollArcHeight: 2 m

28th-April-2026 — HUMANOID COMBAT LOCOMOTION REFINEMENTS:
 * Combat strafe gate (HumanoidController.Walk): previously "in combat
   mode → strafe always". New gate: `useStrafe = inCombat && (bowAimDraw
   || !sprinting)`. Effect:
     - Sword + walk → strafe (faces camera, 8-direction blend).
     - Sword + sprint → rule-based forward run (no strafe).
     - Bow drawing/aiming → always strafe (speed already capped to walk
       by CombatController.IsSlowMovement, so the sprint key is a no-op
       while the bow is up).
     - Bow held but not drawn + sprint → rule-based forward run.
 * CombatLocomotionMixer reshaped:
     - WeaponLocomotionProfile is now walk-only (8 cardinal+diagonal
       clip keys). The per-profile run mixer was removed — sprint is
       no longer driven by the mixer at all.
     - New BowLocomotionProfile (slot 2 only) holds two independent
       8-direction mixers: noAim (bow held) and aim (drawing or
       aiming). SelectMixer(activeWeaponSlot, bowAimDraw) picks the
       right one with a fallback to whichever variant is built.
     - WantsControl gained an `isSprinting` arg and short-circuits
       false when sprinting (any weapon), so sprint always falls
       through to the rule system for a forward run clip.
     - UpdateAndPlay signature now (layer, moveInput, activeWeaponSlot,
       bowAimDraw). RuleAnimancerDriver passes `ctx.BowDrawing ||
       ctx.BowAiming` for the last arg.
 * Sprint dodge override (RuleAnimancerDriver): when the dodge mixer
   starts with sprint held and a non-zero move vector, the input
   passed to PlayDodge is forced to (0, 1) so the front-dodge clip
   plays. The clip's root motion carries the player in their current
   facing direction — which already aligns with movement during a
   sprint — instead of trying to dodge sideways while running.
 * Dodge step now also blocked while sprinting (CombatController
   HandleIdleInput): added `!_input.modifiedHeld` to the dodge-step
   gate (was previously only blocked while bow equipped). Avoids
   alt-tap producing a tiny dodge-step that fights the sprint run.
 * Fist combat gated behind enableFistCombat (CombatController, default
   false). When false:
     - Idle handler no longer flips IsFistCombatMode on first
       unarmed primary press.
     - CanAttack returns false when ActiveSlot == 0 (the slot check,
       not the weapon ref — WeaponManager may still hand back a fist
       weapon for slot 0).
   Net effect: pressing primary while unarmed is a no-op. Used to
   "disable melee combat" while client testing of the new sword/bow
   locomotion is pending. Re-enable by ticking the flag in the
   Inspector once fist clips and stamina costs are tuned.
 * Added CombatController.IsBowEquipped (true when the active weapon's
   type is Bow). Saves callers a WeaponManager.GetActiveWeaponType()
   round-trip.
 * Bow upper-body avatar masks tightened (UpperBody_Bow.mask,
   UpperBody_Bow 1.mask) so spine-aim IK on chest/upperChest doesn't
   fight the bow draw/aim clips. No code change — mask asset edits.
 * Files changed:
   - CharacterScripts/Scripts/Human/Animation/CombatLocomotionMixer.cs
   - CharacterScripts/Scripts/Human/Animation/RuleAnimancerDriver.cs
   - CharacterScripts/Scripts/Human/Combat/CombatController.cs
   - CharacterScripts/Scripts/Human/Controller/Human Controllers/HumanoidController.cs
   - CharacterScripts/Scripts/Human/Animation/AnimationData/BaseAnimationRuleSet.asset
   - CharacterScripts/Scripts/Human/Animation/AnimationData/UpperBody_Bow.mask
   - CharacterScripts/Scripts/Human/Animation/AnimationData/UpperBody_Bow 1.mask
 * Status: melee disabled pending client/host testing of strafe +
   sprint + bow flow over the network. Re-enable enableFistCombat
   once that pass is clean.

═══════════════════════════════════════════════════════════════
 28th-April-2026 — Dragon: pause-menu hover, hover keys, ground-fire trigger
═══════════════════════════════════════════════════════════════

DRAGON PAUSE-MENU HOVER — DONE (logged retroactively, commit 21st-April):
 * Symptom: opening the Esc pause menu while flying froze the dragon mid-
   flap. The animator kept its current pose (BlendFly with non-zero
   thrust/yaw/pitch), so unpausing snapped through whatever was held.
 * Root cause: `DragonFlightController.Update` early-returned on
   `PauseMenu.IsPaused`, leaving `_rmThrust/_rmPitch/_rmYaw` and the
   matching animator floats at their last in-flight values.
 * Fix in DragonFlightController.Update:
   - New private `_wasPauseMenuPaused` edge tracker.
   - On the rising edge (entered pause this frame) zero `_rmThrust`,
     `_rmPitch`, `_rmPitchTarget`, `_rmYaw` and write 0 into the matching
     animator floats so the BlendFly tree transitions to the glide/hover
     pose naturally. Animator.speed is NOT zeroed here — the blend tree
     keeps running so it can ease into the rest pose.
   - The original `if (PauseMenu.IsPaused) return;` still fires after the
     edge handler so input is still suppressed for the rest of the frame.
 * Files changed:
   - CharacterScripts/Scripts/Animal/Dragon/DragonFlightController.cs
   - Sound/DragonSoundPlayer.cs (related VFX/SFX tweaks made the same day)

GROUND FIRE PATCH — TRIGGER DISPATCH FIX — DONE (commit 21st-April):
 * Symptom: dragon's ground fire patches were spawning correctly on every
   client but zombies walking through them took no damage. Player
   characters did take damage.
 * Root cause: Unity's `OnTriggerStay` requires at least one of the two
   parties to have a Rigidbody. The player has a CharacterController
   which carries its own dispatch path, so player-vs-trigger worked.
   Zombies are NavMesh-driven with NO Rigidbody, so zombie-vs-trigger
   silently never fired despite the colliders overlapping.
 * Fix in GroundFirePatch.Awake:
   - If the patch GameObject has no Rigidbody, add a kinematic one
     (`isKinematic = true; useGravity = false`). Kinematic = no forces,
     no gravity, won't move — purely a dispatch enabler so OnTriggerStay
     fires regardless of who walks in.
 * Also added (debug-only, can stay in for now):
   - `[SerializeField] bool debugLogging` — when on, logs every
     OnTriggerStay hit and an `OverlapBox` probe of the trigger volume
     on Activate (lists every collider Unity sees, including layer + RB
     status). Bypasses OnTriggerStay so it tells you whether the issue
     is geometry/layers vs trigger dispatch.
   - `[ContextMenu("Activate For Testing")]` — drops a GroundFirePatch
     prefab into a scene at runtime and turns it on without involving
     the dragon, GroundFireSpawner, or the pool. Source NetObj is empty
     so BurnStatus self-immunity won't filter it out.
 * Files changed:
   - CharacterScripts/Scripts/Shared/GroundFirePatch.cs

DRAGON HOVER — UP/DOWN KEYS + SMART STATE DETECTION — DONE (commit
26th-April, "added hover"):
 * Replaced the old "press Space to toggle hover" semantics with always-
   on smart hover detection plus optional vertical input.
 * Removed:
   - `KeyCode toggleHoverKey` (was Space).
   - `bool hoverRequested` flag and the velocity-zeroing branch on toggle.
 * Added inputs:
   - `KeyCode hoverUpKey   = Space`        — hold to ascend.
   - `KeyCode hoverDownKey = LeftControl`  — hold to descend.
   - `float hoverVerticalSpeed = 4`        — m/s while either is held.
 * State derivation (every frame, no toggle):
   - `isHoverMode = Mathf.Abs(_rmThrust) < 0.05f` — at-rest thrust is
     hover.
   - `isFlapping  = _rmThrust >  0.05f`
   - `isGliding   = _rmThrust < -0.05f`
 * Vertical input only applies while `_rmThrust <= 0f` (i.e. hovering or
   gliding). Pressing Space while flapping forward does nothing — no
   more accidental jump-up while at full throttle.
 * Why it's better: state used to depend on a sticky toggle that could
   go out of sync with thrust. Now hover is purely a function of
   current thrust, which is itself bounded by user input and
   incrementing logic in the same controller. No flag, no race, no
   late-join sync needed.
 * Files changed:
   - CharacterScripts/Scripts/Animal/Dragon/DragonFlightController.cs

DRAGON FIRE BREATH — LATE-JOIN VISUAL RESTORATION — DONE (commit
26th-April, in the same "added hover" commit):
 * Symptom: a client connecting while a host dragon was already
   breathing fire saw a dragon with no flame VFX, no animator
   IsBreathingFire, and no SFX — even though `netIsBreathingFire.Value`
   was already true on spawn.
 * Root cause: the visual side effects are applied via
   `SetBreathingFireClientRpc`, which only fires on the rising edge of
   `IsBreathingFire`. A late joiner missed that RPC, so the value
   replicated correctly but the animator/VFX/SFX never came up.
 * Fix in DragonCombatController:
   - Extracted the body of `SetBreathingFireClientRpc` into a private
     `ApplyFireBreathVisualState(bool)` so it can be reused.
   - Added `OnNetworkSpawn` override: if `!IsOwner &&
     netIsBreathingFire.Value` calls `ApplyFireBreathVisualState(true)`
     so the late joiner instantly catches up to the current breathing
     state (mirrors the `animator.Play("BlendFly")` late-join pattern
     in `DragonAnimatorController`).
 * Files changed:
   - CharacterScripts/Scripts/Animal/Dragon/DragonCombatController.cs

═══════════════════════════════════════════════════════════════
 28th-April-2026 — Horse mount input gate / mouse-turn / dismount decay / fall predict
═══════════════════════════════════════════════════════════════

HORSE INPUT SUPPRESSION — DONE:
 * Symptom: WASD on the player moved every horse on the map
   simultaneously when the player was on foot. Previous workaround
   (`MountInputController.UpdateControllerState` flipping
   `groundController.enabled = false` on dismount) traded one bug for
   others — controller-disabled-while-mid-air froze the horse, and the
   dismount path slammed the rigidbody to FreezeAll (instant stop, no
   decay, frozen-in-air on a mid-jump dismount).
 * Multiple iterations during the session — flag-based gating
   (`_ignoreInput` set/cleared by `MountInputController`) was racy
   because `OnBecameGrounded` reset the flag every grounding tick and
   FixedUpdate runs before Update each frame, opening a one-tick window
   where input leaked. Final fix: source-of-truth gate.
 * Root cause: input was guarded by a stateful flag instead of asking
   the canonical mount state (`MountableEntity.IsMounted` →
   NetworkVariable-backed riderId).
 * Fix:
   - `AnimalGroundController` gained `protected virtual bool IsInputSuppressed()`.
     Default consults `MountableEntity` if attached:
       * `mountableEntity.IsMounted == false` → suppress.
       * `online && !IsOwner` → suppress.
     Otherwise falls back to legacy `_ignoreInput`. Dragon has no
     `MountableEntity` so it stays on the legacy path — behavior
     unchanged for the dragon.
   - All input read sites (movement axes, sprint, jump key, takeoff key,
     `HandleAirSteering`) routed through `IsInputSuppressed()` /
     a local `bool suppressed` cached at the top of `HandleGroundMovement`.
   - `_ignoreInput` was promoted from `private` → `protected`. The
     `_ignoreInput = false` line in `OnBecameGrounded` was removed —
     it had been harmless dead code but became actively harmful when
     subclasses started writing the flag.
   - The misnamed `StopGradually()` (slammed gait/forward to zero) is
     gone — its only caller was removed.
   - `MountableEntity` field added to `AnimalGroundController`
     (`[SerializeField] protected MountableEntity mountableEntity`),
     auto-found in Awake. The user wired this in the inspector for the
     existing horse prefabs.
 * `MountInputController` is now a near-empty legacy stub. The
   enabled-toggle is gone (controller stays enabled at all times, so
   in-air physics and decay-to-idle keep ticking after a dismount).
   Class is preserved so existing horse prefabs that reference it don't
   break their MonoBehaviour list.
 * `MountController.FinishDismount` no longer calls `StopGradually()`.
 * `AnimalAnimatorController.LateUpdate` lost the now-dead "ramp gait
   when controller disabled" workaround that masked the visual side
   effect of the old enable-toggle approach.

HORSE PREFAB INCONSISTENCY — NOTED, NOT FIXED:
 * Discovery: horse prefabs are split — `HorseFEFEBlack` and
   `HorseFEFEPalomino` carry `HorseGroundController`, while
   `HorseFEFE`, `HorseFEFEBrown`, `HorseFEFEGray`, `HorseFEFEWhite`
   carry `DragonGroundController`. This is why the early
   `HorseGroundController.IsInputSuppressed` override silently
   failed to suppress input on most horses — it was dead code on
   prefabs that didn't have the subclass attached.
 * Solution applied: the gate logic was moved to `AnimalGroundController`
   (the common base) so it works regardless of which subclass each
   prefab happens to carry. Long-term cleanup is to standardize all
   horse prefabs on `HorseGroundController` — `DragonGroundController`
   has flight/swim/dragon-fake-gravity branches that are dead weight
   and potentially active on a horse.

HORSE MOUSE-CAMERA TURN — DONE:
 * Symptom: pressing W on the horse moved it forward but the horse
   never rotated to follow the camera. `cam.eulerAngles.y` was being
   read from a `cam` field that was permanently null.
 * Root cause: horses are scene-loaded NetworkObjects. They run
   `Awake` before any player (and their `MainCamera` prefab) spawns,
   so `cam = Camera.main?.transform` at `Awake` time set `cam = null`
   forever. Latent bug — predates this session, exposed once we
   stopped relying on `MountInputController` enabled-toggling (which
   was masking other behaviors).
 * Fix: `HandleGroundMovement` now refreshes `cam = Camera.main.transform`
   at the top of every call (when `Camera.main` is non-null). Same
   pattern in `HandleAirSteering`. The serialized field is preserved
   as an Inspector override hook.
 * Confirmed working with a one-shot diagnostic log:
   `[HorseRot] mounted=False suppressed=True isOwner=True isActive=True
   cam=MainCamera camYaw=247.3 horseYaw=343.3 cappedYaw=343.3
   groundAlign=OK wantsRotation=False` — every gate read correctly.
 * Idle-while-mounted does NOT auto-rotate the horse: rotation is
   gated on `isMoving` (i.e. WASD held). The user explicitly chose
   this behavior — no mouse-only turn while standing. Pressing W with
   mouse-camera does turn the horse.

DISMOUNT DECAY + MID-JUMP DISMOUNT — DONE:
 * Symptom: dismounting on a moving horse stopped it dead instead of
   gliding to a halt; dismounting mid-jump froze the horse in mid-air.
 * Root causes (two stacked):
   - `MountableEntity.SetMounted(false)` slammed
     `RigidbodyConstraints.FreezeAll` on dismount, locking X/Y/Z and
     rotation. Decay logic in `HandleGroundMovement` still ran but had
     no effect because the rigidbody couldn't move.
   - `AnimalGroundController.OnAnimatorMove` zeroed *all* velocity
     (including Y) whenever the animator delta was small. With root
     motion, this killed gravity every frame after a falling clip
     ended, so even with FreezeAll removed the horse would hover.
 * Fix:
   - `MountableEntity.SetMounted` always uses `FreezeRotation`, never
     `FreezeAll`. The horse won't drift because a riderless horse has
     no input → no animator delta → no horizontal velocity (the X/Z
     zeroing in `OnAnimatorMove` still applies in the idle branch).
   - `OnAnimatorMove`'s idle branch only zeros X/Z, preserves Y.
     Gravity (or the predict-cast fake gravity below) can pull the
     body down. Dragon is unaffected — it uses `useRootMotion = false`
     and goes down the `_pendingRootMotion` accumulator branch.

HORSE FAKE-GRAVITY LANDING PREDICT — WIP (started, not smooth yet):
 * Symptom: horse jumps off a ledge → falls → on contact, body clips
   into the surface; recovery via `AnimalGroundAlignment.AdjustHeight`
   is slow (capped at 0.05 / frame, smoothed) so the touch-down looks
   like a thud followed by a slow rise.
 * Root cause: `AnimalGroundController.FixedUpdate`'s fake-gravity
   branch did an unguarded `rb.MovePosition(rb.position + Vector3.down
   * fallStep)` each step. The grounding system has 0.08s of confirm
   debounce, so `IsGrounded` flips true a few frames *after* contact —
   by which time the body has already overshot.
 * First-pass fix (in code now):
   - Added `[SerializeField] float fakeGravityLandHeight = 0.1f` and
     `[SerializeField] LayerMask fakeGravityGroundMask = ~0` on
     `AnimalGroundController` (under "Fake Gravity (for horse)").
   - The fake-gravity branch now does `Physics.RaycastAll` straight
     down from `rb.position`, ignoring any hit whose `transform.root`
     matches the horse's own root (self-filter). If the predicted fall
     step would put the body below `fakeGravityLandHeight` above the
     ground hit, the step is clamped and `_fallVelocity` is reset to
     zero — so the body lands flush instead of clipping.
   - After the predict-clamped MovePosition, `rb.linearVelocity.y` is
     zeroed so any animator-driven Y from a falling clip doesn't pile
     on top of fake gravity.
 * Status — WIP: the predict-cast clean-lands the contact frame, but
   the user reports the descent itself still feels rough. Likely
   suspects for the next pass:
   - Falling animation root motion has a Y component that still
     contributes via `OnAnimatorMove` even with `linearVelocity.y`
     zeroed (MovePosition is its own integration path).
   - `cliffFallPush` impulse on the cliff → falling transition.
   - `targetBodyHeight` on `AnimalGroundAlignment` is 0.1 on Black —
     may not match the rigidbody's actual rest offset to the hooves,
     so the predict-cast lands the body at the wrong absolute height
     and the alignment still has a residual snap to do on contact.
 * Inspector tuning the user needs to do per horse prefab:
   - Toggle "Use Fake Gravity" on the controller (currently `0` in
     `HorseFEFEBlack.prefab` — the gameplay-time value differs).
   - Set "Fake Gravity Land Height" to the actual rb-origin → hoof
     distance at rest (try 1.0–1.2 first; `targetBodyHeight = 0.1`
     suggests the rb is near hoof level but Black's needs verifying).
   - Uncheck the horse's own layers from "Fake Gravity Ground Mask".
 * Next session: revisit the descent smoothness — likely needs the
   falling animation to be flat-Y (no root-motion in Y) plus possibly
   gating `OnAnimatorMove`'s X/Z velocity write while in fake-gravity
   freefall.
 * Files changed:
   - CharacterScripts/Scripts/Animal/Controller/AnimalGroundController.cs
     `IsInputSuppressed` virtual + MountableEntity wiring + lazy
     Camera.main resolve in HandleGroundMovement / HandleAirSteering +
     `_ignoreInput` made protected + `_ignoreInput = false` removed
     from `OnBecameGrounded` + `StopGradually()` deleted +
     `OnAnimatorMove` Y-preserve in idle branch + new fields
     `fakeGravityLandHeight`, `fakeGravityGroundMask` + predict-cast
     in fake-gravity branch.
   - CharacterScripts/Scripts/Animal/Controller/HorseGroundController.cs
     Stripped to bare override (Awake sets useRootMotion + disables
     animator.applyRootMotion, OnJumpTriggered routes to
     HorseSoundPlayer). All input-gate logic moved to base.
   - CharacterScripts/Scripts/Animal/Horse/MountInputController.cs
     Reduced to legacy stub.
   - CharacterScripts/Scripts/Animal/Horse/MountController.cs
     Removed `horseController.StopGradually()` block in `FinishDismount`.
   - CharacterScripts/Scripts/Animal/Horse/MountableEntity.cs
     `SetMounted` always FreezeRotation (never FreezeAll).
   - CharacterScripts/Scripts/Animal/Controller/AnimalAnimatorController.cs
     Removed gait-ramp-when-disabled workaround (LateUpdate).

═══════════════════════════════════════════════════════════════
 28th-April-2026 — Dragon vitals design (health & stamina)
═══════════════════════════════════════════════════════════════

DRAGON VITALS DESIGN — WIP (design only, no code yet):
 * Design conversation only — no implementation this session. Captured
   in design/FEFE_Design.md under new "## Dragon — Vitals (Health &
   Stamina)" section, between "Dragon Attack Commitment" and the Phase
   descriptions.
 * Core model: stamina (fuel, never lethal) + three independent health
   zones (head, wings, torso). Two zones can kill (head, torso); wings
   cripple but cannot kill.
 * Stamina drains: thrust > ~0.7, fire breath, effective melee.
   Regenerates faster grounded idle/walk, slower airborne low-thrust,
   zero during sprint / firebreath / melee.
 * Cross-vital effects:
   - Head damaged → fire breath costs more stamina.
   - Wings damaged → high-thrust flight costs more stamina; low-speed
     glide/hover stays free regardless of wing HP.
   - Wings at 0 → forced ground combat. Wings regen normally during
     retreat, so flight is technically recoverable.
   - Torso damaged → stamina regen slows (threshold + floor: full above
     ~50% torso, ramps to ~30% floor at 0). Affects ONLY stamina regen,
     NOT zone HP regen.
 * Recovery loop: zones regen when dragon is idle and not fighting /
   running / breathing fire. Dragon must physically disengage. No
   respawn — match ends on dragon death; timer favors dragon stalling.
 * Damage routing: existing CritZoneMarker system (head/wings tagged
   with damageMultiplier; torso is the default for any untagged hit).
   Burn DOT routing deferred.
 * Implementation direction (when we code it):
   - Reuse VitalManager / Vital / VitalDefinition pipeline (humans
     already use this). Dragon = 4 vitals: stamina, head HP, wings HP,
     torso HP.
   - Cross-vital effects live in dragon controllers that read the
     vitals, NOT inside the vital pipeline itself.
   - Owner-write NetworkVariable, matching DragonAnimatorController
     pattern. Anti-cheat hardening revisited post pre-alpha.
 * Open tuning questions (in design doc): exact pool sizes, regen
   rates, drain rates, crit multipliers, threshold values; final
   "high speed" threshold (0.5 vs 0.7); burn DOT routing; damaged-zone
   visual feedback.
 * Files changed:
   - design/FEFE_Design.md
     New "## Dragon — Vitals (Health & Stamina)" section.
   - design/ToDo.md
     This entry.
 * Next session: implementation pass — extend VitalDefinition usage
   for the dragon prefab, wire dragon controllers to read head / wings
   / torso / stamina, route damage through CritZoneMarker into the
   right vital, plumb the cross-vital costs (firebreath, flight thrust,
   stamina regen) through the existing controllers.

DRAGON VITALS DESIGN — FOLLOW-UP DECISIONS:
 * Cross-vital cost curve shape (head→firebreath, wings→flight) —
   matches torso→regen shape (threshold + floor), but inverted (cost
   rises as HP falls). Per-zone threshold/floor values are Inspector-
   tweakable; v1 uses the same values as the torso curve for
   simplicity.
 * "Idle / not fighting" detection for ZONE regen — meaningful
   tightening from earlier draft. Zone HP regenerates only when:
   grounded + idle/walking + not breathing fire + not in melee + not
   sprinting. STAMINA regen has its own (looser) rules — stamina can
   regen airborne at low thrust, but zone HP cannot.
   Design implication: dragon must commit to a LANDING to heal its
   body. Defenders can hunt for the dragon's "nest" / safe landing
   zone — real spatial play, not just an HP bar.
 * Zero-stamina behavior — fire breath hard-gated (cannot initiate);
   melee remains possible but with reduced damage; high-thrust flight
   caps at the no-cost ceiling (~0.7).
 * Vital visibility (HUD) — added new sub-section to FEFE_Design.md.
   Dragon player sees all four vitals at all times. Ranger reads
   dragon's vitals at any range using the existing telescope (already
   part of Ranger kit). Other defenders (Warden, Artificer, Commander)
   can only read vitals when dragon is GROUNDED + close + using a
   telescope-equivalent. Specifics still open: tool acquisition for
   non-Rangers, exact close-range threshold, info granularity (full
   four vitals vs. subset), Commander war-room map read.
 * Files changed:
   - design/FEFE_Design.md
     Added zero-stamina behavior bullets to Stamina section. Added
     cross-vital cost curve paragraph after the torso→regen scaling
     paragraph. Replaced Recovery Loop body with the tighter idle
     definition (grounded required). Added new "### Vital Visibility
     (HUD)" sub-section before the open-questions list. Updated
     open-questions checklist (3 items checked off, 1 added for the
     non-Ranger visibility thread).
   - design/ToDo.md
     This follow-up entry.

DRAGON VITALS DESIGN — EXHAUSTION KILL (THIRD KILL PATH):
 * Dragon player HUD is BUILT. Dragon sees stamina + zone health
   (head, wings, torso) at all times. Was an open question; now
   answered.
 * NEW MECHANIC — defender attacks can drain dragon stamina:
   - ONLY critical hits (head / wing colliders tagged with
     CritZoneMarker) drain stamina. Torso hits do not.
   - Drain is proportional to FINAL damage dealt (post crit-multiplier).
     So head crits drain more stamina than wing crits naturally,
     because head's damageMultiplier is higher. Heavy crits drain more
     than chip crits.
   - BurnStatus DOT does NOT drain stamina (only direct crit impact
     does). Confirmed: dragons can't burn dragons; "no dragon-on-
     dragon dogfights" anyway.
 * NEW KILL CONDITION — exhaustion kill:
   - When a critical hit lands and stamina is ≤ 0 AFTER the crit's
     own drain is applied, the dragon dies.
   - Order of operations: (1) crit lands → (2) zone HP reduced by
     damage × multiplier → (3) stamina reduced by amount proportional
     to final damage → (4) death check on stamina ≤ 0.
   - This means a heavy crit on a low-stamina dragon can be lethal
     in a single hit (the crit kills "on the way down"). No one-hit
     grace period at zero. The dragon's safety margin is the size of
     its current stamina pool.
   - This is a THIRD kill path alongside head=0 and torso=0. Each
     kill path now creates a distinct strategic shape:
       Head focus  = burst kill (high-multiplier path)
       Torso focus = attrition kill (slow path)
       Exhaustion  = pressure-the-dragon path (force firebreath /
                     high-thrust spending, then crit closes it out)
   - Cross-vital effects FEED the exhaustion kill: head damage →
     firebreath costs more stamina, wings damage → flight costs more
     stamina, torso damage → stamina regen is slower. Damage to ANY
     zone now pushes toward exhaustion, not just toward its own
     zone-zero kill. This is why the design feels tight — every
     defender contribution matters even if they're not landing the
     final blow.
 * Stamina now describes "Lethal in combination with a crit" instead
   of "Never lethal" (earlier wording was wrong post-exhaustion-kill).
 * Files changed:
   - design/FEFE_Design.md
     Stamina section: split drains into "dragon actions" vs "defender
     attacks" (new); replaced "Never lethal" line with "Lethal in
     combination with a crit". Three Health Zones lead-in: changed
     "Two paths to a kill" to "Three paths". Added new "### Exhaustion
     Kill — The Third Kill Path" sub-section between Three Health
     Zones table and Wings at Zero. Damage Routing: added crit→stamina
     bullet + burn-DOT-no-stamina note. Vital Visibility: confirmed
     dragon HUD is built. Open Questions: checked off zone-bleed
     question and dragon-HUD question; added new item for empty-
     stamina danger-window feedback.
   - design/ToDo.md
     This entry.
 * Next discussion thread (user-flagged): non-Ranger vital visibility
   — what tool the Warden / Artificer / Commander use to read dragon
   vitals when grounded + close, exact close-range threshold, info
   granularity (full four vitals vs. subset), Commander war-room map
   read.

30th-April-2026
DESTRUCTIBLE WALLS — DINOFRACTURE EVALUATION (PARKED FOR LATER):
 * User asked whether DinoFracture
   (https://www.dinofracture.com/doc/latest/index.html) could give us
   pre-authored destructible castle walls. Reviewed the asset's
   quickstart + tutorials. Outcome: good fit, idea parked for later
   testing — not on the active backlog.
 * Why it fits the "preauthored, not complex" requirement:
   - Pre-Fractured Geometry component fractures the mesh in-editor
     and saves chunks as a prefab under FractureMeshes/. Zero runtime
     fracture cost.
   - Drop-in helpers: Fracture On Collision, Explode On Fracture,
     Play Sound On Fracture. All Inspector-tweakable (matches our
     "serialize everything" rule).
   - Custom slice planes produce cleaner masonry-style breaks than
     the default Voronoi shatter — better for walls.
   - Auto convex colliders + rigidbodies per chunk.
 * The catch — zero multiplayer awareness. Asset docs make no
   mention of NGO/Mirror/Photon. Without a wrapper, every client
   sees different rubble physics and remotes still see an intact
   wall.
 * Recommended NGO integration (when we pick this up):
   - DestructibleWallNetwork : NetworkBehaviour per wall segment,
     holding NetworkVariable<bool> netIsDestroyed (Owner = server).
   - Damage source (ballista arrow / firebreath / melee) calls
     BreakServerRpc(impactPoint, force) → server flips NetVar →
     BreakClientRpc fans out → each client disables intact mesh +
     collider, instantiates the pre-fractured prefab locally, applies
     AddExplosionForce at impactPoint. Chunks are non-networked,
     cosmetic-only.
   - Late-join: OnNetworkSpawn reads netIsDestroyed and spawns the
     wall in its broken (settled) state — same pattern as
     DragonAnimatorController.OnNetworkSpawn forcing BlendFly.
   - Pool the chunk prefabs, mirroring GroundFirePool, so repeated
     destruction doesn't Instantiate-spike.
   - Keep Num Pieces / Num Iterations low (≈3/3 → 27 chunks per
     wall) so ~10 simultaneous broken walls stay cheap.
 * Why parked: Phase 0 priorities are dragon vitals, AI tiers, and
   the special-building system. Revisit destructible walls once
   those are stable.
 * Files changed:
   - design/FEFE_Design.md
     New "## Parking Lot — Future Tech to Evaluate" section appended
     after Global Open Questions, with the destructible-walls /
     DinoFracture entry (goal, asset, scope, NGO integration sketch,
     why parked).
   - design/ToDo.md
     This entry.

═══════════════════════════════════════════════════════════════
 2nd-May-2026 — Dragon vitals — implementation pass
═══════════════════════════════════════════════════════════════

DRAGON VITALS — INITIAL IMPLEMENTATION (no scene work yet, code-only):
 * Translated the design doc's Dragon Vitals spec into running code on
   top of the existing VitalManager / Vital / VitalDefinition pipeline
   (humans already use this; reused without changes to Vital.cs).
 * Four vitals on the dragon: stamina + three zones (head, wings,
   torso). Head and torso are kill vitals (killOnDepleted=true);
   wings is not (cripples but doesn't kill). Stamina has its own
   exhaustion-kill path via DamageReceiver (see below).
 * Cross-vital math (firebreath cost ↑ with head damage, flight cost ↑
   with wings damage, stamina regen ↓ with torso damage) lives in a
   new dragon-only DragonStaminaController, NOT in the base Vital
   pipeline — keeps the shared pipeline character-agnostic.
 * Damage routing reuses the existing CritZoneMarker. Promoted the
   marker's existing-but-unused `zoneName` field to a routing key.
   No CritZoneMarker code change — head colliders get
   zoneName="head", wing colliders zoneName="wings". Untagged hits
   route to defaultVitalID on DamageReceiver (set "torso" for the
   dragon prefab; remains "health" for humans).
 * Exhaustion kill: when a crit lands, DamageReceiver drains stamina
   proportional to final damage (critToStaminaRatio, default 1:1).
   If stamina is depleted after the drain, fires a new
   VitalManager.TriggerDeath() helper. No fake-deplete-a-vital hack.

CROSS-AUTHOR STATE READING (server vs owner):
 * DragonStaminaController runs server-only (where ApplyDamage lives)
   but needs to read owner-driven state (firebreath active, flight
   thrust, gait/sprint). Solution matches existing pattern: owner
   pushes via NetworkVariable (already done for these), and we
   exposed public Net* read accessors on the existing components so
   the server can read them on remote-owned dragons:
   - DragonCombatController.NetIsBreathingFire
   - DragonAnimatorController.NetFlightMode, NetFlightThrust
   - AnimalAnimatorController.NetGaitSpeed
   No new NetworkVariables added — just public surfacing of values
   that were already synced.

OWNER-FACING GATES (hard caps):
 * CanFireBreath false at zero stamina → DragonCombatController gates
   firebreath input (mid-breath stamina depletion releases firebreath
   on the next input frame because Input.GetKey + CanFireBreath()
   re-evaluate every frame in HandleCombatInput).
 * MaxFlightThrust drops to highThrustThreshold (~0.7) at zero
   stamina → DragonFlightController clamps _rmThrust each frame.
 * WingsBroken (wings vital depleted) → DragonFlightController
   refuses EnterFlight() and force-ExitFlight() if currently flying.
   Wings auto-regen via standard zone-regen path; flight unlocks
   once back above zero.

REGEN GATING:
 * Stamina auto-regen DISABLED on the Dragon_Stamina asset
   (regenEnabled=0, regenOnlyWhenGrounded=0). DragonStaminaController
   drives stamina restore manually each tick with full state
   awareness (grounded/airborne, thrust band, torso-curve scaling).
   Accumulates locally, flushes to VitalManager at 20 Hz to avoid
   per-frame NetworkList traffic.
 * Zone HP auto-regen STAYS ON in the asset, but
   DragonStaminaController calls SetRegenPaused(true) on head/wings/
   torso whenever the dragon is airborne, sprinting, or breathing
   fire. Recovery only resumes after landing + idle. Standard
   regenDelay=5 inside Vital handles "recently damaged" cooldown.

CURVES:
 * Two curve helpers (CostScale, RegenScale) inside
   DragonStaminaController. Both are threshold + floor:
     above curveThreshold (default 0.5) → multiplier = 1.0
     below threshold → linearly ramps to curveFloor (default 0.3) at HP=0
     CostScale returns 1/multiplier (cost rises ~3.33x at zero HP)
     RegenScale returns multiplier directly (regen drops to 30% at zero HP)
   Single threshold/floor pair shared across head/wings/torso for v1
   per design ("we let the threshold and floor values match the torso
   curve for simplicity at v1; per-zone tuning lives on the Inspector
   and gets dialled in playtest"). If we want per-zone curves later,
   split the field into three pairs.

FILES CHANGED:
 * NEW assets (Multiplayer/CharacterData/Combat/):
   - DragonHead.asset (+ .meta)        — vitalID="head",  300 HP, killOnDepleted, regenRate=30, regenOnlyWhenGrounded
   - DragonWings.asset (+ .meta)       — vitalID="wings", 500 HP, NOT killOnDepleted, regenRate=50, regenOnlyWhenGrounded
   - DragonTorso.asset (+ .meta)       — vitalID="torso", 1000 HP, killOnDepleted, regenRate=50, regenOnlyWhenGrounded
   (Existing DragonHealth.asset is now orphan — leave or delete in Unity once prefab is rewired.)
 * MODIFIED assets:
   - Multiplayer/CharacterData/Combat/Dragon_Stamina.asset
     regenEnabled: 0 (manual via DragonStaminaController),
     regenOnlyWhenGrounded: 0 (controller handles grounded/airborne
     tier itself).
 * NEW script:
   - CharacterScripts/Scripts/Animal/Dragon/DragonStaminaController.cs (+ .meta)
     Server-side cross-vital math. Public API: CanFireBreath,
     MaxFlightThrust, WingsBroken, MeleeReducedDamage, StaminaNormalized,
     OnMeleeAttackServer().
 * MODIFIED scripts:
   - CharacterScripts/Scripts/Human/Combat/DamageReceiver.cs
     Added defaultVitalID, critStaminaVitalID, critToStaminaRatio,
     exhaustionKillEnabled fields. ApplyProjectileDamage now takes
     optional zoneVitalID, routes by zone, drains stamina on crit,
     triggers exhaustion-kill via VitalManager.TriggerDeath().
     RequestDamageServerRpc (sword path) routes via defaultVitalID.
   - CharacterScripts/Scripts/Human/Combat/VitalManager.cs
     Added TriggerDeath() public server-only helper.
   - CharacterScripts/Scripts/Ballista/BallistaArrow.cs
     Passes critZone.ZoneName through to ApplyProjectileDamage.
   - CharacterScripts/Scripts/Animal/Controller/AnimalAnimatorController.cs
     Public NetGaitSpeed accessor.
   - CharacterScripts/Scripts/Animal/Dragon/DragonAnimatorController.cs
     Public NetFlightMode, NetFlightThrust accessors.
   - CharacterScripts/Scripts/Animal/Dragon/DragonCombatController.cs
     Public NetIsBreathingFire accessor. CanFireBreath() consults
     DragonStaminaController. MeleeAttackServerRpc drains stamina.
   - CharacterScripts/Scripts/Animal/Dragon/DragonFlightController.cs
     EnterFlight blocked when WingsBroken. Mid-flight forced ExitFlight
     when wings break. Per-frame thrust clamp uses MaxFlightThrust.
   - CharacterScripts/Scripts/Animal/Dragon/DragonUI.cs
     Replaced one-fill MonoBehaviour with four-fill MonoBehaviour:
     stamina horizontal + head/wings/torso radial. Subscribes to
     VitalManager.OnVitalChanged. Legacy SetStamina(current,max)
     preserved. Auto-find via [SerializeField] vitalManager OR
     runtime BindVitalManager(vm) for scene-Canvas wiring.

PREFAB WORK STILL TO DO IN UNITY (next session):
 * On DragonPlayer_Network prefab — VitalManager component:
   replace vitalDefinitions array entries with the four new assets
   in this order: Dragon_Stamina, DragonHead, DragonWings, DragonTorso.
   (Order doesn't actually matter functionally — vitals are looked up
   by string ID — but matching this order keeps the Inspector tidy.)
 * Add DragonStaminaController component to the dragon prefab root
   (sibling of VitalManager, DragonAnimatorController, etc.).
   References auto-find via GetComponent in Awake but verify in
   Inspector. Tune drain/regen rates against playtest.
 * On DamageReceiver:
     defaultVitalID = "torso"
     critStaminaVitalID = "stamina"
     critToStaminaRatio = 1.0 (start)
     exhaustionKillEnabled = true
 * Author CritZoneMarker on the head + wing colliders:
     head colliders → zoneName = "head"
     wing colliders → zoneName = "wings"
   Existing DamageMultiplier values stay (they're per-collider).
 * Wire DragonUI fills (head/wings/torso/stamina Image references)
   on the HUD. If HUD is inside the dragon prefab, set the
   vitalManager field directly. If HUD is on a scene Canvas,
   call BindVitalManager(localDragon.GetComponent<VitalManager>())
   from whatever finds the local-owner dragon (typical owner-aware
   spawn callback).

OPEN QUESTIONS (still in design doc):
 * All numeric tuning — stamina pool, drain/regen rates, curve
   threshold/floor, melee drain per attack, critToStaminaRatio,
   high-thrust threshold (0.5 vs 0.7). All Inspector-tweakable;
   tune in playtest.
 * Burn DOT routing (deferred per design — currently routes to
   defaultVitalID = "torso" via DamageReceiver fallback path,
   no stamina drain because critMultiplier=0).
 * Visual feedback for damaged zones (broken-wing flap anim, smoking
   head, scorched torso) — animation/VFX work, separate pass.
 * Empty-stamina danger-window UI cue.
 * Non-Ranger vital visibility (deferred design thread).

═══════════════════════════════════════════════════════════════
2026-05-03 — Wing-flap-gated stamina drain + DragonWingActivityTracker extraction

ROOT CAUSE
 High-thrust flight stamina drain was gated only on `Mathf.Abs(thrust) > highThrustThreshold`
 in DragonStaminaController, which stays true during a wings-tucked dive (player holds
 forward/down with thrust ~0.8 but no actual wing work). Result: the dragon paid stamina
 for diving — the cheapest, most efficient flight maneuver in the kit.

 The "are the wings actually flapping" signal already existed inside DragonSoundPlayer
 (smoothed wing-bone angular velocity, threshold 80 deg/sec, runs in LateUpdate on all
 peers) but it was a private field used only to gate flap audio.

FILES CHANGED
 + CharacterScripts/Scripts/Animal/Dragon/DragonWingActivityTracker.cs   (new)
 ~ Sound/DragonSoundPlayer.cs                                            (delegate to tracker)
 ~ CharacterScripts/Scripts/Animal/Dragon/DragonStaminaController.cs     (gate drain on tracker)

FIX
 1. Extracted wing-bone tracking into a dedicated MonoBehaviour
    `DragonWingActivityTracker`. Plain MonoBehaviour, not NetworkBehaviour — pure local
    computation from animator-driven bone, runs on every peer. Public:
      - `WingActivity` (smoothed deg/sec)
      - `IsFlapping`   (activity > flapThreshold; returns true when wingBone is null
                       so consumers fall back to pre-tracker behavior)
      - `FlapThreshold`
    Defaults match the previous DragonSoundPlayer values: threshold=80, smoothing=8.

 2. DragonSoundPlayer no longer owns wing tracking. Removed:
      - wingBone / wingMotionThreshold / wingMotionSmoothing fields
      - _wingActivity / _lastWingRotation private state
      - the LateUpdate that sampled the bone
    Replaced with `[SerializeField] DragonWingActivityTracker wingActivityTracker`,
    auto-found in Awake. Flap-sound gate now reads `!wingActivityTracker.IsFlapping`.

 3. DragonStaminaController gained a `wingActivityTracker` ref and gates the high-thrust
    flight drain on `wingsFlapping`:
      bool wingsFlapping = wingActivityTracker == null || wingActivityTracker.IsFlapping;
      if (inFlight && Mathf.Abs(thrust) > highThrustThreshold && wingsFlapping)
          drain += highThrustDrainRate * CostScale(_wings);
    Fire-breath drain is unchanged (just `firebreathing`, no wing gate).

INSPECTOR WIREUP REQUIRED (manual)
 On the dragon prefab root (DragonPlayer_Network):
  - Add component: DragonWingActivityTracker
  - Drag the same wing bone (formerly on DragonSoundPlayer.wingBone) into its
    `wingBone` field. Threshold/smoothing default to 80 / 8.
  - DragonSoundPlayer and DragonStaminaController auto-find the tracker via
    GetComponent in Awake, so their `wingActivityTracker` Inspector fields can be
    left empty (or wired explicitly for clarity).
 Removed serialized values that Unity will silently drop on next import:
  wingBone, wingMotionThreshold, wingMotionSmoothing on DragonSoundPlayer.

NOTES
 * IsFlapping defaults to true when no wing bone is wired so a missing/unwired tracker
   doesn't silently disable flap audio or stamina drain. Both consumers degrade to the
   pre-refactor behavior in that case.
 * The tracker runs on every peer (no IsOwner guard) so the server's animator-driven
   wing motion is what gates the server-authoritative drain — consistent with how
   DragonSoundPlayer's gate already worked.

═══════════════════════════════════════════════════════════════
2026-05-03 — Stamina drain/regen rework + exhaustion smoothing + thrust % HUD

Continued from the wing-tracker extraction earlier in the day. After playtesting
the binary thrust > 0.7 drain gate, walked through five connected refinements.

ROOT CAUSES (one paragraph each)

 1. Hover/dive paid the wrong amount of stamina. Wings-tucked dive at 90% thrust
    was free (✓ from the wing-flap gate fix), but a hard-flap hover at 0% thrust
    cost full stamina, and a steep 50% climb cost nothing. Thrust threshold alone
    was the wrong axis — the real cost signal is "wings doing work to push the
    dragon" which is approximated well by |thrust| × flapEffort.

 2. Stamina didn't regen on a tucked dive. The airborne regen branch was gated on
    `|thrust| <= highThrustThreshold` from the old drain logic. Under the new
    effort-based drain, a dive (thrust 0.9, flap 0) drains nothing AND regens
    nothing. Stale gate.

 3. Firebreath sound + jaw stuttered at zero stamina. Owner-side per-frame check
    was correct (`CanFireBreath` returned false on `IsDepleted`), but as soon as
    regen pushed stamina above 0 (1s after last drain), CanFireBreath flipped
    true and breath restarted. Sub-second restart loop was perceived as continuous
    sound and a fluttering jaw.

 4. Flight anim jerked at the moment of exhaustion. `MaxFlightThrust` was a
    binary 1.0 → 0.7 snap, so `_rmThrust` clamp transitioned in one frame and the
    Mecanim Thrust param moved through the blend tree as a step.

 5. At zero stamina the dragon could still climb hard. `_rmPitch` was driven
    purely from camera angle, no exhaustion-aware cap. Player could pull the
    nose up to +1 (full climb) at the depleted-thrust ceiling and gain altitude
    almost as well as at full power.

FILES CHANGED
 ~ CharacterScripts/Scripts/Animal/Dragon/DragonStaminaController.cs
     • Drain formula: peakRate × |thrust| × flapEffort × CostScale(wings).
       New SerializeFields: lazyFlapDegPerSec (60), hardFlapDegPerSec (200).
       highThrustThreshold no longer used for drain (still used for stamina-cap
       and regen tier — left in place for those).
     • Regen: dropped the `|thrust| <= highThrustThreshold` gate. _regenCooldown
       already gates "while paying"; airborne tier picks regenAirborneLowRate
       whenever the cooldown has elapsed.
     • Firebreath lockout: new `_fireBreathLockedOut` bool latches on depletion,
       clears once stamina recovers above `fireBreathRearmStamina` (default 100).
       CanFireBreath now requires both `!IsDepleted` AND `!_fireBreathLockedOut`.
       Maintained on every peer (Update runs above the IsServer guard for this
       block) so owner and server agree without an extra NetworkVariable.
     • Smoothed `MaxFlightThrust`: replaced binary getter with
       `_smoothedMaxFlightThrust` eased toward 1f or highThrustThreshold each
       frame. New SerializeField `thrustCapEaseSeconds` (default 0.5) controls
       the ease window.
     • Smoothed `MaxClimbPitch`: new public property that eases between 1f and
       `exhaustedMaxClimbPitch` (default 0.3) using the same ease window.
       Caps positive pitch only — diving (negative) is never capped.
 ~ CharacterScripts/Scripts/Animal/Dragon/DragonFlightController.cs
     • After computing `_rmPitchTarget` from camera, clamp positive side to
       `staminaController.MaxClimbPitch`. Dive untouched; camera + head tracking
       untouched. The animator pitch input is the only thing capped, so the
       feel is "head straining up, body won't follow" rather than a yanked view.
 ~ CharacterScripts/Scripts/Animal/Dragon/DragonUI.cs
     • New TMP_Text `thrustText` field with auto-find for
       `dragonAnimatorController` (GetComponentInParent in OnEnable).
       Per-frame `UpdateThrustText` reads `NetFlightThrust`, formats as a signed
       integer percent (default `"{0}%"`). Cached `_lastThrustPercent` so the
       text only writes on change.

FIXES (cross-referenced to root causes)

 1. Drain formula
    if (inFlight)
    {
        float thrustMag  = Mathf.Clamp01(Mathf.Abs(thrust));
        float flapEffort = wingActivityTracker != null
            ? Mathf.Clamp01(Mathf.InverseLerp(lazyFlapDegPerSec, hardFlapDegPerSec, wingActivityTracker.WingActivity))
            : 1f;
        float effort = thrustMag * flapEffort;
        if (effort > 0f)
            drain += highThrustDrainRate * effort * CostScale(_wings);
    }
    Behavior table (verified in playtest):
      Hover hard      thrust 0     flap 1.0  → effort 0      → no drain
      Tucked dive     thrust 0.9   flap 0    → effort 0      → no drain
      Slight climb    thrust 0.05  flap 1.0  → effort 0.05   → trickle
      Steep climb     thrust 0.5   flap 1.0  → effort 0.5    → half rate
      Sprint cruise   thrust 1.0   flap 1.0  → effort 1.0    → full rate
      Lazy cruise     thrust 0.3   flap 0.2  → effort 0.06   → tiny

 2. Regen cleanup — single-line change:
    OLD: `if (grounded) ... else if (|thrust| <= cap) ... else 0`
    NEW: `baseRate = grounded ? regenGroundedIdleRate : regenAirborneLowRate;`
    Cooldown already gates "currently paying", so the thrust split was redundant
    and wrong (blocked dive regen).

 3. Firebreath lockout (anti-chatter)
    Per-peer Update block:
        if (_stamina.IsDepleted) _fireBreathLockedOut = true;
        else if (_stamina.Current >= fireBreathRearmStamina) _fireBreathLockedOut = false;
    With default rearm = 100 and airborne regen = 30/s, the dragon waits ~3.3s
    after depletion (1s cooldown + 2.3s regen) before firebreath comes back.
    Grounded ~2.25s.

 4. Smoothed thrust cap
    `_smoothedMaxFlightThrust` Mathf.MoveTowards toward
    (IsDepleted ? highThrustThreshold : 1f) at rate
    `(1 - highThrustThreshold) / thrustCapEaseSeconds` per second. Owner's
    flight controller clamps `_rmThrust` to this each frame, so the animator
    Thrust param walks smoothly through the blend tree.

 5. Climb pitch cap
    Same pattern as the thrust cap, sharing `thrustCapEaseSeconds`. Smoothed
    cap eases between 1.0 and `exhaustedMaxClimbPitch` (default 0.3). In
    DragonFlightController, after computing _rmPitchTarget from camera:
        if (staminaController != null)
        {
            float climbCap = staminaController.MaxClimbPitch;
            if (_rmPitchTarget > climbCap) _rmPitchTarget = climbCap;
        }
    Dive (negative) untouched. Camera + head tracking unchanged.

INSPECTOR WIREUP REQUIRED (manual)
 On the dragon prefab root (DragonPlayer_Network):
  - Drag a TMP_Text into DragonUI.thrustText (the field already existed in the
    user's scene before this code change — the script just needed wiring).
  - DragonUI.dragonAnimatorController auto-finds via GetComponentInParent,
    leave empty unless DragonUI lives outside the dragon prefab hierarchy.
 New tunables added to DragonStaminaController (defaults in parens):
  - lazyFlapDegPerSec (60), hardFlapDegPerSec (200)        — drain effort curve
  - fireBreathRearmStamina (100)                           — firebreath rearm
  - thrustCapEaseSeconds (0.5)                             — exhaustion ease
  - exhaustedMaxClimbPitch (0.3)                           — climb cap @ depletion

NOTES & GOTCHAS
 * `dt` name collision: the per-peer Update block sits above the server-only
   block which declares `float dt = Time.deltaTime;`. C# rejects re-declaring
   `dt` in a nested scope when the same name exists in any enclosing scope
   (CS0136), even if the outer declaration appears later in the method. Inner
   ease block uses `capEaseDt` to avoid the conflict.
 * Both caps share `thrustCapEaseSeconds` so depletion reads as ONE coherent
   bog-down event rather than two separate transitions.
 * Cap smoothing runs on every peer for stateless reasons but is only consumed
   by the owner's flight controller. Remote-client values don't matter.

DEFERRED — REVISIT LATER
 * Force-camera-correction on stamina exhaustion. Considered as Option B for
   the climb-pitch fix (rotate the player's view downward when depleted). Not
   implemented — yanking a mouse-controlled camera is risky UX. Option A
   (cap pitch input only, leave camera free) shipped instead. If the animator
   "head straining up, body won't follow" look is too subtle in playtest,
   revisit with a soft camera-pitch nudge — slow rate so it feels like the
   dragon's head pulling the camera, not a snap.

═══════════════════════════════════════════════════════════════
2026-05-04 — Orc AI design session (no code yet, pickup point for next session)

Design discussion only. No source files changed except design docs.

CONTEXT
 Started designing the orc AI. The Phase 0→1 pivot needs orcs as the primary
 enemy type (FEFE_Design.md → 7DaysTillDawn_Design.md). Current state of NPC
 AI in the project: BearAI.cs is shipping and being repurposed for zombies;
 nothing for orcs.

DECISIONS LOCKED
 1. Spawn-on-arrival, NOT abstract simulation. Orc camps exist as data
    records (location, member count, alive/dead state); GameObjects only
    instantiate when a player enters the zone. Skips the active-zone abstract-
    tick system from FEFE_NPC_Architecture.md (that doc was scoped to the
    prior FEFE direction).
 2. Phase 1 ceiling = 20-orc squad. Typical camp 6-8.
 3. Use Mecanim + NetworkAnimator (NOT Animancer). Matches BearAI's pattern.
 4. NavMesh for pathfinding — designed for scale. Path planning split from
    locomotion: agent.updatePosition/updateRotation = false; root motion
    drives position via OnAnimatorMove (BearAI's existing pattern).
 5. Architecture: clone BearAI → OrcAI as a fork (not subclass). Static
    _attackerCounts dictionary survives the fork — mixed zombie+orc crowd
    control still works.
 6. Inspector toggle: [SerializeField] bool useRootMotion (one-line at top
    of OnAnimatorMove, falls back to NavMeshAgent's own movement when off).
 7. Per-orc AI structure: HFSM at top (Patrol/Combat/Flee/Stagger/Dead) +
    Utility AI inside Combat for action selection (Block / Parry / Attack /
    Reposition / Approach / Jump). Flat enum FSM does not scale to the orc
    feature list (~12 states with N×N transitions).
 8. Squad-level intelligence (formation slots, morale, retreat triggers)
    lives in a separate OrcSquadCoordinator entity, networked at squad
    granularity.

UPDATED SCALE PARAMETERS (late in session — affects networking architecture)
 Game design now anticipates:
   - 4-5 players, each fighting ~20 orcs (= ~100 orcs total)
   - Each player commanding 10-20 NPCs (= 50-100 commanded NPCs)
   - Players in DIFFERENT locations (interest management filters cross-zone)
 Reframed: relevant metric is ENTITIES VISIBLE PER CLIENT, not world total.
   - Typical week case: ~40-50 entities per client. NGO comfortable.
   - Climax (Day-7 siege): convergence event, ~100-150 entities visible.
     This is the only meaningful stress case. Squad-level rep + custom anim
     sync target this case.
 Two-tier orc AI decided:
   - Tactical orc (week encounters): HFSM + Utility AI, individual NetworkObjects.
   - Crowd orc (climax): GPU-instanced crowd asset (candidate: Enemy Masses
     Standard, asset evaluation deferred to climax work). Wave-state synced,
     instances rendered locally, ~5-10 squad entities + ~100 local instances.
     Climax networked entity count drops ~200 → ~30-50.
 NGO/FishNet decision deferred to convergence-test gate. Optimizations in
 priority: (1) Interest management - typical-case savior; (2) Custom anim
 sync; (3) Squad-level replication; (4) Tick-rate scaling.

DOCS UPDATED
 ~ Design/OrcAI.md      — full Phase 1 design, BearAI clone plan, HFSM/Utility
                          structure, layered architecture (squad coord +
                          individual + action execution).
 ~ Design/ARCHITECTURE.md — new section 20 "NPC AI" documenting BearAI's
                          path-planning/locomotion split, _attackerCounts
                          static, fragility notes, plus a forecast block
                          for the orc plan. Discovery summary bumped to 21.
 ~ Design/7DaysTillDawn_Design.md — new "The Day-7 Siege" section with wave
                          shape (Probe/Main/Breach/Final), scope targets
                          (300-500 concurrent crowd, ~1000-1500 spawned,
                          60-80 networked tactical), behaviors (wall
                          scale, destruction, giants, archery aggregation),
                          three real cost ceilings.
 + Design/EnemyMasses_Asset.md — new dedicated asset doc. Confirmed feature
                          set from gitbook docs (5000+ render, per-instance
                          damage/death, NavMesh primary, Mecanim+Crowd
                          Animator). BYO networking workstream
                          (INetworkSkillAuthority / DamageAuthority /
                          CommandAuthority interfaces, ~1-2 weeks NGO bridge).
                          Two paid deps: GPUI Pro + Crowd Animator Addon.
                          10 numbered eval prototype targets. Wall climb
                          supported per author YouTube demos (validate
                          during eval). API quick reference extracted.

NEXT SESSION PICKUP
 Phased v1 → v4 plan in OrcAI.md. v1 starts here:
   1. Clone BearAI.cs → OrcAI.cs in CharacterScripts/Scripts/Orc/.
   2. Author OrcAnimations.controller (humanoid clips) with same Mecanim
      params as BearAI: Speed (float), Attack (trigger), Dead (bool).
   3. Inspector-rename biteHitbox → weaponHitbox; wire to a HitboxController
      on the sword/axe child.
   4. Add useRootMotion bool toggle at top of OnAnimatorMove.
   5. Test: one orc, flat plain, NavMesh baked, player runs in.
 Decisions still open BEFORE squad work begins:
   - Detection LoS raycast vs pure radius (BearAI uses pure radius — orcs
     in walled terrain will sense through walls). Cheap to add.
   - applyRootMotion per-clip via SMB vs always-on globally.
   - Weapon for v1: single sword? sword+shield? mixed loadouts later.
 Big architectural decision deferred until v3 (squad work):
   - Stress test required BEFORE committing to the 200-260 entity scope on
     NGO. FEFE_NPC_Architecture.md already flagged this; the scale update
     this session makes it urgent. Build the stress test (5 clients, 200
     dummy networked entities, 20 minutes) before squad replication design.

2026-05-07 - Orc Phase 1.5 HFSM combat implementation

ROOT CAUSE
 Phase 1 had not been implemented yet, but the simple BearAI-style "one attack"
 orc would be a throwaway step. The faster path is to create the first OrcAI
 directly with the Phase 1.5 combat vocabulary: attack selection, block, parry,
 reposition, recover, stagger, and death, while keeping BearAI's server-owned
 NavMesh/root-motion pattern.

FILES CHANGED
 + CharacterScripts/Scripts/Orc/OrcAI.cs
     - New server-authoritative tactical orc controller. Uses top-level
       Patrol/Combat/Stagger/Dead state and sub-states Idle/Wander/Return/
       Approach/Reposition/Attack/Block/Parry/Recover/Stagger/Dead.
     - Serialized OrcAttackOption array drives attackIndex, range, weight,
       duration, cooldown, hitbox timing, and heavy/light context.
     - Keeps BearAI patterns: NavMeshAgent plans, optional root motion applies
       movement in OnAnimatorMove, manual gravity, separation nudge, leash,
       target detection, attacker-count crowd gate, VitalManager death hook,
       DamageReceiver events, and NetworkAnimator-friendly Animator params.
     - Animator contract: Speed, Dead, CombatState, AttackIndex, Attack, Block,
       Parry, Stagger.
 + CharacterScripts/Scripts/Orc.meta
 + CharacterScripts/Scripts/Orc/OrcAI.cs.meta
 + CharacterScripts/Scripts/Human/Combat/IDamageDefenseProvider.cs
 + CharacterScripts/Scripts/Human/Combat/IDamageDefenseProvider.cs.meta
     - New small defense-provider interface and DamageDefenseResult enum so
       NPCs can expose server-authoritative block/parry without pretending to
       be CombatController.
 ~ CharacterScripts/Scripts/Human/Combat/DamageReceiver.cs
     - Added OnDamageParried event.
     - Server damage RPC now asks IDamageDefenseProvider first. Parry resolves
       to zero damage; block resolves through the existing block reduction and
       stamina cost path.
     - Existing human block path remains supported, with the frontal cone
       recomputed on the server.

FIX
 One orc can now be wired as a tactical enemy prefab: add OrcAI, NavMeshAgent,
 NetworkObject, NetworkAnimator, Animator, VitalManager, DamageReceiver, and a
 weapon HitboxController. The AI chooses between valid weighted attacks,
 defensive block/parry responses when the target appears to attack, reposition
 when crowded too close, and recovers back to approach.

INSPECTOR / UNITY WIREUP REQUIRED
 - Create/wire OrcAnimations.controller with Animator params:
   Speed(float), Dead(bool), CombatState(int), AttackIndex(float),
   Attack(trigger), Block(bool), Parry(trigger), Stagger(trigger).
 - Assign playerLayer, groundLayer, weaponHitbox, optional WeaponData, colliders
   to disable on death, and attack option timings/ranges.
 - Test on host with a baked NavMesh. No .csproj/.sln exists in this folder, so
   compile/import validation must happen in Unity.

2026-05-07 - Orc animator controller setup utility

ROOT CAUSE
 Hand-wiring one Animator transition per attack makes the Animator look like the
 state machine, but the intended HFSM lives in OrcAI. The Animator should stay a
 dumb playback graph: OrcAI chooses Attack/Block/Parry/Stagger, then Animator
 plays the matching clip.

FILES CHANGED
 + Editor/OrcAnimatorControllerSetup.cs
 + Editor/OrcAnimatorControllerSetup.cs.meta

FIX
 Added a Unity editor menu command: Window/FEFE/Setup Orc Animator Controller.
 It loads Assets/FEFE/Animations/OrcAnimations.controller, ensures the required
 OrcAI parameters, reuses/creates the Attack state, puts an "Orc AttackIndex
 Tree" BlendTree on it driven by AttackIndex, and creates Any State transitions
 for Attack, Block, Parry, Stagger, and Dead. Attack/Parry/Stagger return to
 locomotion by exit time; Block returns when Block is false.

NOTES
 The tool preserves the existing Attack clip as AttackIndex 0 if one is already
 assigned. Extra attack clips, plus Block/Parry/Stagger clips, still need to be
 assigned in the Animator after running the tool.

2026-05-07 - Orc animator parameter type fix

ROOT CAUSE
 Unity BlendTree blend parameters must be floats. The first setup utility made
 AttackIndex an int because OrcAI conceptually chooses an integer attack slot,
 which produced: "BlendTree uses parameter AttackIndex which is not float type."
 The user's controller also had a Block transition reporting an incompatible
 condition type, which means the parameter had likely been created with the
 wrong type before the bool transition was added.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Writes AttackIndex with Animator.SetFloat instead of SetInteger.
 ~ Editor/OrcAnimatorControllerSetup.cs
     - Ensures AttackIndex is a Float parameter.
     - EnsureParameter now repairs wrong parameter types by removing and
       recreating the parameter instead of only logging a warning.

FIX
 Rerun Window/FEFE/Setup Orc Animator Controller in Unity. The tool will recreate
 AttackIndex as Float and Block as Bool if needed, clearing both Animator errors.

2026-05-07 - Orc root motion split for in-place locomotion and combat lunge clips

ROOT CAUSE
 The first OrcAI used one global useRootMotion toggle. The user's orc pack has
 in-place walk/run clips but combat clips with root motion, so the global toggle
 was the wrong shape: enabled broke locomotion, disabled ignored combat lunges.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Replaced the single useRootMotion field with useLocomotionRootMotion
       (default false) and useCombatRootMotion (default true).
     - NavMeshAgent.updatePosition now changes when the HFSM state changes.
       Idle/Wander/Return/Approach/Reposition use the locomotion setting;
       Attack/Parry/Stagger/Dead use the combat setting; Block/Recover remain
       code/NavMesh controlled.

FIX
 Set useLocomotionRootMotion=false and useCombatRootMotion=true for the current
 orc animations. NavMeshAgent drives walking/running while combat clips can move
 the body through Animator root motion.

2026-05-07 - Orc attack cancel when target dodges out

ROOT CAUSE
 With combat root motion enabled, Attack stayed active for the full clip even
 when the player dodged far outside the selected attack's reach. The orc kept
 playing the attack animation and root motion made it look like the orc floated
 after the player instead of abandoning the whiff and chasing again.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added attackCancelDistanceBuffer, locomotionStatePath, and
       attackCancelFade Inspector fields.
     - UpdateAttack now cancels the attack if target distance exceeds
       activeAttack.maxRange + attackCancelDistanceBuffer.
     - Cancel clears pending hitbox invoke, disables hitbox, unregisters the
       active attacker slot, briefly cools down the attack, crossfades to
       locomotion, and returns the HFSM to Approach.

FIX
 Default cancel buffer is 1.2m beyond the attack's maxRange. For a 2.4m attack,
 the orc cancels and chases if the target gets beyond ~3.6m during the attack.
 If your locomotion state is renamed, update locomotionStatePath on OrcAI.

2026-05-07 - Orc attack cancel re-engage delay

ROOT CAUSE
 The attack cancel fired and returned to Approach, but decisionTimer was often
 already ready to evaluate again, so the orc could immediately choose another
 attack after the locomotion crossfade. That made the cancel look ineffective.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added attackCancelReengageDelay (default 0.75s).
     - Cancelled attacks now set decisionTimer and the cancelled attack's
       cooldown to at least the re-engage delay.
     - Cancelling also resets the Attack trigger before crossfading to
       locomotion, avoiding stale trigger re-entry.

FIX
 After a whiff cancel, the orc must chase/reposition for a short window before
 it can select another attack. Tune attackCancelReengageDelay upward if it still
 retries too fast.

2026-05-07 - Orc NavMesh sliding during attack fix

ROOT CAUSE
 Even with useCombatRootMotion disabled, Attack set agent.updatePosition=true
 because the state was not using root motion. ResetPath alone did not hard-stop
 the NavMeshAgent, so residual path/velocity could continue moving the orc while
 the attack animation played. This looked like the orc sliding and attacking
 while chasing.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added StopAgent/ResumeAgent helpers.
     - Idle and committed action states (Attack/Block/Parry/Recover/Stagger/
       Dead) now hard-stop the NavMeshAgent: isStopped=true, ResetPath,
       velocity zero, nextPosition synced to transform.
     - Movement states (Wander/Return/Approach/Reposition) resume the agent.
     - Attack distance cancel no longer interrupts once the hitbox is active,
       so a valid committed swing can finish its damage window.

FIX
 During attack, movement now comes from combat root motion only when
 useCombatRootMotion=true. If combat root motion is false, the NavMeshAgent is
 stopped and cannot keep sliding the orc toward the target during the attack.

2026-05-07 - Orc dual-wield weapon hitbox support

ROOT CAUSE
 OrcAI only exposed one weaponHitbox, which is not enough for dual-wield orcs.
 Using one oversized hitbox for both weapons would make left/right swings
 inaccurate and hard to tune.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added offHandWeaponHitbox.
     - Added OrcWeaponHitboxSelection enum: MainHand, OffHand, Both.
     - Added hitboxSelection to OrcAttackOption so each attack chooses which
       hand's hitbox is active.
     - Both hitboxes initialize with the same NetworkObject owner and optional
       WeaponData. Attack context is pushed to both before a swing; only the
       selected hitbox or hitboxes enable during the damage window.

FIX
 Dual-wield orcs can now wire one HitboxController per weapon. In the attacks
 array, set hitboxSelection to MainHand for right-hand swings, OffHand for
 left-hand swings, or Both for crossing / dual-slash animations.

2026-05-07 - Orc animation-event hitbox windows

ROOT CAUSE
 Inspector timing fields for hitboxEnableDelay/hitboxActiveTime were hard to
 tune against actual sword contact frames, especially for dual-wield attack
 clips. Animation events are the existing project pattern for precise melee
 windows (humans and dragon paw hitboxes already use this style).

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added useAnimationEventsForHitboxes (default true).
     - Added animation-event callable methods: HitboxEnable/Disable,
       MainHandHitboxEnable/Disable, OffHandHitboxEnable/Disable,
       BothHitboxesEnable/Disable.
     - Timer-based hitbox windows now act as fallback only when
       useAnimationEventsForHitboxes is false.
     - Event methods are server-gated so client-side animation events do not
       produce duplicate damage requests.
 + CharacterScripts/Scripts/Orc/OrcAnimationEventRelay.cs
 + CharacterScripts/Scripts/Orc/OrcAnimationEventRelay.cs.meta
     - Relay for the Animator GameObject. Unity animation events call methods
       on the Animator object, and the relay forwards them to OrcAI on the root.

FIX
 Add OrcAnimationEventRelay to the same GameObject as the orc Animator. On attack
 clips, place events at the contact frames:
   - HitboxEnable / HitboxDisable to use OrcAI.attack.hitboxSelection
   - MainHandHitboxEnable / MainHandHitboxDisable for right-hand-only windows
   - OffHandHitboxEnable / OffHandHitboxDisable for left-hand-only windows
   - BothHitboxesEnable / BothHitboxesDisable for dual-hit windows

2026-05-07 - Orc animation-event attack completion

ROOT CAUSE
 OrcAI still used the attack option duration and distance-cancel path to leave
 Attack while useAnimationEventsForHitboxes was enabled. If the timer or cancel
 path fired before the clip's disable-hitbox event, later animation events ran
 while the HFSM was already in Recover or Approach, which made hitbox windows
 inconsistent and encouraged adding an extra AttackEnd event.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Animation-event attacks now complete from HitboxDisable,
       MainHandHitboxDisable, OffHandHitboxDisable, or BothHitboxesDisable.
     - Added main/offhand hitbox activity tracking so dual-wield attacks only
       complete after all active weapon hitboxes are disabled.
     - Attack duration is now a single safety timeout in animation-event mode.
     - Distance-based attack cancel only applies to the timer-hitbox fallback.

FIX
 Attack clips only need the existing enable/disable hitbox events. The disable
 event is the HFSM edge from Attack to Recover; no additional AttackEnd event is
 required.

2026-05-07 - Orc attack recover duration tuning

ROOT CAUSE
 After an attack completed, OrcAI always entered Recover for a hardcoded 0.2s.
 Recover stops the NavMeshAgent and leaves Animator Speed at 0, so the orc can
 visibly drop into the locomotion idle pose before returning to Approach.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added attackRecoverDuration as an Inspector-tweakable attack setting.
     - Recover now uses attackRecoverDuration instead of a hardcoded 0.2s.

FIX
 Designers can set Attack Recover Duration to 0 for immediate chase/decision
 after the hitbox disable event, or keep a small pause when an attack needs a
 deliberate recovery beat.

2026-05-07 - Orc pre-hitbox attack cancel

ROOT CAUSE
 The animation-event hitbox completion fix disabled distance-based attack cancel
 for all event-driven attacks. That kept disable-hitbox events authoritative, but
 it also meant an orc would stay committed even when the player escaped before
 the weapon hitbox window had opened.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added allowAnimationEventAttackCancelBeforeHitbox.
     - Added attackHitboxWindowStarted tracking.
     - Distance cancel now works for animation-event attacks only before the
       first hitbox enable event; once a hitbox opens, the attack finishes from
       the disable event.

FIX
 Orc attacks can be interrupted when the target dodges away before contact,
 while active/finished hitbox windows still use animation events as the source
 of truth.

2026-05-07 - Orc mid-swing attack cancel

ROOT CAUSE
 Animation-event attacks could only distance-cancel before the first hitbox
 enable event. Once a hitbox opened, the attack was committed until the disable
 event, so a player who escaped during the active swing could still force the
 orc to finish the full sequence.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added allowAnimationEventAttackCancelAfterHitbox.
     - Event-driven attack distance cancel now supports pre-hitbox and
       post-hitbox windows separately.
     - Mid-swing cancel still calls CancelAttackAndApproach, which disables
       weapon hitboxes immediately before returning to Approach.

FIX
 Orcs can cancel a swing after HitboxEnable when the target moves beyond
 activeAttack.maxRange + attackCancelDistanceBuffer. Disable the new toggle if a
 specific attack should always commit once its damage window starts.

2026-05-07 - Dead players no longer stay targetable

ROOT CAUSE
 Player death disabled controllers and started the respawn flow, but the physical
 hit colliders stayed enabled and DamageReceiver continued accepting hits. BearAI
 and OrcAI also relied mostly on a direct health vital lookup, so a dead target
 with active colliders could remain detectable and keep receiving attack/hit
 feedback.

FILES CHANGED
 ~ CharacterScripts/Scripts/Human/Combat/DamageReceiver.cs
     - Added IsDead.
     - Dead receivers now reject local, melee RPC, and projectile damage.
     - Added death-collider handling: enabled non-trigger child colliders are
       disabled on death and restored on respawn.
 ~ CharacterScripts/Scripts/Shared/RespawnController.cs
     - Restores DamageReceiver death colliders on server and clients at respawn.
 ~ CharacterScripts/Scripts/Human/Combat/HitboxController.cs
     - Skips dead DamageReceivers before hit callbacks are fired.
 ~ CharacterScripts/Scripts/Bear/BearAI.cs
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Target validity now respects DamageReceiver.IsDead / VitalManager.IsDead.

FIX
 Dead players stop being valid NPC targets, stop receiving melee/projectile
 damage, and lose their physical hit colliders until respawn reenables them.

2026-05-08 - Orc blocks during attack cooldown

ROOT CAUSE
 After an attack completed, OrcAI went through Recover/Approach while the attack
 option cooldown ticked down. Without a dedicated recovery animation this could
 show awkward idle, and it did not communicate that the orc was guarding between
 swings.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added blockDuringAttackCooldown.
     - Added BeginBlock(duration, fromAttackCooldown).
     - Attack completion can now enter real Block for the attack cooldown
       duration.
     - Post-attack Block returns directly to Approach when its cooldown guard
       ends, while normal utility Block still uses block cooldown and Recover.

FIX
 Orcs can hold the existing block/guard animation during attack cooldown, and
 because this is the real Block state, frontal hits are reduced during that
 guard window.

2026-05-08 - Orc Recover state removed

ROOT CAUSE
 Recover was still present as a generic post-action pause, but the current orc
 combat direction uses Block/guard as the between-action reset. Recover could
 still route the animator back toward idle/locomotion and made the state graph
 harder to reason about.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Removed Recover from OrcSubState.
     - Removed UpdateRecover and all SetState calls to Recover.
     - Block now returns directly to Approach when its timer ends.
     - Parry now routes into Block/guard when its parry window finishes.
     - Attack fallback now returns to Approach if post-attack block is disabled
       or has no cooldown duration.

FIX
 OrcAI no longer emits CombatState 8 / Recover. The combat loop is now Attack or
 Parry into Block/guard, then back to Approach.

2026-05-08 - Orc attack exits when target dies

ROOT CAUSE
 The post-attack animator path expects Attack to flow into Block, but target
 death/leash invalidation can happen while Attack is still playing. In that path
 OrcAI went straight to Return without running attack completion, so the
 animator could remain visually stuck in Attack with Block false.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added HandleInvalidCombatTarget.
     - Target loss now cancels pending hitbox invokes, disables weapon hitboxes,
       clears attack/block animation state, clears post-attack block bookkeeping,
       and crossfades Attack back to locomotion before returning home.

FIX
 If an orc's target dies or the leash invalidates during an attack, the orc exits
 the attack animation cleanly and returns to locomotion/Return instead of waiting
 for the Attack -> Block path.

2026-05-08 - Orc attack option inspector labels

ROOT CAUSE
 OrcAttackOption entries were identified only by attackIndex and tuning values,
 which made larger attack lists hard to scan in the Inspector.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added a blank name field to OrcAttackOption.

FIX
 Each attack option can now be labeled in the Inspector for easier tuning.

2026-05-08 - Tactical orc phase 2 planning

ROOT CAUSE
 Phase 1.5 produced a working tactical OrcAI, but the next work needed a smaller
 roster and a concrete basics-first direction before adding more behavior. The
 design also needed to clarify that an orc leader is a squad role/modifier, not
 a seventh archetype.

FILES CHANGED
 ~ Design/OrcAI.md
     - Added Phase 2 planning for Grunt, Berserker, and Skirmisher.
     - Recorded the full tactical roster: Grunt, Berserker, Skirmisher, Archer,
       Assassin, Giant.
     - Defined Leader as a squad modifier applied to an existing archetype.
     - Locked the next basics to line-of-sight detection and predefined patrol
       patterns before full squad tactics.
 ~ Design/7DaysTillDawn_Design.md
     - Added the reduced tactical roster to the Orcs section.
     - Noted that Archer, Assassin, and Giant are deferred until melee squads
       are stable.

FIX
 The design docs now point the next implementation pass toward LoS, authored
 patrol routes, and simple Grunt/Berserker/Skirmisher tuning, with squad leader
 behavior kept intentionally lightweight.

2026-05-08 - Orc field-of-view detection and pursuit

ROOT CAUSE
 OrcAI used pure radius detection, so orcs could acquire targets through walls
 and had no believable difference between spotting, pursuing, and losing a
 player. The next tactical basics needed vision cone + line-of-sight detection,
 a bounded pursue distance, and a last-known-position search.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Replaced detectionRadius/leashRadius usage with viewDistance,
       viewAngle, closeDetectionRadius, detectionTime, loseSightGraceTime,
       pursueRadiusFromHome, searchDuration, eyeHeight, targetAimHeight, and
       visionObstacleMask.
     - Added FormerlySerializedAs migration for existing detectionRadius and
       leashRadius prefab values.
     - Kept OverlapSphereNonAlloc as a broad phase, then filters candidates
       through field-of-view and line-of-sight raycasts.
     - Added awareness buildup before patrol acquisition.
     - Added combat sight tracking: visible targets refresh last known position,
       hidden targets stay pursued briefly, then the orc moves to the last known
       position, searches, and returns home if it cannot reacquire.
     - Updated perception gizmos for view distance, close detection, FOV edges,
       and pursue radius.
 ~ Design/OrcAI.md
     - Updated the LoS section with the implemented field names.

FIX
 Orcs now detect through a believable perception stack: close-range bubble,
 vision cone, line of sight, awareness buildup, bounded pursuit, and search at
 the last visible target position before returning to patrol/home.

2026-05-08 - Orc ranged hit alert reaction

ROOT CAUSE
 Projectile hits only delivered generic damage feedback to OrcAI, so an orc hit
 by an arrow could not distinguish the shooter/source from a melee hit. The next
 behavior needed orcs to face the arrow source, pursue visible close shooters,
 and guard when the shooter is too far away or not visible.

FILES CHANGED
 ~ CharacterScripts/Scripts/Human/Combat/DamageReceiver.cs
     - Added OnRangedDamageReceived server-only hook.
     - Extended ApplyProjectileDamage with optional hit point, attacker
       NetworkObject, and triggerRangedAlert parameters while preserving existing
       fire/burn projectile-style callers.
 ~ CharacterScripts/Scripts/Ballista/BallistaArrow.cs
     - Looks up the shooter NetworkObject from SetShooter and passes shooter
       position, hit point, attacker object, and ranged-alert intent into
       DamageReceiver.
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added investigateRadiusFromHome and rangedHitGuardDuration.
     - Subscribes to ranged damage alerts.
     - Pursues a visible ranged attacker when the source is within investigate
       radius from home.
     - Otherwise turns toward the hit source, enters Block/guard briefly, can
       reacquire visible targets during the guard, then returns to patrol/home.
     - Suppresses the generic projectile damage stagger immediately after a
       ranged-alert reaction so the alert branch controls the state transition.
 ~ Design/OrcAI.md
     - Documented the ranged hit reaction and deferred cover behavior.

FIX
 Arrow hits now produce a believable source-aware reaction: visible close
 shooters get pursued, while distant or hidden shooters make the orc face the
 shot direction and guard briefly before recovering.

2026-05-08 - Orc ranged source investigation

ROOT CAUSE
 The first ranged-hit alert used investigateRadiusFromHome only as a gate for
 pursuing a visible shooter. If the shooter was hidden but the source was still
 inside investigate range, the orc guarded in place instead of investigating,
 making the field name and behavior misleading.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added BeginInvestigateRangedSource.
     - Ranged hits from inside investigateRadiusFromHome now send the orc to
       the source position when the shooter is not currently visible.
     - Reuses the existing last-known-position search flow: move to source,
       search for searchDuration, reacquire if a target enters FOV/LoS, then
       return home.
     - Sources beyond investigateRadiusFromHome still use guard-only recovery.
 ~ Design/OrcAI.md
     - Updated the ranged hit reaction note to distinguish pursue, investigate,
       and guard-only outcomes.

FIX
 `investigateRadiusFromHome` now means what it says: ranged sources inside the
 radius can trigger movement and search even when the shooter is not visible.

2026-05-08 - Orc investigate radius gizmo

ROOT CAUSE
 The Scene view showed pursue radius but not investigate radius, making it hard
 to tune the new ranged-hit investigation boundary relative to the combat leash.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added a purple/magenta wire sphere for investigateRadiusFromHome in
       OnDrawGizmosSelected.
     - Kept pursueRadiusFromHome red so it reads as the hard combat leash.

FIX
 Orc perception gizmos now show both the red pursue radius and the purple
 investigate radius from home.

2026-05-08 - Orc bounded investigation for long-range shots

ROOT CAUSE
 Shots fired outside investigateRadiusFromHome still produced only a guard
 reaction, so the orc appeared not to investigate even though the shot direction
 was known. The desired behavior is to respect the camp boundary while still
 checking the edge nearest the shot source.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added GetRangedInvestigationDestination.
     - Ranged sources outside investigateRadiusFromHome now clamp the
       investigation destination to the edge of the investigate radius instead
       of falling straight to guard-only behavior.
     - While searching at the bounded destination, the orc faces the original
       shot source direction.
     - Guard-only fallback remains available by setting investigateRadiusFromHome
       to 0.
 ~ Design/OrcAI.md
     - Updated ranged-hit behavior notes for bounded long-range investigation.

FIX
 Long-range arrow hits now make the orc move to the nearest allowed
 investigation boundary, search while facing the shot direction, then return
 home if no target enters FOV/LoS.

2026-05-08 - Orc investigation alert return behavior

ROOT CAUSE
 Bounded long-range investigations still treated any detected player as a normal
 target acquisition, even if the player was outside the investigation boundary.
 The desired placeholder for future squad alerting is for the orc to run back
 home when it spots a player beyond investigateRadiusFromHome during that
 investigation. Investigation movement itself also needed to read as urgent.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added rangedInvestigationActive, boundedRangedInvestigationActive, and
       alertReturnHomeActive state flags.
     - Ranged-source investigation now uses run speed while moving to the source
       or bounded source edge.
     - If a bounded investigation spots a target outside
       investigateRadiusFromHome, the orc cancels pursuit and runs home instead.
     - If no target is spotted, the normal return after search remains a walk.
 ~ Design/OrcAI.md
     - Documented run-speed investigation and alert-return behavior.

FIX
 Long-range ranged-hit investigations now emulate a scout returning to alert the
 camp: the orc sprints to investigate, runs home if it spots a player beyond the
 investigation boundary, and otherwise walks home after an empty search.

2026-05-08 - Orc nearby arrow miss investigation

ROOT CAUSE
 Orcs reacted to direct arrow hits, but near misses that struck terrain or other
 objects inside the orc's close awareness bubble did nothing. That made arrows
 feel silent unless they landed on the orc.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added a server-side OrcAI registry and NotifyNearbyRangedImpact.
     - Added reactToNearbyRangedImpacts.
     - Nearby ranged impacts within an orc's closeDetectionRadius reuse the
       existing ranged-hit investigation/guard behavior.
 ~ CharacterScripts/Scripts/Ballista/BallistaArrow.cs
     - Resolves shooter position/object before damage routing.
     - Calls OrcAI.NotifyNearbyRangedImpact for impacts that do not directly hit
       an OrcAI, so direct hits do not double-trigger the reaction.
 ~ Design/OrcAI.md
     - Documented nearby missed-arrow investigation.

FIX
 Arrows that hit terrain, props, or non-orc objects near an orc now make that
 orc turn toward the shot source and investigate using the same bounded
 ranged-alert flow as direct projectile hits.

2026-05-08 - Orc multiple ranged alert priority

ROOT CAUSE
 Nearby missed-arrow impacts could override the direction of a direct arrow hit,
 and repeated arrows from beyond view distance continued to trigger investigation
 instead of producing the desired "run home to alert camp" placeholder behavior.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added directRangedHitPriorityDuration so direct arrow hits temporarily
       suppress nearby missed-arrow impact alerts.
     - Added outOfViewRangedAlertReturnThreshold and
       outOfViewRangedAlertWindow.
     - Repeated ranged alerts from outside viewDistance now make the orc run
       home via the existing alert-return behavior.
     - Clears out-of-view alert counters when the orc commits to a target or
       completes the alert-return home transition.
 ~ Design/OrcAI.md
     - Documented direct-hit priority and repeated out-of-view ranged alerts.

FIX
 Direct arrow hits now keep priority over nearby misses, and two or more
 out-of-view ranged alerts within the tuning window make the orc run back to
 origin/home to emulate alerting the camp.

2026-05-08 - Orc alert-return ranged alert fixes

ROOT CAUSE
 Alert-return did not have stable priority once it started. Later missed-arrow
 impacts could restart investigation, causing a see-saw between investigating
 and running home. Alert-return also was not allowed to reacquire a player who
 moved back inside investigateRadiusFromHome and normal FOV/LoS.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Alert-return now participates in target acquisition checks.
     - During bounded investigation or alert-return, visible targets outside
       investigateRadiusFromHome keep the orc running home.
     - Visible targets inside investigateRadiusFromHome can be pursued.
     - While alert-return is active, missed-arrow impacts are ignored.
     - Direct hits only interrupt alert-return if the shooter is visible and
       inside investigateRadiusFromHome.
 ~ Design/OrcAI.md
     - Documented alert-return priority, reacquisition, and missed-impact ignore
       behavior.

FIX
 Repeated out-of-view arrows no longer make the orc alternate between
 investigation and alert-return, and a player who moves back inside the
 investigation boundary during alert-return can now be acquired and attacked.

2026-05-08 - Orc ranged pursuit boundary fix

ROOT CAUSE
 Ranged investigation treated investigateRadiusFromHome as enough permission to
 pursue a visible shooter. That let a player inside the investigation circle but
 outside pursueRadiusFromHome cause odd behavior: the orc would begin pursuit,
 immediately fail the combat leash, then walk home instead of using the intended
 alert-return behavior.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Ranged-hit pursuit now requires the visible shooter to be inside both
       investigateRadiusFromHome and pursueRadiusFromHome.
     - Visible ranged threats inside investigate radius but outside pursue
       radius now trigger alert-return instead of pursuit.
     - Patrol/ranged-investigation target acquisition now alert-returns for
       visible targets outside either boundary.
 ~ Design/OrcAI.md
     - Clarified investigate vs pursue boundaries for ranged reactions.

FIX
 Investigation radius now means "allowed to check/confirm," while pursue radius
 remains the combat commitment boundary. If the player is visible but outside
 pursueRadiusFromHome, the orc runs home to alert instead of chasing and then
 walking back.

2026-05-08 - Orc authored patrol modes

ROOT CAUSE
 OrcAI only supported random wander around home, which made camp guards and
 road patrols feel un-authored. The next patrol pass needed Inspector-selected
 modes without disturbing the existing investigation/pursuit override flow.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added OrcPatrolMode with Wander, Loop, and PingPong.
     - Added patrolPoints, randomizePatrolStartPoint, and patrolWaitTimeRange.
     - Idle patrol destination selection now uses authored route points when
       patrolMode is Loop or PingPong, falling back to random wander when no
       valid route exists.
     - Loop wraps through authored points; PingPong reverses at route ends.
     - Added patrol route gizmos with patrolRouteGizmoColor.
 ~ Design/OrcAI.md
     - Updated predefined patrol pattern design with the implemented modes and
       fields.

FIX
 Orcs can now be configured in the Inspector as wandering guards, loop patrols,
 or ping-pong patrols while alert, investigation, pursuit, and return-home
 behavior continue to override calm patrol movement.

2026-05-08 - Orc gizmo colors inspector tuning

ROOT CAUSE
 Pursue radius color was hardcoded in OnDrawGizmosSelected, so it could not be
 adjusted per prefab/archetype from the Inspector.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added serialized Color fields for view distance, close detection, pursue
       radius, investigate radius, wander radius, and preferred combat distance
       gizmos.
     - OnDrawGizmosSelected now reads those Inspector colors instead of
       hardcoded Color values.

FIX
 Orc gizmo colors are now Inspector-tweakable, including pursueRadiusGizmoColor.

2026-05-08 - Humanoid parry and parried-attacker stagger

ROOT CAUSE
 Humanoid defenders had block and hit reaction plumbing, and enemies already
 exposed block/parry through IDamageDefenseProvider, but the human combat path
 had no parry state or way to stagger the attacker when a defender or enemy
 parried a melee hit.

FILES CHANGED
 ~ CharacterScripts/Scripts/Human/Controller/InputController.cs
     - Added secondaryDown to InputSnapshot so tap-secondary can start parry
       while hold-secondary remains block.
 ~ CharacterScripts/Scripts/Human/Combat/CombatController.cs
     - Appended CombatState.Parrying to preserve existing enum order.
     - Implemented IDamageDefenseProvider for humanoid block/parry defense.
     - Added Inspector-tweakable parry duration, active window, cooldown,
       stamina cost, and angle.
     - Tap secondary with melee equipped starts parry; holding secondary after
       the parry duration flows into normal block.
 ~ CharacterScripts/Scripts/Human/Animation/RuleAnimancerDriver.cs
     - Added PlayParry with a serialized parryKey, defaulting to existing
       Sword/Block until a dedicated parry clip is assigned.
 ~ CharacterScripts/Scripts/Human/Combat/DamageReceiver.cs
     - Parried melee hits still deal zero damage.
     - When a defender/enemy parries, the attacker receives a parry stagger
       ClientRpc that reuses the existing human hit reaction Animancer bridge.
 ~ Multiplayer/Scripts/NetworkPlayerMovement/New/AnimanerNetSync.cs
     - Synced humanoid parry state to remote puppets and the server.

FIX
 Humanoid players can now parry from melee guard input with server-authoritative
 zero-damage defense, and an attacker whose melee strike is parried plays the
 existing stagger/hit reaction through Animancer.

2026-05-08 - Orc parry stagger hook and parry damage

ROOT CAUSE
 The initial humanoid parry pass staggered humanoid attackers through
 DamageReceiver's client hit-reaction event, but OrcAI drives stagger through
 its own server state machine and was not subscribed to that callback. Orc
 parry also had no damage knob for future archetypes.

FILES CHANGED
 ~ CharacterScripts/Scripts/Human/Combat/DamageReceiver.cs
     - Added server-side OnParryStaggered event.
     - StaggerFromParryServer now invokes the server event before broadcasting
       the client hit reaction.
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Subscribes to OnParryStaggered and forces the existing OrcState.Stagger /
       OrcSubState.Stagger path.
     - Added serialized parryDamage under Parry, default 0.
     - Successful orc parries apply parryDamage to the attacker when tuned
       above zero.

FIX
 Player parries now push orcs into their authored stagger state, and orc parries
 have an Inspector damage value ready for archetype-specific tuning while
 remaining zero-damage by default.

2026-05-08 - Humanoid parry reliability fix

ROOT CAUSE
 Parry animation was played directly on the Animancer Action layer but the
 layer was not locked, so the normal LateUpdate Action cleanup could fade it
 out almost immediately. Parry defense also depended on the 20 Hz remote combat
 state sync, which made a short active window feel inconsistent under network
 timing.

FILES CHANGED
 ~ CharacterScripts/Scripts/Human/Animation/RuleAnimancerDriver.cs
     - PlayParry now temporarily locks the Action layer and clears the lock
       after parryFadeOutDelay before fading out.
 ~ CharacterScripts/Scripts/Human/Combat/CombatController.cs
     - Added parryServerValidationPadding for Inspector tuning.
     - Owner parry now sends BeginParryServerRpc immediately so server-side
       defense validation does not wait for the 20 Hz animation state sync.

FIX
 Humanoid parry animation should now be visible/reliable, and parry success
 should be less timing-sensitive in networked play.

2026-05-08 - Orc archetype identity field

ROOT CAUSE
 The next tactical orc phase needs Grunt, Berserker, and Skirmisher variants,
 but the code had no explicit identity for prefab variants or future squad
 logic to query without inferring behavior from tuning values.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added OrcArchetype with Grunt, Berserker, and Skirmisher.
     - Added a serialized archetype field on OrcAI, defaulting to Grunt.
     - Added read-only Archetype, IsGrunt, IsBerserker, and IsSkirmisher
       helpers for prefab/squad logic.
 ~ Design/OrcAI.md
     - Documented that archetype is currently identity only; prefab variants
       still own Inspector tuning.

FIX
 Orc prefabs can now declare their tactical archetype in the Inspector, and
 future squad behavior can branch on explicit orc identity without splitting
 the AI into separate scripts or introducing archetype ScriptableObjects early.

2026-05-08 - Orc squad seed controller

ROOT CAUSE
 Tactical orcs could individually detect and balance attacker counts, but camp
 groups had no shared alert/home layer. One orc spotting a player did not give
 nearby squadmates a clean way to wake up, and future leader/skirmisher logic
 needed explicit squad hooks instead of reaching into OrcAI private state.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added read-only squad-facing accessors for current target, home
       position, squad reference, alive state, and combat-target presence.
     - Added server-side SetSquad, SetHomeAnchor, SetPatrolRoute,
       ReceiveSharedTarget, and ReceiveSharedAlert hooks.
     - Preserved externally assigned squad home anchors across OnNetworkSpawn.
 + CharacterScripts/Scripts/Orc/OrcSquadController.cs
     - Added a server-side squad component with serialized roster, optional
       leader, shared home anchor, optional shared patrol route, and target
       broadcast interval/radius.
     - Broadcasts an active member/leader target to nearby alive members while
       leaving OrcAI's existing LoS, leash, attacker-count, and retarget logic
       in control after the wake-up.
     - Leader death weakens target sharing through leaderlessAlertRadiusMultiplier
       instead of breaking the squad.
 + CharacterScripts/Scripts/Orc/OrcSquadController.cs.meta
 ~ Design/OrcAI.md
     - Documented the implemented squad seed behavior and its intentional
       limits.

FIX
 Orc camps can now be wired as lightweight squads: one alerted member can wake
 nearby squadmates around a shared home/patrol setup, while multiple visible
 players or friendly NPCs can still naturally split targets through the
 existing per-orc target-balancing behavior.

2026-05-09 - Orc idle patrol mode

ROOT CAUSE
 Some placed orcs need to act as static guards, but OrcPatrolMode only offered
 Wander, Loop, and PingPong. A guard with no patrol points still fell back to
 random wander instead of simply holding its placed position.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added OrcPatrolMode.Idle at the end of the enum to avoid shifting
       existing serialized prefab values.
     - TryGetNextPatrolDestination now returns no destination in Idle mode,
       leaving the orc in Patrol/Idle until detection, ranged alert, squad
       alert, or combat overrides it.
     - Route gizmos now rely on HasPatrolPoints, and wander radius gizmo only
       draws for Wander mode.
 ~ Design/OrcAI.md
     - Documented Idle patrol mode as the static guard option.

FIX
 Orc prefabs can now select Patrol Mode = Idle to stand in place while still
 retaining normal LoS detection, ranged-alert response, squad wake-up, combat,
 leash, and return-home behavior.

2026-05-09 - Orc squad patrol authoring convenience

ROOT CAUSE
 The first OrcSquadController only accepted a flat member roster and one shared
 patrol assignment, which made it awkward to author mixed camp behavior such as
 one idle guard, one wanderer, and two route patrols. Shared patrol points also
 had no squad-level path gizmo, so route setup had to be inferred from point
 transforms.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcSquadController.cs
     - Replaced the flat OrcAI roster with serialized OrcSquadMemberSetup
       entries containing orc, patrol mode, shared-vs-member route choice,
       optional member patrol points, and per-member randomize start toggle.
     - Auto-filled child orcs now create member setup entries with Inspector
       defaults for patrol mode, shared route usage, and randomize start.
     - Registration assigns each member's selected patrol mode and route
       through OrcAI.SetPatrolRoute.
     - Added selected gizmos for shared patrol paths and member-specific patrol
       paths, with Inspector colors and point radius.
 ~ Design/OrcAI.md
     - Updated the squad seed description with per-member patrol assignment and
       path gizmo behavior.

FIX
 Squad setup can now be authored from one component: each orc can be set to
 Idle, Wander, Loop, or PingPong independently, route patrol members can start
 from randomized patrol points, and patrol paths are visible while the squad
 controller is selected.

2026-05-09 - Orc squad member rebuild context menus

ROOT CAUSE
 Auto-filling squad members only happened automatically when the member list
 was empty, which made adding or removing child orcs awkward after the first
 setup pass.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcSquadController.cs
     - Added Rebuild Members From Children context menu action to recreate the
       roster from child OrcAI components using Auto-Fill Defaults.
     - Added Fill Missing Members From Children context menu action to append
       newly added child orcs without overwriting existing member setup rows.
     - Shared auto-fill creation through CreateDefaultMemberSetup.
 ~ Design/OrcAI.md
     - Noted the roster rebuild/fill context menu workflow.

FIX
 Squad rosters can now be refreshed from the Inspector after child orcs are
 added or removed, while preserving existing per-member patrol settings when
 using the fill-missing workflow.

2026-05-09 - Orc squad auto-fill defaults removed

ROOT CAUSE
 The OrcSquadController Auto-Fill Defaults section made the Inspector harder
 to understand because it only affected newly generated member rows, not the
 squad's live behavior.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcSquadController.cs
     - Removed the Auto-Fill Defaults Inspector section.
     - Generated member rows now use fixed defaults: Wander, use shared patrol
       points, and randomize patrol start.
 ~ Design/OrcAI.md
     - Updated the squad controller note to describe the fixed generated-row
       defaults.

FIX
 The squad controller Inspector is simpler: generated member rows start with
 sensible defaults, and all per-orc patrol behavior is configured directly in
 the Members list.

2026-05-09 - Squad-level orc gizmo visibility

ROOT CAUSE
 Each OrcAI already had a Show Gizmos debug toggle for LoS, detection, chase,
 investigate, wander, and combat-distance gizmos, but squad-authored camps
 required changing that setting one orc at a time.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added SetGizmosVisible so authoring helpers can control the existing
       OrcAI debug gizmo toggle.
 ~ CharacterScripts/Scripts/Orc/OrcSquadController.cs
     - Added Control Member Orc Gizmos and Show Member Orc Gizmos under Gizmos.
     - Applies the member OrcAI gizmo visibility during OnValidate and member
       registration.

FIX
 Selecting a squad controller now gives one Inspector-level switch for showing
 or hiding member orc LoS/radius/combat gizmos, while standalone orcs can still
 use their own OrcAI Show Gizmos toggle.

2026-05-09 - Squad gizmo control simplification

ROOT CAUSE
 The extra Control Member Orc Gizmos toggle made the squad Gizmos section
 harder to understand. The useful authoring control is simply whether member
 orc gizmos are shown.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcSquadController.cs
     - Removed Control Member Orc Gizmos.
     - Show Member Orc Gizmos now directly drives member OrcAI gizmo visibility
       during OnValidate and member registration.

FIX
 The squad controller Gizmos section is simpler: Show Patrol Gizmos controls
 route gizmos, and Show Member Orc Gizmos controls member LoS/radius/combat
 gizmos.

2026-05-09 - Squad patrol path visible from selected points

ROOT CAUSE
 Squad patrol path gizmos only drew from OnDrawGizmosSelected, so selecting a
 patrol point transform hid the connected route and forced authors to reselect
 the squad controller object to inspect the path.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcSquadController.cs
     - Added editor-only Selection checks in OnDrawGizmos.
     - Shared/member patrol paths now draw when any assigned patrol point is
       selected, as well as when the squad controller itself is selected.

FIX
 Clicking a patrol point assigned to an OrcSquadController now shows the
 connected patrol path in Scene view, making route editing easier even when
 the points are separate objects or children of the squad controller.

2026-05-10 - Authored patrol point arrival threshold

ROOT CAUSE
 OrcAI used the general arrivalThreshold for all Patrol/Wander movement,
 including authored Loop/PingPong patrol points. That broader threshold is good
 for wander/return/investigation smoothing, but made route guards stop visibly
 short of their patrol point even when the NavMeshAgent stopping distance was
 zero.

FILES CHANGED
 ~ CharacterScripts/Scripts/Orc/OrcAI.cs
     - Added patrolPointArrivalThreshold under Patrol.
     - Patrol/Wander arrival now uses patrolPointArrivalThreshold only when
       following authored Loop/PingPong patrol points.
     - Random Wander, Return, investigation/search return, and other movement
       continue using the existing arrivalThreshold.

FIX
 Orcs following authored patrol routes now walk much closer to the patrol point
 before waiting, without tightening the broader arrival behavior used by other
 navigation states.

