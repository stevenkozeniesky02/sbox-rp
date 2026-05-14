# Recipe: Adding a New Custom Job

Phase B framework makes a new job ~3 files + a `.jobdef` edit. Use this recipe when adding any new RP job.

## When to use what

| What the job needs | What to add |
|---|---|
| Just a label + salary + default weapons | Just a `.jobdef`. No code. |
| Periodic behavior (income tick, mark of death, etc) OR lifecycle hooks | A `Code/Jobs/<JobName>System.cs` subclassing `JobSystem<>`. |
| A target-interaction tool (mug, beg, pickpocket-style) | A `Code/Weapons/<Tool>/<Tool>.cs` subclassing `BaseInteractionWeapon`. |
| A `/command` chat trigger | An entry in `Code/Game/ChatCommands/ChatCommandSystem.cs` (`StaticCommands` array). |

## Step-by-step

### 1. Create the `.jobdef`

In the editor: right-click `Assets/jobs/` → Create → JobDefinition. Set:
- **Identifier** (snake_case, unique).
- **DisplayName**, **Description**, **Color** (UI metadata).
- **Salary** (int, payday amount), **MaxPlayers** (0 = unlimited).
- **Selectable** (can players pick it), **IsDefault** (one job has this).
- **PlayerModel** (`.vmdl` ref), **Clothing** (list of `.clothing` refs).
- **StartingItems** (list of weapon prefab refs — see step 3).
- **Team** (Citizen / Government / Services / Commerce / Criminal).

Save as `Assets/jobs/<job-id>.jobdef`.

### 2. (Optional) Per-job system class

If your job needs hooks beyond `.jobdef` data — periodic income, mark-of-death cycles, kidnap timers, etc — create `Code/Jobs/<JobName>System.cs`:

```csharp
public sealed class <JobName>System : JobSystem<<JobName>System>
{
    public <JobName>System( Scene scene ) : base( scene ) { }

    public override string JobIdent => "jobs/<job-id>.jobdef";

    public override void OnBecameJob( PlayerData playerData )
    {
        // runs once per player joining the job
    }

    public override void OnLeftJob( PlayerData playerData )
    {
        // runs once per player leaving the job
    }
}
```

The system auto-registers with `JobSystemRegistry` on construction. `Player.SetJobDefinition` fires the hooks automatically.

For periodic logic, drive it from `OnFixedUpdate()` with a `TimeSince` counter (see MauveRP STUDIED #2 `JobManager`-style pattern).

### 3. (Optional) Interaction tool

If the job has a "press attack1 on another player to do X" tool — pickpocket, mug, beg, defibrillator, etc — create `Code/Weapons/<Tool>/<Tool>.cs`:

```csharp
public sealed partial class <Tool> : BaseInteractionWeapon
{
    public override float CooldownSeconds => 30f;
    // public override float InteractionRange => 120f;  // override if needed
    // public override float InteractionTraceRadius => 8f;  // override if needed

    public override bool IsTargetEligible( Player attacker, Player target )
    {
        if ( !base.IsTargetEligible( attacker, target ) ) return false;
        // add your own validation (target's job, distance, weapon ownership, etc)
        return true;
    }

    protected override void OnInteract( Player attacker, Player target )
    {
        // Runs on the owning client. For authoritative effects (money change,
        // damage, state mutation), dispatch a [Rpc.Host] static method.
        RpcServerDoTheThing( attacker.GameObject, target.GameObject );
    }

    [Rpc.Host]
    private static void RpcServerDoTheThing( GameObject attackerGo, GameObject targetGo )
    {
        var attacker = attackerGo?.GetComponent<Player>();
        var target = targetGo?.GetComponent<Player>();
        if ( !attacker.IsValid() || !target.IsValid() ) return;

        // do the thing
    }
}
```

The cooldown HUD overlay is provided by `BaseInteractionWeapon.DrawHud`. Per-Steam-ID cooldown dict is shared across all subclasses — a player can't switch tools to bypass.

Then build a prefab in the editor (right-click `Assets/weapons/<tool>/` → Create → Prefab → add the `<Tool>` component) and reference its path in your jobdef's `StartingItems`.

### 4. (Optional) Chat command

If the job needs `/command`, add to `Code/Game/ChatCommands/ChatCommandSystem.cs` `StaticCommands` array. See existing `/me`, `/advert`, `/ticket` for patterns.

### 5. Test in editor

▶ Play → switch to the new job via F4 → verify the loadout + that any `JobSystem<>` hooks log in console.

## Reference implementations

- `Code/Jobs/HoboSystem.cs` — minimal lifecycle-logging job system (pilot).
- `Code/Weapons/Begging/BeggingTool.cs` — minimal $10-take interaction weapon (pilot).
- `mauverp-reference/STUDIED.md` patterns: #2 JobManager hub, #3 JobDefinition declarative, #8 Murderer-style vertical, #34 Cooldown-interaction weapon family.

## Things that don't work the way you'd expect

- **`SteamId` is `long`** in this fork, not `ulong`. Use `long` when keying cooldown dicts.
- **`AimRay` lives on `BaseCarryable`**, not on `Player`. Use `owner.EyeTransform.ForwardRay` in interaction-target traces (matches existing weapons).
- **Money methods live on `Player`** (`TryTakeMoney`, `GiveMoney`, `Money`), not `PlayerData`.
- **Addons can't be code-baked via `Cloud.Model("ident")`** for asset-bundle packages — only for `type=Model` packages with a single primary asset. See `docs/upstream-package-audit.md`.
- **`OnInteract` runs on the owning client**, not the host. Always use `[Rpc.Host]` for authoritative effects.

## Where the framework came from

- `JobSystem<TSelf>` + `JobSystemRegistry` (`Code/Jobs/JobSystem.cs`) — Phase B.
- `BaseInteractionWeapon` (`Code/Game/Weapon/BaseInteractionWeapon.cs`) — Phase B.
- `Player.SetJobDefinition` dispatch — Phase B.
- Patterns informed by `mauverp-reference/STUDIED.md` #8 + #34.
