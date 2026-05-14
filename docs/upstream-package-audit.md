# Upstream package audit

Living list of sbox.game packages + Facepunch upstream code we'd **reference instead of reimplementing** so we save weeks. Update as we discover new options.

## Confirmed in-repo (already from sousou63/Facepunch)

| Feature | File(s) | Source |
|---|---|---|
| Physgun | `Code/Player/Player.Physgun.cs` + tool support | sousou63/DarkRP (which inherited from Facepunch sandbox) |
| Constraint tools (weld, rope, axis, etc.) | `Code/Weapons/ToolGun/Modes/` | sousou63's port |
| Toolgun base + many modes | `Code/Weapons/ToolGun/` | sousou63's port |
| Money printer base | `Code/Player/Player.MoneyPrinters.cs` | sousou63's port (tier/upgrade ladder is our extension job in Phase D) |
| Job framework | `Code/Jobs/` + `Assets/jobs/*.jobdef` | sousou63's port |

## How to actually install a library

Per [s&box docs › Libraries](https://sbox.game/dev/doc/code/libraries): **libraries (what sbox.game labels "addons") are NOT mounted via `PackageReferences`.** They are source-copied into the project. Correct workflow:

1. In the editor: **View menu → Library Manager**.
2. Browse tab → search for the package (e.g. `realistic_lockpick`, `duplicator`).
3. Click **Install**. The editor writes the library's source files to `~/sbox-rp/Libraries/<org>.<ident>/`.
4. **Commit `Libraries/<org>.<ident>/`** to git — the source becomes part of our repo. We own it from then on (can edit, delete what we don't need, etc).

This means: when a library is good, we own a copy. When it's abandoned, we still have it. If we want to change behavior, we just edit the files.

## Installed via Library Manager → committed under `Libraries/`

*(none yet — install via UI, then add a row here listing org.ident + feature + commit hash where added)*

## Tried via the wrong mechanism (PackageReferences)

| Package | Feature | What happened |
|---|---|---|
| `sanboxstore.realistic_lockpick` | Lockpick (addon — 4 days old, 1 thumb up, 2.3KB) | Page is real, package is real. Added to `PackageReferences` in sbproj first — that's not how libraries are mounted (per docs above). Library Manager Installed tab stayed empty. Yanked the sbproj entry; install via UI when Phase D wants it. |
| `null.duplicator` | Duplicator / CopyPaste (addon — 3 years old, 18 thumbs up, 14.2KB, "The Duplicator tool from Garry's mod") | Same — real package, wrong mounting mechanism. Yank + reinstall via UI. |

## Still to search

| Feature | Status | Notes |
|---|---|---|
| Lockpick (minigame + sound + cooldown) | OPEN — try Library Manager Browse search before adding | Or plan to build in Phase D. |
| Duplicator / CopyPaste | OPEN — check Facepunch upstream `sandbox` source for an extractable mode | Or build in Phase D. |
| Chess engine (minimax + alpha-beta) | TODO | Only needed if we build the chess NPC; defer past v1. |
| YouTube/audio resolver (Cobalt-style) | TODO | For DJ boombox; would need our own API key infrastructure. Skip for v1. |

## Not in upstream — we build

| Feature | Phase | Notes |
|---|---|---|
| `PlacementSaveSystem<TData>` base | Phase A | Done. `Code/Save/PlacementSaveSystem.cs` |
| `JobCache` | Phase A | Done. `Code/Jobs/JobCache.cs` |
| Per-job system classes | Phase B | Murderer-style pattern (STUDIED #8). |
| Interaction weapon template | Phase B | Pickpocket/Mugging-style (STUDIED #34). |
| `DirtyMoneyDecaySystem` | Phase C | ~70 LOC (STUDIED #14). |
| `ServerEventSystem` | Phase C | Random server-wide events (STUDIED #5). |
| Placed RP entities (printer tiers, weed pot, money safe) | Phase D | Inherit `PlacementSaveSystem<TData>`. |

## Workflow for adding an upstream reference

1. Open `sandbox.sbproj`.
2. Add the package ident to the `PackageReferences` array.
3. Reload project in editor.
4. Verify the referenced components/assets are available in asset browser.
5. Document the addition in the table above.
