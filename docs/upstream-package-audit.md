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

## Server-side runtime install (the missing piece)

Per [Physgun's help docs](https://physgun.com/help/game-hosting/sbox/how-to-add-addons-to-sbox-server/), the canonical way to add addons to a **running s&box server** is via the in-game console (or Physgun panel console):

- **`package_install <org>/<ident>`** — fetches + mounts a package onto the server at runtime. No sbproj changes, no republish needed.
- **`package_list`** — shows currently installed packages.

This is how Physgun docs install gamemodes themselves (their example: `package_install dxura/rp` — which is sousou63's DarkRP fork, our upstream).

For **obsidianrp**, this gives admins (or us) a clean operational toggle:
- Want lockpick on the live server? `package_install sanboxstore/realistic_lockpick` from Physgun panel. Our gamemode can then `Package.Fetch` its assets at runtime without us publishing a new build.
- Want to remove an addon? Probably a `package_uninstall` command or rolling back to a pre-install state.

**Important caveat:** server-installed addons don't bake into our published package. They're per-server. If we change Physgun servers we'd need to `package_install` again. So compile-time `Cloud.Model("ident")` is still preferable for assets we *always* want shipped with our gamemode.

**Confirmed working 2026-05-13 on obsidianrp TestRP:**
```
package_install sanboxstore/realistic_lockpick
[Physgun] downloading Realistic Lockpick...
[Physgun] compiling Realistic Lockpick code...    ← addon DID have compileable code, despite docs saying otherwise
[Physgun]   ✓ sanboxstore.realistic_lockpick (0.0s)
[Physgun] ✓ Realistic Lockpick: 2.3KB across 3 files in 0.1s
[Physgun] ✓ Realistic Lockpick compiled in 0.0s

package_install null/duplicator
[Physgun] downloading Sandbox...              ← pulled facepunch.sandbox as dependency
[Physgun] compiling Sandbox code...
[Physgun]   ✓ facepunch.sandbox (7.2s)
[Physgun] ✓ Duplicator: 181B across 2 files in 0.9s  ← duplicator itself is just metadata; behavior lives in facepunch.sandbox
```

`package_list` then shows 183 active packages including:
- `sanboxstore.realistic_lockpick [console]`
- `null.duplicator [console]`
- `facepunch.sandbox [console]` (dragged in by duplicator)
- And ~180 other packages auto-mounted by the published gamemode (props, weapons, sounds, fonts — every asset MauveRP/sousou63 chose lives in its own little package).

**Correction to earlier note:** addons can absolutely ship compileable code — saw `compiling Realistic Lockpick code` in the install output. The docs saying otherwise either apply only to the "Addon Project" type at publish time, or are outdated.

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
- Need just a model/material/sound that *always* ships with our gamemode? → use a **Cloud Asset** by ident with compile-time `Cloud.Model(...)`.
- Need a model/asset that's *server-toggleable* (admin can enable/disable per-server)? → `package_install <ident>` in server console + `Package.Fetch + MountAsync` at runtime.
- Need to ship a model/material yourself for others to use? → publish via **Addon Project**.

## Currently referenced

*(none — `PackageReferences` array in sandbox.sbproj is empty. Libraries we've installed live under `Libraries/`. Cloud assets are referenced inline in code or via property bindings.)*

## Verified working: `Cloud.Model(<ident>)` for type=Model packages

Confirmed 2026-05-13 in the editor via a temporary probe (`CloudAssetProbe.cs`, since removed):
```csharp
Cloud.Model( "fish.wrench" )
// → models/items/wrench/wrench.vmdl
// → bounds: mins -0.0868,-0,-10.5007  maxs 0.0868,1.912,0.1694
```

Requirements observed:
- Argument **must be a string literal** (compiler does the download at build time — variables fail with `SB2000: Must use a string literal for a CloudAssetProvider`).
- Package must be type=Model (or at least have a Model as the bakeable primary asset). Addon-type packages fail with `SB2000: Could not resolve package asset`.
- Once resolved, the model is **baked into our published package** — survives without runtime fetches.

## Verified working: `Package.Fetch(<ident>)` at runtime

For addon-type packages where `Cloud.Model` can't bake (no primary Model), `Package.Fetch` succeeds at runtime — gets the package metadata. To actually use contents, follow with `package.MountAsync()` and load files from the mounted filesystem.

Both `sanboxstore.realistic_lockpick` and `null.duplicator` fetch cleanly but expose no `PrimaryAsset` meta (they're bundles, not single-asset packages).

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
