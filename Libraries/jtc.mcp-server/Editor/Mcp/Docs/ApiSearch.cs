using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SboxMcp.Mcp.Docs;

public sealed class ApiSearchResult
{
	public string FullName { get; set; }
	public string Name { get; set; }
	public string Namespace { get; set; }
	public string Description { get; set; }
	public string Url { get; set; }
	public List<string> TopMembers { get; set; } = new();
	public double Score { get; set; }
}

public sealed class ApiSearch
{
	private FuzzyIndex<ApiType> _index = CreateIndex();
	private readonly Dictionary<string, ApiType> _byName = new( StringComparer.OrdinalIgnoreCase );

	private static FuzzyIndex<ApiType> CreateIndex() => new(
		new FuzzyIndexConfig
		{
			Fields = new[]
			{
				new IndexedField( "name", 4.0 ),
				new IndexedField( "fullName", 3.0 ),
				new IndexedField( "memberNames", 2.0 ),
				new IndexedField( "namespace", 1.5 ),
				new IndexedField( "description", 1.0 ),
			},
		},
		type => new Dictionary<string, string>
		{
			["name"] = type.Name,
			["fullName"] = type.FullName,
			["namespace"] = type.Namespace ?? "",
			["description"] = type.Documentation?.Summary ?? "",
			["memberNames"] = string.Join( ' ', CollectMemberNames( type ) ),
		} );

	public int TypeCount
	{
		get
		{
			var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var t in _byName.Values ) seen.Add( t.FullName );
			return seen.Count;
		}
	}

	public void BuildIndex( IEnumerable<ApiType> types )
	{
		_index = CreateIndex();
		_byName.Clear();
		foreach ( var t in types )
		{
			_index.Add( t );
			_byName[t.FullName] = t;
			if ( !_byName.ContainsKey( t.Name ) )
				_byName[t.Name] = t;
		}
	}

	public IReadOnlyList<ApiSearchResult> Search( string query, int limit = 8 )
	{
		return _index.Search( query, limit )
			.Select( h => new ApiSearchResult
			{
				FullName = h.Item.FullName,
				Name = h.Item.Name,
				Namespace = h.Item.Namespace ?? "",
				Description = h.Item.Documentation?.Summary ?? "",
				Url = TypeUrl( h.Item ),
				TopMembers = CollectMemberNames( h.Item ).Take( 5 ).ToList(),
				Score = h.Score,
			} )
			.ToList();
	}

	public ApiType LookupType( string name ) =>
		_byName.TryGetValue( name, out var t ) ? t : null;

	internal static string TypeUrl( ApiType t ) => $"https://sbox.game/api/t/{t.FullName}";

	internal static IEnumerable<string> CollectMemberNames( ApiType t )
	{
		if ( t.Methods is { Count: > 0 } )
			foreach ( var m in t.Methods ) yield return m.Name;
		if ( t.Properties is { Count: > 0 } )
			foreach ( var p in t.Properties ) yield return p.Name;
		if ( t.Fields is { Count: > 0 } )
			foreach ( var f in t.Fields ) yield return f.Name;
	}

	public static string FormatTypeDetail( ApiType type, int startIndex, int maxLength )
	{
		var sb = new StringBuilder();
		var kind = type.IsInterface ? "interface" : type.IsAbstract ? "abstract class" : "class";
		sb.AppendLine( $"# {type.FullName}" );
		sb.AppendLine( $"**Type:** {kind} | **Namespace:** {(string.IsNullOrEmpty( type.Namespace ) ? "(global)" : type.Namespace)}" );
		if ( !string.IsNullOrEmpty( type.BaseType ) )
			sb.AppendLine( $"**Inherits:** {type.BaseType}" );
		var url = TypeUrl( type );
		sb.AppendLine( $"**URL:** [{url}]({url})" );
		sb.AppendLine();

		if ( !string.IsNullOrEmpty( type.Documentation?.Summary ) )
		{
			sb.AppendLine( type.Documentation.Summary );
			sb.AppendLine();
		}

		if ( type.Constructors is { Count: > 0 } )
		{
			sb.AppendLine( "## Constructors" );
			foreach ( var c in type.Constructors )
			{
				sb.AppendLine( $"- `{FormatMethodSignature( c )}`" );
				if ( !string.IsNullOrEmpty( c.Documentation?.Summary ) )
					sb.AppendLine( $"  {c.Documentation.Summary}" );
			}
			sb.AppendLine();
		}

		if ( type.Properties is { Count: > 0 } )
		{
			sb.AppendLine( "## Properties" );
			foreach ( var p in type.Properties )
			{
				var stat = p.IsStatic ? "static " : "";
				sb.AppendLine( $"- `{stat}{p.PropertyType ?? "?"} {p.Name}`" );
				if ( !string.IsNullOrEmpty( p.Documentation?.Summary ) )
					sb.AppendLine( $"  {p.Documentation.Summary}" );
			}
			sb.AppendLine();
		}

		if ( type.Methods is { Count: > 0 } )
		{
			sb.AppendLine( "## Methods" );
			foreach ( var m in type.Methods )
			{
				var stat = m.IsStatic ? "static " : "";
				sb.AppendLine( $"- `{stat}{FormatMethodSignature( m )}`" );
				if ( !string.IsNullOrEmpty( m.Documentation?.Summary ) )
					sb.AppendLine( $"  {m.Documentation.Summary}" );
			}
			sb.AppendLine();
		}

		if ( type.Fields is { Count: > 0 } )
		{
			sb.AppendLine( "## Fields" );
			foreach ( var f in type.Fields )
			{
				var stat = f.IsStatic ? "static " : "";
				sb.AppendLine( $"- `{stat}{f.FieldType ?? "?"} {f.Name}`" );
				if ( !string.IsNullOrEmpty( f.Documentation?.Summary ) )
					sb.AppendLine( $"  {f.Documentation.Summary}" );
			}
			sb.AppendLine();
		}

		var full = sb.ToString();
		var totalLength = full.Length;
		var start = Math.Min( startIndex, totalLength );
		var clampedLength = Math.Min( maxLength, totalLength - start );
		var chunk = full.Substring( start, clampedLength );
		var endIndex = start + chunk.Length;
		var hasMore = endIndex < totalLength;

		var footer = hasMore
			? $"\n\n---\n_Showing characters {start}–{endIndex} of {totalLength}. Use start_index={endIndex} to read the next chunk._"
			: $"\n\n---\n_End of type page ({totalLength} characters total)._";

		return chunk + footer;
	}

	private static string FormatMethodSignature( ApiMethod m )
	{
		var paramStr = "";
		if ( m.Parameters is { Count: > 0 } )
		{
			paramStr = string.Join( ", ", m.Parameters.Select( p =>
			{
				var prefix = p.Out ? "out " : "";
				return string.IsNullOrEmpty( p.Type ) ? $"{prefix}{p.Name}" : $"{prefix}{p.Type} {p.Name}";
			} ) );
		}
		var ret = string.IsNullOrEmpty( m.ReturnType ) || m.ReturnType == "void" ? "void" : m.ReturnType;
		return $"{ret} {m.Name}({paramStr})";
	}
}
