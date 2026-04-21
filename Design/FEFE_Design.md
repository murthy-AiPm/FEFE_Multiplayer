# FEFE — Game Design Document

*Asymmetric 1v4 multiplayer action game*
*Last updated: 2026-04-13*

---

## Core Concept

**FEFE** is an asymmetric 1v4 multiplayer game: **one dragon player vs. four defenders** protecting a city. Matches run 30–60 minutes and are structured around chaotic, phased siege combat involving the dragon, horde enemies (zombies/orcs — dead citizens, TBD), panicked civilian NPCs, and player-commanded military units.

Target player count: **5** (1 dragon + 4 defenders).

Tech stack: Unity 6, Netcode for GameObjects, Animancer Pro (humans), Mecanim (dragon), Cinemachine 3.x, UGS (Relay/Lobby/Remote Config), NavMesh.

---

## Match Structure

Matches are phased to sustain a long siege without fatigue:

1. **Preparation (5–10 min)** — City is quiet. Defenders gather resources, build/repair ballistae and walls, position supplies, scout. Dragon flies recon, may harass outskirts.
2. **Siege (15–30 min)** — Dragon assaults: aerial attacks, fire, strafing runs. Dragon also makes **forced ground landings** to steal/consume resources (pillar mechanic). Fires spread; panicked civilians die to fire and collapse. Zombies **do not** continuously spawn — they appear only via the dragon's **once-per-match Zombie Shout**, which raises all currently-dead civilians at once.
3. **Climax (5–10 min)** — Branches into two paths: **Kill the Dragon** (dragon wounded/grounded, defenders go offensive) or **Evacuate** (city failing, escort survivors to extraction). Win condition flips from "hold" to "finish."

**Default game mode:** Protect the City. Other modes possible later.

---

## Civilians and Win Condition

Civilian NPCs (Level 1) are not flavor — they are a **first-class gameplay resource** tied directly to win/lose conditions and horde scaling.

**Civilian behavior:**
- During peacetime (Phase 1): go about their lives in the city
- During attacks: flee to designated shelter buildings
- Fortified shelters protect them; unfortified shelters collapse and kill them

**Consequences of civilian death:**
- **Horde fuel (immediate):** Each dead civilian adds to the dragon's "zombie pool" — corpses that can be raised as horde enemies
- **Population threshold (strategic):** If total civilian population drops below a threshold, the city falls and defenders lose — regardless of wall integrity

**Dragon's Zombie Shout — Once Per Match:**
- The dragon has a single-use ability: a shout that raises **all currently-dead civilians** as a zombie horde simultaneously
- Telegraphed (wind-up animation, sound) so defenders can react
- Strategic timing: shout early with few corpses = small wave; shout late with many corpses = devastating
- **Creates the dragon's signature peak moment** — the offensive counterpart to the Artificer's dragonslayer bolt
- Dragon player is incentivized to burn civilians first, shout later
- Defenders can track the growing threat (visible corpse count) and race to craft/deploy the dragonslayer bolt before the shout lands

**Design intent:** Every match builds toward two signature climaxes — the dragon's shout and the Artificer's dragonslayer bolt. Which lands first (or whether neither lands) shapes the match's narrative arc.

---

## Universal Fire Response

Fire is a **shared problem, not a role-specific task.**

**Core principle:** Any player can fight any fire. Civilians also help, up to a threshold. No role "owns" firefighting — every player decides whether to peel off from their specialist job to respond.

**Fire's role in the game:**
- Active fires damage structures and kill civilians
- Burning buildings spread fire to adjacent buildings if not extinguished in time
- Burned civilians become corpses → fuel for the dragon's zombie shout
- More fires = fewer shelters intact → more civilian deaths → closer to the population-threshold loss condition

**Who fights fires:**
- **Civilians** handle small/early fires autonomously — the baseline response up to a threshold
- **Any player** can respond directly — grab a bucket, extinguish, move on
- **Artificer** is most in-character (engineering flavor) but has opportunity cost (mid-repair, mid-craft)
- **Warden** can divert squads to firefight stance, but those squads aren't holding the line
- **Ranger** can firefight in a pinch, but their telescope/special-arrow work is more valuable
- **Commander** can redirect squads or use team abilities to support fire response

**Design intent:** Fire response is a constant **opportunity-cost decision**. Every fire tests every player's priorities: "Do I do what I'm best at, or do I do what's closest to burning right now?" This creates continuous tactical tension without forcing anyone into a firefighter role.

