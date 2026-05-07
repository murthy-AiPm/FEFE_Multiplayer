# 7 Days Till Dawn — Design Document

*Asymmetric coop survival defense. The project's pivot from FEFE after Phase 0 playtests — same DNA, simpler shape.*
*Created: 2026-05-03*

---

## Core Concept

**7 Days Till Dawn** is an asymmetric multiplayer survival-defense game. A small group of defenders holds a castle against an orc army that arrives in seven in-game days. Between now and then, the world around the castle is theirs to fight in: scouting parties harass nearby villages, resources have to be secured, and a dragon — also a player — has to be convinced to fight on the right side.

Target player count: **1–N defenders + 1 dragon** (exact ceiling TBD). The game scales with player count; it does not change shape.

Tech stack: Unity 6, Netcode for GameObjects, Mecanim (dragon), Animancer or simpler humanoid setup (defenders), Cinemachine 3.x.

**Relationship to FEFE:** 7DTD is the project's pivot from FEFE's 5-role siege design after Phase 0 playtesting. Same DNA — asymmetric coop, dragon-as-player, fortified location, NGO multiplayer. Stripped down: no role classes, no 5-player specialization, no crowd-scale civilian system. The fight is week-shaped, not match-shaped. Most existing FEFE code (dragon controllers, vitals, ballista, networking patterns) carries forward; the role and city-scale systems do not.

---

## Core Loop

A run is one week. One week is one prep cycle followed by one siege.

1. **Days 1–6 — Prep.** Open-region play. Defenders travel out from the castle to deal with scouting parties, secure resource sites, hunt for the dragon. The dragon decides what they want from this week.
2. **Day 7 — Siege.** The orc army arrives at dawn. All prep cashes in. Hold or fall.
3. **Resolve.** Win → run ends, new run available. Lose → run ends, new run available.

**Pristine runs.** No meta-progression. Each run is a fresh week — gear, castle state, dragon trust all reset. The week is the unit of play.

Each in-game day is a real-time block (length tunable, 15–30 min target). At the end of each day, an event resolves: new scouting party spotted, village under attack, dragon sighting, supply request. Players choose what to respond to. **You cannot do everything in a day.**

---

## Roles

No class system. Every defender shares the same baseline kit. **Specialization comes from equipment, not class** — see *Equipment* below. The structural role split is:

**Defender (1–N humans).** Castle-dwellers. Travel, fight, gather, build, hunt. Same kit, differentiated by what they choose to carry and what they choose to do.

**The Dragon (1 player, optional).** Player character. Starts the run uncommitted — won't fight by default. Defenders must earn its loyalty (or fail to). The dragon player has a real choice: defend the castle on Day 7, or side with the orcs.

**The Orc Warlord (future).** AI in v1; eventually a human player commanding the orc army. Probably top-down RTS-style rather than unit POV. Out of scope for the prototype.

---

## Equipment

The defender team's specialization lives in a small set of named items, picked up and carried by individual players. Each item is a verb that used to be a class identity in FEFE — now decoupled from a player's role.

**Solo play:** one player picks up everything and uses each item as the situation calls for it.
**Group play:** items distribute across the team. Whoever holds the telescope is the team's spotter; whoever holds the hammer is the team's forge. Roles are *what you carry*, not *what you picked at start*.

**The team has one of each.** Items are persistent across the week — pick up on Day 1, still yours on Day 7. Drop / hand off freely. Loss in the field is recoverable (return to the body / castle), not run-ending.

**Players can carry multiple items at once.** No single-slot rule, no encumbrance, no swap UI. The cost of carrying everything is being one body that has to be everywhere.

### v1 items (locked)

- **Telescope.** Reads enemy stats — orc party composition, dragon health zones, scout count, sector threat level. The team's intel layer. (FEFE Ranger lineage.)
- **Hammer.** Forges special weapons at the castle's smithy from gathered materials — special arrows, dragonslayer-equivalent bolts, reinforced gates. (FEFE Artificer lineage.)

That is the entire equipment set for v1. Other items (horn, raven, etc.) are deferred — added only when a concrete playtest gap demands one.

### Implementation Direction

This is **not an inventory system.** It is "named team items" with single-instance carry — closer to `BallistaOperator` mounting than to a backpack.

