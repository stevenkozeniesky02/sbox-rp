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

	public void Save( IReadOnlyList<TData> entries )
	{
		var dir = Path.GetDirectoryName( FilePath );
		if ( !string.IsNullOrEmpty( dir ) )
			FileSystem.Data.CreateDirectory( dir );

		var json = JsonSerializer.Serialize( entries, new JsonSerializerOptions { WriteIndented = true } );
		FileSystem.Data.WriteAllText( FilePath, json );
	}

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

	public void LoadAndSpawn()
	{
		foreach ( var entry in Load() )
			SpawnFromSave( entry );
	}

	protected abstract void SpawnFromSave( TData data );
}
