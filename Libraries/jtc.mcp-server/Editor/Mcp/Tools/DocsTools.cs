using Sandbox;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SboxMcp.Mcp.Docs;

namespace SboxMcp.Mcp.Tools;

/// <summary>
/// s&amp;box documentation + API reference search. Runs entirely in-process, no
/// editor APIs needed — these tools work even with no scene loaded.
/// </summary>
[McpToolGroup]
public static class DocsTools
{
	[McpTool( "sbox_search_docs",
		Description = "Search s&box documentation for guides, tutorials, and concepts. Returns matching pages with titles, URLs, and relevant snippets." )]
	public static async Task<object> SearchDocs(
		[Description( "Search terms" )] string query,
		[Description( "Max number of results (1-25, default 10)" )] int? limit = null,
		[Description( "Optional category filter (e.g. 'Systems', 'About', 'Scenes')" )] string category = null )
	{
		var docs = DocsService.GetOrCreate();
		await docs.EnsureDocsIndexedAsync( CancellationToken.None );

		var actualLimit = Math.Clamp( limit ?? 10, 1, 25 );
		var results = docs.DocSearch.Search( query, actualLimit, category );

		if ( results.Count == 0 )
		{
			var hint = !string.IsNullOrEmpty( category )
				? $" Try without the category filter \"{category}\"."
				: "";
			return $"No documentation found for \"{query}\".{hint}\n\nUse sbox_list_doc_categories to see what's available.";
		}

		var sb = new StringBuilder();
		sb.AppendLine( $"## Search results for \"{query}\"" );
		sb.AppendLine();
		for ( var i = 0; i < results.Count; i++ )
		{
			var r = results[i];
			sb.AppendLine( $"{i + 1}. **[{r.Title}]({r.Url})** — _{r.Category}_" );
			if ( !string.IsNullOrEmpty( r.Snippet ) )
				sb.AppendLine( $"   > {r.Snippet}" );
			sb.AppendLine();
		}
		sb.Append( $"_{results.Count} result(s). Use sbox_get_doc_page to read full content._" );
		return sb.ToString();
	}

	[McpTool( "sbox_get_doc_page",
		Description = "Fetch a specific s&box documentation page and return its content as Markdown. Supports chunked reading via start_index and max_length." )]
	public static async Task<object> GetDocPage(
		[Description( "Full URL of the documentation page (from docs.facepunch.com/s/sbox-dev/doc/...)" )] string url,
		[Description( "Character offset to start reading from (default 0)" )] int? startIndex = null,
		[Description( "Maximum content length in characters (100-20000, default 5000)" )] int? maxLength = null )
	{
		var docs = DocsService.GetOrCreate();
		await docs.EnsureDocsIndexedAsync( CancellationToken.None );

		var normalised = url.EndsWith( '/' ) ? url.Substring( 0, url.Length - 1 ) : url;
		var page = docs.DocSearch.GetPage( normalised )
			?? await docs.DocCrawler.CrawlSinglePageAsync( normalised, CancellationToken.None );

		if ( page is null )
			return $"Could not fetch the page at {url}. The page may not exist, require authentication, or be temporarily unavailable.";

		var actualStart = Math.Max( 0, startIndex ?? 0 );
		var actualMax = Math.Clamp( maxLength ?? 5000, 100, 20000 );

		var totalLength = page.Markdown.Length;
		var start = Math.Min( actualStart, totalLength );
		var clampedLen = Math.Min( actualMax, totalLength - start );
		var chunk = page.Markdown.Substring( start, clampedLen );
		var endIndex = start + chunk.Length;
		var hasMore = endIndex < totalLength;

		var header = $"# {page.Title}\n\n**Section:** {page.Category} | **Source:** [{page.Url}]({page.Url})";
		var lastUpdated = string.IsNullOrEmpty( page.LastUpdated ) ? "" : $"\n**Last updated:** {page.LastUpdated}";
		var footer = hasMore
			? $"\n\n---\n_Showing characters {start}–{endIndex} of {totalLength}. Use start_index={endIndex} to read the next chunk._"
			: $"\n\n---\n_End of page ({totalLength} characters total)._";

		return $"{header}{lastUpdated}\n\n---\n\n{chunk}{footer}";
	}

	[McpTool( "sbox_list_doc_categories",
		Description = "List all available s&box documentation categories with page counts. Use this to discover what documentation is available before searching." )]
	public static async Task<object> ListCategories()
	{
		var docs = DocsService.GetOrCreate();
		await docs.EnsureDocsIndexedAsync( CancellationToken.None );

		var categories = docs.DocSearch.GetCategories();
		if ( categories.Count == 0 )
			return "No documentation has been indexed yet. The server may still be crawling. Try again shortly.";

		var sb = new StringBuilder();
		sb.AppendLine( "## S&box Documentation Categories" );
		sb.AppendLine();
		sb.AppendLine( $"Total: {docs.DocSearch.PageCount} pages across {categories.Count} categories" );
		sb.AppendLine();
		foreach ( var cat in categories )
		{
			sb.AppendLine( $"### {cat.Name} ({cat.PageCount} pages)" );
			sb.AppendLine();
			foreach ( var page in cat.Pages )
				sb.AppendLine( $"- [{page.Title}]({page.Url})" );
			sb.AppendLine();
		}
		return sb.ToString();
	}