---

## Civilian Panic System

Civilians are not passive background — they react to danger in ways that create ongoing management challenges for the Warden.

**Panic states:**
- **Flight panic:** civilian runs, but in the *wrong direction* — toward danger (fires, dragon, walls) instead of shelters
- **Freeze panic:** civilian stops moving entirely, stands in place shivering. Sitting duck for dragon fire, falling debris, zombies.

**Panic is contagious.** Panicked civilians visible to others spread the state. Crowded areas (markets, shelters, main plazas) can cascade from one scared civilian to dozens in seconds.

**Triggers for panic:**
- Dragon appearance / roar nearby
- Fire / damage nearby
- Witnessing another civilian's death
- Ambient fear level (rising city damage)
- Contagion from nearby panicked civilians

**Calming panic:**
- **Warden's presence** (via heal aura — dual-purpose: heals wounded, calms panicked)
- **Warden's Shout of Courage** — breaks panic in a radius, cooldown-based
- **Commander's team abilities** (TBD)

**Design intent:** The Warden must read **crowd density** and prioritize calming interventions where panic would spread fastest. Isolated panicked civilian = low priority. Plaza full of trembling citizens = crisis waiting to cascade.

---

## Dragon Needs — Forced Landings (Pillar Mechanic)

The dragon is not a pure flying boss. It has **resource needs that force it to the ground**, creating repeated cat-and-mouse encounters throughout the match.

**Core principle:** The dragon must periodically land to steal or consume something (food, water, civilians, treasure — specifics TBD). Each landing is a risk/reward decision for the dragon player and a combat opportunity for the Warden and defenders.

**Why this works:**
- Gives the Warden continuous combat purpose throughout Phase 2 (not just after the zombie shout)
- Gives the dragon player meaningful tactical decisions (when to land, where, is this location defended?)
- Creates readable dragon behavior — defenders can anticipate and prepare ("dragon is hungry, watch the granary")
- Re-frames the match: the dragon isn't just attacking, it's **parasitizing** the city
- Connects to existing systems — stolen/consumed resources affect the Artificer's economy; civilian theft triggers Warden response
- Creates potential for **baiting / trap setups** — Artificer + Ranger can prepare ambush sites near likely landing zones

**Open questions (full pass needed when designing dragon):**
- What resources does the dragon need? Food, water, civilians, treasure?
- What's the consumption/need rate — how often must the dragon land?
- Where are these resources located in the city (map-dependent)?
- What does "stealing" look like — grab-and-fly, or land-and-consume?
- If the dragon can't feed, does it weaken? (Probably yes — creates real pressure to land)
- Can defenders deplete/relocate resources to starve the dragon? (Probably yes — strategic Artificer/Commander option)

## Special Buildings (Sector Infrastructure + War Rooms)

Five special buildings exist in the city from the start of every match. Each provides a **continuous passive buff** while standing, contains a **Commander war room** with themed abilities, and is a **strategic target** for the dragon.

**Core principle:** These are the city's existing institutions. The Artificer doesn't choose which to build — they inherit all five and must fortify and protect them.

### Building Behavior

- **Tiered damage states:** Full → Damaged → Destroyed
  - Damaged = reduced buff effectiveness, repairable by Artificer
  - Destroyed = no buff, war room unusable, requires full rebuild
- **Rebuildable by Artificer** — slow and expensive (3–5 minute rebuild, heavy resource cost). Prevents a single dragon strike from being match-ending but creates a real absence window.
- **Dragon damage commitment required** — see *Dragon Attack Commitment* below. The dragon can't casually burn these down — it must commit to a vulnerable attack state.

### The Five Buildings

**1. Church — Morale + Civilian Calm**
- **Continuous buff:** ambient morale across the entire city (all roles, civilians, squads); reduces panic contagion
- **War room ability:**
  - **Call to Faith** — massive panic reduction in a radius (civilians calm, panic contagion halts, panicked civilians return to correct pathing) AND nearby squads receive morale/defense buff for the duration. Single button, dual effect — civilian + military morale simultaneously.
- **Loss consequence:** morale collapses city-wide, panic contagion accelerates, squads break more easily
- **Paired field role:** Warden (civilian management)

**2. Mining Forge — Raw Production**
- **Continuous buff:** mines produce raw resources faster (all three types: stone/metal/special)
- **War room abilities:**
  - **Overdrive Mining** — temporarily boost raw output rate for a duration (short-term economic sprint)
