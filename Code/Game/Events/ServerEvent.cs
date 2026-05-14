/// <summary>
/// One server-wide event (DoublePaycheck, BountyBoard, etc).
/// Subclasses override the lifecycle hooks; <see cref="ServerEventSystem"/> orchestrates them.
/// Pattern from MauveRP STUDIED #5.
/// </summary>
public abstract class ServerEvent
{
	/// <summary>Display name used in chat / HUD broadcasts.</summary>
	public abstract string DisplayName { get; }

	/// <summary>How long the event runs once started, in seconds.</summary>
	public abstract float DurationSeconds { get; }

	/// <summary>Relative weight for random selection. Higher = more likely. Default 1.</summary>
	public virtual float Weight => 1f;

	/// <summary>Called once when the event starts. Set state flags on <paramref name="system"/> here.</summary>
	public virtual void OnStart( ServerEventSystem system ) { }

	/// <summary>Called every fixed-update tick while the event is active.</summary>
	public virtual void OnTick( ServerEventSystem system ) { }

	/// <summary>Called once when the event ends. Clear state flags on <paramref name="system"/> here.</summary>
	public virtual void OnEnd( ServerEventSystem system ) { }
}

/// <summary>
/// Global registry of available events. <see cref="ServerEventBootstrap"/> populates this at scene start.
/// </summary>
public static class ServerEventRegistry
{
	private static readonly List<ServerEvent> _events = new();

	/// <summary>Adds <paramref name="ev"/> to the pool of selectable events. Nulls are ignored.</summary>
	public static void Register( ServerEvent ev )
	{
		if ( ev is null ) return;
		_events.Add( ev );
	}

	/// <summary>All registered events, newest-first.</summary>
	public static IReadOnlyList<ServerEvent> All => _events;

	/// <summary>Flushes all registrations. Call on scene unload or hot-reload.</summary>
	public static void Clear() => _events.Clear();
}
