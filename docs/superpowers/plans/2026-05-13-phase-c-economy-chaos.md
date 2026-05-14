# Phase C — Economy + First Chaos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the economy actually tick. Today `JobDefinition.Salary` is a display value with no payout mechanism. Phase C adds:
- A working **PaydaySystem** that pays Salary every N minutes server-wide.
- An abstract **ServerEventSystem** that fires random server-wide events at intervals.
- One concrete event — **DoublePaycheck** — that doubles payouts while active.

This is the "first chaos lever" from the v2 roadmap.

**Architecture:**
- `PaydaySystem` — `GameObjectSystem<PaydaySystem>` singleton (host-only). `OnFixedUpdate` checks a `TimeSince` against `PaydayIntervalSeconds`; on tick, iterates all online players, gives each their current job's `Salary` × any active multiplier from `ServerEventSystem`. Broadcasts a "payday hit" notification.
- `ServerEventSystem` — singleton hub with public read-only state flags (`IsDoublePaycheck` etc.). Picks a random event every 10–20 min, runs its lifecycle (`Start*` / `Tick*` / `End*`). Mirrors MauveRP STUDIED #5.
- `DoublePaycheck` — first event. State flag `IsDoublePaycheck` is set during the active window; PaydaySystem multiplies salaries by 2× when set.
- Salary audit — quick manual pass through 11 jobdefs to set sensible values. Trivial editor work.

