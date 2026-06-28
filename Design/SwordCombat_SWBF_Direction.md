# Sword Combat — Direction: Star Wars Battlefront Feel (Design Notes)

*Created: 2026-06-28. Status: **direction-setting, not yet implemented.** Captures a design discussion so it can be picked up later. No code has been written for this yet.*

---

## Why this exists

The current melee is **Witcher-3-inspired**: committed light/heavy attacks, dodge/parry, soft target acquisition. It works functionally. But the playtest feel is **"tiring to fight an orc."**

Goal: shift the *feel* toward **Star Wars Battlefront (DICE) hero saber combat** — accessible, impactful, power-fantasy melee — without a ground-up rewrite.

A Chivalry-2 direction was also considered and **deliberately rejected** for this game (reasoning captured below) so we don't relitigate it.

---

## The reference spectrum (the core decision)

The three references sit on one axis: **accessible power-fantasy ↔ deep manual skill-expression.**

| | **SWBF (target)** | **Witcher 3 (current)** | **Chivalry 2 (rejected)** |
|---|---|---|---|
| Targeting | Soft lock-on / auto-orient | Soft target | Free-aim, no lock-on |
| Attacks | Light/heavy **combos**, auto-oriented | Committed light/heavy | **Directional** (overhead/slash/stab) |
| Depth lives in | **Abilities + positioning** | Reactions (dodge/parry/signs) | **Swing manipulation** (drag/accel) |
| Block | Hold block/deflect + meter | Block reduces, parry negates | Aimed directional block |
| Feel | **Accessible, impactful, power fantasy** | Reactive, committed | Deep, chaotic, high skill ceiling |
| Built for | **Mowing through AI hordes** | Single-target duels vs AI | **Human-vs-human dueling** |

SWBF *removes* manual swing control to make you feel powerful. Chiv *maximizes* it to reward mastery. They are opposite philosophies.

---

## Why SWBF fits THIS game

1. **Enemies are AI hordes.** The core loop is a defender wading through orc waves — exactly what SWBF heroes do (carve through trooper bots). SWBF's power fantasy is *designed* for this.
2. **Small lift from where we are.** The current Witcher-3 base (light/heavy, block, parry, dodge, stamina, swept-blade hitboxes) is already ~80% of the SWBF model. Reaching SWBF feel is **additive** — juice, soft-lock, abilities — **not a combat-model rewrite**.
3. **It directly cures "tiring."** SWBF's answer to tedium is impact + snappy time-to-kill + crowd sweeps + ability spectacle. You feel strong and fights resolve fast.
4. **Maps onto the 7DTD equipment design.** SWBF's depth is its **ability layer**; that slots straight onto the equipment/tools direction in `7DaysTillDawn_Design.md` — special weapons/gear *are* the "hero abilities."

---

## Why NOT Chivalry 2 (considered, deferred)

- Chiv's depth (drags, feints, aimed blocks) exists to outplay **humans**. Against AI it's mostly wasted — a drag only matters if the defender can be *fooled*, which would require a large reactive-directional-blocking AI investment just to give the mechanic something to beat.
- **Third-person + full drag/accel** (the combo we discussed wanting) is the hardest version: drags are built around first-person aim-coupling; third-person hurts readability (Mordhau-viable but players flick to first-person for precision), and the input-driven blade path makes the **netcode** expensive and finicky.
- Revisit Chiv-style depth **only** if a dedicated **symmetric human-vs-human melee PvP mode** is ever added (see PvP note) — decide that as its own project.

---

## PvP consideration (why melee depth isn't the priority)

SWBF saber combat *works* in PvP but is **shallow**: between even players it devolves into **block-stare standoffs**, **ability-trading**, and the **soft lock-on feels cheap**. Great for casual/chaotic PvP; weak as a competitive dueling system.

**But this game has no symmetric human melee duel:**
- The **dragon** is the only human "enemy," and it fights *as a dragon* — flight, firebreath, paws — not as a sword duelist.
- The future **Orc Warlord** player is **RTS / top-down**, not a unit-POV melee fighter.

So the PvP here is **asymmetric** (ground defenders vs aerial dragon / vs RTS commander). There is nothing for "dueling depth" to duel against. **Don't pick a combat system to win duels the game doesn't contain.** Build for the primary PvE experience and add a little counterplay so any human friction isn't a pure block-stare (see "Light-PvP counterplay").

---

## What "SWBF feel" means concretely (mapped to existing systems)

All additive to current code — nothing here requires replacing the combat model.

