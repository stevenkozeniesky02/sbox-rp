using Sandbox;
using SboxMcp;

namespace SboxMcp.Handlers;

/// <summary>
/// Handles component-related commands: component.list, component.get,
/// component.set, component.add, component.remove.
/// </summary>
public static class ComponentHandler
{
	/// <summary>
	/// component.list — List all components on a GameObject.
	/// Params: { "id": "guid-string" }
	/// </summary>
	public static Task<object> ListComponents( HandlerRequest request )
	{
		var go = ResolveGameObject( request );
		var list = new List<object>();

		foreach ( var comp in go.Components.GetAll() )
		{
			list.Add( new
			{
				type    = comp.GetType().Name,
				enabled = comp.Enabled,
			} );
		}

		return Task.FromResult<object>( list );
	}

	/// <summary>
	/// component.get — Get a specific component's properties by type name.
	/// Params: { "id": "guid-string", "type": "TypeName" }
	/// </summary>
	public static Task<object> GetComponent( HandlerRequest request )
	{
		var go       = ResolveGameObject( request );
		var typeName = GetParam( request, "type" );
		var comp     = FindComponentByType( go, typeName );

		var props = new Dictionary<string, object>();
		try
		{
			var td = TypeLibrary.GetType( comp.GetType() );
			if ( td is not null )
			{
				foreach ( var prop in td.Properties )
				{
					try { props[prop.Name] = prop.GetValue( comp )?.ToString() ?? ""; }
					catch { props[prop.Name] = "<error>"; }
				}
			}
			else
			{
				// Fall back to standard reflection if TypeLibrary returns null.
				foreach ( var prop in comp.GetType().GetProperties() )
				{
					try { props[prop.Name] = prop.GetValue( comp )?.ToString() ?? ""; }
					catch { props[prop.Name] = "<error>"; }
				}
			}
		}
		catch ( Exception ex )
		{
			props["_error"] = ex.Message;
		}

		return Task.FromResult<object>( (object)new
		{
			type       = comp.GetType().Name,
			enabled    = comp.Enabled,
			properties = props,
		} );
	}

	/// <summary>
	/// component.set — Set a property on a component.
	/// Params: { "id": "guid", "type": "TypeName", "property": "PropName", "value": "value" }
	/// </summary>
	public static async Task<object> SetComponent( HandlerRequest request )
	{
		var go       = ResolveGameObject( request );
		var typeName = GetParam( request, "type" );
		var propName = GetParam( request, "property" );
		var rawValue = GetParam( request, "value" );
		var comp     = FindComponentByType( go, typeName );

		var td = TypeLibrary.GetType( comp.GetType() );
		if ( td is not null )
		{
			var prop = td.Properties.FirstOrDefault( p => p.Name == propName );
			if ( prop is null )
				throw new KeyNotFoundException( $"Property '{propName}' not found on {typeName}" );

			var converted = await ConvertValueAsync( rawValue, prop.PropertyType );
			prop.SetValue( comp, converted );
		}
		else
		{
			// Fall back to standard reflection.
			var prop = comp.GetType().GetProperty( propName )
				?? throw new KeyNotFoundException( $"Property '{propName}' not found on {typeName}" );
			var converted = await ConvertValueAsync( rawValue, prop.PropertyType );
			prop.SetValue( comp, converted );
		}

		EditorChanges.MarkDirty();
		return (object)new { set = true, property = propName, value = rawValue };
	}

	/// <summary>
	/// component.add — Add a component to a GameObject by type name.
	/// Params: { "id": "guid", "type": "TypeName" }
	/// </summary>
	public static Task<object> AddComponent( HandlerRequest request )
	{
		var go       = ResolveGameObject( request );
		var typeName = GetParam( request, "type" );

		// EditorTypeLibrary would catch editor-context types but it's only resolvable
		// when Sandbox.Tools is linked. TypeLibrary (in Sandbox.System) covers
		// the same types we care about and compiles in any context.
		var typeDesc = TypeLibrary.GetType( typeName );
		if ( typeDesc is null )
			throw new TypeLoadException( $"Type not found: {typeName}" );

		var comp = go.Components.Create( typeDesc );
		Log.Info( $"[MCP] Added component {typeName} to {go.Name}" );

		EditorChanges.MarkDirty();
		return Task.FromResult<object>( (object)new
		{
			added   = true,
			type    = comp.GetType().Name,
			enabled = comp.Enabled,
		} );
	}

