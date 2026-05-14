# Phase B — Custom Jobs Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the framework that makes adding a new custom job ≈150 LOC + a `.jobdef` instead of weaving job-specific code throughout the player system. Validate with ONE pilot job end-to-end so we know the pattern works.

**Architecture:**
- `JobSystem<TSelf>` — abstract base for per-job behavior systems (singletons via `GameObjectSystem<TSelf>`). Each subclass declares the job ident it watches; the manager fires `OnBecameJob`/`OnLeftJob` hooks when players switch in/out. Mirrors MauveRP STUDIED #8 (Murderer-style).
- `BaseInteractionWeapon` — abstract `BaseCarryable` subclass with built-in per-Steam-ID cooldowns, aim-target trace helper, abstract `OnInteract(attacker, target)`. Mirrors MauveRP STUDIED #34 (Pickpocket/Mugging/Begging family).
- Hook into existing `Player.SetJobDefinition` (in `Code/Player/Player.Jobs.cs`) to dispatch the lifecycle events.
- **Pilot job:** chosen at execution time. Plan defaults to Hobo + BeggingTool for the smallest-possible validation (Hobo job already exists; BeggingTool is 3 minutes of code on top of the new base).

**Tech Stack:** C# 12 (s&box flavored), `Sandbox.GameObjectSystem<TSelf>` base, `Component.IPressable` for interaction targets, `Sandbox.Diagnostics.Assert` for self-tests. No CLI test runner — verification is editor play-mode + console logs.

**Working dir:** `~/sbox-rp/` on `main`.

---

## File map

| File | Action | Responsibility |
|---|---|---|
| `Code/Jobs/JobSystem.cs` | Create | Abstract base + static registry for per-job behavior systems. |
| `Code/Jobs/JobSystemSelfTest.cs` | Create | DEBUG-only self-test asserting hooks fire correctly. |
| `Code/Player/Player.Jobs.cs` | Modify | After job change, dispatch OnLeftJob to old system + OnBecameJob to new system. |
| `Code/Game/Weapon/BaseInteractionWeapon.cs` | Create | Abstract weapon base with cooldown + target trace + HUD overlay. |
| `Code/Jobs/HoboSystem.cs` *(pilot)* | Create | Minimal per-job system for Hobo — logs lifecycle, no game effect. |
| `Code/Weapons/Begging/BeggingTool.cs` *(pilot)* | Create | Interaction weapon — beg target player for money. |
| `Assets/jobs/hobo.jobdef` *(pilot)* | Modify | Add BeggingTool to `DefaultWeapons`. |
| `docs/recipes/adding-a-custom-job.md` | Create | Step-by-step recipe for future jobs. |

---

## Task 1: `JobSystem<TSelf>` abstract base + registry

**Files:**
- Create: `Code/Jobs/JobSystem.cs`

- [ ] **Step 1: Write the base class**

Create `Code/Jobs/JobSystem.cs`:

```csharp
namespace Sandbox;

/// <summary>
/// Marker interface implemented by every <see cref="JobSystem{TSelf}"/>.
/// Lets <see cref="JobSystemRegistry"/> hold them without generic gymnastics.
/// </summary>
public interface IJobSystem
{
	string JobIdent { get; }
	void OnBecameJob( PlayerData playerData );
	void OnLeftJob( PlayerData playerData );
}

/// <summary>
/// Abstract base for per-job behavior systems. Subclass once per custom job
/// that needs hooks beyond what a plain .jobdef provides.
/// Auto-registers with <see cref="JobSystemRegistry"/> on construction.
/// </summary>
public abstract class JobSystem<TSelf> : GameObjectSystem<TSelf>, IJobSystem
	where TSelf : JobSystem<TSelf>, new()
{
	protected JobSystem( Scene scene ) : base( scene )
	{
		JobSystemRegistry.Register( this );
	}

	/// <summary>Resource path of the .jobdef this system handles (e.g. "jobs/hobo.jobdef").</summary>
	public abstract string JobIdent { get; }

	/// <summary>Called once when a player enters this job. Default is no-op.</summary>
	public virtual void OnBecameJob( PlayerData playerData ) { }

	/// <summary>Called once when a player leaves this job. Default is no-op.</summary>
	public virtual void OnLeftJob( PlayerData playerData ) { }
}

/// <summary>Global registry mapping job-ident → system. Lookup is case-insensitive.</summary>
public static class JobSystemRegistry
{
	private static readonly Dictionary<string, IJobSystem> _byIdent = new( StringComparer.OrdinalIgnoreCase );

	public static void Register( IJobSystem system )
	{
		if ( system is null || string.IsNullOrWhiteSpace( system.JobIdent ) )
			return;
		_byIdent[system.JobIdent] = system;
	}

	public static IJobSystem Find( string jobIdent )
	{
		if ( string.IsNullOrWhiteSpace( jobIdent ) )
			return null;
		return _byIdent.TryGetValue( jobIdent, out var sys ) ? sys : null;
	}
}
```

