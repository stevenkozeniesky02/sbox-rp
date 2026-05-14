/// <summary>
/// Host-only singleton. Every <see cref="MinEventIntervalSeconds"/>&#8211;<see cref="MaxEventIntervalSeconds"/>,
/// picks a random registered <see cref="ServerEvent"/> (weighted) and runs its lifecycle.
/// Other systems (e.g. <see cref="PaydaySystem"/>) read public state flags here to react.
/// Pattern from MauveRP STUDIED #5.
/// </summary>
public sealed class ServerEventSystem : GameObjectSystem<ServerEventSystem>
{
	/// <summary>Minimum seconds between events.</summary>
	public const float MinEventIntervalSeconds = 600f;  // 10 min

	/// <summary>Maximum seconds between events.</summary>
	public const float MaxEventIntervalSeconds = 1200f; // 20 min

	/// <summary>True while a DoublePaycheck event is active. <see cref="PaydaySystem"/> reads this.</summary>
	public bool IsDoublePaycheck { get; internal set; }

	private ServerEvent _activeEvent;
	private TimeSince _sinceEventStart;
	private TimeSince _sinceLastEventEnded;
	private float _nextEventIn;

	public ServerEventSystem( Scene scene ) : base( scene )
	{
		ResetInterval();
		Listen( Stage.StartFixedUpdate, 10, OnTick, "ServerEventSystem" );
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

		picked ??= available[0];

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
