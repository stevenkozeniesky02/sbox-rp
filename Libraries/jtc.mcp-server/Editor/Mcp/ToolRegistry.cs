using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SboxMcp.Mcp;

/// <summary>
/// Discovers <see cref="McpToolAttribute"/>-decorated static methods via reflection,
/// builds JSON Schemas for their parameters, and dispatches incoming MCP
/// <c>tools/call</c> requests by name.
/// </summary>
public static class ToolRegistry
{
	private static readonly Dictionary<string, ToolEntry> _tools = new();
	private static readonly List<ToolDescriptor> _descriptors = new();
	private static bool _initialised;

	private sealed class ToolEntry
	{
		public ToolDescriptor Descriptor { get; init; }
		public MethodInfo Method { get; init; }
		public ParameterInfo[] Parameters { get; init; }
	}

	public static IReadOnlyList<ToolDescriptor> List()
	{
		EnsureInitialised();
		return _descriptors;
	}

	public static async Task<ToolCallResult> InvokeAsync( string name, JsonElement? arguments )
	{
		EnsureInitialised();

		if ( !_tools.TryGetValue( name, out var entry ) )
		{
			return new ToolCallResult
			{
				IsError = true,
				Content = new List<ToolContent> { ToolContent.FromText( $"Unknown tool: {name}" ) },
			};
		}

		try
		{
			var args = BuildArguments( entry.Parameters, arguments );
			var result = entry.Method.Invoke( null, args );
			var resolved = await UnwrapAsync( result );
			return new ToolCallResult
			{
				Content = new List<ToolContent> { ToolContent.FromText( FormatResult( resolved ) ) },
			};
		}
		catch ( Exception ex )
		{
			var inner = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
			Log.Warning( $"[MCP] Tool '{name}' failed: {inner.Message}" );
			return new ToolCallResult
			{
				IsError = true,
				Content = new List<ToolContent> { ToolContent.FromText( $"Error: {inner.Message}" ) },
			};
		}
	}