- **Loss consequence:** mining slows significantly; Artificer's allocation has less to allocate; everything downstream (ballistae, repairs, bolts) suffers
- **Paired field role:** Artificer

**3. Garrison — Squad Organization + Warden Morale**
- **Continuous buff:** squad reallocation takes less time; Warden NPCs have morale buff
- **War room ability:**
  - **Rapid Reallocation** — transfer squads between sectors instantly (no travel time)
- **Loss consequence:** reallocation becomes slow and painful; Warden squads break more easily
- **Paired field role:** Warden

**4. Blacksmith — Skilled Production + Artificer Morale**
- **Continuous buff:** crafting queue runs faster; repair materials produced faster; Artificer NPCs have morale buff
- **War room ability:**
  - **Expedite Craft** — push one item to front of Artificer's queue, 3x speed (e.g., dragonslayer bolt urgency)
- **Loss consequence:** bolt supply slows; repair materials slow; Ranger runs dry; city falls apart faster
- **Paired field role:** Artificer (production affects Ranger downstream)

**5. Tavern — Ranger Support + Air Defense Hub**
- **Continuous buff:** Ranger NPC accuracy boost; Ranger squads morale-buffed
- **War room abilities:**
  - **Ranger's Toast** — accuracy and morale buff to all Ranger NPCs for a duration
- **Loss consequence:** Ranger accuracy drops; squads break more easily; air defense degrades
- **Paired field role:** Ranger

### The Keep (Central Command)

Not a "special building" in the same category — it's always present, always the safest, and is the Commander's default home.

- **Continuous effect:** none (Keep doesn't buff — it's the command center itself)
- **War room abilities:**
  - **Horn of Muster** (once per match) — convert nearby civilian NPCs into temporary militia (spearmen with reduced stats, 3–5 min duration). Mirrors the dragon's Zombie Shout: dragon raises *dead* citizens; Commander raises *living* ones. Symmetric once-per-match beats.
  - **All-sector rally** — global morale buff to all squads and civilians for a duration; long cooldown
- **Loss consequence:** the match is likely already lost if the Keep falls

### Design Implications

- **The dragon now has *specific* high-value targets.** Burning the blacksmith cripples the Artificer. Burning the tavern cripples the Ranger. Strategic destruction, not just ambient damage.
- **Artificer's Phase 1 fortification decisions matter.** Which of the five buildings do you reinforce first? Real priority tension.
- **The Commander has five war rooms, each with clear identity.** Travel decisions are themed: go to the church for morale crises, the tavern for air defense support, the garrison for rapid squad shuffling.
- **Destroyed war rooms = lost capabilities AND lost passive buffs.** Multi-layered consequences make losses feel heavy.

---

## Dragon Attack Commitment

The dragon cannot cripple the city through casual strafing runs. To significantly damage structures — especially special buildings — the dragon must **commit to a vulnerable attack state.**

**Two commitment modes:**

**1. Sustained Fire Breath**
- Dragon must hover stationary (or nearly so) and sustain fire breath for **4–6 seconds** to seriously damage a building
- Quick strafes produce only cosmetic damage
- Sustained breath has a **1–2 second charge-up telegraph** (eyes glow, smoke billows) giving defenders warning
- During sustained breath, dragon is a massive stationary target for ballistae
- **This is the air defense's opportunity window**

**2. Ground Melee**
- Dragon lands and physically smashes a building with claws, tail, head
- Maximum commitment — dragon is on the ground, fully exposed to spearman assault and close-range ballista fire
- **This is the Warden's opportunity window**

