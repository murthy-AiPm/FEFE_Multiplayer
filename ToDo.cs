/* 7th-March-2026
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
   Needs investigation: DragonAnimatorController LateUpdate sets FlightMode/Thrust/Yaw/Pitch
   on remotes from NetworkVariables, but something is overriding or the values aren't arriving.
   Suspect: the zero-out block at top of LateUpdate, or UpdateFallAnimParams setting IsFalling=true.
 * Dragon respawn inversion on remotes — partially fixed (applyRootMotion owner-only on respawn),
   may still have edge cases. Loading screen added to respawn flow to mask sync delay.

 */