- Each item is its own `NetworkObject` in the world, with one instance per item type.
- `NetworkVariable<ulong> currentHolder` (Owner-write per the standard FEFE pattern) — sentinel value = sitting in world, otherwise the clientId of the carrying player.
- Pickup / drop / hand-off = `ServerRpc → ClientRpc` fan-out, same pattern as `BallistaOperator`.
- Held visual = parent to a hand bone (or item-specific attach point) on the holder's avatar. Dropped = detach, leave in world with a re-pickup trigger.
- Multiple items carried = each parented to its own attach point on the avatar; the player invokes whichever one via a dedicated input per item. No slot grid, no swap UI, no inventory persistence.
- Cost: ~50–80 lines per item type plus a shared base. Complexity stays linear in item count, which is why locking at two for v1 matters.

---

## The Castle

A single keep with walls, a gate, a courtyard, and a few fortifiable positions (towers, choke points, parapets). It is not a city — it is a fort.

The castle improves over the week as defenders spend gathered resources:
- Reinforce walls and gates
- Build / repair ballistae and crossbow nests
- Stockpile arrows, bolts, oil, food
- Garrison NPC militia (food/morale → garrison strength)

Morale matters. Low morale = NPC garrison fights worse during the siege. Food, secured villages, and dragon recruitment all feed morale.

Improvements are persistent across the week, not the run — when Day 7 arrives, what's built is what's there. Lost during the siege, lost for good.

---

## The 7-Day Cycle

The week is the game's tempo.

**Per day:**
- Open prep window (real-time block, length tunable).
- During the window: travel, fight, gather, build, hunt, recruit the dragon.
- At day-end, an event resolves and seeds the next day (new scouting party, village falls/secured, weather change, dragon mood shift, etc.).
- Day 7 dawn = siege begins.

Days are **not just timers** — they are pacing beats. The game telegraphs threats getting closer (smoke on the horizon, scout reports, refugees arriving) so the team can read the escalation curve.

---

## Resource & Objective Sites

The region around the castle is a small set of points of interest. Each site:

- Is **contested** — there is a scouting/skirmish party there or arriving.
- Yields a specific resource on success.
- Can be **lost** to the orcs, removing it from the run.

**Canonical examples:**
- **Mining village** — secure for ore/metal (weapon-grade material, hammer fodder).
- **Logging camp** — timber (walls, repairs, ballista bolts).
- **Farmstead** — food (morale, garrison support).
- **Chapel / shrine** — morale buff or unique item.
- **Hunter's lodge / wilds** — animal hunts (dragon offerings).

**Loss is real.** A site lost on Day 3 is gone for the rest of the week. The team has to triage which sites matter most for *their* castle and *their* dragon strategy.

Site count and exact yields are tunable. Probably 5–8 POIs around the castle in the prototype.

---

## The Dragon

The dragon is a player character, not a tame mount and not a boss.

**Starts uncommitted.** The dragon player begins the run weak and not aligned with either side. They have their own goals (food, comfort, treasure — TBD) and their own readout of the world.

**Defenders must buff the dragon.** Hunting animals as offerings is the canonical example. Other recruitment paths TBD: protecting the dragon's lair, gifting treasure, performing rituals, simply leaving the dragon alone in its territory. The point is: the dragon's strength on Day 7 is partly defenders' work.

**Loyalty axis.** Two endings on Day 7:
- **Defend** — dragon fights with the castle. Probably the harder path for the dragon player to commit to (less power upside than siding with the winners), so defender investment has to feel worth it.
- **Defect** — dragon joins the orc army at the siege. Devastates the castle from the air while the army breaches walls.

The dragon's choice should be **live, not default**. Their incentives have to make either path real. If "defect" is always optimal, the system is broken; if "defend" is always optimal, the betrayal hook does nothing.

**Visibility — hidden with leaks.** The dragon's choice is not visible to defenders on the HUD. Telegraphs leak through the week based on how it went: animal offerings refused, lair raided, dragon seen circling orc camps, mood-shift events at day-end. Defenders read the signals and decide how much to invest. Paranoia is the design intent — the team should never be sure whose side the dragon is on until it's already on Day 7.

This is the asymmetric-betrayal hook of the game. It's what keeps every run tense even with the same map and the same orcs.

---

## The Orcs

**Phase 1 (prototype):** fully AI-driven.
- Scouting and skirmish parties spawn during the week, occupy POIs, raid villages, harass defenders.
- The Day-7 siege is a scripted assault that scales with player count and (later) week-state inputs (lost villages → larger army; secured villages → smaller).

**Phase 2 (later):** human-controlled Warlord.
- One player directs the orc army. RTS-style top-down most likely.
- Whether the Warlord is active during the 7 days, or only during the siege, is open.

The orcs are the threat that forces the week's structure. The defenders' week is shaped by *which* orc activity they ignore.

### Two-tier orc AI

Week and siege are different problems with different solutions. Two distinct orc AI systems with a clean boundary, each used where it fits:

