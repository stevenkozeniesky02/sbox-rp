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
	where TSelf : JobSystem<TSelf>
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

	/// <summary>Registers <paramref name="system"/> by its <see cref="IJobSystem.JobIdent"/>. Logs a warning if the ident is already taken.</summary>
	public static void Register( IJobSystem system )
	{
		if ( system is null || string.IsNullOrWhiteSpace( system.JobIdent ) )
			return;

		if ( _byIdent.ContainsKey( system.JobIdent ) )
			Log.Warning( $"[JobSystemRegistry] Duplicate JobIdent '{system.JobIdent}' — overwriting previous registration." );

		_byIdent[system.JobIdent] = system;
	}

	/// <summary>Returns the registered system for <paramref name="jobIdent"/>, or <c>null</c> if none is found.</summary>
	public static IJobSystem Find( string jobIdent )
	{
		if ( string.IsNullOrWhiteSpace( jobIdent ) )
			return null;
		return _byIdent.TryGetValue( jobIdent, out var sys ) ? sys : null;
	}

	/// <summary>Clears all registered systems. Call on scene unload or hot-reload to prevent stale references.</summary>
	public static void Clear() => _byIdent.Clear();
}