	/// <summary>
	/// component.remove — Remove a component by type name.
	/// Params: { "id": "guid", "type": "TypeName" }
	/// </summary>
	public static Task<object> RemoveComponent( HandlerRequest request )
	{
		var go       = ResolveGameObject( request );
		var typeName = GetParam( request, "type" );
		var comp     = FindComponentByType( go, typeName );

		comp.Destroy();
		Log.Info( $"[MCP] Removed component {typeName} from {go.Name}" );

		EditorChanges.MarkDirty();
		return Task.FromResult<object>( (object)new { removed = true, type = typeName } );
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Converts a raw string value to the target type, handling s&box resource types.
	/// </summary>
	private static async Task<object> ConvertValueAsync( string rawValue, Type targetType )
	{
		var typeName = targetType.Name;

		// s&box resource types — must use their Load methods
		if ( typeName == "Model" )
		{
			if ( rawValue.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) )
				return Model.Load( rawValue );
			// Cloud ident — fetch, mount, add to project refs, then load
			var pkg = await Package.Fetch( rawValue, true );
			if ( pkg is not null )
			{
				await pkg.MountAsync();
				AssetHandler.AddPackageReference( rawValue );
				var primary = pkg.GetMeta( "PrimaryAsset", "" );
				if ( !string.IsNullOrEmpty( primary ) )
					return Model.Load( primary );
			}
			throw new InvalidOperationException( $"Could not load cloud model: {rawValue}" );
		}

		if ( typeName == "Material" )
		{
			if ( rawValue.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase ) )
				return Material.Load( rawValue );
			var pkg = await Package.Fetch( rawValue, true );
			if ( pkg is not null )
			{
				await pkg.MountAsync();
				AssetHandler.AddPackageReference( rawValue );
				var primary = pkg.GetMeta( "PrimaryAsset", "" );
				if ( !string.IsNullOrEmpty( primary ) )
					return Material.Load( primary );
			}
			throw new InvalidOperationException( $"Could not load cloud material: {rawValue}" );
		}

		if ( typeName == "Color" )
			return Color.Parse( rawValue ) ?? Color.White;

		if ( typeName == "Vector3" )
		{
			var parts = rawValue.Split( ',' );
			if ( parts.Length == 3 )
				return new Vector3( float.Parse( parts[0].Trim() ), float.Parse( parts[1].Trim() ), float.Parse( parts[2].Trim() ) );
		}

		if ( typeName == "Angles" )
		{
			var parts = rawValue.Split( ',' );
			if ( parts.Length == 3 )
				return new Angles( float.Parse( parts[0].Trim() ), float.Parse( parts[1].Trim() ), float.Parse( parts[2].Trim() ) );
		}

		if ( targetType == typeof( bool ) )
			return bool.Parse( rawValue );

		if ( targetType == typeof( float ) )
			return float.Parse( rawValue );

		if ( targetType == typeof( int ) )
			return int.Parse( rawValue );

		return Convert.ChangeType( rawValue, targetType );
	}

	private static GameObject ResolveGameObject( HandlerRequest request )
	{
		var id = GetParam( request, "id" );
		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );

		return SceneHandler.FindObjectById( guid )
			?? throw new KeyNotFoundException( $"GameObject not found: {id}" );
	}

	private static Component FindComponentByType( GameObject go, string typeName )
	{
		foreach ( var comp in go.Components.GetAll() )
		{
			if ( comp.GetType().Name.Equals( typeName, StringComparison.OrdinalIgnoreCase ) )
				return comp;
		}
		throw new KeyNotFoundException( $"Component '{typeName}' not found on '{go.Name}'" );
	}

	private static string GetParam( HandlerRequest request, string key )
	{
		if ( request.Params is JsonElement el && el.TryGetProperty( key, out var prop ) )
		{
			var val = prop.GetString();
			if ( val is not null ) return val;
		}
		throw new ArgumentException( $"Missing required parameter: {key}" );
	}
}