	[McpTool( "sbox_search_api",
		Description = "Search the s&box API reference for classes, structs, interfaces, and their members. Use sbox_get_api_type for full details." )]
	public static async Task<object> SearchApi(
		[Description( "Type name, namespace, method name, or keyword" )] string query,
		[Description( "Max number of results (1-20, default 8)" )] int? limit = null )
	{
		var docs = DocsService.GetOrCreate();
		await docs.EnsureApiIndexedAsync( CancellationToken.None );

		var actualLimit = Math.Clamp( limit ?? 8, 1, 20 );
		var results = docs.ApiSearch.Search( query, actualLimit );

		if ( results.Count == 0 )
			return $"No API types found for \"{query}\".\n\nThe API reference covers all public types in the s&box engine.";

		var sb = new StringBuilder();
		sb.AppendLine( $"## API search results for \"{query}\"" );
		sb.AppendLine();
		for ( var i = 0; i < results.Count; i++ )
		{
			var r = results[i];
			var ns = string.IsNullOrEmpty( r.Namespace ) ? "" : $" _({r.Namespace})_";
			sb.AppendLine( $"{i + 1}. **[{r.Name}]({r.Url})**{ns}" );
			if ( !string.IsNullOrEmpty( r.Description ) )
				sb.AppendLine( $"   > {r.Description}" );
			if ( r.TopMembers.Count > 0 )
				sb.AppendLine( $"   Members: `{string.Join( "`, `", r.TopMembers )}`" );
			sb.AppendLine();
		}
		sb.Append( $"_{results.Count} result(s). Use sbox_get_api_type for full details._" );
		return sb.ToString();
	}

	[McpTool( "sbox_get_api_type",
		Description = "Get full API reference for a specific s&box type: methods, properties, fields, signatures." )]
	public static async Task<object> GetApiType(
		[Description( "Short type name (e.g. 'Component') or fully-qualified name (e.g. 'Sandbox.Component')" )] string name,
		[Description( "Character offset to start reading from (default 0)" )] int? startIndex = null,
		[Description( "Maximum content length in characters (100-20000, default 5000)" )] int? maxLength = null )
	{
		var docs = DocsService.GetOrCreate();
		await docs.EnsureApiIndexedAsync( CancellationToken.None );

		var type = docs.ApiSearch.LookupType( name );
		if ( type is null )
		{
			var hits = docs.ApiSearch.Search( name, 1 );
			if ( hits.Count > 0 )
				type = docs.ApiSearch.LookupType( hits[0].FullName );
		}

		if ( type is null )
			return $"No API type found for \"{name}\".\n\nUse sbox_search_api to find the correct name.";

		var actualStart = Math.Max( 0, startIndex ?? 0 );
		var actualMax = Math.Clamp( maxLength ?? 5000, 100, 20000 );

		return ApiSearch.FormatTypeDetail( type, actualStart, actualMax );
	}

	[McpTool( "sbox_cache_status",
		Description = "Show the current status of the documentation cache and search index." )]
	public static Task<object> CacheStatus()
	{
		var docs = DocsService.GetOrCreate();
		var sb = new StringBuilder();
		sb.AppendLine( "## S&box Docs MCP — Cache Status" );
		sb.AppendLine();
		sb.AppendLine( "### Documentation" );
		sb.AppendLine( $"- **Index ready:** {(docs.DocIndexReady ? "Yes" : "No (still crawling…)")}" );
		sb.AppendLine( $"- **Pages in cache:** {docs.DocCache.GetPageCount()}" );
		sb.AppendLine( $"- **Pages in search index:** {docs.DocSearch.PageCount}" );
		sb.AppendLine( $"- **Cache fresh:** {(docs.DocCache.IsFresh() ? "Yes" : "No (will re-crawl on next use)")}" );
		sb.AppendLine();
		sb.AppendLine( "### API Reference" );
		sb.AppendLine( $"- **Index ready:** {(docs.ApiIndexReady ? "Yes" : "No (still loading…)")}" );
		sb.AppendLine( $"- **Types in cache:** {docs.ApiCache.GetTypeCount()}" );
		sb.AppendLine( $"- **Types in search index:** {docs.ApiSearch.TypeCount}" );
		sb.AppendLine( $"- **Cache fresh:** {(docs.ApiCache.IsFresh() ? "Yes" : "No (will re-fetch on next use)")}" );
		sb.AppendLine( $"- **Schema URL:** {(string.IsNullOrEmpty( docs.ApiCache.GetSchemaUrl() ) ? "(not yet fetched)" : docs.ApiCache.GetSchemaUrl())}" );
		sb.AppendLine();
		sb.AppendLine( "### Cache directory" );
		sb.AppendLine( $"- {docs.DocCache.CacheDir}" );
		return Task.FromResult<object>( sb.ToString() );
	}
}