| System | Used for | AI style | Networked? |
|---|---|---|---|
| **Tactical orc** | Week encounters (camps, patrols, village raids), Day-7 lieutenants and giants | HFSM + Utility AI (block, parry, surround, flee) | Yes — individual NetworkObjects |
| **Crowd orc** | Day-7 siege horde, large-scale convergence battles | GPU-instanced crowd asset (formation/wave system) | No — wave-state synced; instances rendered locally |

Tactical orcs are detailed, expensive, and few. Crowd orcs are deliberately less individually expressive but render and simulate cheaply at large counts. The Day-7 horde mixes both — crowd as the bulk, tactical as the standout threats embedded inside it.

See `design/OrcAI.md` for full orc AI design.

---

## The Day-7 Siege

The siege is not one mass — it is **four waves** over a real-time block (length tunable, probably 25–40 min). Waves rotate so total orcs spawned exceeds concurrent count on the field; players see "endless tide" without ever rendering more than the concurrent ceiling.

### Wave shape (design target)

Numbers are design targets to validate at prototype. Final ceiling depends on crowd-asset eval and target hardware floor.

| Wave | Concurrent crowd | Notable threats | Beat |
|---|---|---|---|
| 1 — Probe | ~150 | — | Orcs test the gate. Defenders hold the line. |
| 2 — Main assault | ~250 | 1 giant (stone-thrower) | Walls take real hits. Garrison + player NPCs hold ramparts. |
| 3 — Breach | ~400 | 2 giants | First wall segment falls. Crowd orcs scale walls; melee on ramparts. |
| 4 — Final push | ~500 | 3 giants | Full breach. Fight to the last; courtyard contested. |

**Total orcs spawned over the night: ~1000–1500.** Concurrent peak: ~500.

### Scope targets (numbers to design against)

- **Concurrent crowd orcs at peak:** 300–500. Aggressive enough to feel overwhelming; conservative enough that mid-range hardware (~RTX 3060 floor) holds.
- **Total crowd spawned over the siege:** 1000–1500 across 4 waves.
- **Networked tactical entities at climax:** 60–80. (5 players + 1 dragon + ~20 squad-replicated commanded NPCs + ~10 squad-replicated garrison + 2–5 giants + 10–30 tactical orc lieutenants embedded in crowd squads + ~10 wall segments.)
- **Castle garrison NPCs:** 30–50 (replicated as ~10 squad entities — 3–5 squads of archers/swordsmen on walls and at gates).
- **Giants per wave:** 1–3. Networked individuals, full AI (target selection, throw arc, projectile spawn).
- **Wall segments with networked HP:** 8–12.

### Behaviors that need to work

