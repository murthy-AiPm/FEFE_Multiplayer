# FEFE_Multiplayer / 7 Days Till Dawn

**7 Days Till Dawn** is a Unity 6 asymmetric multiplayer survival-defense prototype. A small team of defenders prepares a castle over a seven-day cycle, then tries to survive a Day-7 orc siege. One player can take the role of the dragon: a powerful wildcard ally who may defend the castle or side with the invading army.

The project began as **FEFE_Multiplayer**, a broader asymmetric siege prototype, and is now evolving into a tighter playtestable game loop built around equipment-based teamwork, castle defense, and a player-controlled dragon.



https://github.com/user-attachments/assets/05c018e5-be55-4011-a0fa-061e8c52bea1



## Why This Project Matters

This is a systems-heavy Unity multiplayer project focused on the kinds of problems that show up in real production work: network ownership, animation synchronization, player/NPC combat, complex character controllers, reusable gameplay architecture, and performance-aware encounter design.

The goal is not just to build isolated mechanics, but to make those mechanics survive together in a real multiplayer playtest: humans, a dragon, enemies, mounts, projectiles, fire damage, vitals, UI, audio, cameras, respawning, and networked character selection all running in the same session.

## Game Overview

- **Genre:** Asymmetric cooperative survival defense
- **Engine:** Unity 6
- **Networking:** Unity Netcode for GameObjects
- **Animation:** Mecanim for the dragon, Animancer-style rule-driven systems for humanoids
- **Camera:** Cinemachine 3.x
- **Current phase:** Phase 1 testing after a Phase 0 design pivot

Players spend the early days scouting, fighting skirmishes, securing resources, and strengthening the castle. The final day converts those choices into a siege: walls, ballistae, NPC defenders, equipment, morale, and dragon loyalty all determine whether the castle survives.

## Core Gameplay Systems

### Player-Controlled Dragon

The dragon is a full multiplayer character, not a scripted boss or simple mount. It supports ground movement, swimming, takeoff, flight, landing, melee attacks, fire breath, hit reactions, procedural body motion, camera switching, owner-only HUD, sound events, and networked animation state.

The dragon uses Mecanim blend trees and NetworkVariable-based state sync so remote clients can see flight, swimming, combat, and persistent states correctly, including late-join recovery for clients entering while the dragon is already airborne.

### Human Defender Stack

Human players use a separate controller stack with camera-relative movement, combat, weapon loadouts, damage reactions, stamina/health vitals, UI, respawn flow, and animation rule evaluation. The project intentionally supports different animation technologies for different character types, which creates realistic integration challenges around sync, combat timing, and shared gameplay contracts.

### Multiplayer Architecture

The codebase uses a consistent ownership model:

- Owner-written NetworkVariables for continuous player state.
- Server-authoritative damage, spawning, vitals, and shared truth.
- ServerRpc to ClientRpc fan-out for one-shot effects such as sounds, attacks, fire patches, and death/respawn events.
- Late-join handling for long-lived states like flight, swimming, mounting, and character selection.

This has been especially important for keeping high-motion characters, like the dragon, believable across clients.

### Combat, Vitals, and Damage

The shared combat layer includes health/stamina vitals, damage receivers, melee hitboxes, projectile damage, critical hit zones, burn damage-over-time, self-immunity checks using NetworkObject identity, and character-specific damage animation bridges.

The same foundations support sword hits, ballista arrows, dragon fire breath, ground fire patches, NPC damage, and death/respawn behavior.

### Ballista, Mounts, and Interaction Systems

The project includes networked ballista operation, arrow projectiles, mountable horses, rider input gating, owner-aware camera behavior, prompt UI, and reusable interaction patterns. These systems are built to work in multiplayer instead of being single-player-only prototypes retrofitted later.

### Sound and Feedback

Audio is driven through a sound database and per-character sound players, including animation-event-driven dragon sounds, combat sounds, footstep/hoofstep playback, terrain surface detection, proximity audio, fire breath loops, wing motion gates, and ambient zones.

## Technical Highlights

- Built a multiplayer dragon controller across ground, air, and water locomotion.
- Implemented owner-authoritative animation sync patterns for high-frequency character state.
- Added server-authoritative damage flows with client-side visual feedback.
- Designed late-join recovery so clients spawn into the correct animation state instead of default idle.
- Created reusable systems for vitals, damage receivers, burn status, critical zones, respawn, and HUD.
- Developed networked character selection and spawn handling with persistent selections across scene transitions.
- Integrated Cinemachine owner-only camera rigs for multiplayer characters.
- Built Phase 1 systems with playtest stability in mind, prioritizing surgical fixes over broad rewrites.

## Current Design Direction

The active game direction is **7 Days Till Dawn**:

1. **Days 1-6:** defenders explore, gather resources, secure sites, build defenses, and influence the dragon.
2. **Day 7:** the orc army attacks the castle in a large-scale siege.
3. **Dragon choice:** the dragon can defend the castle or defect to the orcs, making trust and preparation part of the core tension.

The project intentionally moved away from a fixed five-role class structure. Instead, defender specialization comes from equipment and in-world responsibilities, making the game scale more naturally from solo play to groups.

## What A Hiring Manager Should Notice

This repository shows hands-on work across gameplay engineering, multiplayer architecture, animation systems, technical design, and iterative product thinking. It is not a narrow mechanic demo; it is a connected prototype where systems have to cooperate under real playtest conditions.

The most representative work is in:

- `CharacterScripts/Scripts/Animal/Dragon` - dragon locomotion, combat, animation, camera, UI, and sound.
- `CharacterScripts/Scripts/Human` - humanoid movement, combat, animation, vitals, HUD, and damage.
- `CharacterScripts/Scripts/Shared` - respawn, burn status, critical zones, and reusable combat infrastructure.
- `CharacterScripts/Scripts/Ballista` - turret operation and projectile damage.
- `Multiplayer/Scripts` - session lifecycle, character selection, spawning, networking, menus, and player display systems.
- `Sound` - sound database, surface detection, proximity audio, and per-character sound playback.

## Status

The project is in active Phase 1 testing. Phase 0 playtesting validated the asymmetric dragon/castle-defense concept and led to the current 7-day survival-defense direction. The codebase continues to prioritize playable multiplayer builds, network correctness, and systems that can survive real iteration.
