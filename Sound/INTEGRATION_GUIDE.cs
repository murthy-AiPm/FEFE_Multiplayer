// ═══════════════════════════════════════════════════════════════
// FEFE SOUND SYSTEM — INTEGRATION GUIDE
// ═══════════════════════════════════════════════════════════════
//
// This file documents every change needed in your existing scripts.
// The sound system scripts are standalone — these are just the
// hook-up points where existing code triggers sounds.
//
// ───────────────────────────────────────────────────────────────
// STEP 1: SCENE SETUP
// ───────────────────────────────────────────────────────────────
//
// 1. Create a SoundDatabase asset:
//    Assets > Create > FEFE/Sound/Sound Database
//    Configure all entries in Inspector (see SOUND_ENTRIES.md)
//
// 2. Create a "SoundManager" GameObject in your scene:
//    - Add NetworkObject component
//    - Add ProximitySoundManager component
//    - Assign your SoundDatabase asset
//    - Mark as DontDestroyOnLoad or place in persistent scene
//    - Add to NetworkManager's NetworkPrefabs list if spawned dynamically
//
// 3. Attach sound components to prefabs:
//    - Humanoid player prefab: FootstepSoundPlayer + CombatSoundPlayer
//    - Dragon prefab: FootstepSoundPlayer (isHeavyFootstep=true) + DragonSoundPlayer
//    - Horse prefab: HorseSoundPlayer
//    - Ballista prefab: BallistaSoundPlayer
//
// 4. Place AmbientSoundZone triggers in the scene:
//    - Large box trigger over the whole map for wind
//    - Forest areas for bird sounds
//    - Near rivers/water for flowing sounds
//    - City area for distant city ambiance
//
// 5. Add SurfaceTag to non-terrain floors:
//    - City stone floors → SurfaceTag(surfaceType="Stone")
//    - Wooden bridges → SurfaceTag(surfaceType="Wood")
//    - etc.
//
// 6. Add MaterialTag to weapon prefabs:
//    - Sword prefab → MaterialTag(materialType="Metal")
//    - Arrow prefab → MaterialTag(materialType="Wood")
//    - Ballista bolt prefab → MaterialTag(materialType="Wood")
//
// ───────────────────────────────────────────────────────────────
// STEP 2: CODE CHANGES IN EXISTING SCRIPTS
// ───────────────────────────────────────────────────────────────
//
// ┌─────────────────────────────────────────┐
// │  BallistaController.cs                  │
// │  File: Scripts/Ballista/                │
// └─────────────────────────────────────────┘
//
// In NotifyFireClientRpc(), add after the existing code:
//
//     [ClientRpc]
//     private void NotifyFireClientRpc()
//     {
//         // ... existing code ...
//         _currentOperator?.OnFired();
//
//         // ► ADD: Play ballista fire sound for all clients
//         var ballistaSound = GetComponent<BallistaSoundPlayer>();
//         if (ballistaSound != null) ballistaSound.OnFire();
//     }
//
//
// ┌─────────────────────────────────────────┐
// │  DamageReceiver.cs                      │
// │  File: Scripts/Human/Combat/            │
// └─────────────────────────────────────────┘
//
// In NotifyRespawnClientRpc(), add at the end:
//
//     [ClientRpc]
//     private void NotifyRespawnClientRpc(Vector3 spawnPos, Quaternion spawnRot)
//     {
//         // ... existing code ...
//
//         // ► ADD: Play respawn sound
//         var combatSound = GetComponentInChildren<CombatSoundPlayer>();
//         if (combatSound != null) combatSound.PlayRespawnSound();
//     }
//
//
// ┌─────────────────────────────────────────┐
// │  HitboxController.cs                    │
// │  File: Scripts/Human/Combat/            │
// └─────────────────────────────────────────┘
//
// In EnableHitbox(), add swing sound:
//
//     public void EnableHitbox()
//     {
//         _active = true;
//         _alreadyHit.Clear();
//
//         // ► ADD: Play swing whoosh sound
//         var combatSound = GetComponentInParent<CombatSoundPlayer>();
//         if (combatSound != null) combatSound.PlaySwingSound();
//     }
//
//
// ┌─────────────────────────────────────────┐
// │  WeaponManager.cs                       │
// │  File: Scripts/Human/Combat/            │
// └─────────────────────────────────────────┘
//
// NOTE: CombatSoundPlayer needs a way to get the active weapon
// GameObject for MaterialTag lookup. Add this public method
// if it doesn't exist:
//
//     /// <summary>
//     /// Returns the active weapon's visual GameObject (for MaterialTag lookup).
//     /// </summary>
//     public GameObject GetActiveWeaponObject()
//     {
//         // Return the instantiated weapon visual, however your
//         // WeaponManager tracks it. Example:
//         return _activeWeaponVisual; // adapt to your field name
//     }
//
//
// ┌─────────────────────────────────────────┐
// │  MountableEntity.cs                     │
// │  File: Scripts/Horse/                   │
// └─────────────────────────────────────────┘
//
// No changes needed — HorseSoundPlayer observes IsMounted state.
// But if you want the horse to neigh when the dragon grabs it:
//
//     // In your dragon eat/grab logic:
//     var horseSound = targetHorse.GetComponent<HorseSoundPlayer>();
//     if (horseSound != null) horseSound.OnDeath();
//
//
// ┌─────────────────────────────────────────┐
// │  DragonFlightController.cs              │
// │  File: Scripts/Dragon/Controller/       │
// └─────────────────────────────────────────┘
//
// DragonSoundPlayer auto-detects flight state from the controller.
// You may need to expose a public property if one doesn't exist:
//
//     // ► ADD if not present:
//     public bool IsFlying => /* your flight state check */;
//
// For fire breath, in whatever method activates it:
//
//     // When fire breath starts:
//     var dragonSound = GetComponent<DragonSoundPlayer>();
//     if (dragonSound != null) dragonSound.OnFireBreathStart();
//
//     // When fire breath ends:
//     if (dragonSound != null) dragonSound.OnFireBreathEnd();
//
// For melee attacks (grounded):
//
//     // When claw attack triggers:
//     if (dragonSound != null) dragonSound.OnMeleeAttack("claw");
//
//     // When bite triggers:
//     if (dragonSound != null) dragonSound.OnMeleeAttack("bite");
//
//     // When tail sweep triggers:
//     if (dragonSound != null) dragonSound.OnMeleeAttack("tail");
//
//
// ───────────────────────────────────────────────────────────────
// STEP 3: SOUND DATABASE CONFIGURATION
// ───────────────────────────────────────────────────────────────
//
// After creating the SoundDatabase asset, populate these entries:
//
// CATEGORY DISTANCES (adjust in Inspector):
//   Quiet:  min=2, max=30
//   Combat: min=5, max=50
//   Loud:   min=20, max=500
//   Global: min=0, max=999999
//
// TERRAIN LAYER MAPPINGS (match your Gaia terrain):
//   Layer 0 → "Grass"     (or whatever your first layer is)
//   Layer 1 → "Dirt"
//   Layer 2 → "Stone"
//   Layer 3 → "Sand"
//   (add more as you add terrain layers)
//
// FOOTSTEP SETS:
//   "Grass" → [grass_step_01, grass_step_02, grass_step_03]
//   "Dirt"  → [dirt_step_01, dirt_step_02, dirt_step_03]
//   "Stone" → [stone_step_01, stone_step_02, stone_step_03]
//   "Wood"  → [wood_step_01, wood_step_02, wood_step_03]
//
// IMPACT MATRIX:
//   Metal + Flesh   → [sword_hit_flesh_01, sword_hit_flesh_02]
//   Metal + Metal   → [metal_clang_01, metal_clang_02]
//   Wood + Flesh    → [arrow_hit_flesh_01, arrow_hit_flesh_02]
//   Wood + Stone    → [arrow_hit_stone_01]
//   Wood + Wood     → [wood_impact_01]
//   Default+Default → [generic_impact_01] (fallback)
//
// SOUND ENTRIES (name, category, clips):
//   "Sword_Swing"            Combat    [swing_01, swing_02]
//   "Block_Clang"            Combat    [block_01, block_02]
//   "Bow_Draw"               Quiet     [bow_draw_01]
//   "Bow_Release"            Combat    [bow_release_01]
//   "Arrow_Whoosh"           Combat    [arrow_whoosh_01]
//   "Dodge_Whoosh"           Quiet     [dodge_01]
//   "Player_Death"           Combat    [death_01]
//   "Player_Respawn"         Global    [respawn_01]
//   "Dragon_WingFlap"        Combat    [wing_flap_01, wing_flap_02]
//   "Dragon_Roar"            Loud      [roar_01, roar_02]
//   "Dragon_FireBreath_Start"Loud      [fire_start_01]
//   "Dragon_FireBreath_Loop" Loud      [fire_loop_01]
//   "Dragon_FireBreath_End"  Loud      [fire_end_01]
//   "Dragon_Landing"         Combat    [dragon_land_01]
//   "Dragon_Takeoff"         Combat    [dragon_takeoff_01]
//   "Dragon_Death"           Loud      [dragon_death_01]
//   "Dragon_Eat"             Combat    [eat_01]
//   "Dragon_Claw"            Combat    [claw_01]
//   "Dragon_Bite"            Combat    [bite_01]
//   "Dragon_TailSweep"       Combat    [tail_01]
//   "Horse_Neigh"            Combat    [neigh_01, neigh_02]
//   "Horse_Snort"            Quiet     [snort_01]
//   "Horse_Death"            Combat    [horse_death_01]
//   "Horse_Mount"            Quiet     [mount_01]
//   "Horse_Dismount"         Quiet     [dismount_01]
//   "Ballista_Fire"          Loud      [ballista_fire_01]
//   "Ballista_Reload"        Combat    [ballista_reload_01]
//   "Ballista_Creak"         Quiet     [creak_01]
//
// ───────────────────────────────────────────────────────────────
// STEP 4: PLACEHOLDER AUDIO CLIPS
// ───────────────────────────────────────────────────────────────
//
// Download free placeholder sounds from:
//   - Freesound.org (CC licensed)
//   - Sonniss GDC bundles
//   - OpenGameArt.org
//   - Unity Asset Store free packs
//
// Minimum clips needed to test the system:
//   - 3 footstep clips (any surface)
//   - 2 sword swing clips
//   - 1 impact clip
//   - 1 dragon roar
//   - 1 wind ambient loop
//
// Once these are in, the full system will be functional.
// Swap in better clips anytime — just replace in the ScriptableObject.
//
// ═══════════════════════════════════════════════════════════════
