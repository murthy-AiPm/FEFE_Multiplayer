// ═══════════════════════════════════════════════════════════════════
//  COMBAT SYSTEM INTEGRATION GUIDE
//  Changes needed in your EXISTING scripts to wire up the new system.
//  Apply these as edits — don't replace the whole files.
// ═══════════════════════════════════════════════════════════════════


// ─────────────────────────────────────────────────────────────────
//  1. AnimationRuleSet.cs — Add new BoolParams for combat states
// ─────────────────────────────────────────────────────────────────
//
//  In the BoolParam enum, add these entries:
//
//      Dodging,
//      Blocking,
//      BowDrawing,
//      BowAiming,
//      WeaponSlot0,   // fists
//      WeaponSlot1,   // primary melee
//      WeaponSlot2,   // bow
//


// ─────────────────────────────────────────────────────────────────
//  2. AnimationContext.cs — Expose combat state
// ─────────────────────────────────────────────────────────────────
//
//  Add a field for CombatController:
//
//      public CombatController combatController;
//      public WeaponManager weaponManager;
//
//  Add computed properties:
//
//      public bool Dodging => combatController != null && combatController.IsDodging;
//      public bool Blocking => combatController != null && combatController.IsBlocking;
//      public bool BowDrawing => combatController != null && combatController.IsBowDrawing;
//      public bool BowAiming => combatController != null && combatController.IsBowAiming;
//      public int ActiveWeaponSlot => weaponManager != null ? weaponManager.ActiveSlot : 0;
//


// ─────────────────────────────────────────────────────────────────
//  3. RuleAnimancerDriver.cs — Wire up combat integration
// ─────────────────────────────────────────────────────────────────
//
//  a) Add a field:
//
//      [Header("Combat Integration")]
//      [SerializeField] private CombatController combatController;
//      [SerializeField] private WeaponManager weaponManager;
//
//  b) In Awake(), auto-wire:
//
//      if (combatController == null)
//          combatController = GetComponentInParent<CombatController>();
//      if (weaponManager == null)
//          weaponManager = GetComponentInParent<WeaponManager>();
//
//  c) Add public method for CombatController to set active weapon:
//
//      public void SetActiveWeapon(string profileName)
//      {
//          defaultWeaponName = profileName;
//      }
//
//  d) In LateUpdate(), build context with combat refs:
//
//      var ctx = new AnimationContext
//      {
//          tps = tps,
//          input = input,
//          snapshot = input != null ? input.Snapshot : default,
//          mountController = mountController,
//          combatController = combatController,    // NEW
//          weaponManager = weaponManager,           // NEW
//          ActionId = _actionId,
//          ActionStart = _actionStartEdge,
//      };
//
//  e) In HandleWitcherAttacks(), add stamina gate:
//
//      // At the top of HandleWitcherAttacks:
//      if (combatController != null && !combatController.CanAttack())
//          return false;
//
//  f) In StartRandomSingle() and StartCombo(), consume stamina:
//
//      // In StartRandomSingle, before return:
//      combatController?.ConsumeAttackStamina(false);
//
//      // In StartCombo, before return:
//      combatController?.ConsumeAttackStamina(heavy);
//
//  g) In ConditionMatches(), add the new BoolParam cases:
//
//      BoolParam.Dodging => ctx.Dodging,
//      BoolParam.Blocking => ctx.Blocking,
//      BoolParam.BowDrawing => ctx.BowDrawing,
//      BoolParam.BowAiming => ctx.BowAiming,
//      BoolParam.WeaponSlot0 => ctx.ActiveWeaponSlot == 0,
//      BoolParam.WeaponSlot1 => ctx.ActiveWeaponSlot == 1,
//      BoolParam.WeaponSlot2 => ctx.ActiveWeaponSlot == 2,
//
//  h) In LateUpdate(), skip attacks when CombatController has priority:
//
//      // Before the Witcher attack check:
//      bool combatBusy = combatController != null &&
//          (combatController.IsDodging || combatController.IsBlocking ||
//           combatController.IsBowDrawing || combatController.IsBowAiming);
//
//      if (!_isRemoteClient && !combatBusy && HandleWitcherAttacks(ctx))
//          return;
//