- [ ] **Step 2: Verify compile**

Reload project in editor. Console should have no new red errors. `Code/Jobs/JobSystem.cs` should be picked up by the next compile.

- [ ] **Step 3: Commit**

```bash
cd ~/sbox-rp
git add Code/Jobs/JobSystem.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(jobs): add JobSystem<TSelf> base + JobSystemRegistry

Phase B framework piece — subclass JobSystem<TSelf> once per custom
job that needs lifecycle hooks (OnBecameJob/OnLeftJob) beyond what a
plain .jobdef provides. Auto-registers via JobSystemRegistry, lookup
by ident is case-insensitive.

Mirrors MauveRP STUDIED #8 (Murderer-style per-job hub).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Self-test for the registry

**Files:**
- Create: `Code/Jobs/JobSystemSelfTest.cs`

- [ ] **Step 1: Write the self-test**

Create `Code/Jobs/JobSystemSelfTest.cs`:

```csharp
#if DEBUG
namespace Sandbox;

/// <summary>
/// Phase B Task 2 — assert JobSystemRegistry register/find works
/// before we wire it into Player.SetJobDefinition.
/// </summary>
internal sealed class JobSystemSelfTest : GameObjectSystem<JobSystemSelfTest>
{
	private sealed class FakeJobSystem : IJobSystem
	{
		public string JobIdent => "test/fake.jobdef";
		public int BecameCount;
		public int LeftCount;
		public void OnBecameJob( PlayerData pd ) => BecameCount++;
		public void OnLeftJob( PlayerData pd ) => LeftCount++;
	}

	public JobSystemSelfTest( Scene scene ) : base( scene )
	{
		var fake = new FakeJobSystem();
		JobSystemRegistry.Register( fake );

		var found = JobSystemRegistry.Find( "test/fake.jobdef" );
		Assert.True( ReferenceEquals( fake, found ), "JobSystemRegistry.Find returned wrong instance" );

		var caseInsensitive = JobSystemRegistry.Find( "TEST/FAKE.JOBDEF" );
		Assert.True( ReferenceEquals( fake, caseInsensitive ), "JobSystemRegistry should be case-insensitive" );

		var missing = JobSystemRegistry.Find( "test/does-not-exist.jobdef" );
		Assert.True( missing is null, "Missing job should return null, not throw" );

		Log.Info( "[JobSystemRegistry] self-test PASSED" );
	}
}
#endif
```

- [ ] **Step 2: Verify by reloading editor + hitting Play**

In the s&box editor, hit ▶ Play. Console should print `[JobSystemRegistry] self-test PASSED`. If any `Assert.True` fails, the failure message tells you what broke.

- [ ] **Step 3: Commit**

```bash
cd ~/sbox-rp
git add Code/Jobs/JobSystemSelfTest.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
test(jobs): self-test for JobSystemRegistry

Verifies Register/Find round-trip, case insensitivity, and that
missing lookups return null instead of throwing. Runs at scene start
under DEBUG; will go away once we delete the file post-Phase-B.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Wire `Player.SetJobDefinition` to fire hooks

**Files:**
- Modify: `Code/Player/Player.Jobs.cs` — the `SetJobDefinition` method

- [ ] **Step 1: Read the method first**

Open `Code/Player/Player.Jobs.cs` and read the body of `SetJobDefinition(JobDefinition definition)` — should be ~10 lines starting around line 29. Note the existing `oldJobDefinitionPath` capture and `CleanupPreviousJobItems` call; we'll add our dispatch *after* those finish (so cleanup runs first).

- [ ] **Step 2: Patch SetJobDefinition**

Edit `Code/Player/Player.Jobs.cs`. Find the `SetJobDefinition` method (the one that takes a `JobDefinition definition` parameter, not a string). After the existing assignments to `JobDefinitionPath` / `SetJobTitle` / `PlayerData?.SetJob(...)`, add the hook dispatch.