**Tech Stack:** Same as Phase B. `GameObjectSystem<TSelf>`, `[Rpc.Broadcast(NetFlags.HostOnly)]` for notifications, `Sandbox.Diagnostics.Assert` for self-tests. Verification is editor play-mode + console logs (subagents can't run the editor).

**Working dir:** `~/sbox-rp/` on `main`.

**Defer to future phase:** `DirtyMoneyDecaySystem` — our fork doesn't have a separate DirtyMoney pile yet; adding the mechanic requires more design work than fits here. `BountyBoard` event — needs damage/kill hooks we haven't verified; one event is enough to prove the framework.

---

## File map

| File | Action | Responsibility |
|---|---|---|
| `Code/Game/Economy/PaydaySystem.cs` | Create | Host-driven payday tick that pays Salary to every online player; respects active event multipliers. |
| `Code/Game/Economy/PaydaySystemSelfTest.cs` | Create | DEBUG-only self-test asserting payout math + multiplier composition. |
| `Code/Game/Events/ServerEvent.cs` | Create | Abstract base for individual events + `ServerEventRegistry` static. |
| `Code/Game/Events/ServerEventSystem.cs` | Create | Singleton manager that picks/starts/ticks/ends events on a random interval. |
| `Code/Game/Events/DoublePaycheckEvent.cs` | Create | First concrete event: sets `ServerEventSystem.IsDoublePaycheck = true` for N seconds. |
| `Code/Game/Events/ServerEventSelfTest.cs` | Create | DEBUG-only self-test for the registry + lifecycle. |
| `Assets/jobs/*.jobdef` | Modify (manual) | Salary audit pass; tune all 11 jobdef Salary fields. |
| `docs/recipes/adding-a-server-event.md` | Create | Recipe for future events. |

---

## Task 1: `PaydaySystem` — host-only tick that pays salaries

**Files:**
- Create: `Code/Game/Economy/PaydaySystem.cs`

- [ ] **Step 1: Write the system**

Read first: `Code/Player/Player.Roleplay.cs` for `GiveMoney(int)` signature. Read `Code/Jobs/JobManager.cs` and `Code/Jobs/JobDefinition.cs` for how to iterate active players + read their current job's salary.

Then create `Code/Game/Economy/PaydaySystem.cs`:

```csharp
namespace Sandbox;

/// <summary>
/// Host-only system that pays every online player their job's Salary
/// at a fixed interval. Honors active multipliers from <see cref="ServerEventSystem"/>
/// (e.g. DoublePaycheck event sets IsDoublePaycheck=true → 2× payouts).
/// </summary>
public sealed class PaydaySystem : GameObjectSystem<PaydaySystem>
{
	/// <summary>Seconds between payouts. Default 180 (3 minutes).</summary>
	public const float PaydayIntervalSeconds = 180f;

	private TimeSince _sinceLastPayday;

	public PaydaySystem( Scene scene ) : base( scene )
	{
		_sinceLastPayday = 0f;
		Listen( Stage.FinishUpdate, 0, OnTick, "PaydaySystem.Tick" );
	}

	private void OnTick()
	{
		if ( !Networking.IsHost )
			return;

		if ( (float)_sinceLastPayday < PaydayIntervalSeconds )
			return;

		_sinceLastPayday = 0f;
		RunPayday();
	}

	private void RunPayday()
	{
		var multiplier = ComputeMultiplier();

		foreach ( var player in Scene.GetAll<Player>() )
		{
			if ( !player.IsValid() ) continue;
			if ( player.Network?.Owner is null ) continue;
			TryPayPlayer( player, multiplier );
		}

		BroadcastPaydayNotice( multiplier );
	}

	private static float ComputeMultiplier()
	{
		var mult = 1f;
		if ( ServerEventSystem.Instance?.IsDoublePaycheck == true )
			mult *= 2f;
		return mult;
	}

	private static void TryPayPlayer( Player player, float multiplier )
	{
		var jobDef = player.CurrentJobDefinition;
		if ( jobDef is null ) return;

		var amount = (int)System.MathF.Round( jobDef.Salary * multiplier );
		if ( amount <= 0 ) return;

		player.GiveMoney( amount );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BroadcastPaydayNotice( float multiplier )
	{
		var prefix = multiplier > 1.01f ? $"💰💰 PAYDAY (×{multiplier:0.#}) " : "💰 Payday ";
		var local = Player.FindLocalPlayer();
		var jobDef = local?.CurrentJobDefinition;
		if ( jobDef is null ) return;

		var amount = (int)System.MathF.Round( jobDef.Salary * multiplier );
		Log.Info( $"{prefix}— received ${amount}" );
	}
}
```

NOTE: The exact `GameObjectSystem` event-listener mechanism (`Listen(Stage...)` etc.) may differ from this sketch. **Read existing host-only periodic systems** (`Code/Cleanup/CleanupSystem.cs`, `Code/Jobs/JobCache.cs`, or anything that does a periodic tick) to find the correct pattern, and adapt accordingly. If `OnFixedUpdate` is a simple `protected override`, use that. Don't invent APIs.

If `Player.CurrentJobDefinition` or `Scene.GetAll<Player>()` aren't the right APIs, find the real ones in `Code/Player/Player.Jobs.cs` / similar.

- [ ] **Step 2: Verify by reading the file + sibling references**

Confirm the periodic-tick pattern matches at least one existing system in the codebase.

- [ ] **Step 3: Commit**

```bash
cd ~/sbox-rp
git add Code/Game/Economy/PaydaySystem.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(economy): add PaydaySystem — host-only periodic salary tick

First piece of Phase C economy work. Every 3 minutes (configurable
via PaydayIntervalSeconds const), iterates online players and gives
each their JobDefinition.Salary, multiplied by any active event
multiplier from ServerEventSystem (e.g. 2× during DoublePaycheck).

Salary is currently a display-only field on JobDefinition; this
turns it into actual gameplay.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `PaydaySystem` self-test

**Files:**
- Create: `Code/Game/Economy/PaydaySystemSelfTest.cs`

- [ ] **Step 1: Write a self-test asserting payout math**

Match the style of `Code/Jobs/JobCacheSelfTest.cs` / `Code/Save/PlacementSaveSystemSelfTest.cs`.

The self-test can't iterate real players (none exist at scene init). It should test the **multiplier composition** instead. The cleanest approach: make `PaydaySystem.ComputeMultiplier()` `internal` so the self-test can call it, AND make a way to override the `ServerEventSystem.IsDoublePaycheck` flag for the test. Alternative: extract `ComputeMultiplier` to a pure static helper that takes flags as parameters — then the self-test just asserts the helper math without touching system state.

Recommended: refactor `ComputeMultiplier` into a pure static helper that takes `bool isDoublePaycheck` (and any other future flags) and returns the float multiplier. The instance method becomes a thin wrapper that reads `ServerEventSystem.Instance` and calls the pure helper.

Test asserts:
- `ComputeMultiplierFor(isDoublePaycheck: false) == 1f`
- `ComputeMultiplierFor(isDoublePaycheck: true) == 2f`

Log `[PaydaySystem] self-test PASSED` at the end.

- [ ] **Step 2: Update PaydaySystem.cs accordingly** (refactor to extract the pure helper).

- [ ] **Step 3: Commit both files** (update + self-test).

```bash
cd ~/sbox-rp
git add Code/Game/Economy/
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
test(economy): self-test for PaydaySystem multiplier math

Refactored ComputeMultiplier into a pure static helper so the math
can be asserted in isolation without touching ServerEventSystem state.

Self-test asserts:
- No active multipliers → 1×
- DoublePaycheck active → 2×

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `ServerEvent` abstract base + `ServerEventRegistry`

**Files:**
- Create: `Code/Game/Events/ServerEvent.cs`

- [ ] **Step 1: Write the abstract base + registry**

```csharp
namespace Sandbox;

/// <summary>
/// One server-wide event (DoublePaycheck, BountyBoard, etc).
/// Subclasses override the lifecycle hooks; ServerEventSystem orchestrates them.
/// Pattern from MauveRP STUDIED #5.
/// </summary>
public abstract class ServerEvent
{
	/// <summary>Display name for chat/HUD broadcasts.</summary>
	public abstract string DisplayName { get; }

	/// <summary>How long the event runs once started, in seconds.</summary>
	public abstract float DurationSeconds { get; }

	/// <summary>Relative weight for random selection. Higher = more likely. Default 1.</summary>
	public virtual float Weight => 1f;

	/// <summary>Called once when the event starts. Set state flags here.</summary>
	public virtual void OnStart( ServerEventSystem system ) { }

	/// <summary>Called every fixed-update tick while the event is active.</summary>
	public virtual void OnTick( ServerEventSystem system ) { }

	/// <summary>Called once when the event ends. Clear state flags here.</summary>
	public virtual void OnEnd( ServerEventSystem system ) { }
}

/// <summary>Global registry of available events. Auto-populated at scene start.</summary>
public static class ServerEventRegistry
{
	private static readonly List<ServerEvent> _events = new();

	public static void Register( ServerEvent ev )
	{
		if ( ev is null ) return;
		_events.Add( ev );
	}

	public static IReadOnlyList<ServerEvent> All => _events;

	public static void Clear() => _events.Clear();
}
```

- [ ] **Step 2: Commit**

```bash
cd ~/sbox-rp
git add Code/Game/Events/ServerEvent.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(events): add ServerEvent abstract base + ServerEventRegistry

Phase C event framework. Subclasses override OnStart/OnTick/OnEnd
lifecycle hooks. ServerEventSystem (next task) orchestrates random
selection + timing.

Pattern from MauveRP STUDIED #5.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `ServerEventSystem` — random event orchestrator

**Files:**
- Create: `Code/Game/Events/ServerEventSystem.cs`

- [ ] **Step 1: Write the orchestrator**

```csharp
namespace Sandbox;

/// <summary>
/// Host-only singleton. Every <see cref="MinEventIntervalSeconds"/>—<see cref="MaxEventIntervalSeconds"/>,
/// picks a random registered <see cref="ServerEvent"/> (weighted) and runs its lifecycle.
/// Other systems (PaydaySystem, etc) read public state flags to react.
/// </summary>
public sealed class ServerEventSystem : GameObjectSystem<ServerEventSystem>
{
	public const float MinEventIntervalSeconds = 600f;  // 10 min
	public const float MaxEventIntervalSeconds = 1200f; // 20 min

	/// <summary>True while a DoublePaycheck event is active. PaydaySystem reads this.</summary>
	public bool IsDoublePaycheck { get; internal set; }

	private ServerEvent _activeEvent;
	private TimeSince _sinceEventStart;
	private TimeSince _sinceLastEventEnded;
	private float _nextEventIn;

	public ServerEventSystem( Scene scene ) : base( scene )
	{
		ResetInterval();
		// TODO: wire periodic update — same approach as PaydaySystem uses (Listen or OnFixedUpdate).
	}

	private void ResetInterval()
	{
		_nextEventIn = Game.Random.Float( MinEventIntervalSeconds, MaxEventIntervalSeconds );
		_sinceLastEventEnded = 0f;
	}

	private void OnTick()
	{
		if ( !Networking.IsHost ) return;

		if ( _activeEvent is null )
		{
			if ( (float)_sinceLastEventEnded < _nextEventIn ) return;
			TryStartRandomEvent();
			return;
		}

		_activeEvent.OnTick( this );

		if ( (float)_sinceEventStart >= _activeEvent.DurationSeconds )
		{
			EndCurrentEvent();
		}
	}

	private void TryStartRandomEvent()
	{
		var available = ServerEventRegistry.All;
		if ( available.Count == 0 ) return;

		var totalWeight = 0f;
		foreach ( var ev in available ) totalWeight += ev.Weight;
		if ( totalWeight <= 0 ) return;

		var roll = Game.Random.Float( 0f, totalWeight );
		ServerEvent picked = null;
		var cursor = 0f;
		foreach ( var ev in available )
		{
			cursor += ev.Weight;
			if ( roll <= cursor ) { picked = ev; break; }
		}

		if ( picked is null ) picked = available[0];

		_activeEvent = picked;
		_sinceEventStart = 0f;
		picked.OnStart( this );
		BroadcastEventStarted( picked.DisplayName, picked.DurationSeconds );
	}

	private void EndCurrentEvent()
	{
		if ( _activeEvent is null ) return;
		var ev = _activeEvent;
		_activeEvent = null;
		ev.OnEnd( this );
		BroadcastEventEnded( ev.DisplayName );
		ResetInterval();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BroadcastEventStarted( string name, float duration )
	{
		Log.Info( $"[ServerEventSystem] EVENT STARTED: {name} (running for {duration:0}s)" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BroadcastEventEnded( string name )
	{
		Log.Info( $"[ServerEventSystem] EVENT ENDED: {name}" );
	}
}
```

The TODO is intentional — subagent needs to match whatever periodic-tick pattern PaydaySystem ended up using in Task 1 (probably `Listen(Stage.FinishUpdate, ...)` or `protected override void OnFixedUpdate()`). Use the same approach.

- [ ] **Step 2: Commit**

```bash
cd ~/sbox-rp
git add Code/Game/Events/ServerEventSystem.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(events): add ServerEventSystem orchestrator

Host-only singleton that picks random ServerEventRegistry entries
every 10-20min (weighted random), runs OnStart/OnTick/OnEnd
lifecycle for the picked event, and broadcasts notices.

Exposes IsDoublePaycheck state flag for PaydaySystem to read.
Future events add their own state flags here.

Pattern from MauveRP STUDIED #5 (ServerEventSystem).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: First concrete event — `DoublePaycheckEvent`

**Files:**
- Create: `Code/Game/Events/DoublePaycheckEvent.cs`

- [ ] **Step 1: Write the event**

```csharp
namespace Sandbox;

/// <summary>
/// "Double Paycheck" — for the duration, every PaydaySystem tick pays 2× salary.
/// Phase C pilot event.
/// </summary>
public sealed class DoublePaycheckEvent : ServerEvent
{
	public override string DisplayName => "Double Paycheck";
	public override float DurationSeconds => 300f;  // 5 minutes
	public override float Weight => 1f;

	public override void OnStart( ServerEventSystem system )
	{
		system.IsDoublePaycheck = true;
	}

	public override void OnEnd( ServerEventSystem system )
	{
		system.IsDoublePaycheck = false;
	}
}
```

We also need to **register** the event somewhere. Add a small bootstrap system that registers all known events at scene start — `Code/Game/Events/ServerEventBootstrap.cs`:

```csharp
namespace Sandbox;

/// <summary>
/// Auto-registers concrete ServerEvent subclasses with ServerEventRegistry at scene start.
/// Add new events to the array as they're created.
/// </summary>
internal sealed class ServerEventBootstrap : GameObjectSystem<ServerEventBootstrap>
{
	public ServerEventBootstrap( Scene scene ) : base( scene )
	{
		ServerEventRegistry.Clear();
		ServerEventRegistry.Register( new DoublePaycheckEvent() );
	}
}
```

- [ ] **Step 2: Commit both files**

```bash
cd ~/sbox-rp
git add Code/Game/Events/DoublePaycheckEvent.cs Code/Game/Events/ServerEventBootstrap.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
feat(events): add DoublePaycheckEvent + bootstrap registrar

First concrete ServerEvent — sets IsDoublePaycheck = true for 5
minutes. PaydaySystem multiplies salaries by 2× while the flag is set.

ServerEventBootstrap registers all known events with the registry
at scene start. Add new events to that file as they're created.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: ServerEvent self-test

**Files:**
- Create: `Code/Game/Events/ServerEventSelfTest.cs`

- [ ] **Step 1: Write the self-test**

DEBUG-only, scene-start. Assert:
- `ServerEventRegistry.All.Count >= 1` (bootstrap ran)
- `DoublePaycheckEvent.DurationSeconds == 300f`
- `DoublePaycheckEvent` instance properly toggles a fake `ServerEventSystem.IsDoublePaycheck` flag through OnStart/OnEnd (test with a manually-constructed event instance + a system instance via `ServerEventSystem.Instance`).

If `ServerEventSystem` requires a `Scene` to construct, just test the event's behavior in isolation by setting `Instance.IsDoublePaycheck` directly via reflection or via a public test helper. Simplest: just test the event's `OnStart`/`OnEnd` by passing it `null` and observing — but DoublePaycheck dereferences `system.IsDoublePaycheck = ...`, so it'd NRE on null. Better: assert against `ServerEventSystem.Instance` directly (if it exists by that point in scene init).

Pragmatic minimum: assert the registry got populated and the event metadata looks right. Skip the lifecycle assertion if it requires bending the instance state model.

- [ ] **Step 2: Commit**

```bash
cd ~/sbox-rp
git add Code/Game/Events/ServerEventSelfTest.cs
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "$(cat <<'EOF'
test(events): self-test for ServerEvent registry + DoublePaycheck

Asserts ServerEventBootstrap registered at least one event, that
DoublePaycheckEvent's duration is the expected 5 minutes, and
(if possible cleanly) the on-start/on-end flag toggle works.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Salary audit (HUMAN — manual editor work)

Current state: every jobdef has its DarkRP-port default salary. Some are probably wrong for our gamemode. The roadmap calls for "chaotic DarkRP = generous." Quick audit of all 11 jobdefs.

- [ ] **Step 1:** In editor, open each of these jobdefs one at a time and review/adjust the **Salary** field:
  - `citizen.jobdef`
  - `civil_protection.jobdef`
  - `cook.jobdef`
  - `gangster.jobdef`
  - `gun_dealer.jobdef`
  - `hobo.jobdef` (Hobo should have very low — they earn via begging)
  - `mayor.jobdef` (should be highest — political power)
  - `medic.jobdef`
  - `mob_boss.jobdef` (Criminal, high)
  - `police_chief.jobdef`
  - `thief.jobdef`

- [ ] **Step 2:** Suggested values (chaotic-DarkRP-style, payday every 3min):

| Job | Suggested Salary | Notes |
|---|---|---|
| Citizen | 45 | Default, keep |
| Hobo | 10 | Earns via /beg |
| Cook | 65 | Workman tier |
| Gangster | 55 | Criminal entry |
| Thief | 50 | Criminal entry |
| Gun Dealer | 85 | Money-printer route makes salary secondary |
| Medic | 75 | Earns revives too |
| Civil Protection | 80 | Police tier |
| Police Chief | 110 | Voted-job premium |
| Mayor | 150 | Highest base; gets political cuts later (Phase F) |
| Mob Boss | 95 | Voted-job premium |

These are starting points — tune to taste.

- [ ] **Step 3:** Commit:

```bash
cd ~/sbox-rp
git add Assets/jobs/
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "balance: salary audit pass — chaotic-DarkRP generous tuning at 3-min payday"
git push
```

---

## Task 8: Editor smoke test (HUMAN)

- [ ] Hit ▶ Play.
- [ ] Watch console for `[PaydaySystem] self-test PASSED` and `[ServerEventSystem]` activity.
- [ ] Wait 3 minutes (or change `PaydayIntervalSeconds` to 15f temporarily for faster validation).
- [ ] Observe a payday tick — your money goes up by your Salary, console logs the payday line.
- [ ] (Optional, takes 10-20 min real time) Wait for `[ServerEventSystem] EVENT STARTED: Double Paycheck`. Trigger a payday during it; verify the payout is doubled.
- [ ] (For faster validation) Temporarily lower `MinEventIntervalSeconds` to 30f, `MaxEventIntervalSeconds` to 60f in `ServerEventSystem` to see events fire quickly; revert after testing.

---

## Task 9: Recipe doc — adding a server event

**Files:**
- Create: `docs/recipes/adding-a-server-event.md`

- [ ] **Step 1:** Document the 3-step recipe (create event class, add bootstrap line, optional state flag on `ServerEventSystem`).

- [ ] **Step 2: Commit + push**

```bash
cd ~/sbox-rp
git add docs/recipes/adding-a-server-event.md
git -c user.name="Steven Kozenies" -c user.email="stevenkozeniesky@gmail.com" commit -m "docs: recipe for adding a new ServerEvent"
git push
```

---

## Self-Review

**Spec coverage:**
- ✅ Payday tick — Task 1
- ✅ Payday self-test — Task 2
- ✅ ServerEvent abstract base — Task 3
- ✅ ServerEventSystem orchestrator — Task 4
- ✅ DoublePaycheck event — Task 5
- ✅ Event self-test — Task 6
- ✅ Salary audit — Task 7 (manual)
- ✅ Editor smoke test — Task 8 (manual)
- ✅ Recipe doc — Task 9

**Placeholder scan:** Task 1 + Task 4 each have a TODO that says "match the existing periodic-tick pattern in this codebase." That's a deliberate "find the real API yourself" — not a placeholder for the human.

**Type consistency:** `ServerEvent.OnStart/OnTick/OnEnd(ServerEventSystem system)` matches between the base class (Task 3) and the override in DoublePaycheckEvent (Task 5). `IsDoublePaycheck` consistent between ServerEventSystem (Task 4) and PaydaySystem (Task 1).

No issues found.

---

## Execution Handoff

Plan saved at `docs/superpowers/plans/2026-05-13-phase-c-economy-chaos.md`. Same execution choices as Phase B:

1. **Subagent-Driven (recommended)** — same flow as Phase B, fresh subagent per task.
2. **Inline Execution** — execute in this session.

Recommend subagent-driven again given the Phase B run went smoothly.
