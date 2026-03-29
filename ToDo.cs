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
 28th-March-2026 — Dragon Combat System Progress
═══════════════════════════════════════════════════════════════

COMPLETED:
 * Dual attack mode system — Press 1 = Melee, Press 2 = Fire Breath (mutually exclusive, toggle off by pressing same key again)
 * AttackMode changed from bool to int (0=none, 1=melee, 2=firebreath) — animator parameter updated accordingly
 * Melee mode: head (Neck1→Neck4) yaw tracks camera while stationary, left-click triggers melee attack with spine twist
 * Fire Breath mode: head tracks camera in yaw AND pitch, hold left-click = continuous fire breath
 * Head yaw distributed across 5 neck bones (Neck→Neck4) with Inspector-tweakable weights for natural S-curve
 * Head pitch on Neck3 using yaw-rotated rb.rotation * Vector3.right axis — no twist artifacts
 * Head pitch offset (Inspector field) to correct for bone rest pose
 * Spine twist compensation — head yaw subtracts current spine twist so they don't double up
 * Procedural jaw open/close — jawBone Z rotation driven by code, no animation needed
 * Jaw synced over network via netIsBreathingFire NetworkVariable
 * Fire breath VFX — prefab instantiated/destroyed on all clients via ClientRpc
 * Fire breath SFX — routed through DragonSoundPlayer (start + loop + end sounds)
 * Melee VFX/SFX — spawned on all clients via MeleeAttackClientRpc, sound via DragonSoundPlayer
 * Right-click spine aim fully disabled for dragon
 * TurnAngle SmoothDamp clamped to ±1 in AnimalGroundController (overshoot fix)

KEY FILE: CharacterScripts/Scripts/Animal/Dragon/DragonCombatController.cs

BONE AXIS NOTES:
 * Neck1 rest Y is 270° world space when body faces 0° — never use bone.right for pitch
 * Pitch axis: rb.rotation * Vector3.right works twist-free, then rotate by yaw for head-relative pitch
 * Yaw: world Vector3.up works across all neck bones
 * Jaw: local Z rotation (-106.534 closed, -125 open)

NETWORK SYNC:
 * AttackMode (int) — NetworkVariable + ServerRpc/ClientRpc
 * IsBreathingFire (bool) — NetworkVariable + ServerRpc/ClientRpc
 * Head yaw, head pitch, spine twist — NetworkVariables at 20Hz
 * MeleeAttack — ServerRpc → ClientRpc trigger
 * VFX/SFX fire on ClientRpc so all clients see/hear effects
 * Jaw reads netIsBreathingFire on remote clients

OUTSTANDING / NEXT:
 * TurnAngle animator parameter still occasionally flickers / goes out of range (-2.8 seen)
   — C# property is clamped to ±1, may be Animator blend tree internal damping overshoot
 * Fire breath while walking — DESIGN QUESTION:
   Currently fire breath requires stationary (GaitSpeed <= stationaryThreshold).
   Should the dragon be able to breathe fire while walking/trotting?
   If yes: head tracking needs to work during movement (currently returns to zero).
   Neck yaw could follow camera-to-body delta while moving, but blend tree also
   controls the body direction — risk of fighting between procedural neck and
   animation-driven body. Options:
     A) Allow fire breath at walk only (not trot/sprint) — less conflict with blend tree
     B) Allow at all gaits — need to dampen neck yaw contribution so it doesn't fight locomotion
     C) Lock body rotation to camera while breathing fire during movement — dragon walks in aim direction
   Each has trade-offs for gameplay feel. Needs testing.
 */