Expected before:
```csharp
public void SetJobDefinition( JobDefinition definition )
{
	if ( definition is null )
		return;

	CleanupPreviousJobItems( JobDefinitionPath, definition.ResourcePath );

	JobDefinitionPath = definition.ResourcePath;
	SetJobTitle( definition.Title );
	PlayerData?.SetJob( definition.ResourcePath, definition.Title );
}
```

Expected after (add the `var oldPath` capture at top, dispatch at bottom):
```csharp
public void SetJobDefinition( JobDefinition definition )
{
	if ( definition is null )
		return;

	var oldPath = JobDefinitionPath;
	CleanupPreviousJobItems( JobDefinitionPath, definition.ResourcePath );

	JobDefinitionPath = definition.ResourcePath;
	SetJobTitle( definition.Title );
	PlayerData?.SetJob( definition.ResourcePath, definition.Title );

	if ( PlayerData.IsValid() )
		DispatchJobChange( oldPath, definition.ResourcePath, PlayerData );
}

private static void DispatchJobChange( string oldPath, string newPath, PlayerData pd )
{
	if ( string.Equals( oldPath, newPath, StringComparison.OrdinalIgnoreCase ) )
		return;

	JobSystemRegistry.Find( oldPath )?.OnLeftJob( pd );
	JobSystemRegistry.Find( newPath )?.OnBecameJob( pd );
}
```

Use the Edit tool with the exact "Expected before" and "Expected after" strings.

- [ ] **Step 3: Verify compile, no behavior change yet**

Reload editor → no new compile errors. Hit Play. The self-test from Task 2 still passes. (No job-system subclasses exist yet, so dispatch is a no-op in practice — but the wiring is in.)

- [ ] **Step 4: Commit**

```bash
cd ~/sbox-rp
git add Code/Player/Player.Jobs.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(jobs): dispatch OnBecameJob/OnLeftJob hooks from SetJobDefinition

Phase B framework wiring — after a player's job changes via
SetJobDefinition, JobSystemRegistry.Find(oldPath) fires OnLeftJob
and Find(newPath) fires OnBecameJob. No-op until a JobSystem<>
subclass actually exists for one of the jobs.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `BaseInteractionWeapon` abstract base

**Files:**
- Create: `Code/Game/Weapon/BaseInteractionWeapon.cs`

- [ ] **Step 1: Write the base**

Create `Code/Game/Weapon/BaseInteractionWeapon.cs`:

```csharp
namespace Sandbox;

/// <summary>
/// Abstract base for interaction tools that target another player with a cooldown
/// (Pickpocket, Mugging, Begging, Hostage, Defibrillator, etc).
/// Subclasses declare CooldownSeconds + override OnInteract(attacker, target).
/// Pattern from MauveRP STUDIED #34.
/// </summary>
public abstract partial class BaseInteractionWeapon : BaseCarryable
{
	private static readonly Dictionary<ulong, RealTimeSince> _cooldowns = new();

	/// <summary>Cooldown between interactions, per-Steam-ID (shared across all subclasses of this type).</summary>
	public abstract float CooldownSeconds { get; }

	/// <summary>Max distance from owner to a target.</summary>
	public virtual float InteractionRange => 120f;

	/// <summary>Override to require something of the target (e.g. not seated, not in vehicle).</summary>
	public virtual bool IsTargetEligible( Player attacker, Player target )
	{
		if ( !target.IsValid() ) return false;
		if ( target == attacker ) return false;
		return true;
	}

	/// <summary>Server-authoritative action when interaction fires successfully.</summary>
	protected abstract void OnInteract( Player attacker, Player target );

	public override void OnControl( Player player )
	{
		base.OnControl( player );

		if ( !player.IsValid() )
			return;

		if ( !Input.Pressed( "attack1" ) )
			return;

		if ( IsOnCooldown( player.SteamId ) )
			return;

		var target = FindAimedPlayer( player );
		if ( !IsTargetEligible( player, target ) )
			return;

		StartCooldown( player.SteamId );
		OnInteract( player, target );
	}

	public override void DrawHud( HudPainter painter, Vector2 crosshair )
	{
		base.DrawHud( painter, crosshair );

		var local = Player.FindLocalPlayer();
		if ( !local.IsValid() )
			return;

		var remaining = CooldownRemaining( local.SteamId );
		if ( remaining > 0 )
		{
			painter.DrawText(
				new TextRendering.Scope( $"{remaining:0.0}s", Color.White.WithAlpha( 0.8f ), 14 ),
				crosshair + new Vector2( 0, 28 )
			);
		}
	}

