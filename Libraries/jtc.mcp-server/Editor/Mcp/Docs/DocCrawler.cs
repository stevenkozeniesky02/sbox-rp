using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SboxMcp.Mcp.Docs;

public sealed class CrawlStats
{
	public int Crawled { get; set; }
	public int Failed { get; set; }
	public int FromCache { get; set; }
	public int Total { get; set; }
}

public sealed class DocCrawler
{
	private const string OutlineApi = "https://docs.facepunch.com/api";
	private const string ShareId = "sbox-dev";
	private const string DocsBase = "https://docs.facepunch.com/s/sbox-dev";
	private const int RequestDelayMs = 100;
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds( 15 );

	private readonly DocCache _cache;
	private readonly HttpClient _http;
	private string _shareUuid;
	private List<TreeEntry> _docTree = new();

	public DocCrawler( DocCache cache, HttpClient http )
	{
		_cache = cache;
		_http = http;
	}

	private sealed class TreeEntry
	{
		public string Id { get; set; }
		public string Title { get; set; }
		public string Path { get; set; }
		public string Url { get; set; }
	}

	private sealed class TreeNode
	{
		[JsonPropertyName( "id" )] public string Id { get; set; } = "";
		[JsonPropertyName( "url" )] public string Url { get; set; } = "";
		[JsonPropertyName( "title" )] public string Title { get; set; } = "";
		[JsonPropertyName( "children" )] public List<TreeNode> Children { get; set; }
	}

	private sealed class ShareInfoData
	{
		[JsonPropertyName( "shares" )] public List<ShareEntry> Shares { get; set; }
		[JsonPropertyName( "sharedTree" )] public TreeNode SharedTree { get; set; }
	}

	private sealed class ShareEntry
	{
		[JsonPropertyName( "id" )] public string Id { get; set; } = "";
	}

	private sealed class DocumentData
	{
		[JsonPropertyName( "title" )] public string Title { get; set; } = "";
		[JsonPropertyName( "text" )] public string Text { get; set; } = "";
		[JsonPropertyName( "updatedAt" )] public string UpdatedAt { get; set; }
	}

	private async Task<JsonElement?> ApiPostAsync( string endpoint, object body, CancellationToken ct )
	{
		try
		{
			using var cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
			cts.CancelAfter( RequestTimeout );

			using var content = new StringContent(
				JsonSerializer.Serialize( body ), Encoding.UTF8, "application/json" );
			using var response = await _http.PostAsync( $"{OutlineApi}/{endpoint}", content, cts.Token );
			if ( !response.IsSuccessStatusCode ) return null;

			var raw = await response.Content.ReadAsStringAsync( cts.Token );
			using var doc = JsonDocument.Parse( raw );
			if ( !doc.RootElement.TryGetProperty( "data", out var data ) ) return null;
			return data.Clone();
		}
		catch
		{
			return null;
		}
	}

	private static List<TreeEntry> FlattenTree( TreeNode node, string parentPath = "" )
	{
		var result = new List<TreeEntry>();
		var currentPath = string.IsNullOrEmpty( parentPath ) ? node.Title : $"{parentPath}/{node.Title}";
		result.Add( new TreeEntry { Id = node.Id, Title = node.Title, Path = currentPath, Url = node.Url } );
		if ( node.Children is { Count: > 0 } )
		{
			foreach ( var c in node.Children )
				result.AddRange( FlattenTree( c, currentPath ) );
		}
		return result;
	}

	internal static string ExtractCategory( string path )
	{
		var parts = path.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		return parts.Length >= 2 ? parts[1] : "root";
	}

	private async Task<bool> LoadTreeAsync( CancellationToken ct )
	{
		var data = await ApiPostAsync( "shares.info", new { id = ShareId }, ct );
		if ( data is null ) return false;

		var typed = JsonSerializer.Deserialize<ShareInfoData>( data.Value.GetRawText(), JsonOpts.Default );
		if ( typed?.SharedTree is null || typed.Shares is null || typed.Shares.Count == 0 )
			return false;

		_shareUuid = typed.Shares[0].Id;
		_docTree = FlattenTree( typed.SharedTree );
		return true;
	}