// ─────────────────────────────────────────────────────────────────
//  4. HumanoidController.cs — Read from CombatController
// ─────────────────────────────────────────────────────────────────
//
//  a) Add reference:
//
//      [SerializeField] private CombatController combatController;
//
//  b) Replace SpeedLogic() body:
//
//      protected virtual void SpeedLogic()
//      {
//          // Combat controller takes priority
//          if (combatController != null)
//          {
//              if (combatController.IsActionLocked())
//              {
//                  speed = 0;
//                  return;
//              }
//              if (combatController.IsSlowMovement())
//              {
//                  speed = combatSpeed * 0.5f;
//                  return;
//              }
//          }
//
//          if (playerController.inputController.isCombatMode)
//              speed = combatSpeed;
//          else if (playerController.inputController.isCrouch)
//              speed = crouchSpeed;
//          else
//              speed = walkSpeed;
//      }
//
//  c) In Walk(), use CombatController for facing:
//
//      // In the combat mode branch, check ShouldFaceCamera:
//      if (combatController != null && combatController.ShouldFaceCamera())
//      {
//          transform.rotation = Quaternion.Euler(transform.rotation.x, cam.eulerAngles.y, transform.rotation.z);
//      }
//
//  d) Replace Stamina() method — now handled by VitalManager:
//
//      // Delete the old Stamina() method entirely.
//      // VitalManager handles stamina regen, consumption, and networking.
//      // Sprint stamina drain: add to Update():
//      //
//      //   if (isMoving && isModified && vitalManager != null)
//      //       vitalManager.SetStaminaRegenPaused(true);
//      //   else if (vitalManager != null)
//      //       vitalManager.SetStaminaRegenPaused(false);
//


// ─────────────────────────────────────────────────────────────────
//  5. InputController.cs — Simplify combat input
// ─────────────────────────────────────────────────────────────────
//
//  The old ApplyHumanFromSnapshot() combat logic (fistEquip, swordEquip toggling)
//  is now replaced by CombatController + WeaponManager.
//
//  Keep isCombatMode as a readable flag but let WeaponManager drive it:
//
//      // Replace the slot1Down/slot2Down block in ApplyHumanFromSnapshot with:
//      //
//      //   // Combat mode is now driven by WeaponManager.ActiveSlot
//      //   // CombatController handles slot switching.
//      //   // We just set the flag for backward compat:
//      //   var wm = GetComponentInParent<WeaponManager>();
//      //   if (wm != null)
//      //       isCombatMode = wm.ActiveSlot != 0;
//      //
//      //   // Remove fistEquip, swordEquip, isSheating toggle logic
//


// ─────────────────────────────────────────────────────────────────
//  6. ClientAuthoritativePlayerDriver.cs — Register new scripts
// ─────────────────────────────────────────────────────────────────
//
//  In ApplyOwnershipMode(), also toggle:
//
//      var combat = GetComponentInChildren<CombatController>(true);
//      if (combat) combat.enabled = isOwner;
//
//      // WeaponManager stays enabled on all (needs to respond to NetworkVariable changes)
//      // VitalManager stays enabled on all (server ticks regen, clients sync)
//      // DamageReceiver stays enabled on all (server processes RPCs)
//


// ═══════════════════════════════════════════════════════════════════
//  ANIMATION SET KEYS YOU'LL NEED IN HumanoidAnimationSet
// ═══════════════════════════════════════════════════════════════════
//
//  Base Layer (locomotion):
//      "Locomotion/Idle"
//      "Locomotion/Walk"
//      "Locomotion/Run"
//      "Locomotion/CombatIdle"         — idle with weapon drawn
//      "Locomotion/CombatWalk"         — strafing with weapon
//      "Locomotion/CombatWalkBack"
//      "Locomotion/Dodge"              — dodge roll
//      "Locomotion/BlockIdle"          — holding block
//      "Locomotion/BowAimIdle"         — holding bow at full draw
//
//  Action Layer (upper body masked):
//      "Action/Equip_1H"
//      "Action/Holster_1H"
//      "Action/Equip_2H"
//      "Action/Holster_2H"
//      "Action/Equip_Bow"
//      "Action/Holster_Bow"
//      "Action/Unsheathe"
//      "Action/Sheathe"
//
//  Attack Layer (configured per WeaponAttackProfile in RuleAnimancerDriver):
//      "Attack/Fist_1"
//      "Attack/Fist_2"
//      "Attack/Fist_Kick"
//      "Attack/Sword_Single_1"
//      "Attack/Sword_Single_2"
//      "Attack/Sword_Light_1"          — combo chain
//      "Attack/Sword_Light_2"
//      "Attack/Sword_Light_3"
//      "Attack/Sword_Heavy_1"
//      "Attack/Sword_Heavy_2"
//      "Attack/2H_Single_1"
//      "Attack/2H_Light_1"
//      "Attack/2H_Light_2"
//      "Attack/2H_Heavy_1"
//      "Attack/Bow_Draw"
//      "Attack/Bow_Release"
//
//  These are just naming suggestions — use whatever keys match your clips.
//  The important thing is that WeaponData.weaponProfileName matches
//  WeaponAttackProfile.weaponName in RuleAnimancerDriver.
