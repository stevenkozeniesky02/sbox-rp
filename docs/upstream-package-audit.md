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

## Three s&box dependency mechanisms — pick the right one

After multiple wrong turns, here's the actual taxonomy:

**1. Libraries** (per [docs › Libraries](https://sbox.game/dev/doc/code/libraries))
- C# source code packages — shared reusable code + assets.
- Install via **View → Library Manager → Browse → Install**.
- Source files land in `<project>/Libraries/<org>.<ident>/`.
- Commit `Libraries/<...>/` to git — we own the source after install.
- The "addons" on sbox.game tagged with `extension Addon` are **NOT** libraries (different category).

**2. Addons** (per [docs › Addon Project](https://sbox.game/dev/doc/getting-started/project-types/addon-project))
- *Asset-only* packages (maps, models, materials, ActionGraph) that target a base game.
- **Cannot contain C# code.** Only ActionGraph for behavior.
- The author published the package via an "Addon Project" type in the editor, targeting a specific base game.
- **Consumed as Cloud Assets** (see #3) — NOT via `PackageReferences`.

**3. Cloud Assets** (per [docs › Cloud Assets](https://sbox.game/dev/doc/assets/resources/cloud-assets)) — this is how you USE an addon's content
- **Cloud Browser drag-and-drop** in the editor (easiest) — drag asset from cloud browser into your scene; editor auto-downloads + caches.
- **Property reference**: `public Model MyModel { get; set; }` — the Cloud Browser lets the editor pick a cloud asset for that property.
- **Code reference**: `Cloud.Model("sanboxstore.realistic_lockpick")` — compile-time download. Asset gets baked into our published package.
- **Runtime fetch**: `await Package.Fetch(ident); await package.MountAsync();` — dynamic GMod-style asset mounting.

**Decision tree for adding new external content:**
- Need executable C# code? → use a **Library**.
- Need just a model/material/sound? → use a **Cloud Asset** by ident.
- Need to ship a model/material yourself for others to use? → publish via **Addon Project**.

## Currently referenced

*(none — `PackageReferences` array in sandbox.sbproj is empty. Libraries we've installed live under `Libraries/`. Cloud assets are referenced inline in code or via property bindings.)*

## Lessons learned the hard way

| Package | Mistake | Correct approach |
|---|---|---|
| `sanboxstore.realistic_lockpick` (addon — Released, 4d old, 1 👍, 2.3KB) | Added to `PackageReferences` → did nothing | Phase D: `Cloud.Model("sanboxstore.realistic_lockpick")` in our weapon class, write C# behavior ourselves (addons can't contain code). |
| `null.duplicator` (addon — Released, 3yr old, 18 👍, 14.2KB, tagged `gmod`) | Same | Phase E: similar approach — `Cloud.Model(...)` + ActionGraph if the addon ships behavior that way. If we want full C# control, study MauveRP STUDIED #31 (CopyPasteTool) and build it from scratch. |

Both are confirmed-real packages. We just can't *mount* them — only use their assets at compile-or-runtime via `Cloud.*` APIs.

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