	private static void EnsureInitialised()
	{
		if ( _initialised ) return;
		_initialised = true;

		// Look only at classes tagged [McpToolGroup] — avoids scanning every type in the
		// s&box editor's combined assembly, and makes discovery deterministic.
		var thisAssembly = typeof( ToolRegistry ).Assembly;
		var groupTypes = thisAssembly.GetTypes()
			.Where( t => t.GetCustomAttribute<McpToolGroupAttribute>() is not null );
		var methods = groupTypes
			.SelectMany( t => t.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
			.Where( m => m.GetCustomAttribute<McpToolAttribute>() is not null );

		foreach ( var m in methods )
		{
			var attr = m.GetCustomAttribute<McpToolAttribute>();
			var description = attr.Description ?? GetAttributeDescription( m ) ?? "";
			var parameters = m.GetParameters();
			var schema = BuildInputSchema( parameters );

			var descriptor = new ToolDescriptor
			{
				Name = attr.Name,
				Description = description,
				InputSchema = schema,
			};

			_tools[attr.Name] = new ToolEntry
			{
				Descriptor = descriptor,
				Method = m,
				Parameters = parameters,
			};
			_descriptors.Add( descriptor );
		}

		_descriptors.Sort( ( a, b ) => string.Compare( a.Name, b.Name, StringComparison.Ordinal ) );
		Log.Info( $"[MCP] Tool registry initialised: {_descriptors.Count} tools." );
	}

	private static object BuildInputSchema( ParameterInfo[] parameters )
	{
		var properties = new Dictionary<string, object>();
		var required = new List<string>();

		foreach ( var p in parameters )
		{
			if ( p.ParameterType == typeof( CancellationToken ) ) continue;

			var description = p.GetCustomAttribute<ParamDescriptionAttribute>()?.Description
				?? GetAttributeDescription( p )
				?? "";

			var prop = new Dictionary<string, object>
			{
				["type"] = JsonTypeFor( p.ParameterType ),
			};
			if ( !string.IsNullOrEmpty( description ) ) prop["description"] = description;

			properties[p.Name] = prop;

			if ( !p.IsOptional && !IsNullableReferenceLike( p.ParameterType ) )
				required.Add( p.Name );
		}

		var schema = new Dictionary<string, object>
		{
			["type"] = "object",
			["properties"] = properties,
		};
		if ( required.Count > 0 ) schema["required"] = required;
		return schema;
	}

	/// <summary>
	/// Find the Description property on any attribute applied to <paramref name="provider"/>.
	/// Avoids the System.ComponentModel.DescriptionAttribute / Sandbox.DescriptionAttribute
	/// namespace tug-of-war by reading whichever was actually applied via reflection.
	/// </summary>
	private static string GetAttributeDescription( ICustomAttributeProvider provider )
	{
		foreach ( var attr in provider.GetCustomAttributes( inherit: false ) )
		{
			var prop = attr.GetType().GetProperty( "Description" );
			if ( prop?.GetValue( attr ) is string s && !string.IsNullOrEmpty( s ) ) return s;
		}
		return null;
	}

	private static string JsonTypeFor( Type t )
	{
		if ( t == typeof( string ) ) return "string";
		if ( t == typeof( bool ) || t == typeof( bool? ) ) return "boolean";
		if ( t == typeof( int ) || t == typeof( int? )
			|| t == typeof( long ) || t == typeof( long? )
			|| t == typeof( float ) || t == typeof( float? )
			|| t == typeof( double ) || t == typeof( double? ) ) return "number";
		if ( t.IsArray || (t.IsGenericType && typeof( IEnumerable<object> ).IsAssignableFrom( t )) ) return "array";
		return "string"; // safe fallback
	}

	private static bool IsNullableReferenceLike( Type t )
	{
		// Treat reference types as optional when the call site doesn't set [Required].
		// We do this conservatively to avoid forcing strings to be present everywhere.
		return !t.IsValueType || Nullable.GetUnderlyingType( t ) is not null;
	}

	private static object[] BuildArguments( ParameterInfo[] parameters, JsonElement? arguments )
	{
		var result = new object[parameters.Length];
		var hasArgs = arguments.HasValue && arguments.Value.ValueKind == JsonValueKind.Object;

		for ( var i = 0; i < parameters.Length; i++ )
		{
			var p = parameters[i];

			if ( p.ParameterType == typeof( CancellationToken ) )
			{
				result[i] = CancellationToken.None;
				continue;
			}

			if ( hasArgs && arguments.Value.TryGetProperty( p.Name, out var prop )
				&& prop.ValueKind != JsonValueKind.Null && prop.ValueKind != JsonValueKind.Undefined )
			{
				result[i] = ConvertJson( prop, p.ParameterType );
			}
			else if ( p.HasDefaultValue )
			{
				result[i] = p.DefaultValue;
			}
			else if ( IsNullableReferenceLike( p.ParameterType ) )
			{
				result[i] = null;
			}
			else
			{
				throw new ArgumentException( $"Missing required argument: {p.Name}" );
			}
		}

		return result;
	}

	private static object ConvertJson( JsonElement el, Type targetType )
	{
		var nullable = Nullable.GetUnderlyingType( targetType );
		var t = nullable ?? targetType;

		if ( t == typeof( string ) ) return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
		if ( t == typeof( bool ) ) return el.GetBoolean();
		if ( t == typeof( int ) ) return el.GetInt32();
		if ( t == typeof( long ) ) return el.GetInt64();
		if ( t == typeof( double ) ) return el.GetDouble();
		if ( t == typeof( float ) ) return (float)el.GetDouble();
		if ( t == typeof( JsonElement ) ) return el.Clone();

		// Last resort: deserialise via JsonSerializer
		return JsonSerializer.Deserialize( el.GetRawText(), t );
	}

	private static async Task<object> UnwrapAsync( object value )
	{
		if ( value is Task task )
		{
			await task.ConfigureAwait( false );
			var resultProp = task.GetType().GetProperty( "Result" );
			return resultProp?.GetValue( task );
		}
		return value;
	}

	private static string FormatResult( object value )
	{
		if ( value is null ) return "(no output)";
		if ( value is string s ) return s;
		try
		{
			return JsonSerializer.Serialize( value, new JsonSerializerOptions { WriteIndented = true } );
		}
		catch
		{
			return value.ToString() ?? "(no output)";
		}
	}
}
