# FEFE — NPC Architecture & Scale

*Companion document to FEFE_Design.md*
*Last updated: 2026-04-13*

This document captures the NPC/systems architecture decisions for FEFE — how the game scales from "300+ agents in a 0.85 sq km city" to something actually shippable on NGO with 5 players. These are technical decisions, not gameplay design.

---

## Core Constraint

- **Map size:** 0.85 sq km (~920m × 920m)
- **Players:** 5 (4 defenders + 1 dragon, all human-controlled)
- **Networking:** Unity Netcode for GameObjects (NGO), with UGS Relay
- **Target:** ~150-250 networked entities at peak, ~600-1,200 visible "people" per active player view

NGO comfortably handles ~150-250 networked entities at the 5-player count, with proper optimization. Naive replication of 300+ individual agents exceeds NGO's comfortable ceiling. The architecture below stays under the ceiling while still delivering the visual scale of a populated city under siege.

---

## Active-Zone System (Streaming AI)

**Core principle:** Agents exist as data records when no player can see them. They spawn into full simulation only inside active zones around players.

### How it works

- An "agent" is a data record: position, health, state, current order, type
- When far from players, the agent is **abstractly simulated** — data updates (e.g., "this squad is moving to the east gate, ETA 30 seconds") but no GameObject exists
- When inside an active zone, the agent **spawns into the scene** as a full GameObject (animation, AI tick, collision, networking)
- When the player leaves, the agent **despawns** but data persists; abstract simulation resumes from the captured state

### Active-zone calculation

- For each player + the dragon, define a **view radius** (~80m full simulation, ~120m network replication buffer)
- The active region is the **union of all view circles** (not fixed slots per player)
- If players cluster, zones merge → one bigger active region, fewer total simulated agents
- If players spread out, more separate active zones → more simulated agents but covering more ground

### Worst-case modeling

Even with all 4 defenders + 1 dragon converged at one location (Phase 3 climax):
- One large active zone, ~120-150 full agents inside
- Total full-simulation networked agents: ~150 max
- Within NGO's comfortable range

### Best/typical case

- 5 zones spread across the city, ~25-30 agents per zone
- Total full-simulation: ~125-150 agents
- Approximately the same as worst case — the system is bounded by **player attention**, not raw agent count

### Implementation requirements

1. **Active-zone calculation** every frame or every 0.5s — compute union of player + dragon view radii
2. **Agent state system** — each agent knows whether it's currently abstract or full
3. **Transition handlers:**
   - Abstract → Full: spawn GameObject, initialize from data record
   - Full → Abstract: capture current state, despawn GameObject, hand off to abstract tick
4. **Abstract tick** — runs slower (every 0.5s or 1s), updates positions/states with simple rules
5. **Notification layer** — abstract events (combat, deaths, fires) surface on Commander's map and player HUDs

### Abstract combat resolution

When combat happens in an abstract zone (no players watching):
- **Statistical resolution:** roll based on stats. "Squad of 5 spearmen vs. 8 zombies → 70% squad wins, takes 1.5 casualties on average"
- **Notifications:** Commander or relevant player gets pinged ("east squad under attack")
- Players choose to travel there to spawn full simulation, or accept the abstract result

### Dragon as viewer

Wherever the dragon is, that zone is full simulation. The dragon's presence elevates a zone to full simulation immediately — this means defenders can't escape consequences by simply not being where the dragon is.

---

## Three-Layer Civilian System

The city's civilian population is split into three layers, each doing one job well.

### Layer 1: Hero Civilians (~10 total)

- Fully individual, networked entities
- Named, with possibly unique behavior, animation polish, voice
- Story anchors — specific people the Warden saves or loses
- Trigger narrative moments ("the priest reached the shelter")
- Tied to special buildings (priest at church, master smith at blacksmith, etc.) and key civic roles
- **Always full simulation** regardless of zone (small enough count that this is cheap)
- Eligible for dragon's Zombie Shout (raised as named zombies for drama)

### Layer 2: Squad Civilians (~15 squads × ~13 members = ~195 represented)

