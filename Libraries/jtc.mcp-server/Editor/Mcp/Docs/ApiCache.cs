using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SboxMcp.Mcp.Docs;

internal sealed class ApiCacheManifest
{
	public int Version { get; set; } = 1;
	public string SchemaUrl { get; set; } = "";
	public long FetchedAt { get; set; }
	public int TypeCount { get; set; }
}

public sealed class ApiCache
{
	private const int CacheVersion = 1;
	private const int DefaultTtlSeconds = 86400;

	private readonly string _cacheDir;
	private readonly string _manifestPath;
	private readonly string _typesPath;
	private readonly long _ttlMs;
	private ApiCacheManifest _manifest = new();

	public ApiCache()
	{
		_cacheDir = Environment.GetEnvironmentVariable( "SBOX_DOCS_CACHE_DIR" )
			?? Path.Combine(
				Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ),
				".sbox-mcp", "cache" );
		_manifestPath = Path.Combine( _cacheDir, "api-manifest.json" );
		_typesPath = Path.Combine( _cacheDir, "api-types.json" );

		var ttlSeconds = int.TryParse(
			Environment.GetEnvironmentVariable( "SBOX_API_CACHE_TTL" ),
			out var t ) ? t : DefaultTtlSeconds;
		_ttlMs = ttlSeconds * 1000L;
	}

	public void Init()
	{
		Directory.CreateDirectory( _cacheDir );
		if ( File.Exists( _manifestPath ) )
		{
			try
			{
				var raw = File.ReadAllText( _manifestPath );
				var parsed = JsonSerializer.Deserialize<ApiCacheManifest>( raw, JsonOpts.Default );
				if ( parsed is { Version: CacheVersion } )
					_manifest = parsed;
			}
			catch
			{
				// Corrupt cache — start fresh
			}
		}
	}

	public bool IsFresh()
	{
		if ( _manifest.FetchedAt == 0 ) return false;
		return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _manifest.FetchedAt < _ttlMs;
	}

	public int GetTypeCount() => _manifest.TypeCount;
	public string GetSchemaUrl() => _manifest.SchemaUrl;

	public List<ApiType> LoadTypes()
	{
		if ( !File.Exists( _typesPath ) ) return null;
		try
		{
			var raw = File.ReadAllText( _typesPath );
			return JsonSerializer.Deserialize<List<ApiType>>( raw, JsonOpts.Default );
		}
		catch
		{
			return null;
		}
	}

	public void Save( string schemaUrl, List<ApiType> types )
	{
		Directory.CreateDirectory( _cacheDir );
		File.WriteAllText( _typesPath, JsonSerializer.Serialize( types, JsonOpts.Default ) );
		_manifest = new ApiCacheManifest
		{
			Version = CacheVersion,
			SchemaUrl = schemaUrl,
			FetchedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			TypeCount = types.Count,
		};
		File.WriteAllText( _manifestPath, JsonSerializer.Serialize( _manifest, JsonOpts.Default ) );
	}

	public void Clear()
	{
		_manifest = new ApiCacheManifest();
		if ( File.Exists( _typesPath ) ) File.Delete( _typesPath );
		if ( File.Exists( _manifestPath ) )
			File.WriteAllText( _manifestPath, JsonSerializer.Serialize( _manifest, JsonOpts.Default ) );
	}
}
