using Sandbox;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SboxMcp.Mcp.Docs;

/// <summary>
/// Singleton that owns the docs/API caches, crawlers, and search indexes.
/// Tools call <see cref="EnsureDocsIndexedAsync"/> / <see cref="EnsureApiIndexedAsync"/>
/// before every search; the first caller triggers the network crawl, subsequent
/// callers reuse the same Task.
///
/// Lifecycle: explicitly started/stopped from the dock widget. All HTTP traffic
/// flows through a single <see cref="HttpClient"/> for connection pooling.
/// </summary>
public sealed class DocsService : IDisposable
{
	public static DocsService Instance { get; private set; }

	private readonly DocCache _docCache = new();
	private readonly ApiCache _apiCache = new();
	private readonly DocSearch _docSearch = new();
	private readonly ApiSearch _apiSearch = new();
	private readonly DocCrawler _docCrawler;
	private readonly ApiCrawler _apiCrawler;
	private readonly HttpClient _http;

	private Task _docIndexTask;
	private Task _apiIndexTask;
	private bool _docIndexReady;
	private bool _apiIndexReady;
	private readonly object _lock = new();
	private readonly CancellationTokenSource _shutdown = new();
	private bool _disposed;

	public DocsService()
	{
		_http = new HttpClient();
		_http.DefaultRequestHeaders.UserAgent.ParseAdd( "sbox-mcp/2.0 (+https://github.com/Facepunch/sbox)" );
		_docCrawler = new DocCrawler( _docCache, _http );
		_apiCrawler = new ApiCrawler( _apiCache, _http );
	}

	public DocCache DocCache => _docCache;
	public ApiCache ApiCache => _apiCache;
	public DocSearch DocSearch => _docSearch;
	public ApiSearch ApiSearch => _apiSearch;
	public DocCrawler DocCrawler => _docCrawler;
	public bool DocIndexReady => _docIndexReady;
	public bool ApiIndexReady => _apiIndexReady;

	public static DocsService GetOrCreate()
	{
		if ( Instance is null )
		{
			Instance = new DocsService();
			Instance.Start();
		}
		return Instance;
	}

	public void Start()
	{
		_ = EnsureDocsIndexedAsync( _shutdown.Token );
		_ = EnsureApiIndexedAsync( _shutdown.Token );
	}

	public Task EnsureDocsIndexedAsync( CancellationToken ct )
	{
		if ( _docIndexReady ) return Task.CompletedTask;
		lock ( _lock )
		{
			// Cold-start race: when the editor first boots, our crawl can run
			// before s&box networking is ready, leaving the index empty. Retry on
			// the next caller by clearing the cached task whenever it ended in
			// an empty index.
			if ( _docIndexTask is { IsCompleted: true } && _docSearch.PageCount == 0 )
				_docIndexTask = null;
			_docIndexTask ??= IndexDocsAsync( ct );
		}
		return _docIndexTask;
	}

	public Task EnsureApiIndexedAsync( CancellationToken ct )
	{
		if ( _apiIndexReady ) return Task.CompletedTask;
		lock ( _lock )
		{
			if ( _apiIndexTask is { IsCompleted: true } && _apiSearch.TypeCount == 0 )
				_apiIndexTask = null;
			_apiIndexTask ??= IndexApiAsync( ct );
		}
		return _apiIndexTask;
	}

	private async Task IndexDocsAsync( CancellationToken ct )
	{
		try
		{
			_docCache.Init();
			var stats = await _docCrawler.CrawlAllAsync( s =>
			{
				var done = s.Crawled + s.Failed + s.FromCache;
				if ( done % 10 == 0 || done == s.Total )
				{
					var pct = s.Total > 0 ? done * 100 / s.Total : 0;
					Log.Info( $"[MCP Docs] Crawling... {done}/{s.Total} ({pct}%)" );
				}
			}, ct );

			Log.Info( $"[MCP Docs] Crawl complete: {stats.Crawled} fetched, {stats.FromCache} cached, {stats.Failed} failed" );
			var pages = _docCache.GetAllPages();
			_docSearch.BuildIndex( pages );
			Log.Info( $"[MCP Docs] Doc search index ready: {pages.Count} pages indexed" );

			// Only flip the ready flag if we actually have content. Empty index
			// means networking wasn't up yet — let the next call retry.
			if ( pages.Count > 0 ) _docIndexReady = true;
			else Log.Info( "[MCP Docs] Empty index — next tool call will retry the crawl" );
		}
		catch ( OperationCanceledException ) { /* shutdown */ }
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP Docs] Doc indexing error: {ex.Message}" );
		}
	}

	private async Task IndexApiAsync( CancellationToken ct )
	{
		try
		{
			_apiCache.Init();
			var stats = await _apiCrawler.CrawlAllAsync(
				msg => Log.Info( $"[MCP Docs] API: {msg}" ), ct );
			Log.Info( $"[MCP Docs] API schema ready: {stats.TypeCount} types ({(stats.FromCache ? "from cache" : "freshly downloaded")})" );

			var types = _apiCache.LoadTypes() ?? new List<ApiType>();
			_apiSearch.BuildIndex( types );
			Log.Info( $"[MCP Docs] API search index ready: {_apiSearch.TypeCount} types indexed" );

			if ( _apiSearch.TypeCount > 0 ) _apiIndexReady = true;
			else Log.Info( "[MCP Docs] Empty API index — next tool call will retry" );
		}
		catch ( OperationCanceledException ) { /* shutdown */ }
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP Docs] API indexing error: {ex.Message}" );
		}
	}

	public void Dispose()
	{
		if ( _disposed ) return;
		_disposed = true;
		_shutdown.Cancel();
		_http.Dispose();
		if ( Instance == this ) Instance = null;
	}
}