1. **Soft lock-on / target assist + lunge** — swings auto-orient toward the nearest orc, with a short lunge to close the gap. *(Builds on existing AI targeting + the player controller.)*
2. **Impact "juice"** — the biggest feel-per-effort win:
   - **Hitstop** (~50–90 ms freeze on impact). `DamageReceiver.hitStunDuration` (0.2) already exists — verify it's actually felt.
   - **Knockback** using `HitInfo.hitPoint` / `hitNormal` (already provided by `HitboxController`).
   - **Hit VFX + punchy SFX** at the hit point; **light camera shake** on the player.
   - **Reliable enemy stagger** on clean hits (see "tiring" root causes).
3. **Crowd sweeps** — wide attacks that hit multiple orcs. `HitboxController` already supports multi-hit + dedup, so this is mostly animation + tuning. Feels heroic vs hordes.
4. **Block/deflect meter + guard break** — hold-block with a stamina/guard meter (`DamageReceiver.blockStaminaRatio` + `VitalManager` stamina already exist), and a **guard break** so turtling loses.
5. **Abilities on cooldown** — the SWBF depth layer. Wire to **7DTD equipment/special weapons** via `WeaponManager` / `CombatLoadout` / `WeaponData`.
6. **Power-fantasy tuning** — fast TTK on basic orcs (carve through), tougher **elites** (Berserker/Giant) for difficulty spikes.

---

## Curing "tiring" (root causes identified in playtest discussion)

These are the concrete reasons fights drag, with the levers:

- **TTK too long** → tighten so a basic grunt dies in **~3–5 clean hits** (orc `Health` on VitalManager down, or player `WeaponData.baseDamage` up). Reserve big health pools for elites.
- **Hits lack impact** → the juice pass above (hitstop, knockback, VFX/SFX, shake).
- **Orc gives no openings** → pace its attacks. *(Known issue: the grunt round-robins 3 attacks, so per-attack `cooldown` never gates overall cadence — use fewer/slower attacks + a readable wind-up/telegraph so the player gets safe windows.)*
- **Blocks absorb too much / no flinch** → reliable **stagger** on clean hits, **guard break**, and lower `blockChance`/`parryChance` on basic orcs. *(Note: a blocking orc still takes 20% by default — `blockDamageReduction = 0.8` — but never staggers while in Block/Attack/Parry, which is why hits feel ignored.)*

---

## Light-PvP counterplay additions (cheap; avoids the block-stare problem)

So the *occasional* human conflict isn't miserable, add on top of the SWBF base — far cheaper than full Chiv:

- **Dodge with i-frames.**
- **Guard break** (turtling loses).
- Optional **light feint** (cancel an attack during wind-up).

---

## Existing systems to leverage (no rewrite needed)

- **`HitboxController`** — swept blade trace, multi-hit, per-swing dedup. *Already Chiv/SWBF-friendly collision.* Keep.
- **`DamageReceiver`** — block reduce / parry negate+stagger / block stamina / `hitStunDuration`. Extend for guard break + knockback hook.
- **`VitalManager`** — health + stamina; drives the stamina economy.
- **`OrcAI`** (combat layer) — tune for openings/telegraphs; archetype ScriptableObjects (`OrcArchetypeDefinition`) give per-type variety.
- **`WeaponManager` / `WeaponData` / `CombatLoadout`** — the hook for cooldown abilities / equipment.

---

## Suggested phased plan

- **Phase 1 — Juice + TTK pass** (biggest feel-per-effort, mostly additive, low risk): hitstop, knockback, hit VFX/SFX, reliable enemy stagger; retune TTK and orc attack pacing. *This alone should flip "chipping a sponge" → "landing real blows."*
- **Phase 2 — Soft lock-on + lunge + crowd sweeps.**
- **Phase 3 — Deflect meter + guard break + dodge i-frames** (the block/counterplay layer).
- **Phase 4 — Ability system on cooldown**, wired to 7DTD equipment.

Do Phase 1 first and re-feel before committing to the rest.

---

## Open decisions / questions (resolve when picking this up)

- Target **TTK per orc archetype** (grunt vs berserker vs elite).
- **Soft lock-on strength** — full auto-orient vs subtle assist. Too strong feels cheap, especially in any human-facing moment.
- **Ability set** — which equipment/tools map to which "hero move."
- Whether to include the **light feint** or stay strictly accessible.
- **Screen shake / camera feedback** intensity — co-op friendly, don't nauseate.

---

## Cross-references

- `design/7DaysTillDawn_Design.md` — equipment-based direction; cooldown abilities map onto this.
- `design/OrcAI.md` — orc combat AI; the pacing/telegraph changes live here.
- Code: `HitboxController.cs`, `DamageReceiver.cs`, `VitalManager`, `OrcAI.cs`, `WeaponManager`.
