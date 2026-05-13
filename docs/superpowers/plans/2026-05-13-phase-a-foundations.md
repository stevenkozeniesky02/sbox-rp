# Phase A — Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the persistence + perf-cache templates that every later Phase (B/D/E) builds on, plus republish Phase 3 chat commands to live.

**Architecture:**
- `PlacementSaveSystem<TData>` — generic `GameObjectSystem<>` base; per-type subclasses just declare their data shape and `SpawnFromSave`. Mirrors MauveRP's per-placement-type save pattern (STUDIED #1).
- `JobCache` — static class with TTL-cached scoped role queries (king/royal-guard equivalents). Mirrors STUDIED #4.
- Phase 3 chat commands (already on `main`) get a republish to `obsidianrp/rp` on sbox.game so the live Physgun server picks them up.

**Tech Stack:** C# 12 (s&box-flavored — no `unsafe`, no `dynamic`), `Sandbox.GameObjectSystem<T>` base, `System.Text.Json` for serialization, `FileSystem.Data` for read/write. No CLI test runner exists for s&box — verification is **editor play-mode + `Sandbox.Diagnostics.Assert`** calls in startup hooks.

**Working dir:** `~/sbox-rp/` (already a git checkout on `main`).

---

## File map

| File | Action | Responsibility |
|---|---|---|
| `Code/Save/PlacementSaveSystem.cs` | Create | Generic `GameObjectSystem<TSelf>` base for per-placement-type JSON persistence. |
| `Code/Save/PlacementSaveSystemSelfTest.cs` | Create | `Sandbox.Diagnostics.Assert`-based startup self-test for the base class (runs on every server start in editor). |
| `Code/Jobs/JobCache.cs` | Create | TTL-cached scoped role queries (current job-id → players). |
| `Code/Jobs/JobCacheSelfTest.cs` | Create | Startup self-test for TTL behavior. |
| `docs/upstream-package-audit.md` | Create | Lookup list for sbox.game packages we'd reference instead of reimplementing. |
| `ROADMAP.md` | (already created) | Strategic map. |

The two `*SelfTest.cs` files are temporary scaffolding to give us **TDD-like guardrails inside s&box's runtime** — they `Log.Assert` on construction and remove themselves from real builds via `#if DEBUG`. Cheap, no test framework needed.

---

## Task 1: Generic `PlacementSaveSystem<TData>` base class

**Files:**
- Create: `Code/Save/PlacementSaveSystem.cs`
- Test (self-test scaffold): `Code/Save/PlacementSaveSystemSelfTest.cs`

