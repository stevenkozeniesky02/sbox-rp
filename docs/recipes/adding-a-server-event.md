# Recipe: Adding a New Server Event

The Phase C framework makes a new server-wide event ≈3 minute job. Use this for things like:

- "DoublePaycheck" — multiplies salary payouts
- "Crackdown" — police income boost / criminal heat
- "MarketBoom" — drug prices spike
- "TaxDay" — one-off mayor revenue tick
- "BountyBoard" — random player marked, killer paid

## Anatomy of a server event

Three pieces:
1. **A `ServerEvent` subclass** (mandatory) — declares duration, weight, lifecycle hooks
2. **A bootstrap registration line** (mandatory) — added to `ServerEventBootstrap`
3. **(Optional) A state flag on `ServerEventSystem`** — public read-only-ish field other systems read

## Step-by-step

### 1. Create the event class

New file: `Code/Game/Events/<EventName>Event.cs`

```csharp
public sealed class <EventName>Event : ServerEvent
{
    public override string DisplayName => "<Pretty Name>";
    public override float DurationSeconds => 300f; // 5 minutes — tune
    public override float Weight => 1f;            // higher = more likely

    public override void OnStart( ServerEventSystem system )
    {
        // Set state flags on `system`, or trigger one-shot effects.
    }

    public override void OnTick( ServerEventSystem system )
    {
        // (Optional) per-fixed-update logic. Don't allocate every tick.
    }

    public override void OnEnd( ServerEventSystem system )
    {
        // Clean up. Clear flags you set in OnStart.
    }
}
```

### 2. (Optional) Add a state flag on `ServerEventSystem`

If your event affects other systems' behavior (like DoublePaycheck affects PaydaySystem), add a public flag to `ServerEventSystem`:

```csharp
// In ServerEventSystem.cs
public bool IsCrackdown { get; internal set; }
```

`internal set` means only event classes (in the same assembly) can mutate it.

Then read it from the affected system:
```csharp
var isCrackdown = ServerEventSystem.Current?.IsCrackdown ?? false;
```

### 3. Register it in `ServerEventBootstrap`

Open `Code/Game/Events/ServerEventBootstrap.cs`. Add one line in the constructor:

```csharp
public ServerEventBootstrap( Scene scene ) : base( scene )
{
    ServerEventRegistry.Clear();
    ServerEventRegistry.Register( new DoublePaycheckEvent() );
    ServerEventRegistry.Register( new <EventName>Event() );  // ← here
}
```

### 4. Test in editor

▶ Play. Watch Console for `[ServerEventSystem] EVENT STARTED: <Pretty Name>`. To trigger faster during dev, temporarily lower `MinEventIntervalSeconds` / `MaxEventIntervalSeconds` in `ServerEventSystem` to like 30/60 seconds. Revert before shipping.

## Design notes

- **Pure events** (no state flag) are useful for one-shot effects: "ExecuteTaxDay" just transfers money once on OnStart.
- **Stateful events** (with a flag) are useful for windows during which other systems behave differently.
- **Don't let events read or write each other's state directly** — funnel everything through `ServerEventSystem` fields. Keeps coupling unidirectional.
- **Host-only.** All event logic runs on the host; clients get state via the `BroadcastEvent*` notices in `ServerEventSystem` plus the synced flags (when the flag is `[Sync]` — currently it isn't, add `[Sync]` if clients need to see).

## Reference implementations

- `Code/Game/Events/DoublePaycheckEvent.cs` — stateful, 5-min window, sets a flag PaydaySystem reads.

## Where the framework came from

Pattern from MauveRP STUDIED #5 (`ServerEventSystem` — 7 events in MauveRP). Phase C ships 1 event; more can be added with this recipe.