	protected Player FindAimedPlayer( Player owner )
	{
		var ray = owner.AimRay;
		var tr = Game.ActiveScene.Trace
			.Ray( ray.Position, ray.Position + ray.Forward * InteractionRange )
			.IgnoreGameObjectHierarchy( owner.GameObject )
			.Run();

		return tr.Hit ? tr.GameObject?.GetComponentInParent<Player>() : null;
	}

	protected bool IsOnCooldown( ulong steamId )
	{
		if ( !_cooldowns.TryGetValue( steamId, out var since ) )
			return false;
		return (float)since < CooldownSeconds;
	}

	protected float CooldownRemaining( ulong steamId )
	{
		if ( !_cooldowns.TryGetValue( steamId, out var since ) )
			return 0f;
		var remaining = CooldownSeconds - (float)since;
		return remaining > 0 ? remaining : 0f;
	}

	protected void StartCooldown( ulong steamId )
	{
		_cooldowns[steamId] = 0f;
	}
}
```

- [ ] **Step 2: Verify compile**

Reload editor. Should compile clean. (If any API mismatches show up — e.g. `Player.FindLocalPlayer` not existing, or `Input.Pressed` signature differing — read the error and adjust. Common s&box differences: `AimRay` might be different, `RealTimeSince` cast might need `(float)` removal.)

- [ ] **Step 3: Commit**

```bash
cd ~/sbox-rp
git add Code/Game/Weapon/BaseInteractionWeapon.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(weapons): add BaseInteractionWeapon abstract base

Phase B framework piece — for criminal/job tools that target another
player with a cooldown (pickpocket, mugging, begging, defibrillator).
Subclasses declare CooldownSeconds + override OnInteract.

Built-ins: per-Steam-ID cooldown dict, aim-target trace,
target-eligibility hook, HUD cooldown overlay.

Pattern from MauveRP STUDIED #34.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Pilot job — choose, then implement

**Decision point.** The framework above is job-agnostic. The pilot validates it end-to-end. The plan defaults to **Hobo + BeggingTool** because:
- The Hobo job already exists in our fork (`Assets/jobs/hobo.jobdef`).
- BeggingTool is the smallest meaningful interaction weapon (1 line of game effect — give attacker $10 from target).
- The validation surface is tiny: spawn as Hobo, see tool, fire it, see cooldown HUD, switch jobs, see hook log.

**Alternatives:**
- **Reporter + /news chat command** — exercises the JobSystem but not the BaseInteractionWeapon.
- **Drug Dealer + PickpocketTool** — too coupled to Phase D entities.

If the user picks a different pilot, swap the names below — the shape is identical.

### Files (assuming Hobo + Begging):
- Create: `Code/Jobs/HoboSystem.cs`
- Create: `Code/Weapons/Begging/BeggingTool.cs`
- Modify: `Assets/jobs/hobo.jobdef` (add BeggingTool to DefaultWeapons)

- [ ] **Step 1: Implement `HoboSystem`**

Create `Code/Jobs/HoboSystem.cs`:

```csharp
namespace Sandbox;

/// <summary>
/// Per-job system for Hobo. Phase B pilot — minimal scope: logs lifecycle
/// so we can verify the JobSystem framework works end-to-end.
/// Add real Hobo behavior (e.g. unique HUD, /broadcast spam cooldown, etc) later.
/// </summary>
public sealed class HoboSystem : JobSystem<HoboSystem>
{
	public HoboSystem( Scene scene ) : base( scene ) { }

	public override string JobIdent => Player.HoboJobDefinitionPath; // "jobs/hobo.jobdef"

	public override void OnBecameJob( PlayerData playerData )
	{
		Log.Info( $"[HoboSystem] {playerData.DisplayName} became Hobo." );
	}

	public override void OnLeftJob( PlayerData playerData )
	{
		Log.Info( $"[HoboSystem] {playerData.DisplayName} left Hobo." );
	}
}
```

