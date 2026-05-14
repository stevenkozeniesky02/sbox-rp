namespace SboxMcp;

/// <summary>
/// Captures recent Log output so editor.console_output can return it.
/// Appends entries via AddEntry() — call that from any log hook or manually.
/// </summary>
public static class ConsoleCapture
{
	private static readonly List<string> _entries = new();
	private static readonly object _lock = new();
	private const int MaxEntries = 200;
	private static bool _hooked;

	/// <summary>
	/// Call once during addon initialisation to start capturing log output.
	/// Safe to call multiple times — only hooks once.
	/// </summary>
	public static void EnsureHooked()
	{
		if ( _hooked )
			return;
		_hooked = true;

		// s&box Logger does not expose an OnEntry event.
		// Entries are added manually via AddEntry() from handlers.
		Log.Info( "[MCP] ConsoleCapture initialised (manual capture mode)" );
	}

	/// <summary>
	/// Manually append a line (e.g. from ExecutionHandler output).
	/// </summary>
	public static void AddEntry( string line )
	{
		lock ( _lock )
		{
			_entries.Insert( 0, line );
			if ( _entries.Count > MaxEntries )
				_entries.RemoveAt( _entries.Count - 1 );
		}
	}

	/// <summary>
	/// Returns a snapshot of recent log lines (newest-first).
	/// </summary>
	public static List<string> GetRecent()
	{
		lock ( _lock )
		{
			return new List<string>( _entries );
		}
	}

}
