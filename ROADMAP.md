# obsidianrp — Roadmap

Working roadmap for the `obsidianrp/rp` s&box gamemode. This is the strategic map; detailed implementation plans for each phase live under `docs/superpowers/plans/`.

Last updated: 2026-05-13.

## Direction

Chaotic DarkRP-style RP, modern-city setting on `rp_downtown` for now. Fun-first. Built solo by [@stevenkozeniesky02](https://github.com/stevenkozeniesky02) on a 2–3 month evening/weekend timeline.

## What we have

- Forked from [`sousou63/DarkRP`](https://github.com/sousou63/DarkRP) at `b90cdc9`.
- Published to sbox.game as `obsidianrp/rp` (Unlisted) on 2026-05-13.
- Live on Physgun TestRP server. Full publish → server pipeline validated.
- 11 stock jobs (citizen, civil_protection, cook, gangster, gun_dealer, hobo, mayor, medic, mob_boss, police_chief, thief).
- Phase 3 chat commands `/me`, `/pm`, `/advert`, `/ooc`, `/dropmoney`, `/name` from upstream; `/roll`, `/ticket`, `/report` added locally (commit `be4430d`, **not yet republished**).

## MauveRP reference

`mauverp-reference/STUDIED.md` (git-ignored) holds 90+ pattern entries extracted from MauveRP's `.cll` source container. Cross-references appear below as `STUDIED #N`. **We do not copy MauveRP code** — we study patterns and write our own implementations, or pull from upstream packages where available.

## Phases

### ✅ Phase 0 — Foundation
Fork, rebrand, sync to PC, confirm build. Done at commit `cba2101`.

### ✅ Phase 1 — Publish baseline
Build in editor, publish to sbox.game, apply on Physgun, smoke-test. Done.

### ⏳ Phase A — Foundations for fast iteration
Templates that compound across all later phases.

**Goals:**
- Generic `PlacementSaveSystem<TData>` base class (STUDIED #1) — every future placeable entity inherits its persistence shape.
- `JobCache` TTL utility (STUDIED #4) — cheap insurance against scene-walk perf cliffs.
- Audit doc for upstream packages: lockpick, duplicator, constraint tools, etc. — list what we use vs. what we'd reimplement.
- Republish to push Phase 3 chat commands to live server.

**Plan:** [docs/superpowers/plans/2026-05-13-phase-a-foundations.md](docs/superpowers/plans/2026-05-13-phase-a-foundations.md)

**Done when:** `PlacementSaveSystem<TData>` exists, `JobCache` exists, upstream audit doc exists, Phase 3 commands live on Physgun.

### 🔜 Phase B — Custom jobs framework + pilot
Prove the per-job pattern with ONE pilot job. Subsequent jobs (3+ total target) added once the framework is solid.

**Goals:**
- Pilot job: `.jobdef` data + dedicated `*System` class (STUDIED #8 Murderer-style hooks) + optional interaction weapon (STUDIED #34).
- Hook into existing F4 menu.
- Document the "adding a new job" recipe.

**Done when:** 1 custom job is live, behaves distinctly from stock jobs, and the recipe for adding more is documented.

### 📋 Phase C — Economy + first chaos
Gameplay rhythm. Periodic beats.

**Goals:**
- `DirtyMoneyDecaySystem` (STUDIED #14, ~70 LOC).
- `ServerEventSystem` (STUDIED #5) with 2 events: DoublePaycheck + BountyBoard.
- Audit + tune all job salaries.
- Decide payday interval (default 5min; candidate 3min).

**Done when:** server has periodic chaos events; economy has friction.

### 📋 Phase D — Placed entities (criminal loot loop)
Multi-step grow → store → risk loop.

**Goals:**
- MoneyPrinter tier/upgrade ladder (STUDIED #11).
- WeedPot OR MethCooker (pick one for first ship).
- MoneySafe (decay-immune storage).
- Pickpocket weapon (STUDIED #34).
- All entities inherit Phase A's `PlacementSaveSystem<TData>`.

**Done when:** criminals have a multi-step economic loop with theft risk.

### 📋 Phase E — Admin level editing
Shape the world without scene editing.

**Goals:**
- ToolMode framework (STUDIED #7) — verify upstream availability first.
- 3 initial toolgun modes: JailSpawn, custom-prop spawn, Sign placement.
- All paired with Phase A's save base.

**Done when:** you can lay out RP infrastructure live in-game.

### 📋 Phase F — Polish + stretch
Open-ended; not a single milestone.

**Candidates:**
- Wealth + RP-stats leaderboards (STUDIED #64).
- More events (target 5+ in `ServerEventSystem`).
- Custom F4 menu / scoreboard branding (drop the "DarkRP" label).
- StockMarket money laundering (STUDIED #15).
- Lightweight Discord webhook for chat/staff actions (no full bridge).
- Map signage / spawn-point polish.

## Deferred — not pursuing for v1

- **Police AI NPC stack** (STUDIED #57, ~5000 LOC). Player cops only. Revisit if demand justifies.
- **Production-ops layer** — remote SQLite save backend, full `RemoteAdminBridge`, `MovementValidator` + `ReachGate` anti-cheat, `PlaytimeRewardSystem` web integration. Local JSON until player count makes a backend cost-justified.
- **Full casino subsystem** (STUDIED #6). Start with ONE game when replacing `/roll`, not 5.
- **Physgun rewrite** — keep upstream/sousou63's implementation.
- **Lockpick + Duplicator** — source from sbox.game packages where possible (see Phase A audit).

## Realistic timeline

Solo, evenings/weekends:
- Phase A: 1–2 days
- Phase B: 1–2 weeks (pilot + a few more jobs)
- Phase C: 3–5 days
- Phase D: 1–2 weeks
- Phase E: 3–5 days
- Phase F: ongoing

Roughly tracks with how MauveRP got built. Phases A–D as the substance push (≈2 months of evening time); E–F as polish (≈1 month).

## See also

- Stock workflow + machine roles: [HANDOFF.md](HANDOFF.md)
- MauveRP patterns extracted: `mauverp-reference/STUDIED.md` (private, git-ignored)
- Original roadmap discussion: [issue #1](https://github.com/stevenkozeniesky02/sbox-rp/issues/1)
