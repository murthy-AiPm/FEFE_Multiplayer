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
 1st-April-2026 — Dragon Combat + Flight System Session
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
 * Jaw synced over network via netIsBreathingFire NetworkVariable
 * Fire breath VFX — prefab instantiated/destroyed on all clients via ClientRpc (fireBreathVFXPrefab)
 * Fire breath SFX — routed through DragonSoundPlayer (OnFireBreathStart/OnFireBreathEnd)
 * Melee VFX/SFX — spawned on all clients via MeleeAttackClientRpc, sound via DragonSoundPlayer.OnMeleeAttack
 * Right-click spine aim fully disabled for dragon
 * TurnAngle SmoothDamp clamped to ±1 in AnimalGroundController (overshoot fix)

FLIGHT SYSTEM — ROOT MOTION BLEND TREE (IN PROGRESS):
 * Added useFlightRootMotion toggle on DragonFlightController — when ON, new blend tree system; when OFF, old mouse-heavy controls
 * Three animator parameters: Thrust, Yaw, Pitch (all -1 to 1 range, written directly by flight controller)
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
 * DragonGroundController overrides OnAnimatorMove — when FlightRootMotionActive is true, applies animator root motion directly (position + rotation), skips all ground logic
 * DragonGroundController.FlightRootMotionActive flag — set by flight controller during root motion flight
 * AnimalGroundAlignment.SuspendAlignment flag — when true, FixedUpdate bails out entirely (no rotation, no height adjustment)
 * Flight controller sets both flags on entry, clears both + calls SetYawImmediate on exit
 * AnimalGroundController.TurnAngle changed from private set to protected set
 * DragonGroundController.SetFlightAnimParams(thrust, yaw, pitch) — public method for flight controller (currently unused since flight writes directly to animator)

KEY FILES:
 * DragonCombatController.cs — combat modes, head tracking, spine twist, jaw, fire breath VFX/SFX
 * DragonFlightController.cs — flight modes, root motion blend tree params, takeoff/landing
 * DragonGroundController.cs — FlightRootMotionActive flag, OnAnimatorMove override, HasGroundBelow flight check
 * AnimalGroundController.cs — base class, OnAnimatorMove now virtual, TurnAngle now protected set
 * AnimalGroundAlignment.cs — SuspendAlignment flag
 * DragonAnimatorController.cs — writes Pitch to animator from flightController.FlightPitch
 * DragonSoundPlayer.cs — fire breath start/loop/end sounds, melee attack sounds

BONE AXIS NOTES:
 * Neck1 rest Y is 270° world space when body faces 0° — never use bone.right for pitch
 * Pitch axis: rb.rotation * Vector3.right works twist-free, then rotate by head yaw for head-relative pitch
 * Yaw: world Vector3.up works across all neck bones
 * Jaw: local Z rotation (-106.534 closed, -125 open)

NETWORK SYNC:
 * AttackMode (int) — NetworkVariable + ServerRpc/ClientRpc
 * IsBreathingFire (bool) — NetworkVariable + ServerRpc/ClientRpc
 * Head yaw, head pitch, spine twist — NetworkVariables at 20Hz
 * MeleeAttack — ServerRpc → ClientRpc trigger
 * VFX/SFX fire on ClientRpc so all clients see/hear effects
 * Jaw reads netIsBreathingFire on remote clients
 * Flight params (Thrust, Yaw, Pitch, FlightMode) — NOT YET NETWORKED for root motion flight

OUTSTANDING / NEXT:
 * Ground-to-flight transition: dragon clips into ground during jump→hover transition. Needs either:
   - Better jump animation with enough root motion lift
   - Or a small takeoffLiftSpeed value during the jump animation timer
 * Flight blend tree tuning — need more animation clips mapped to blend tree positions
 * Fire breath while walking/flying — design question still open
 * Network sync for root motion flight params (Thrust, Yaw, Pitch)
 * TurnAngle animator parameter occasionally flickers (-2.8 seen) — may be blend tree internal damping
 * IsFalling should be false during FlightMode (HasGroundBelow override handles this but needs verification)
 */