(If `Player.HoboJobDefinitionPath` isn't the right const name, grep for it in `Code/Player/Player.Jobs.cs` — we saw it at line 6 in the earlier survey.)

- [ ] **Step 2: Implement `BeggingTool`**

Create `Code/Weapons/Begging/BeggingTool.cs`:

```csharp
namespace Sandbox;

/// <summary>
/// Hobo-only interaction weapon. Aim at a player, attack1 → take $10 from them
/// and give it to the Hobo. 10s cooldown per Hobo. Loud sound + chat broadcast.
/// </summary>
public sealed partial class BeggingTool : BaseInteractionWeapon
{
	public override float CooldownSeconds => 10f;
	public override float InteractionRange => 100f;

	private const int BegAmount = 10;

	public override bool IsTargetEligible( Player attacker, Player target )
	{
		if ( !base.IsTargetEligible( attacker, target ) ) return false;
		// Don't beg from someone who can't afford it.
		if ( (target.PlayerData?.Money ?? 0) < BegAmount ) return false;
		return true;
	}

	protected override void OnInteract( Player attacker, Player target )
	{
		if ( !Networking.IsHost ) return;

		var targetPd = target.PlayerData;
		var attackerPd = attacker.PlayerData;
		if ( targetPd is null || attackerPd is null ) return;

		if ( !targetPd.TryTakeMoney( BegAmount ) ) return;
		attackerPd.GiveMoney( BegAmount );

		Log.Info( $"[BeggingTool] {attacker.DisplayName} begged ${BegAmount} from {target.DisplayName}." );
	}
}
```

(If `PlayerData.TryTakeMoney`/`GiveMoney`/`Money` aren't the exact names, read `Code/Player/Player.Roleplay.cs` to find the actual API — we saw the same names already in chat commands #29 work.)

- [ ] **Step 3: Add BeggingTool to hobo.jobdef**

Read `Assets/jobs/hobo.jobdef` first to see its structure. Then edit the `DefaultWeapons` list (or whatever the loadout array is named) to include the BeggingTool prefab path.

The new weapon needs a `.prefab` to be spawned — for the smallest-possible version, we can create a minimal prefab via the editor:
- Right-click in `Assets/weapons/begging/` (create the folder if needed)
- Create → Prefab
- Name it `begging_tool.prefab`
- Add a `BeggingTool` component to its root GameObject
- Set DisplayName, InventorySlot, etc. via the inspector

Then update `hobo.jobdef` `DefaultWeapons` array to include `"weapons/begging/begging_tool.prefab"`.

(Doing the prefab inside the editor is the right call — easier than hand-authoring JSON.)

- [ ] **Step 4: Commit**

```bash
cd ~/sbox-rp
git add Code/Jobs/HoboSystem.cs Code/Weapons/Begging/BeggingTool.cs Assets/jobs/hobo.jobdef Assets/weapons/begging/
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(hobo): pilot HoboSystem + BeggingTool for Phase B framework validation

Smallest-possible pilot exercising both Phase B framework pieces:
- HoboSystem: JobSystem<> subclass that logs OnBecameJob/OnLeftJob.
- BeggingTool: BaseInteractionWeapon subclass; aim+attack1 takes $10
  from the targeted player, gives it to the Hobo. 10s cooldown.
- hobo.jobdef: BeggingTool added to default loadout.

Validates that the framework works end-to-end before we templatize
for more jobs.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Editor smoke test

- [ ] **Step 1: Solo test in editor**

In s&box editor: ▶ Play, switch to Hobo via F4. Verify:
- Console shows `[HoboSystem] <name> became Hobo.` immediately after the switch.
- You spawn holding the BeggingTool (check inventory / view model).
- Aim crosshair at yourself (won't work — `target == attacker` blocked) — should show no cooldown HUD.
- (If you can spawn a bot or have a second client) aim at another Player. Attack1 → cooldown HUD shows `10.0s` counting down. Console shows `[BeggingTool] <hobo> begged $10 from <target>.`
- Switch to a non-Hobo job. Console shows `[HoboSystem] <name> left Hobo.`

- [ ] **Step 2: If anything failed, paste console output**

Common failure modes:
- "TryTakeMoney does not exist" — check actual API name in `Code/Player/Player.Roleplay.cs`.
- BeggingTool not in hand at spawn — `DefaultWeapons` path wrong in hobo.jobdef, or prefab didn't save.
- No `OnBecameJob` log — dispatch wiring in Task 3 didn't take. Re-check `Player.Jobs.cs` change.

- [ ] **Step 3: Commit any final adjustments**

```bash
cd ~/sbox-rp
git status
# if anything changed:
git add -A
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
fix(hobo): post-smoke-test adjustments for pilot job

[Brief note on what changed]

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Recipe doc — "Adding a new custom job"

**Files:**
- Create: `docs/recipes/adding-a-custom-job.md`

- [ ] **Step 1: Write the recipe**

Create `docs/recipes/adding-a-custom-job.md`:

```markdown
# Recipe: Adding a New Custom Job

The Phase B framework makes a new job 3 files + a jobdef edit. Use this when adding any new RP job.

## When to use what

| What the job needs | What to add |
|---|---|
| Just a label + salary + default weapons | Just a `.jobdef`. No code. |
| Periodic behavior (income tick, mark of death, etc) or lifecycle hooks | A `Code/Jobs/<JobName>System.cs` subclassing `JobSystem<>`. |
| A target-interaction tool (mug, beg, pickpocket-style) | A `Code/Weapons/<Tool>/<Tool>.cs` subclassing `BaseInteractionWeapon`. |

## Step-by-step

1. **Create `Assets/jobs/<job-id>.jobdef`** in the editor (Right-click → Create → JobDefinition).
   - Set Identifier, DisplayName, Description, Color, Salary, MaxPlayers, Team, PlayerModel, Clothing, DefaultWeapons.

2. **(If you need hooks)** Create `Code/Jobs/<JobName>System.cs`:
   ```csharp
   public sealed class <JobName>System : JobSystem<<JobName>System>
   {
       public <JobName>System( Scene scene ) : base( scene ) { }
       public override string JobIdent => "jobs/<job-id>.jobdef";
       public override void OnBecameJob( PlayerData pd ) { /* ... */ }
       public override void OnLeftJob( PlayerData pd ) { /* ... */ }
   }
   ```

3. **(If you want an interaction tool)** Create `Code/Weapons/<Tool>/<Tool>.cs`:
   ```csharp
   public sealed partial class <Tool> : BaseInteractionWeapon
   {
       public override float CooldownSeconds => 30f;
       public override bool IsTargetEligible( Player attacker, Player target ) { /* ... */ }
       protected override void OnInteract( Player attacker, Player target ) { /* ... */ }
   }
   ```
   Then create a prefab `Assets/weapons/<tool>/<tool>.prefab` with the component, and reference its path in the jobdef's `DefaultWeapons`.

4. **Test in editor:** ▶ Play, switch to the new job, verify the loadout + hooks fire.

5. **(Optional) Add a chat command:** if the job needs a `/command`, add it to `Code/Game/ChatCommands/ChatCommandSystem.cs` `StaticCommands` array and write the handler.

## Reference implementations

- `Code/Jobs/HoboSystem.cs` — minimal lifecycle logging.
- `Code/Weapons/Begging/BeggingTool.cs` — minimal $10-take interaction weapon.

## Where the patterns came from

See `mauverp-reference/STUDIED.md`:
- #2 JobManager tick hub
- #3 JobDefinition declarative
- #8 Murderer-style vertical (lifecycle hooks)
- #34 Cooldown-interaction weapon family
```

- [ ] **Step 2: Commit**

```bash
cd ~/sbox-rp
git add docs/recipes/adding-a-custom-job.md
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
docs: add recipe for adding a custom job

Phase B closes — new jobs become 3 files + a jobdef edit:
- .jobdef (declarative)
- Code/Jobs/<JobName>System.cs (lifecycle hooks)
- Code/Weapons/<Tool>/<Tool>.cs (optional interaction tool)

References the framework pieces from Phase B and the MauveRP patterns
they were modeled on.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)" && git push origin main
```

---

## Self-Review

**1. Spec coverage:** all roadmap Phase B items covered:
- ✅ Pilot job .jobdef data — Task 5 step 3
- ✅ Per-job system class — Tasks 1+2+5 step 1
- ✅ Interaction weapon template — Tasks 4 + 5 step 2
- ✅ F4 menu hook — no change needed; existing JobVoteManager + SetJobDefinition path handles it
- ✅ Recipe doc — Task 7
- ✅ Smallest viable end-to-end pilot — Task 6

**2. Placeholder scan:**
- Task 5 leaves "the user picks a different pilot" as a fork — but defaults concretely to Hobo+Begging with full code. Not a placeholder; a documented decision point with a default.
- The prefab creation in Task 5 step 3 is necessarily editor-driven (no JSON-only path) — clearly stated.

**3. Type consistency:**
- `JobSystem<TSelf>` / `IJobSystem` / `JobSystemRegistry` names match between Tasks 1, 2, 3, 5.
- `BaseInteractionWeapon.OnInteract(attacker, target)` matches between Task 4 and Task 5's `BeggingTool` override.
- `OnBecameJob` / `OnLeftJob` consistent.
- `JobIdent` consistent.

No issues found.

---

## Execution Handoff

Plan saved at `docs/superpowers/plans/2026-05-13-phase-b-jobs-framework.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session via the executing-plans skill, batch with checkpoints.

Which approach?