- [ ] **Step 1: Write the self-test scaffold (failing — file doesn't compile yet)**

Create `Code/Save/PlacementSaveSystemSelfTest.cs`:

```csharp
#if DEBUG
using System.IO;

namespace Sandbox;

internal sealed class PlacementSaveSystemSelfTest : GameObjectSystem<PlacementSaveSystemSelfTest>
{
	public PlacementSaveSystemSelfTest( Scene scene ) : base( scene )
	{
		RunRoundTripTest();
	}

	private sealed class FakeData
	{
		public int Value { get; set; }
		public string Tag { get; set; }
	}

	private sealed class FakeSaveSystem : PlacementSaveSystem<FakeData>
	{
		public FakeSaveSystem() : base( "selftest/fakesave.json" ) { }
		protected override void SpawnFromSave( FakeData data ) { /* no-op for round-trip */ }
	}

	private static void RunRoundTripTest()
	{
		var fake = new FakeSaveSystem();
		var sample = new List<FakeData>
		{
			new() { Value = 7, Tag = "alpha" },
			new() { Value = 42, Tag = "beta" }
		};

		fake.Save( sample );
		var loaded = fake.Load();

		Log.Assert( loaded.Count == 2, "PlacementSaveSystem round-trip count mismatch" );
		Log.Assert( loaded[0].Value == 7, "PlacementSaveSystem round-trip Value mismatch" );
		Log.Assert( loaded[1].Tag == "beta", "PlacementSaveSystem round-trip Tag mismatch" );

		FileSystem.Data.DeleteFile( "selftest/fakesave.json" );
		Log.Info( "[PlacementSaveSystem] self-test PASSED" );
	}
}
#endif
```

- [ ] **Step 2: Verify it fails to compile (file referenced doesn't exist)**

Open project in s&box editor → wait for compile → expect errors:
- `The type or namespace name 'PlacementSaveSystem<>' could not be found`
- `'GameObjectSystem<PlacementSaveSystemSelfTest>' does not contain a constructor that takes 1 argument` (should be fine, but if so we may need a different test approach)

Reading the editor compile output is our "test failed" signal.

- [ ] **Step 3: Implement `PlacementSaveSystem<TData>`**

Create `Code/Save/PlacementSaveSystem.cs`:

```csharp
using System.IO;
using System.Text.Json;

namespace Sandbox;

/// <summary>
/// Generic base for per-placement-type JSON persistence under FileSystem.Data.
/// Subclasses declare their data shape and how to materialize entries on load.
/// </summary>
/// <typeparam name="TData">POCO record per placement (flat fields, JSON-friendly).</typeparam>
public abstract class PlacementSaveSystem<TData> where TData : class, new()
{
	protected string FilePath { get; }

	protected PlacementSaveSystem( string filePath )
	{
		if ( string.IsNullOrWhiteSpace( filePath ) )
			throw new ArgumentException( "filePath required", nameof( filePath ) );
		FilePath = filePath;
	}

	/// <summary>Persist a list of TData to <see cref="FilePath"/> under FileSystem.Data.</summary>
	public void Save( IReadOnlyList<TData> entries )
	{
		var dir = Path.GetDirectoryName( FilePath );
		if ( !string.IsNullOrEmpty( dir ) )
			FileSystem.Data.CreateDirectory( dir );

		var json = JsonSerializer.Serialize( entries, new JsonSerializerOptions { WriteIndented = true } );
		FileSystem.Data.WriteAllText( FilePath, json );
	}

	/// <summary>Load all TData entries from <see cref="FilePath"/>. Returns empty list if missing or invalid.</summary>
	public List<TData> Load()
	{
		if ( !FileSystem.Data.FileExists( FilePath ) )
			return new List<TData>();

		try
		{
			var json = FileSystem.Data.ReadAllText( FilePath );
			return JsonSerializer.Deserialize<List<TData>>( json ) ?? new List<TData>();
		}
		catch ( JsonException ex )
		{
			Log.Warning( $"[{GetType().Name}] failed to parse {FilePath}: {ex.Message}; returning empty." );
			return new List<TData>();
		}
	}

	/// <summary>
	/// Iterate loaded entries and instantiate placements in the scene.
	/// Subclasses implement per-entry spawn behavior.
	/// </summary>
	public void LoadAndSpawn()
	{
		foreach ( var entry in Load() )
			SpawnFromSave( entry );
	}

	/// <summary>Materialize one TData entry into the active scene.</summary>
	protected abstract void SpawnFromSave( TData data );
}
```

- [ ] **Step 4: Verify by re-opening project; self-test runs at scene start**

In s&box editor: File → Reload Project. Wait for compile. Open `Code/Save/PlacementSaveSystem.cs` to confirm it's compiled clean.

Press ▶ Play. In the **Console** tab look for the log line `[PlacementSaveSystem] self-test PASSED`. Stop play.

If you see `Log.Assert` failures instead, the round-trip is broken — read the message, fix the impl, repeat.

- [ ] **Step 5: Commit**

```bash
cd ~/sbox-rp
git add Code/Save/PlacementSaveSystem.cs Code/Save/PlacementSaveSystemSelfTest.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(save): add PlacementSaveSystem<TData> base + self-test

Generic per-placement-type JSON persistence under FileSystem.Data.
Phase A foundation that future Phase D/E placements will inherit
(doors, jail spawns, custom-prop spawns, signs, etc.).

Self-test runs at scene start under DEBUG, round-trips a fake
data list and asserts.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `JobCache` TTL utility

**Files:**
- Create: `Code/Jobs/JobCache.cs`
- Test: `Code/Jobs/JobCacheSelfTest.cs`

- [ ] **Step 1: Write the failing self-test**

Create `Code/Jobs/JobCacheSelfTest.cs`:

```csharp
#if DEBUG
namespace Sandbox;

internal sealed class JobCacheSelfTest : GameObjectSystem<JobCacheSelfTest>
{
	public JobCacheSelfTest( Scene scene ) : base( scene )
	{
		RunCacheTest();
	}

	private static int _factoryCalls;

	private static List<string> Factory()
	{
		_factoryCalls++;
		return new List<string> { "alpha", "beta" };
	}

	private static void RunCacheTest()
	{
		_factoryCalls = 0;

		var first = JobCache.Get( "test-key", 0.5f, Factory );
		Log.Assert( first.Count == 2, "JobCache.Get returned wrong count on first call" );
		Log.Assert( _factoryCalls == 1, "JobCache.Get should call factory on first miss" );

		var second = JobCache.Get( "test-key", 0.5f, Factory );
		Log.Assert( _factoryCalls == 1, "JobCache.Get should hit cache within TTL (no second factory call)" );
		Log.Assert( second.Count == 2, "JobCache cached value count mismatch" );

		JobCache.Invalidate( "test-key" );
		var third = JobCache.Get( "test-key", 0.5f, Factory );
		Log.Assert( _factoryCalls == 2, "JobCache.Get should call factory after Invalidate" );
		Log.Assert( third.Count == 2, "JobCache post-invalidate count mismatch" );

		Log.Info( "[JobCache] self-test PASSED" );
	}
}
#endif
```

- [ ] **Step 2: Confirm compile error (`JobCache` not defined)**

Reload project in editor. Expect error: `The name 'JobCache' does not exist in the current context`.

- [ ] **Step 3: Implement `JobCache`**

Create `Code/Jobs/JobCache.cs`:

```csharp
namespace Sandbox;

/// <summary>
/// TTL-cached per-key store. Use for many-reader scoped queries
/// (e.g. "who's currently King?") where every component asking would
/// otherwise walk the scene every frame.
/// Pattern from MauveRP STUDIED #4.
/// </summary>
public static class JobCache
{
	private sealed class Entry
	{
		public object Value;
		public RealTimeSince CachedAt;
	}

	private static readonly Dictionary<string, Entry> _entries = new();

	/// <summary>
	/// Get a cached value or compute via factory if missing/stale.
	/// </summary>
	public static T Get<T>( string key, float ttlSeconds, Func<T> factory ) where T : class
	{
		if ( _entries.TryGetValue( key, out var entry )
			&& entry.Value is T cached
			&& (float)entry.CachedAt < ttlSeconds )
		{
			return cached;
		}

		var fresh = factory();
		_entries[key] = new Entry { Value = fresh, CachedAt = 0f };
		return fresh;
	}

	/// <summary>Flush a single cached entry. Call on job-change events that invalidate the query.</summary>
	public static void Invalidate( string key )
	{
		_entries.Remove( key );
	}

	/// <summary>Flush everything. Useful at scene transitions.</summary>
	public static void Clear()
	{
		_entries.Clear();
	}
}
```

- [ ] **Step 4: Verify self-test passes**

In editor: reload project, ▶ Play. Watch Console for `[JobCache] self-test PASSED`. If any `Log.Assert` fires, fix and retry.

- [ ] **Step 5: Commit**

```bash
cd ~/sbox-rp
git add Code/Jobs/JobCache.cs Code/Jobs/JobCacheSelfTest.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(jobs): add JobCache TTL utility + self-test

TTL-cached per-key store for many-reader scoped queries (current
king, royal guards, etc.). Replaces per-frame scene walks where
many systems ask the same question.

Pattern from MauveRP STUDIED #4. Self-test verifies first-miss,
within-TTL hit, and post-Invalidate behavior.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Upstream package audit doc

**Files:**
- Create: `docs/upstream-package-audit.md`

- [ ] **Step 1: Draft the audit doc**

Create `docs/upstream-package-audit.md`:

```markdown
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

## Not in upstream — we'll build

| Feature | Phase | Notes |
|---|---|---|
| `PlacementSaveSystem<TData>` base | Phase A | Done in this plan. |
| `JobCache` | Phase A | Done in this plan. |
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
5. Document the addition here.
```

- [ ] **Step 2: Commit**

```bash
cd ~/sbox-rp
git add docs/upstream-package-audit.md
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
docs: add upstream package audit

Living list of sbox.game/Facepunch packages we'd reference instead
of reimplementing (lockpick, duplicator, etc). Phase A foundation.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Search sbox.game for lockpick + duplicator packages

This task is **research, not code**. Output is updates to `docs/upstream-package-audit.md` and possibly `sandbox.sbproj`.

- [ ] **Step 1: Search sbox.game for lockpick packages**

Open https://sbox.game/ and search for `lockpick`. For each promising hit:
- Note the package ident (`<org>/<name>`).
- Read the package description and example.
- Check the source/repo link if available.

Pick the most legitimate-looking package (or none if all look thin).

- [ ] **Step 2: Search for duplicator packages**

Same flow — search `duplicator`, `dupe`, `copy paste`.

Also check Facepunch's sandbox source: https://github.com/Facepunch/sandbox-game — the official sandbox gamemode likely has a duplicator.

- [ ] **Step 3: Update the audit doc with findings**

Edit `docs/upstream-package-audit.md`, replace the "TODO — search" rows for lockpick + duplicator with either:
- `<org>/<package>` (use), or
- `none viable — build in Phase D`.

- [ ] **Step 4: (If a package was picked) Reference it in sbproj**

Edit `sandbox.sbproj`, add to `PackageReferences`:

```json
"PackageReferences": [ "<org>/<package>" ],
```

Reload project. Verify it loads (Console tab shouldn't show package-not-found errors).

- [ ] **Step 5: Commit the audit update (and sbproj if changed)**

```bash
cd ~/sbox-rp
git add docs/upstream-package-audit.md sandbox.sbproj 2>/dev/null || git add docs/upstream-package-audit.md
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
docs: update upstream audit with lockpick + duplicator decisions

[Brief note on what was found and decided]

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

Replace `[Brief note...]` with the actual decision: e.g. "Lockpick: foo/bar referenced. Duplicator: no viable package, plan to build in Phase D."

---

## Task 5: Republish to push Phase 3 chat commands live

Phase 3 commands `/roll`, `/ticket`, `/report` are committed locally on `main` (`be4430d`) but the live sbox.game package is the pre-Phase-3 baseline. This task republishes.

- [ ] **Step 1: Verify everything's committed and pushed**

```bash
cd ~/sbox-rp
git status
git log --oneline origin/main..HEAD
```

`git status` should show `working tree clean`. `git log` should list all the commits ahead of origin (Phase A commits from Tasks 1–4 + Phase 3 commit `be4430d` if not yet pushed).

If there are uncommitted changes, commit them first. Then push:

```bash
git push origin main
```

- [ ] **Step 2: Open project in s&box editor**

If not already open: launch s&box editor via Steam, the project should be in recent.

Wait for compile. Check Console tab for red errors. If clean, proceed.

- [ ] **Step 3: Publish via the editor**

File menu (or Project menu) → Publish (or the publish icon).

The publish dialog should pre-fill from sbproj: Org=`obsidianrp`, Ident=`rp`. Title is "RP (working title)".

Keep visibility as **Unlisted**.

Click Upload Files. Wait for upload to complete. The diff from last publish should be small (just the chat command file + Phase A files), so this upload is fast.

- [ ] **Step 4: Wait for sbox.game CDN to settle**

After "Publish Successful", wait **~10 minutes** before starting the Physgun server. Per [project-sbox-cdn-cold-cache memory entry], fresh publishes have cold CDN edges that hit dedicated servers with BadGateway floods on first download.

If you're impatient, you can pre-warm: `curl https://sbox.game/obsidianrp/rp` and a few asset URLs.

- [ ] **Step 5: Restart Physgun server and smoke-test**

In Physgun panel: Stop → Start the server. Watch the console for the download stage. If it fails with BadGateway flood, stop and retry after a few more minutes.

Once running, connect to the server. In chat:
- `/roll` — should broadcast `* <name> rolls N (1-100)`.
- `/ticket testing` — should reply "Ticket submitted..." and (if you're admin) broadcast `[Ticket] ...`.
- `/report <yourname> testing` — should reply "Report against ... submitted."

If all three work, Phase A is **DONE.**

- [ ] **Step 6: Commit anything else open (e.g. updated HANDOFF.md if you tweaked it)**

```bash
cd ~/sbox-rp
git status
# if anything changed:
git add -A
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
chore: phase A complete; republished obsidianrp/rp build with Phase 3 commands

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
git push origin main
```

---

## Task 6: Update issue #1 body and link ROADMAP.md

- [ ] **Step 1: Open issue #1 in browser**

Visit https://github.com/stevenkozeniesky02/sbox-rp/issues/1 . Click the pencil "Edit" icon on the original post.

- [ ] **Step 2: Replace the body with the v2 body**

Paste the content prepared during plan delivery (the "Issue #1 body" block from the conversation). Save.

- [ ] **Step 3: Confirm cross-links work**

- The link from issue body to `ROADMAP.md` should resolve to the file on `main`.
- The link from `ROADMAP.md` to `docs/superpowers/plans/2026-05-13-phase-a-foundations.md` should resolve.

If either is broken, fix the link target and re-save.

---

## Self-Review

**Spec coverage:** ✅
- `PlacementSaveSystem<TData>` — Task 1.
- `JobCache` — Task 2.
- Upstream audit doc — Task 3.
- Search sbox.game for lockpick/duplicator — Task 4.
- Republish Phase 3 commands — Task 5.
- ROADMAP.md + issue body — Tasks 6 (file) + conversation deliverable (issue body).

**Placeholder scan:** No `TODO`/`fill in details`/"similar to Task N" in any step. Task 3's doc itself has TODO rows but those are *content for the reader*, not unfilled plan steps; Task 4 explicitly resolves them.

**Type consistency:**
- `PlacementSaveSystem<TData>` class name consistent across Tasks 1 and 3.
- `Save(IReadOnlyList<TData>)` / `Load() : List<TData>` / `LoadAndSpawn()` / `protected abstract void SpawnFromSave(TData)` — names match in implementation and self-test.
- `JobCache.Get<T>(key, ttlSeconds, factory)` / `Invalidate(key)` / `Clear()` — match between impl and self-test.

No issues found.
