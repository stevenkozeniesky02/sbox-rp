using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SboxMcp.Mcp.Docs;

public sealed class IndexedField
{
	public string Name { get; }
	public double Boost { get; }
	public IndexedField( string name, double boost ) { Name = name; Boost = boost; }
}

public sealed class FuzzyIndexConfig
{
	public IReadOnlyList<IndexedField> Fields { get; init; } = Array.Empty<IndexedField>();
	public bool Prefix { get; init; } = true;
	public double Fuzzy { get; init; } = 0.2;
}

public sealed class FuzzyHit<T>
{
	public T Item { get; }
	public double Score { get; }
	public FuzzyHit( T item, double score ) { Item = item; Score = score; }
}

/// <summary>
/// Per-field inverted token map with TF + IDF + field boosts, plus optional
/// prefix matching and bounded edit-distance fuzzy fallback. Pragmatic
/// re-implementation of the slice of MiniSearch we need; indexes a few thousand
/// small documents with millisecond query latency.
/// </summary>
public sealed class FuzzyIndex<T>
{
	private static readonly Regex Tokenizer = new( @"[\p{L}\p{N}_]+", RegexOptions.Compiled );

	private readonly FuzzyIndexConfig _config;
	private readonly Func<T, IReadOnlyDictionary<string, string>> _extract;

	private readonly List<T> _items = new();
	private readonly Dictionary<string, Dictionary<string, List<(int DocId, int Count)>>> _index = new();
	private readonly Dictionary<string, List<int>> _fieldLengths = new();

	public FuzzyIndex( FuzzyIndexConfig config, Func<T, IReadOnlyDictionary<string, string>> extract )
	{
		_config = config;
		_extract = extract;
		foreach ( var f in _config.Fields )
		{
			_index[f.Name] = new Dictionary<string, List<(int, int)>>( StringComparer.Ordinal );
			_fieldLengths[f.Name] = new List<int>();
		}
	}

	public int Count => _items.Count;
	public T this[int idx] => _items[idx];

	public void Clear()
	{
		_items.Clear();
		foreach ( var f in _config.Fields )
		{
			_index[f.Name].Clear();
			_fieldLengths[f.Name].Clear();
		}
	}

	public void AddRange( IEnumerable<T> items )
	{
		foreach ( var item in items ) Add( item );
	}

	public void Add( T item )
	{
		var docId = _items.Count;
		_items.Add( item );
		var fieldValues = _extract( item );

		foreach ( var f in _config.Fields )
		{
			fieldValues.TryGetValue( f.Name, out var text );
			var tokens = Tokenize( text ?? "" );
			_fieldLengths[f.Name].Add( tokens.Count );

			var counts = new Dictionary<string, int>( StringComparer.Ordinal );
			foreach ( var t in tokens )
				counts[t] = counts.TryGetValue( t, out var c ) ? c + 1 : 1;

			var idx = _index[f.Name];
			foreach ( var (token, count) in counts )
			{
				if ( !idx.TryGetValue( token, out var list ) )
				{
					list = new List<(int, int)>();
					idx[token] = list;
				}
				list.Add( (docId, count) );
			}
		}
	}

	public List<FuzzyHit<T>> Search( string query, int limit )
	{
		if ( string.IsNullOrWhiteSpace( query ) || _items.Count == 0 )
			return new List<FuzzyHit<T>>();

		var queryTokens = Tokenize( query );
		if ( queryTokens.Count == 0 ) return new List<FuzzyHit<T>>();

		var scores = new Dictionary<int, double>();

		foreach ( var qToken in queryTokens )
		{
			foreach ( var f in _config.Fields )
			{
				var fieldIdx = _index[f.Name];
				var lengths = _fieldLengths[f.Name];

				if ( fieldIdx.TryGetValue( qToken, out var exactList ) )
					Accumulate( scores, exactList, _items.Count, f.Boost, 1.0, lengths );

				if ( _config.Prefix && qToken.Length >= 2 )
				{
					foreach ( var (token, list) in fieldIdx )
					{
						if ( token.Length > qToken.Length && token.StartsWith( qToken, StringComparison.Ordinal ) )
							Accumulate( scores, list, _items.Count, f.Boost, 0.6, lengths );
					}
				}

				if ( _config.Fuzzy > 0 && qToken.Length >= 4 && !fieldIdx.ContainsKey( qToken ) )
				{
					var maxDist = (int)Math.Floor( qToken.Length * _config.Fuzzy );
					if ( maxDist >= 1 )
					{
						foreach ( var (token, list) in fieldIdx )
						{
							if ( Math.Abs( token.Length - qToken.Length ) > maxDist ) continue;
							var d = LevenshteinBounded( qToken, token, maxDist );
							if ( d >= 0 && d <= maxDist )
							{
								var w = 1.0 - (double)d / (qToken.Length + 1);
								Accumulate( scores, list, _items.Count, f.Boost, w * 0.5, lengths );
							}
						}
					}
				}
			}
		}

		return scores
			.OrderByDescending( kv => kv.Value )
			.Take( limit )
			.Select( kv => new FuzzyHit<T>( _items[kv.Key], kv.Value ) )
			.ToList();
	}

	private static void Accumulate(
		Dictionary<int, double> scores,
		List<(int DocId, int Count)> postings,
		int totalDocs,
		double fieldBoost,
		double weight,
		List<int> fieldLengths )
	{
		if ( postings.Count == 0 ) return;
		var idf = Math.Log( (totalDocs - postings.Count + 0.5) / (postings.Count + 0.5) + 1.0 );
		if ( idf <= 0 ) idf = 0.01;

		foreach ( var (docId, count) in postings )
		{
			var len = docId < fieldLengths.Count ? fieldLengths[docId] : 0;
			var tf = count / (count + 1.0 + 0.001 * len);
			var contribution = idf * tf * fieldBoost * weight;
			scores[docId] = scores.TryGetValue( docId, out var existing ) ? existing + contribution : contribution;
		}
	}

	internal static List<string> Tokenize( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return new List<string>();
		var matches = Tokenizer.Matches( text );
		var result = new List<string>( matches.Count );
		foreach ( Match m in matches )
		{
			if ( m.Length < 2 ) continue;
			result.Add( m.Value.ToLowerInvariant() );
		}
		return result;
	}

	internal static int LevenshteinBounded( string a, string b, int maxDist )
	{
		if ( a == b ) return 0;
		var la = a.Length;
		var lb = b.Length;
		if ( Math.Abs( la - lb ) > maxDist ) return -1;

		var prev = new int[lb + 1];
		var curr = new int[lb + 1];
		for ( var j = 0; j <= lb; j++ ) prev[j] = j;

		for ( var i = 1; i <= la; i++ )
		{
			curr[0] = i;
			var rowMin = curr[0];
			for ( var j = 1; j <= lb; j++ )
			{
				var cost = a[i - 1] == b[j - 1] ? 0 : 1;
				curr[j] = Math.Min(
					Math.Min( curr[j - 1] + 1, prev[j] + 1 ),
					prev[j - 1] + cost );
				if ( curr[j] < rowMin ) rowMin = curr[j];
			}
			if ( rowMin > maxDist ) return -1;
			(prev, curr) = (curr, prev);
		}

		return prev[lb];
	}
}
