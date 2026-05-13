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

## To search on sbox.game (Phase A audit task)

| Feature | Status | Notes |
|---|---|---|
| Lockpick (minigame + sound + cooldown) | TODO — search sbox.game for `lockpick` | If a good package exists, add via `PackageReferences` in `sandbox.sbproj`. Otherwise plan to build in Phase D. |
| Duplicator / CopyPaste | TODO — search Facepunch sandbox source | Probably in upstream sandbox; mount it. |
| Chess engine (minimax + alpha-beta) | TODO — search for chess libraries | Only needed if we build the chess NPC; defer past v1. |
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