- **Wall scaling.** Crowd orcs that reach a climbing point switch local behavior to scale-then-engage. They remain crowd instances — no networked entity is created at the transition. Defender NPCs on the rampart take damage from the climbing crowd via synced damage volumes (the same model as the dragon's firebreath / `BurnStatus` pattern).
- **Wall destruction.** ~10 wall segments with networked HP. Giants and rams reduce HP; crossing zero fires a one-shot RPC for "segment X broke" and each client locally collapses the geometry into debris. Local debris physics, not networked.
- **Giants throwing stones.** Each giant is its own NetworkObject with full AI. Stone projectiles are networked (impact damage is real). Probably 2–5 giants per wave.
- **Defender archers shooting into the horde.** Don't network individual arrows. Defender squads aggregate-damage the nearest crowd squad over time; arrows are local cosmetic projectiles fired deterministically from synced squad pose. (Same pattern as cosmetic kills above.)
- **Dragon at the siege.** Dragon's firebreath uses the existing damage volume → each client locally drops matching crowd instances. One sweep can kill 30 instances with one volume update on the wire.

### Hidden costs to design around (not bandwidth)

The constraint at this scale is *not* network — it is three other things:

1. **Client GPU/CPU.** Instanced rendering + local crowd simulation runs on every client. ~500 concurrent animated units is the realistic mid-range ceiling.
2. **Server CPU for tactical AI.** Every networked individual runs full AI on the server. Tactical orc count must stay bounded (~30 max).
3. **Combat event rate.** Aggregate kills via synced damage volumes, not per-kill RPCs. Without aggregation, peak combat saturates bandwidth fast even though entity count is small.

Networking specifically is not the wall here — with the two-tier AI and damage-volume aggregation, climax sits in NGO's comfortable range.

### What still needs prototyping

- Crowd-asset benchmark on target hardware floor. Candidate: `Enemy Masses Standard` (5,000+ unit render scale per the docs). First prototype day = spawn 500 instances, march at castle, measure frame rate. See `Design/EnemyMasses_Asset.md` for the full eval target list.
- Wall climbing API path. Supported per the asset author's YouTube demos but not in the gitbook docs at last read; validate during eval that the API surface composes cleanly with our wall-segment HP and breach flow.
- Networking bridge cost. The asset is interface-driven, transport BYO. Estimated ~1–2 weeks to implement `INetworkSkillAuthority` / `INetworkDamageAuthority` / `INetworkCommandAuthority` against NGO.
- Animation variety / loadout flexibility (3–5 visual orc variants is plenty).
- Mixed-tier interaction — confirm tactical orcs (regular `NetworkObject` + Mecanim) coexist on the field with crowd agents (Crowd Animator GPU-baked).

See `Design/OrcAI.md` for tactical orc design, `Design/EnemyMasses_Asset.md` for crowd-tier asset notes, and `Design/FEFE_NPC_Architecture.md` for the squad-replication / damage-volume / interest-management patterns this builds on.

---

## Difficulty & Player Scaling

Phasmophobia model. The game runs at any player count from 1 to N. The world is tuned so:

- **Solo is brutal.** One body, one set of eyes, one player carrying every piece of equipment. Possible — that's the point — but punishing.
- **More defenders = easier prep.** More POIs covered per day, more resources, more dragon offerings. Equipment can be split across the team so multiple specialists work in parallel.
- **More defenders = harder siege.** Orc army size and pacing scale up so the siege doesn't become trivial.

Same map, same orcs, same dragon. Only the dial moves.

No drop-in/drop-out planned for the prototype. The team that starts the run finishes the run. Reconnect-on-disconnect is desirable but not core.

---

## Win / Lose

- **Win:** survive the Day-7 siege. Castle stands; at least one defender alive (TBD).
- **Lose:**
  - Castle falls (gate breached + courtyard taken, or similar).
  - All defenders dead.
  - Dragon defects and kills the lord / objective NPC.

Possible **soft-fail states** during the week (lose enough villages, morale floor) that auto-trigger an early siege or unwinnable Day 7. TBD.

---

## What This Drops From FEFE

- The 5-player role specialization (Commander/Warden/Artificer/Ranger) — replaced by equipment.
- The complex Animancer rule-set system for humans (probably — open question).
- The crowd-scale civilian system and panic mechanics.
- The full city map (replaced by a small POI cluster around a single castle).
- The phased single-match structure (replaced by the week + siege).
- The Universal Fire Response and special-buildings systems.

## What This Keeps From FEFE

- Asymmetric coop with a dragon player.
- Mecanim-driven dragon, code reuse from `DragonFlightController` / `DragonGroundController` / `DragonCombatController` / vitals system.
- Buff-the-dragon dynamic, repurposed into the loyalty axis.
- NGO networking patterns (Owner-write NetworkVariables for state, ServerRpc → ClientRpc for one-shots).
- Existing damage / vital / crit-zone systems where applicable.
- The telescope and hammer are direct lifts from FEFE's Ranger telescope and Artificer crafting — same systems, picked up as items instead of attached to a class.

The dragon, in particular, can probably reuse most of the FEFE work as-is. The defenders need a much simpler controller stack.

---

## Open Questions

- [ ] Real-time length of one in-game day. (15 min? 30?)
- [ ] Player ceiling. 4? 6? 8?
- [ ] Does the Warlord (future) play during the 7 days or only the siege?
- [ ] POI count and yields. 5–8 sites?
- [ ] Day-end events. Procedurally generated from a pool, or scripted week arcs?
- [ ] What does "buffing the dragon" actually consist of beyond animal offerings?
- [ ] Does the dragon have its own progression / goals visible to the dragon player but not the defenders?
- [ ] Dropped runs — pause-and-resume, save-and-quit, or a run is a single sitting?
- [ ] Reconnect-on-disconnect support.
- [ ] Soft-fail conditions during the week (auto-loss before Day 7).
- [ ] Equipment loss in the field — drop on death, recoverable from body, or returns to castle?
- [ ] Which leaks signal the dragon's leaning, and how readable should they be?
- [ ] Real-time length of the Day-7 siege block. (25 min? 40?)
- [ ] Crowd asset final selection (candidate: `Enemy Masses Standard`).
- [ ] Target hardware floor for the climax (`RTX 3060`-class? lower?). Determines the realistic concurrent crowd ceiling.
- [ ] Castle layout and wall-segment count for siege scoping (currently estimated 8–12 segments).
- [ ] Whether tactical orc lieutenants visually distinguish from crowd orcs (distinct rig vs same rig + colour/badge).