- Group AI — squads treated as single networked entities
- Each squad represents ~13 visual civilians moving and acting together
- Network entity = the squad object (one), not the members (many)
- Active-zone system applies — abstract when distant, full when nearby
- **The bulk simulation layer** — provides population texture

**Damage model: aggregate HP with member count**
- Squad has total HP (e.g., 13 members × 50 HP = 650 squad HP)
- Squad has current member count (starts at 13)
- Damage drops HP; each HP threshold crossed kills one visible member (animated death, member count -1)
- Squad continues until 0 members
- Eligible for Zombie Shout (proportional to lost members)

### Layer 3: Cosmetic Crowd (~300-500 per active zone, local-only)

- **NOT networked** — each client renders independently
- Visual fill around squads and in active districts
- Cheap rendering (vertex animation textures, GPU instancing, low-poly)
- Movement is simple (waypoint-following, deterministic flocking from synced state)
- **Die deterministically from synced kill volumes** — fires, dragon claws, zombie melee auras
- Each client computes deaths locally; counts may differ slightly between clients but visual chaos is consistent
- **NOT eligible for Zombie Shout** — no networked corpse, no shout fuel

### Distribution proposal

| District | Squads | Hero Civilians |
|----------|--------|----------------|
| Residential quarter | 4 | 3 |
| Market plaza | 3 | 2 |
| Civilian shelter zones | 2 | 1 |
| Workshop / forge area | 2 | 2 |
| Church / civic area | 4 | 2 |
| **Total** | **15** | **10** |

**Total gameplay civilians: ~205**
**Networked civilian entities: 25** (very cheap)
**Visible per active zone (with cosmetics): 600-1,200**

---

## Damage System Per Layer

| Layer | Damage tracking | Death event | Networking | Shout-eligible |
|-------|----------------|-------------|------------|----------------|
| Hero | Per-individual HP | Networked, named | Full | Yes (named zombies) |
| Squad | Aggregate HP + member count | Member-by-member visual | Squad-level updates | Yes (proportional to lost members) |
| Cosmetic | Deterministic kill volumes | Local instant | None | No |

### Synced kill volumes

For cosmetic civilian damage from melee sources:
- Dragon and zombie units have small "killing aura" radii (1-2m)
- Auras are not networked; their existence is implied by the unit's networked position
- Each client locally kills cosmetic civs that enter those auras
- Visually consistent across clients (deaths happen in roughly the same places)

### Fire damage

- Fire volumes are networked (they damage real things)
- Each client's cosmetic civs in the breath/fire volume die locally
- Mass deaths look spectacular; networking cost is just the fire volume itself
- Hero and squad civilians take fire damage via standard damage system

### Example scene

Dragon strafes a market plaza for 4 seconds. Plaza contains:
- 1 hero civilian (a named merchant)
- 1 civilian squad (12 members visible)
- ~80 cosmetic civilians

Result:
- Hero dies after ~2 seconds → networked event, named in kill feed, corpse persists, shout-eligible
- Squad loses 8 of 12 members → 1 networked update (squad HP + member count), 8 corpses available for shout
- ~80 cosmetic civs die locally on each client — visual carnage, no networking, no shout fuel

**Total networked events:** 1 hero death + 1 squad update
**Visible deaths from player perspective:** ~89 people
**Dragon player feels:** like a god

---

## Squad-Level Replication (Player NPCs)

The same aggregate principle applies to player-commanded NPCs (Warden spearmen, Ranger archers, Artificer engineers).

- Each squad of 5 = 1 networked entity (not 5)
- Squad object holds: position (squad leader), formation offsets, member HPs, current orders, current target
- Members are visual and locally-simulated; squad behavior is server-authoritative
- 4 players × 6 squads = 24 squad entities (vs. 120 individual NPC entities) — **5x reduction**

This is essential to staying within NGO's entity budget.

---

## Network Entity Budget

Estimated peak entity counts during heavy combat (post-shout):

