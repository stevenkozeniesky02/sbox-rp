using Sandbox;
using SboxMcp;

namespace SboxMcp.Handlers;

/// <summary>
/// Handles scene-related commands: scene.list, scene.get, scene.create,
/// scene.delete, scene.find, scene.set_transform.
/// </summary>
public static class SceneHandler
{
	// -------------------------------------------------------------------------
	// Command handlers
	// -------------------------------------------------------------------------

	/// <summary>
	/// scene.list — Enumerate all GameObjects in the active scene.
	/// </summary>
	public static Task<object> ListObjects( HandlerRequest request )
	{
		var scene = GetActiveScene();

		var results = new List<object>();
		foreach ( var go in EnumerateAll( scene ) )
			results.Add( SerializeGameObjectShallow( go ) );

		return Task.FromResult<object>( results );
	}

	/// <summary>
	/// scene.get — Get detailed info about a specific GameObject by ID (Guid).
	/// Params: { "id": "guid-string" }
	/// </summary>
	public static Task<object> GetObject( HandlerRequest request )
	{
		var id = GetParam( request, "id" );
		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );

		var go = FindObjectById( guid );
		if ( go is null )
			throw new KeyNotFoundException( $"GameObject not found: {id}" );

		return Task.FromResult<object>( SerializeGameObjectDetailed( go ) );
	}

	/// <summary>
	/// scene.create — Create a new GameObject.
	/// Params: { "name"?: string, "position"?: "x,y,z", "parentId"?: "guid-string" }
	/// </summary>
	public static Task<object> CreateObject( HandlerRequest request )
	{
		var scene = GetActiveScene();

		var name     = GetParamOptional( request, "name" ) ?? "New GameObject";
		var posStr   = GetParamOptional( request, "position" );
		var parentId = GetParamOptional( request, "parentId" );

		var go = scene.CreateObject();
		go.Name = name;

		if ( posStr is not null )
			go.WorldPosition = ParseVector3( posStr );

		if ( parentId is not null && Guid.TryParse( parentId, out var pguid ) )
		{
			var parent = FindObjectById( pguid );
			if ( parent is not null )
				go.SetParent( parent );
		}

		Log.Info( $"[MCP] Created GameObject '{name}' ({go.Id})" );
		EditorChanges.MarkDirty();
		return Task.FromResult<object>( SerializeGameObjectShallow( go ) );
	}

	/// <summary>
	/// scene.delete — Destroy a GameObject by ID.
	/// Params: { "id": "guid-string" }
	/// </summary>
	public static Task<object> DeleteObject( HandlerRequest request )
	{
		var id = GetParam( request, "id" );
		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );

		var go = FindObjectById( guid );
		if ( go is null )
			throw new KeyNotFoundException( $"GameObject not found: {id}" );

		go.Destroy();
		Log.Info( $"[MCP] Deleted GameObject {id}" );
		EditorChanges.MarkDirty();
		return Task.FromResult<object>( (object)new { deleted = id } );
	}

	/// <summary>
	/// scene.find — Find GameObjects matching a name pattern (supports * wildcard).
	/// Params: { "pattern": "My*Object" }
	/// </summary>
	public static Task<object> FindObjects( HandlerRequest request )
	{
		var pattern = GetParam( request, "pattern" );
		var scene   = GetActiveScene();

		var results = new List<object>();
		foreach ( var go in EnumerateAll( scene ) )
		{
			if ( MatchesPattern( go.Name, pattern ) )
				results.Add( SerializeGameObjectShallow( go ) );
		}

		return Task.FromResult<object>( results );
	}

	/// <summary>
	/// scene.hierarchy — Return the full scene hierarchy as indented text.
	/// </summary>
	public static Task<object> GetHierarchy( HandlerRequest request )
	{
		var scene = GetActiveScene();

		var text = EditorHandler.BuildHierarchyText( scene );
		return Task.FromResult<object>( text );
	}

	/// <summary>
	/// scene.clone — Clone a GameObject by ID.
	/// Params: { "id": "guid-string" }
	/// </summary>
	public static Task<object> CloneObject( HandlerRequest request )
	{
		var id = GetParam( request, "id" );
		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );

		var go = FindObjectById( guid );
		if ( go is null )
			throw new KeyNotFoundException( $"GameObject not found: {id}" );

		var clone = go.Clone();
		clone.Name = go.Name + " (Clone)";

		Log.Info( $"[MCP] Cloned '{go.Name}' -> '{clone.Name}' ({clone.Id})" );
		EditorChanges.MarkDirty();
		return Task.FromResult<object>( SerializeGameObjectShallow( clone ) );
	}

	/// <summary>
	/// scene.reparent — Re-parent a GameObject under another.
	/// Params: { "id": "guid", "parentId": "guid" }
	/// </summary>
	public static Task<object> ReparentObject( HandlerRequest request )
	{
		var id       = GetParam( request, "id" );
		var parentId = GetParam( request, "parentId" );

		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );
		if ( !Guid.TryParse( parentId, out var parentGuid ) )
			throw new ArgumentException( $"Invalid GUID for parentId: {parentId}" );

		var go     = FindObjectById( guid );
		var parent = FindObjectById( parentGuid );

		if ( go is null )
			throw new KeyNotFoundException( $"GameObject not found: {id}" );
		if ( parent is null )
			throw new KeyNotFoundException( $"Parent GameObject not found: {parentId}" );

		go.SetParent( parent );

		Log.Info( $"[MCP] Reparented '{go.Name}' under '{parent.Name}'" );
		EditorChanges.MarkDirty();
		return Task.FromResult<object>( SerializeGameObjectShallow( go ) );
	}

	/// <summary>
	/// scene.find_by_component — Find GameObjects that have a component of the given type.
	/// Params: { "type": "TypeName" }
	/// </summary>
	public static Task<object> FindByComponent( HandlerRequest request )
	{
		var typeName = GetParam( request, "type" );
		var scene    = GetActiveScene();

		var results = new List<object>();
		foreach ( var go in EnumerateAll( scene ) )
		{
			foreach ( var comp in go.Components.GetAll() )
			{
				if ( comp.GetType().Name.Equals( typeName, StringComparison.OrdinalIgnoreCase ) )
				{
					results.Add( SerializeGameObjectShallow( go ) );
					break;
				}
			}
		}

		return Task.FromResult<object>( results );
	}

	/// <summary>
	/// scene.find_by_tag — Find GameObjects that have a specific tag.
	/// Params: { "tag": "tagName" }
	/// </summary>
	public static Task<object> FindByTag( HandlerRequest request )
	{
		var tag   = GetParam( request, "tag" );
		var scene = GetActiveScene();

		var results = new List<object>();
		foreach ( var go in EnumerateAll( scene ) )
		{
			if ( go.Tags.Has( tag ) )
				results.Add( SerializeGameObjectShallow( go ) );
		}

		return Task.FromResult<object>( results );
	}

	/// <summary>
	/// tag.add — Add a tag to a GameObject.
	/// Params: { "id": "guid", "tag": "tagName" }
	/// </summary>
	public static Task<object> TagAdd( HandlerRequest request )
	{
		var id  = GetParam( request, "id" );
		var tag = GetParam( request, "tag" );

		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );

		var go = FindObjectById( guid );
		if ( go is null )
			throw new KeyNotFoundException( $"GameObject not found: {id}" );

		go.Tags.Add( tag );

		Log.Info( $"[MCP] Added tag '{tag}' to '{go.Name}'" );
		EditorChanges.MarkDirty();
		return Task.FromResult<object>( (object)new { tags = GetTagList( go ) } );
	}

	/// <summary>
	/// tag.remove — Remove a tag from a GameObject.
	/// Params: { "id": "guid", "tag": "tagName" }
	/// </summary>
	public static Task<object> TagRemove( HandlerRequest request )
	{
		var id  = GetParam( request, "id" );
		var tag = GetParam( request, "tag" );

		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );

		var go = FindObjectById( guid );
		if ( go is null )
			throw new KeyNotFoundException( $"GameObject not found: {id}" );

		go.Tags.Remove( tag );

		Log.Info( $"[MCP] Removed tag '{tag}' from '{go.Name}'" );
		EditorChanges.MarkDirty();
		return Task.FromResult<object>( (object)new { tags = GetTagList( go ) } );
	}

	/// <summary>
	/// tag.list — List all tags on a GameObject.
	/// Params: { "id": "guid" }
	/// </summary>
	public static Task<object> TagList( HandlerRequest request )
	{
		var id = GetParam( request, "id" );

		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );

		var go = FindObjectById( guid );
		if ( go is null )
			throw new KeyNotFoundException( $"GameObject not found: {id}" );

		return Task.FromResult<object>( (object)new { tags = GetTagList( go ) } );
	}

	/// <summary>
	/// scene.load — Open a scene file in the editor.
	/// Params: { "path": "path/to/scene.scene" }
	/// </summary>
	public static Task<object> LoadScene( HandlerRequest request )
	{
		var path = GetParam( request, "path" );

		try
		{
			EditorSession.CreateFromPath( path );
			Log.Info( $"[MCP] scene.load '{path}'" );
			return Task.FromResult<object>( (object)new { loaded = true, path = path } );
		}
		catch ( Exception ex )
		{
			throw new InvalidOperationException( $"scene.load failed: {ex.Message}", ex );
		}
	}

	/// <summary>
	/// scene.set_transform — Set position/rotation/scale on a GameObject.
	/// Params: { "id": "guid", "position"?: "x,y,z", "rotation"?: "x,y,z", "scale"?: "x,y,z" }
	/// </summary>
	public static Task<object> SetTransform( HandlerRequest request )
	{
		var id = GetParam( request, "id" );
		if ( !Guid.TryParse( id, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {id}" );

		var go = FindObjectById( guid );
		if ( go is null )
			throw new KeyNotFoundException( $"GameObject not found: {id}" );

		var posStr   = GetParamOptional( request, "position" );
		var rotStr   = GetParamOptional( request, "rotation" );
		var scaleStr = GetParamOptional( request, "scale" );

		if ( posStr   is not null ) go.WorldPosition = ParseVector3( posStr );
		if ( scaleStr is not null ) go.WorldScale    = ParseVector3( scaleStr );
		if ( rotStr   is not null )
		{
			var euler = ParseVector3( rotStr );
			go.WorldRotation = Rotation.From( euler.x, euler.y, euler.z );
		}

		EditorChanges.MarkDirty();
		return Task.FromResult<object>( SerializeGameObjectShallow( go ) );
	}

	// -------------------------------------------------------------------------
	// Scene access
	// -------------------------------------------------------------------------

	/// <summary>
	/// Gets the scene currently open in the editor, falling back to Game.ActiveScene.
	/// </summary>
	private static Scene GetActiveScene()
	{
		return EditorSession.ActiveScene ?? Game.ActiveScene
			?? throw new InvalidOperationException( "No active scene." );
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Serializes a GameObject to a shallow summary dict.
	/// </summary>
	public static Dictionary<string, object> SerializeGameObjectShallow( GameObject go )
	{
		var pos = go.WorldPosition;
		return new Dictionary<string, object>
		{
			["id"]         = go.Id.ToString(),
			["name"]       = go.Name,
			["enabled"]    = go.Enabled,
			["parentId"]   = go.Parent != null ? go.Parent.Id.ToString() : "",
			["childCount"] = go.Children.Count,
			["position"]   = new { x = pos.x, y = pos.y, z = pos.z },
		};
	}

	/// <summary>
	/// Serializes a GameObject with full transform and component details.
	/// </summary>
	private static Dictionary<string, object> SerializeGameObjectDetailed( GameObject go )
	{
		var pos   = go.WorldPosition;
		var rot   = go.WorldRotation.Angles();
		var scale = go.WorldScale;

		var components = new List<object>();
		foreach ( var comp in go.Components.GetAll() )
		{
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
			}
			catch ( Exception ex )
			{
				props["_error"] = ex.Message;
			}

			components.Add( new
			{
				type       = comp.GetType().Name,
				properties = props,
			} );
		}

		return new Dictionary<string, object>
		{
			["id"]         = go.Id.ToString(),
			["name"]       = go.Name,
			["enabled"]    = go.Enabled,
			["parentId"]   = go.Parent != null ? go.Parent.Id.ToString() : "",
			["position"]   = new { x = pos.x,    y = pos.y,   z = pos.z   },
			["rotation"]   = new { x = rot.pitch, y = rot.yaw, z = rot.roll },
			["scale"]      = new { x = scale.x,  y = scale.y, z = scale.z },
			["components"] = components,
		};
	}

	/// <summary>
	/// Searches the active scene for a GameObject by Guid. Returns null if not found.
	/// </summary>
	public static GameObject FindObjectById( Guid id )
	{
		var scene = EditorSession.ActiveScene ?? Game.ActiveScene;
		if ( scene is null ) return null;

		foreach ( var go in EnumerateAll( scene ) )
		{
			if ( go.Id == id )
				return go;
		}
		return null;
	}

	/// <summary>
	/// Enumerates all GameObjects in the scene using the scene directory.
	/// </summary>
	private static IEnumerable<GameObject> EnumerateAll( Scene scene )
	{
		return scene.GetAllObjects( false );
	}

	/// <summary>
	/// Parses "x,y,z" into a Vector3.
	/// </summary>
	public static Vector3 ParseVector3( string s )
	{
		var parts = s.Split( ',' );
		if ( parts.Length != 3 )
			throw new FormatException( $"Expected 'x,y,z' format, got: {s}" );

		return new Vector3(
			float.Parse( parts[0].Trim() ),
			float.Parse( parts[1].Trim() ),
			float.Parse( parts[2].Trim() ) );
	}

	/// <summary>
	/// Returns true if name matches pattern (supports leading/trailing * wildcard).
	/// </summary>
	private static bool MatchesPattern( string name, string pattern )
	{
		if ( pattern == "*" )
			return true;
		if ( pattern.StartsWith( "*" ) && pattern.EndsWith( "*" ) )
			return name.Contains( pattern.Trim( '*' ), StringComparison.OrdinalIgnoreCase );
		if ( pattern.StartsWith( "*" ) )
			return name.EndsWith( pattern.TrimStart( '*' ), StringComparison.OrdinalIgnoreCase );
		if ( pattern.EndsWith( "*" ) )
			return name.StartsWith( pattern.TrimEnd( '*' ), StringComparison.OrdinalIgnoreCase );
		return string.Equals( name, pattern, StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// Returns the tag set for a GameObject as a plain string list.
	/// TagSet implements IEnumerable&lt;string&gt; in s&box.
	/// </summary>
	private static List<string> GetTagList( GameObject go )
	{
		var list = new List<string>();
		foreach ( var tag in go.Tags )
			list.Add( tag );
		return list;
	}

	// -------------------------------------------------------------------------
	// Param helpers
	// -------------------------------------------------------------------------

	private static string GetParam( HandlerRequest request, string key )
	{
		var val = GetParamOptional( request, key );
		if ( val is null )
			throw new ArgumentException( $"Missing required parameter: {key}" );
		return val;
	}

	private static string GetParamOptional( HandlerRequest request, string key )
	{
		if ( request.Params is not JsonElement el )
			return null;
		if ( el.TryGetProperty( key, out var prop ) )
			return prop.GetString();
		return null;
	}
}
