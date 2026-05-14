using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SboxMcp.Mcp.Docs;

internal sealed class DocCacheManifest
{
	public int Version { get; set; } = 1;
	public Dictionary<string, CachedPage> Pages { get; set; } = new();
	public long LastFullCrawl { get; set; }
}

public sealed class DocCache
{
	private const int CacheVersion = 1;

	private readonly string _cacheDir;
	private readonly string _manifestPath;
	private readonly long _ttlMs;
	private DocCacheManifest _manifest = new();

	public DocCache()
	{
		_cacheDir = Environment.GetEnvironmentVariable( "SBOX_DOCS_CACHE_DIR" )
			?? Path.Combine(
				Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ),
				".sbox-mcp", "cache" );
		_manifestPath = Path.Combine( _cacheDir, "docs-manifest.json" );

		var ttlSeconds = int.TryParse(
			Environment.GetEnvironmentVariable( "SBOX_DOCS_CACHE_TTL" ),
			out var t ) ? t : 14400;
		_ttlMs = ttlSeconds * 1000L;
	}

	public string CacheDir => _cacheDir;

	public void Init()
	{
		Directory.CreateDirectory( _cacheDir );
		if ( File.Exists( _manifestPath ) )
		{
			try
			{
				var raw = File.ReadAllText( _manifestPath );
				var parsed = JsonSerializer.Deserialize<DocCacheManifest>( raw, JsonOpts.Default );
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
		if ( _manifest.LastFullCrawl == 0 ) return false;
		return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _manifest.LastFullCrawl < _ttlMs;
	}

	public bool IsPageFresh( string url )
	{
		if ( !_manifest.Pages.TryGetValue( url, out var page ) ) return false;
		return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - page.FetchedAt < _ttlMs;
	}

	public CachedPage GetPage( string url ) =>
		_manifest.Pages.TryGetValue( url, out var p ) ? p : null;

	public IReadOnlyList<CachedPage> GetAllPages() => _manifest.Pages.Values.ToList();

	public int GetPageCount() => _manifest.Pages.Count;

	public void SetPage( CachedPage page ) => _manifest.Pages[page.Url] = page;

	public void MarkFullCrawl() =>
		_manifest.LastFullCrawl = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	public void Save()
	{
		Directory.CreateDirectory( _cacheDir );
		File.WriteAllText( _manifestPath, JsonSerializer.Serialize( _manifest, JsonOpts.Default ) );
	}

	public int RemovePagesNotIn( HashSet<string> validUrls )
	{
		var toRemove = _manifest.Pages.Keys.Where( k => !validUrls.Contains( k ) ).ToList();
		foreach ( var k in toRemove ) _manifest.Pages.Remove( k );
		return toRemove.Count;
	}

	public void Clear()
	{
		_manifest = new DocCacheManifest();
		Save();
	}
}
