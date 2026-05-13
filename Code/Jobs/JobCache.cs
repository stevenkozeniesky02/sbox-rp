namespace Sandbox;

/// <summary>
/// TTL-cached per-key store for many-reader scoped queries
/// (e.g. "who's currently mayor?") where every component asking
/// would otherwise walk the scene every frame.
/// Pattern from MauveRP study notes (mauverp-reference/STUDIED.md #4).
/// </summary>
public static class JobCache
{
	private sealed class Entry
	{
		public object Value;
		public RealTimeSince CachedAt;
	}

	private static readonly Dictionary<string, Entry> _entries = new();

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

	public static void Invalidate( string key )
	{
		_entries.Remove( key );
	}

	public static void Clear()
	{
		_entries.Clear();
	}
}