**Design intent:**
- Prevents kite-burning gameplay (dragon can't just hit-and-run the city to victory)
- Creates the core Phase 2 combat rhythm: dragon probes → defenders respond → dragon commits → defenders exploit the window → dragon succeeds or retreats wounded
- Connects to the forced-landing mechanic (dragon already must land for resources; now it also *chooses* to land for destruction)
- Explains why wing-breaking matters so much: a wing-broken dragon caught mid-commitment is trapped

---

### Phase 1 — Planning (5–10 min)

City is quiet. Defenders set up; dragon may do light reconnaissance.

**Artificer's Phase 1 (most activity):**
- Place ballistae and mobile carriages
- Designate workshops and special civilian shelters (structure conversions)
- Set initial resource allocation (Stone / Metal / Special %)
- Plan initial crafting queue for opening bolt stock

**Warden's Phase 1:**
- Position squads — each squad provides a heal/calm aura
- Decide: place squads near civilian density (panic prevention) OR near critical structures (anticipate breaches)
- Define patrol routes (optional)

**Ranger's Phase 1:**
- Position NPC squads at ballista stations (as Artificer builds them)
- Set fire discipline defaults per sector
- Place scouts on high points
- Assign mobile carriage default districts
- Request opening bolt priorities from Artificer

**Commander's Phase 1:**
- Set up at Keep; open map, review threats
- Deploy scouts — assign initial patrol routes and observation points
- Travel to field war rooms as needed to coordinate with Artificer/Warden/Ranger on sector plans
- Pre-position bodyguard squad
- No combat; pure orchestration

### Phase 2 — Battle (15–30 min)

**Dragon behavior:**
- Aerial attacks, fire breath, strafing runs
- **Forced landings** to steal/consume resources (pillar mechanic — creates ongoing ground engagements)
- Strategic timing of **Zombie Shout** (once per match) to maximize impact

**No continuous zombie spawning** — zombies only appear via the Shout. Pre-shout, ground threats are panicked civilians, fires, dragon's ground raids, and possibly light orc probes (TBD).

**Phase 2 internal beats (to be detailed further):**
- **2a — First Contact:** initial dragon attacks, defenders calibrate, small losses
- **2b — Escalation:** fires spread, dragon raids intensify, dragonslayer bolt crafting begins
- **2c — Crisis:** multiple fronts under pressure, dragon shout likely lands here, zombies wave erupts
- **2d — Tipping Point:** dragon wounded enough for Phase 3 OR city failing toward evacuation

### Phase 3 — Climax (5–10 min)

**Two possible paths (triggered by current match state):**

**Phase 3a — Kill the Dragon**
- Triggered when dragon is wounded/grounded (wing broken, HP threshold, or successful dragonslayer bolt hit)
- Warden leads infantry charge with pikes
- Ranger delivers finishing shots
- Hordes become background pressure
- Win condition: dragon dies before recovering/fleeing

**Phase 3b — Evacuate the City**
- Triggered when city is failing (civilian population or structural threshold crossed)
- Defenders protect civilian flow to an extraction point
- Warden shepherds; Artificer builds last-ditch barriers; Ranger covers retreat; Commander coordinates the flow
- Win condition: minimum number of civilians reach extraction alive

**Focal points (POIs):** Phase 3 concentrates action at 1–2 landmarks (Great Ballista at keep, extraction gate, dragon's landing site) for legibility at the climax.

---

## AI Tiers

The world is populated by three tiers of AI, totaling 300+ NPCs (excluding hordes):

- **Level 0 — Wildlife:** predator/prey animals outside the city. Ambient flavor, not core gameplay.
- **Level 1 — Civilian population + hostile hordes:** city people going about their lives; zombies/orcs breaching during siege. Primary source of chaos and legibility pressure.
- **Level 2 — Player-commanded military units:** ~30–40 units per field commander, organized into squads of ~5–6. Autonomous baseline behavior; respond to player orders.

---

## Defender Team Structure (4 Players)

Four roles, **pick at start**. Each has a distinct strategic orientation:

- **Commander** — map-level play (Hell Let Loose-style), provides team abilities, reallocates units, has avatar + bodyguard squad
- **City Warden** — ground war, line-holding, infantry. Fights the horde.
- **Artificer** — construction, repair, siege weapons, special arrow crafting. Keeps the city functional.
- **Ranger** — air defense, ballistae, mobile carriages, scouts. Fights the dragon.

**Core design principle across all roles:** *NPCs handle steady-state; player handles peaks.*
Autonomous NPCs do the volume work; players handle the decisions and moments that require judgment, precision, or mobility.

---

## Commander

The **Commander** is the defender team's strategic backbone — the only player seeing the whole board, orchestrating scouts, coordinating sector responses, and anchoring morale. Unlike the field commanders (Warden, Artificer, Ranger), the Commander's power is primarily **informational and coordinative**: they empower the other three players rather than replacing their jobs.

**Core identity:** Map-view strategist and sector coordinator (Hell Let Loose model).

**Core design principle:** *Force multiplier, not replacement.* Commander abilities empower field players' existing actions; they don't duplicate them.

### Core Mechanics

**1. Map Access Gated to War Rooms**
- The Commander can **only open the battle map from a war room** (see Special Buildings below)
- This prevents "map-turtle" play — the Commander must commit to a physical location to command
- Travel between war rooms is real, exposed, and carries risk
- The Commander's **current location determines which themed abilities are available**

**2. Scouts — Information as Gameplay**
- Commander commands 6–10 **scout NPCs** who rove the city capturing and relaying information
- Scouts reveal dragon position, civilian panic levels, structure damage, threat approaches in their vicinity
- **Scouts can die** — losing scouts creates blind spots on the map
- Scouts respawn slowly at nearest war room (60–90 seconds)
- Scout behavior modes: Patrol (follow a route), Observe (stationary detailed info), Follow (shadow a target), Recall (return to war room)
- Complements the Ranger's telescope: telescope = close-range dragon detail; scouts = broad battlefield awareness

**3. All-Ping**
- Places urgent markers on all defenders' HUDs with coloring (white = look, yellow = warning, red = crisis)
- Short text tags supported ("Breach east gate", "Dragon incoming")
- The team's coordination backbone, complementing voice chat

**4. Squad Reallocation**
- Transfer NPC squads between sectors/field commanders
- Reallocation is *transfer of command*, not automatic task change — the receiving field commander must issue new orders
- Takes time by default; reduced with **Garrison** war room (see below)

**5. Personal Avatar Combat**
- Large sword — heroic melee
- **Bodyguard squad of 6** with shields accompanies the Commander's avatar
- Bodyguards travel with the Commander between war rooms
- Respawn on death at safe war room

**6. Future: Dragon Finisher Blade (Phase 3)**
- In Phase 3a (dragon wounded/grounded), Commander can personally travel to the landing site
- Commander's blade is a ceremonial finisher — executes an already near-death dragon
- Not a main damage source; a narrative beat at the climax
- Deferred — implement once core combat is stable

### Role Relationships

- **Commander → Warden:** reallocates squads to Warden's sector; coordinates through Garrison war room
- **Commander → Artificer:** expedites critical crafting; coordinates through Blacksmith and Mining Forge war rooms
- **Commander → Ranger:** shares telescope intel, boosts accuracy; coordinates through Tavern war room
- **Commander → Civilians:** manages panic through Church war room and Keep-based abilities

### Open Questions (Commander)

- [x] Map access gated to war rooms — confirmed
- [x] Scouts as information layer — confirmed
- [x] War room abilities — confirmed (see Special Buildings)
- [x] Commander dies → respawns — confirmed
- [ ] Scout count (6–10) — tune in-game
- [ ] Bodyguard squad size — 6 confirmed
- [ ] Dragon finisher blade — deferred to future implementation
- [ ] Travel time between war rooms — tune in-game
- [ ] What happens to scouts when Commander respawns — preserved or reset?

---

## City Warden (City Watch Captain)

The **City Warden** is the defender team's infantry commander. While the Ranger looks up and the Artificer manages the city's physical integrity, the Warden holds the ground war — horde defense, civilian protection, wounded recovery, and (in Phase 3) the infantry assault on a grounded dragon.

**Core identity:** Ground-war infantry commander, protector of living beings.

**Key framing:** *"The dragon is the sky's problem. The city's problem is what's coming through the gates."*

### Core Mechanics

**1. Infantry Command — Spearmen with Shields**
- ~30–40 spearmen with shields, organized into 6–8 squads of **5** (plus squad leader = 6 per squad)
- Single unit type for clarity and identity — spears are anti-large-creature weapons (good vs. hordes *and* vs. grounded dragon)
- Default behavior: **Hold Line at assigned position** — form up, attack hostiles in range
- Warden issues special orders to individual squads via radial/command UI

**2. Squad Stance Orders**

Each squad can be assigned one of several stances:

- **Hold Line** — form up at a position, engage hostiles in range (default)
- **Escort** — follow a specified NPC (engineer squad, civilian, wounded) and protect them
- **Rescue** — move to wounded/panicked civilians, heal/shepherd them toward shelters
- **Firefight** — respond to a fire (see Universal Fire Response below)
- **Follow Me** — stick with the Warden as personal retinue

Most squads stay on autopilot (Hold Line at assigned position). The Warden intervenes on 1–2 at a time as the chaos shifts. Tactical game = reading the city state and reassigning squads.

**3. Heal Wounded NPCs In-Place**
- Warden + nearby squad members emit a "healing presence" aura
- Wounded NPCs within the aura recover over time
- Once healed, they path to the nearest shelter autonomously
- No stretcher-bearing, no fetch quests — healing happens where they fell
- Incentivizes the Warden to fight *where wounded civilians are* (streets, plazas) rather than only at chokepoints

**4. Lead Civilians to Shelter (Pied Piper)**
- Panicked civilians near the Warden follow him
- He can walk them to the nearest shelter, then peel off and return to combat
- Civilians sprint to keep up (no Warden speed penalty)
- Civilians are low-priority targets for zombies (enemies prefer the Warden)
- If the Warden enters combat mid-transit, following civilians continue to the last-known shelter on their own
- Warden is a *guide*, not a nanny

**5. Engineer Escort**
- When Artificer's engineers work in contested zones, Warden squads escort them
- Creates the core cross-role dependency: without Warden protection, engineers can't repair under fire, and the city breaks down
- Warden decides which engineer squad gets protection vs. which is left exposed

**Escort combat behavior (realistic AI):**
- Zombies/orcs target whoever is closest — no artificial priority bias toward spearmen
- **Default:** engineers keep working while nearby Warden squads engage threats
- **Threshold:** if a hostile gets within melee range of an engineer, that engineer drops tools and defends themselves (weak combat — can survive 1–2 zombies briefly, loses to 3+)
- **Recovery:** once the threat clears, engineer picks up tools and resumes; work progress is paused, not lost
- **Player feedback loop:** engineer dropping tools = visible signal that the Warden's screen failed. Good Warden play = spearmen positioned all around engineers, zombies never reach melee range, engineers never stop working.
- **Stakes:** engineers can die if overwhelmed. Losing an engineer squad means lost repair capacity for the rest of the match.

**6. Dragon Engagement — Grounded Dragon (Phase 3)**
- When the Ranger (or ballista fire) breaks the dragon's wing, dragon crashes and must fight on the ground
- **The Warden's identity shifts:** from line-holder to dragon-killer
- Leads the spearman charge into the grounded dragon — pikes are anti-dragon weapons, overwhelming infantry melee
- Classic "brave men with polearms surround the wounded wyrm" fantasy
- **Signature Warden moment** — peak of their role

### Personal Combat

- **Sword-and-shield melee** — tanky, durable, rally presence
- Can duel orcs and handle small zombie groups personally
- Not fast or flashy — the Warden is the immovable center of the defense
- Presence is a buff for nearby squads (morale, cohesion)

### Signature Mechanics

**Shout of Courage (signature — cinematic peak)**

A battle cry where the Warden and all nearby Warden NPCs shout in unison. Cinematic audio/animation beat — the defining "Warden moment."

- **Calms nearby civilians instantly** — breaks panic in a radius, un-freezes frozen civilians, redirects fleeing ones back toward shelters
- **Intimidates nearby zombies** — stagger / brief slowdown / hesitation
- **Requires nearby Warden NPCs** to participate (presence-based — scales with how many squad members are near the Warden)
- **Cooldown-based**, not once-per-match (estimated 60–90s; tune in-game)
- Reinforces infantry-commander identity: Warden must be *with* his troops to use it

**Shield Wall (tactical formation)**

A command ordering nearby squads to lock shields into a defensive formation.

- Squads physically line up and commit to a position
- Zombies/orcs break against the wall; high damage resistance on the front
- Exposed flanks — positioning matters
- Tactical tool, not signature — used continuously when holding chokepoints
- Formation vs. ability-buff variant TBD (test in-game)

### Role Relationships

- **Warden → Artificer:** escorts engineers in contested zones; protects repair/build operations from being overrun
- **Warden → Ranger:** mostly independent while dragon is airborne. Phase 3 — Warden and Ranger jointly execute the grounded-dragon kill (Ranger's bolt or ballista damage brings it down, Warden's spearmen finish it)
- **Warden → Commander:** receives priority calls ("east gate is about to fall"); reports ground-war status; reallocation target when pressure shifts
- **Warden → Civilians:** directly responsible for evacuation, healing, shepherding to shelter

### Open Questions (Warden)

- [x] Signature mechanic — Shout of Courage (signature) + Shield Wall (tactical). Both confirmed.
- [ ] Exact squad count (6 squads of 5 + leader = 36? 8 squads = 48?). Squad size locked at 5.
- [x] Command UI — map overlay + radial. Detail TBD later.
- [ ] Warden's heal aura radius and rate — test in-game
- [x] Civilian follower cap — 20
- [ ] Shield Wall variant — physical formation vs. in-place buff
- [ ] Shout of Courage cooldown and radius — tune in-game
- [ ] Grounded dragon fight — does the Warden have a special stance/attack for it, or just normal combat?

---

## Artificer

The **Artificer** is the defender team's economic and engineering backbone. They keep the city functional, the ballistae firing, and the civilians alive — and they craft the team's single best shot at killing the dragon.

**Core identity:** Triage engineer and economic strategist.

### Core Mechanics

**1. Build/Repair — Ballistae and Mobile Carriages**
- Builds static ballistae, mobile ballista carriages, walls, barricades, gates, traps
- Places new structures in blueprint mode; NPC engineer squads execute the build
- NPC repair crews autonomously head to damaged structures on a priority queue
- Artificer personally handles critical repairs under fire (the ballista the dragon just hit mid-combat)
- **Phase gating (TBD):** greenfield construction largely locked to Phase 1 (prep); Phase 2 focuses on repairs + emergency structures; Phase 3 enables climax-specific builds

**2. Manufacture Special Bolts for Ranger**
- NPC crafters at multiple workshops (workshops are cosmetic; the Artificer doesn't micromanage individual crafters)
- Artificer sets the **crafting queue** via UI (e.g., "3 fire bolts, then 2 armor-piercing")
- Artificer has an **aura effect** — standing near a workshop speeds up nearby crafting
- Position matters: same design pattern as the Ranger's accuracy aura

**3. Fortify Civilian Shelters**
- Civilian NPCs flee to designated shelter buildings during dragon attacks
- Unfortified shelters collapse under fire → civilians die
- Fortified shelters (reinforced walls, fire-resistant roofing) protect civilians
- **Links directly to win condition** (see below)

**4. Resource Allocation — Strategic Dial**
- Mines (inside city walls for now; map-dependent) produce raw output at a fixed rate
- Artificer sets percentages: **Stone / Metal / Special**
  - Stone → walls, barricades, civilian shelters
  - Metal → ballistae, carriages, repairs
  - Special → Ranger's bolts
- Allocation is "set and adjust" — not continuous micromanagement
- Creates real tension: every other role depends on Artificer's allocation choices
- Commander can *recommend* via pings; Artificer has final call
- Changes are transparent to the team (notification broadcast) to reduce blame ambiguity

**5. The Dragonslayer Bolt — Signature Moment**
- The Artificer can **personally craft** one type of ultimate bolt: 1-hit-1-kill potential against the dragon
- Requires **long workshop time + heavy special resources**
- Handed off to the Ranger, who must land the shot
- **One path to victory, not the only path.** Ranger can miss. Artificer can die mid-craft. Bolt can be wasted on a bad shot. Backup: sustained ballista attrition damage (slower, lets the dragon bleed the city)
- **Emotional payoff:** this is the Artificer's signature contribution to the kill

### Personal Combat

- Light melee / utility weapon (hammer, wrench-like tool)
- Can defend against a handful of zombies in a pinch, but not a line-holder
- NPC engineer squads similarly: can fight, but every second fighting is a second not repairing — "time wasted"
- Creates cross-role dependency: if orcs breach into the repair corridor, **City Warden's spearmen need to protect the Artificer's engineers**, or construction stops

### Role Relationships

- **Artificer → Ranger:** builds ballistae, carriages; crafts special bolts and the dragonslayer bolt
- **Artificer → City Warden:** needs Warden's infantry to protect engineers in contested areas; supplies fortified positions
- **Artificer → Commander:** receives allocation suggestions; Commander can prioritize which repairs get engineer attention
- **Artificer → Civilians:** shelter fortification directly determines civilian survival

### Open Questions (Artificer)

- [ ] Phase-gating for construction — exact rules for what can/can't be built in Phase 2
- [ ] Dragonslayer bolt — exact craft time, resource cost, any alternate paths to equivalent damage
- [ ] Crafting queue UI — how the Artificer sets/adjusts it without leaving the field
- [ ] Aura radius and magnitude — how much speed boost, how close the Artificer must be
- [ ] Multiple mines or single mine? — map layout decision
- [ ] Workshop loss — if a workshop building burns down, does it pause crafting, or does crafting abstractly continue?
- [ ] Fortification tiers — is a shelter binary (fortified/not) or tiered (light/medium/heavy)?

---

## Ranger

The **Ranger** is the defender team's anti-dragon air defense coordinator. Their NPCs and personal combat are primarily concerned with threats in the sky, not the horde war on the ground.

**Core identity:** Mobile anti-air coordinator.

The Ranger cycles between three modes of play:

1. **Skirmisher mode** — personal crossbow, rooftop traversal, mobile positioning
2. **Command mode** — reading the dragon via telescope, issuing fire discipline orders to static ballistae
3. **Carriage mode** — piloting a summoned mobile ballista carriage for heavy precision shots

### Core Mechanics

**1. Special Arrows (Ranger-exclusive)**
- Only the Ranger can fire special arrows — from their personal crossbow or a mobile ballista carriage
- Crafted by the Artificer using resources gathered in and around the castle
- Specific arrow types TBD (expected 3–4 at launch: e.g. fire, armor-piercing, tether/signal)
- Creates a supply-chain dependency: **resources → Artificer crafts → Ranger carries → Ranger fires at the right moment**

**2. Telescope — Dragon Intelligence**
- Reveals dragon health and locational damage states (head, wings, body, tail)
- Informs the team which body parts are weak (e.g. "wings are damaged, focus fire")
- Shared with the Commander, but **not** with the City Warden or Artificer
- Requires line-of-sight and reasonable proximity — the Ranger cannot hide in the keep; must stay engaged
- This is the Ranger's anti-camping mechanic: all downstream decisions require active telescope reads

**3. Local Accuracy Aura**
- Ranger NPC squads near the Ranger fire with high accuracy
- Distant Ranger squads chip with low accuracy
- The Ranger's **position** is a tactical resource — whichever sector they're in becomes the lethal sector
- Ranger + Commander decide which front gets the "hero accuracy" treatment in real time

**4. Fire Discipline Orders**
- **Volley:** Ballistae in range hold fire, then fire simultaneously on command. Trade-off: holding fire means the dragon isn't being pressured during the hold window. High-risk/high-reward burst.
- **Targeted fire:** Ballistae focus on a specific dragon body part (head, wings, body) designated by the Ranger. Exploits the locational damage system revealed by the telescope.
- Both volleys and targeted fire grant **higher critical hit chance**
- Default state is "free fire" — autonomous NPC targeting

**5. Mobile Ballista Carriages**

Horse-drawn wagons with ballistae mounted on top — summoned Tesla-style to the Ranger's chosen location.

- **Summon mechanic:** Ranger calls a carriage to a location ("Mobile Ballista 2 to east gate"); horses and driver NPCs autonomously route via NavMesh; Ranger boards on arrival and takes direct control of the ballista
- **Ranger-exclusive operation:** only the Ranger can fire the carriage's ballista
- **Parks when unmanned:** no autonomous fire — if the Ranger isn't aboard, the carriage sits idle
- **Expected count:** ~3–4 mobile carriages across the city (premium assets; losing one matters)
- **Static ballistae:** separately, ~20–30 static ballistae on walls/towers, crewed by Ranger NPC squads
- **Carriage defense:** NPC guards (driver + 1–2 guards) protect the carriage from melee threats

### Role Relationships

- **Artificer → Ranger:** builds ballistae and carriages; repairs them when damaged; crafts special arrows from gathered resources
- **Ranger → Artificer:** consumes structures and ammo; emergency slow-repair on ballistae if Artificer is occupied
- **Ranger → Commander:** shares telescope intelligence; receives sector-level priority calls; coordinates on dragon targeting
- **Ranger → City Warden:** mostly independent; Ranger's focus is vertical (air), Warden's is horizontal (ground). Ranger NPCs may provide ranged support on horde breaches when dragon pressure is low

### Open Questions (Ranger)

- [ ] Special arrow types — finalize list (3–4 expected)
- [ ] Special arrow crafting — resource types, craft times, inventory cap
- [ ] Mobile carriage movement control UI — point-to-move vs. radial commands vs. WASD
- [ ] Carriage failure states — horse death, wheel damage, driver loss behaviors
- [ ] Exact number of mobile carriages vs. static ballistae
- [ ] Telescope UX — always-on HUD element vs. deliberate "raise telescope" action
- [ ] Ranger NPC squad composition — all archers? Mix of archers + ballista crews + scouts?
- [ ] Scout squads — are they a separate Ranger asset for information-gathering?

---

## Global Open Questions

- [ ] Command UI/granularity for all field commanders (how do you issue orders to 6–8 squads while fighting?)
- [ ] Legibility systems — color coding, audio priority, threat indicators for the 400–600 agent scene
- [ ] Resource economy — what resources exist, how are they gathered, who manages them
- [ ] Win/lose conditions per phase — specifics of "city falls" and "dragon dies"
- [ ] 4 vs fewer scaling — which role is optional if a player drops?
