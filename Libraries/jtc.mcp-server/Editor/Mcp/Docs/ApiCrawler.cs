using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SboxMcp.Mcp.Docs;

public sealed class ApiCrawlStats
{
	public int TypeCount { get; set; }
	public bool FromCache { get; set; }
	public string SchemaUrl { get; set; } = "";
}

public sealed class ApiCrawler
{
	private const string SchemaPageUrl = "https://sbox.game/api/schema";
	private static readonly TimeSpan SchemaTimeout = TimeSpan.FromSeconds( 30 );
	private static readonly TimeSpan ScrapeTimeout = TimeSpan.FromSeconds( 15 );
	private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds( 10 );

	private const string KnownSchemaUrl =
		"https://cdn.sbox.game/releases/2026-04-09-18-37-34.zip.json";

	private static readonly Regex SchemaUrlRegex = new(
		@"https://cdn\.sbox\.game/releases/[^""'\s<>]+\.json",
		RegexOptions.Compiled );

	private readonly ApiCache _cache;
	private readonly HttpClient _http;

	public ApiCrawler( ApiCache cache, HttpClient http )
	{
		_cache = cache;
		_http = http;
	}

	private async Task<string> DiscoverSchemaUrlAsync( CancellationToken ct )
	{
		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
			cts.CancelAfter( ScrapeTimeout );
			using var resp = await _http.GetAsync( SchemaPageUrl, cts.Token );
			if ( !resp.IsSuccessStatusCode ) return null;
			var html = await resp.Content.ReadAsStringAsync( cts.Token );
			var m = SchemaUrlRegex.Match( html );
			return m.Success ? m.Value : null;
		}
		catch
		{
			return null;
		}
	}

	private async Task<bool> VerifyUrlAsync( string url, CancellationToken ct )
	{
		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
			cts.CancelAfter( VerifyTimeout );
			using var req = new HttpRequestMessage( HttpMethod.Head, url );
			using var resp = await _http.SendAsync( req, cts.Token );
			return resp.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	private async Task<string> ResolveSchemaUrlAsync( string cachedUrl, CancellationToken ct )
	{
		var envUrl = Environment.GetEnvironmentVariable( "SBOX_API_SCHEMA_URL" );
		if ( !string.IsNullOrWhiteSpace( envUrl ) ) return envUrl;

		var scraped = await DiscoverSchemaUrlAsync( ct );
		if ( !string.IsNullOrEmpty( scraped ) ) return scraped;

		if ( !string.IsNullOrEmpty( cachedUrl ) && await VerifyUrlAsync( cachedUrl, ct ) )
			return cachedUrl;

		if ( await VerifyUrlAsync( KnownSchemaUrl, ct ) )
			return KnownSchemaUrl;

		return null;
	}

	private async Task<List<ApiType>> DownloadSchemaAsync( string url, CancellationToken ct )
	{
		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
			cts.CancelAfter( SchemaTimeout );
			using var resp = await _http.GetAsync( url, HttpCompletionOption.ResponseHeadersRead, cts.Token );
			if ( !resp.IsSuccessStatusCode ) return null;

			var raw = await resp.Content.ReadAsStringAsync( cts.Token );
			var wrapper = JsonSerializer.Deserialize<ApiSchemaWrapper>( raw, JsonOpts.Default );
			return wrapper?.Types;
		}
		catch
		{
			return null;
		}
	}

	private static List<ApiType> FilterTypes( List<ApiType> types ) =>
		types.Where( t =>
			t.IsPublic
			&& !string.IsNullOrEmpty( t.Name )
			&& !t.Name.StartsWith( '<' )
			&& !t.Name.StartsWith( "__" )
			&& !string.IsNullOrEmpty( t.FullName ) )
		.ToList();

	public async Task<ApiCrawlStats> CrawlAllAsync( Action<string> onProgress, CancellationToken ct )
	{
		if ( _cache.IsFresh() )
		{
			var cached = _cache.LoadTypes() ?? new List<ApiType>();
			return new ApiCrawlStats
			{
				TypeCount = cached.Count,
				FromCache = true,
				SchemaUrl = _cache.GetSchemaUrl(),
			};
		}

		onProgress?.Invoke( "Resolving schema URL..." );
		var schemaUrl = await ResolveSchemaUrlAsync( _cache.GetSchemaUrl(), ct );
		if ( string.IsNullOrEmpty( schemaUrl ) )
		{
			Log.Warning( "[MCP Docs] Could not find a valid API schema URL. Set SBOX_API_SCHEMA_URL." );
			var stale = _cache.LoadTypes();
			return new ApiCrawlStats
			{
				TypeCount = stale?.Count ?? 0,
				FromCache = true,
				SchemaUrl = "",
			};
		}

		onProgress?.Invoke( $"Downloading schema from {schemaUrl}..." );
		var types = await DownloadSchemaAsync( schemaUrl, ct );
		if ( types is null )
		{
			Log.Warning( $"[MCP Docs] Could not download or parse API schema from {schemaUrl}" );
			var stale = _cache.LoadTypes();
			return new ApiCrawlStats
			{
				TypeCount = stale?.Count ?? 0,
				FromCache = true,
				SchemaUrl = schemaUrl,
			};
		}

		var filtered = FilterTypes( types );
		Log.Info( $"[MCP Docs] API schema: {types.Count} total, {filtered.Count} public" );

		_cache.Save( schemaUrl, filtered );
		return new ApiCrawlStats
		{
			TypeCount = filtered.Count,
			FromCache = false,
			SchemaUrl = schemaUrl,
		};
	}
}