| Entity type | Count |
|-------------|-------|
| Players | 5 |
| Dragon | 1 |
| Player squads (4 players × 6 squads each) | 24 |
| Commander scouts | 8 |
| Commander bodyguard squad | 1 |
| Hero civilians | 10 |
| Civilian squads | 15 |
| Active zombies (post-shout, individual) | 60-100 |
| Mobile ballista carriages | 3-4 |
| **Total peak networked entities** | **~125-170** |

Comfortably within NGO's comfortable range.

---

## Required Optimizations (Non-Negotiable)

1. **Active-zone system** — without this, naive replication exceeds NGO's ceiling
2. **Squad-level replication** — both for player NPCs and civilian squads
3. **Custom animation sync** — NOT NetworkAnimator. Single byte representing animation state, replicated only on change.
4. **Tick-rate scaling** — nearby agents at full tick (20-30 Hz), distant active agents slower (10 Hz)
5. **State compression** — position deltas, quaternion compression, half-floats where appropriate

---

## Architectural Risks (Ranked)

### 1. Networking at scale (still the biggest risk)

Even with all the above optimizations, real-world conditions (5 clients with internet latency, not localhost) can surface problems that don't appear in dev. Mitigation: build a stress test early — 5 clients, 200 dummy networked entities, run for 20 minutes, measure tick rate and bandwidth. If it holds, scope is achievable. If not, migrate to FishNet (which has built-in interest management).

### 2. L2 Squad AI

Building squads that behave well across all stances (Hold Line, Escort, Firefight, Rescue, Shield Wall, Follow Me) with proper perception, formation, target selection, and player override is significant work. Estimated 2-3 months of focused systems work.

### 3. City simulation systems

Panic contagion, fire spread, morale, civilian shelter-seeking, abstract combat resolution — interdependent simulations that need iterative tuning. The code is moderate; the *balancing* is the hard part.

### 4. Active-zone transitions

Smooth abstract↔full transitions without visible popping or state inconsistencies. Important to get right; not theoretically hard but lots of edge cases (squad mid-combat when player leaves, dragon attacking abstract zone, etc.).

---

## Net Networking Recommendation

**Stay with NGO.** The architectural decisions in this document bring the project within NGO's comfortable range. Migrating to FishNet would save some implementation work (built-in interest management) but cost weeks of migration and lose existing NGO knowledge.

**Build a stress test early.** Within the next 1-3 months of development, prove that 5 clients + 200 networked entities holds under realistic conditions. The result determines whether scope is achievable on NGO or whether migration is forced.

---

## Open Questions

- [ ] Active-zone radius — ~80m full / ~120m buffer is a guess; tune in playtesting
- [ ] Abstract tick rate — every 0.5s or 1s? Trade-off between responsiveness and cost
- [ ] Cosmetic civilian count per active zone — 300-500 is a starting range; depends on rendering tech chosen
- [ ] Squad civilian member count — 12-15 per squad; tune for combat readability
- [ ] Hero civilian count — start at 10, adjust based on narrative impact in playtesting
- [ ] Custom animation sync implementation — design the byte-encoded state system
- [ ] Stress test specifications — exactly what to spawn, what to measure, success criteria
- [ ] Cosmetic crowd rendering tech — VAT (Vertex Animation Textures)? GPU-instanced skinned mesh? Impostors at distance?
- [ ] Abstract combat resolver — design the statistical model for off-screen combat outcomes
- [ ] Notification system design — how abstract events surface to Commander map and player HUDs

---

## Summary

FEFE's architectural feasibility depends on **four foundational systems**, none of which are individually trivial but all of which are achievable:

1. **Active-zone streaming** — agents exist as data when distant, spawn when nearby
2. **Three-layer civilians** — heroes individual, squads aggregate, cosmetics local-only
3. **Squad-level replication** — both player NPCs and civilian groups as single networked entities
4. **Custom animation sync** — bypass NetworkAnimator's per-parameter cost

With these in place, 250 gameplay civilians + 100 player-commanded NPCs + dragon + post-shout zombies + 300-500 cosmetic civilians per active zone is achievable on NGO with 5 players in a 0.85 sq km city.

The biggest remaining unknown is real-world networking performance under latency. A stress test built early in development is the only way to confirm the approach holds.