	private async Task<DocumentData> FetchDocAsync( string docId, CancellationToken ct )
	{
		var data = await ApiPostAsync( "documents.info",
			new { id = docId, shareId = _shareUuid }, ct );
		if ( data is null ) return null;
		return JsonSerializer.Deserialize<DocumentData>( data.Value.GetRawText(), JsonOpts.Default );
	}

	public async Task<CrawlStats> CrawlAllAsync( Action<CrawlStats> onProgress, CancellationToken ct )
	{
		if ( _cache.IsFresh() )
		{
			var count = _cache.GetPageCount();
			return new CrawlStats { FromCache = count, Total = count };
		}

		var stats = new CrawlStats();

		if ( !await LoadTreeAsync( ct ) )
		{
			Log.Warning( "[MCP Docs] Could not load document tree from docs.facepunch.com" );
			stats.FromCache = _cache.GetPageCount();
			return stats;
		}

		stats.Total = _docTree.Count;
		Log.Info( $"[MCP Docs] Found {_docTree.Count} docs in tree" );

		foreach ( var doc in _docTree )
		{
			ct.ThrowIfCancellationRequested();
			var fullUrl = $"{DocsBase}{doc.Url}";

			if ( _cache.IsPageFresh( fullUrl ) )
			{
				stats.FromCache++;
				onProgress?.Invoke( stats );
				continue;
			}

			var fetched = await FetchDocAsync( doc.Id, ct );
			if ( fetched is null || string.IsNullOrEmpty( fetched.Text ) || fetched.Text.Length < 10 )
			{
				stats.Failed++;
				onProgress?.Invoke( stats );
				await Task.Delay( RequestDelayMs, ct );
				continue;
			}

			_cache.SetPage( new CachedPage
			{
				Url = fullUrl,
				Title = string.IsNullOrEmpty( fetched.Title ) ? doc.Title : fetched.Title,
				Category = ExtractCategory( doc.Path ),
				Markdown = fetched.Text,
				FetchedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				LastUpdated = fetched.UpdatedAt,
			} );
			stats.Crawled++;
			onProgress?.Invoke( stats );
			await Task.Delay( RequestDelayMs, ct );
		}

		var validUrls = _docTree.Select( d => $"{DocsBase}{d.Url}" ).ToHashSet();
		var pruned = _cache.RemovePagesNotIn( validUrls );
		if ( pruned > 0 )
			Log.Info( $"[MCP Docs] Pruned {pruned} stale page(s) from cache" );

		_cache.MarkFullCrawl();
		_cache.Save();
		return stats;
	}

	public async Task<CachedPage> CrawlSinglePageAsync( string url, CancellationToken ct )
	{
		if ( _cache.IsPageFresh( url ) )
			return _cache.GetPage( url );

		if ( _docTree.Count == 0 )
			await LoadTreeAsync( ct );

		var urlPath = url.StartsWith( DocsBase ) ? url.Substring( DocsBase.Length ) : url;
		var entry = _docTree.FirstOrDefault( d => d.Url == urlPath );
		if ( entry is null ) return null;

		var fetched = await FetchDocAsync( entry.Id, ct );
		if ( fetched is null || string.IsNullOrEmpty( fetched.Text ) ) return null;

		var page = new CachedPage
		{
			Url = url,
			Title = string.IsNullOrEmpty( fetched.Title ) ? entry.Title : fetched.Title,
			Category = ExtractCategory( entry.Path ),
			Markdown = fetched.Text,
			FetchedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			LastUpdated = fetched.UpdatedAt,
		};

		_cache.SetPage( page );
		_cache.Save();
		return page;
	}
}
