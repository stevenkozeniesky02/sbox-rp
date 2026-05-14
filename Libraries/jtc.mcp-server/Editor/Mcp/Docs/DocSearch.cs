using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SboxMcp.Mcp.Docs;

public sealed class DocSearchResult
{
	public string Title { get; set; }
	public string Url { get; set; }
	public string Category { get; set; }
	public string Snippet { get; set; }
	public double Score { get; set; }
}

public sealed class CategoryInfo
{
	public string Name { get; set; }
	public int PageCount { get; set; }
	public List<(string Title, string Url)> Pages { get; set; } = new();
}

public sealed class DocSearch
{
	private FuzzyIndex<CachedPage> _index = CreateIndex();
	private readonly Dictionary<string, CachedPage> _byUrl = new();

	private static FuzzyIndex<CachedPage> CreateIndex() => new(
		new FuzzyIndexConfig
		{
			Fields = new[]
			{
				new IndexedField( "title", 3.0 ),
				new IndexedField( "category", 2.0 ),
				new IndexedField( "markdown", 1.0 ),
			},
		},
		page => new Dictionary<string, string>
		{
			["title"] = page.Title,
			["category"] = page.Category,
			["markdown"] = page.Markdown,
		} );

	public int PageCount => _byUrl.Count;

	public void BuildIndex( IEnumerable<CachedPage> pages )
	{
		_index = CreateIndex();
		_byUrl.Clear();
		foreach ( var p in pages )
		{
			_index.Add( p );
			_byUrl[p.Url] = p;
		}
	}

	public IReadOnlyList<DocSearchResult> Search( string query, int limit = 10, string category = null )
	{
		var hits = _index.Search( query, limit * 4 );
		return hits
			.Where( h => category is null
				|| string.Equals( h.Item.Category, category, StringComparison.OrdinalIgnoreCase ) )
			.Take( limit )
			.Select( h => new DocSearchResult
			{
				Title = h.Item.Title,
				Url = h.Item.Url,
				Category = h.Item.Category,
				Snippet = ExtractSnippet( h.Item.Markdown, query ),
				Score = h.Score,
			} )
			.ToList();
	}

	public IReadOnlyList<CategoryInfo> GetCategories()
	{
		return _byUrl.Values
			.GroupBy( p => p.Category )
			.Select( g => new CategoryInfo
			{
				Name = g.Key,
				PageCount = g.Count(),
				Pages = g.Select( p => (p.Title, p.Url) ).ToList(),
			} )
			.OrderBy( c => c.Name, StringComparer.OrdinalIgnoreCase )
			.ToList();
	}

	public CachedPage GetPage( string url ) =>
		_byUrl.TryGetValue( url, out var p ) ? p : null;

	internal static string ExtractSnippet( string markdown, string query, int maxLength = 200 )
	{
		if ( string.IsNullOrEmpty( markdown ) ) return "";

		var lower = markdown.ToLowerInvariant();
		var queryLower = query.ToLowerInvariant();
		var words = queryLower.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );

		var bestPos = 0;
		var bestScore = -1;

		for ( var i = 0; i < lower.Length - 50; i += 20 )
		{
			if ( i + maxLength > lower.Length ) break;
			var window = lower.Substring( i, maxLength );
			var score = 0;
			foreach ( var w in words )
				if ( window.IndexOf( w, StringComparison.Ordinal ) >= 0 ) score++;
			if ( score > bestScore )
			{
				bestScore = score;
				bestPos = i;
			}
		}

		if ( bestScore <= 0 ) bestPos = 0;

		var start = Math.Max( 0, markdown.LastIndexOf( ' ', Math.Max( 0, bestPos - 10 ) ) + 1 );
		var end = Math.Min( markdown.Length, start + maxLength );
		var spaceEnd = end < markdown.Length ? markdown.IndexOf( ' ', end ) : -1;
		if ( spaceEnd != -1 && spaceEnd - end < 20 ) end = spaceEnd;

		var snippet = markdown.Substring( start, end - start ).Trim();
		snippet = Regex.Replace( snippet, @"#{1,6}\s*", "" );
		snippet = Regex.Replace( snippet, @"\n{2,}", "\n" );
		var sb = new StringBuilder();
		if ( start > 0 ) sb.Append( '…' );
		sb.Append( snippet );
		if ( end < markdown.Length ) sb.Append( '…' );
		return sb.ToString();
	}
}
