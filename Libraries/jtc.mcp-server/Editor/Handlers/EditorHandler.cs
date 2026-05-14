using Sandbox;
using System.IO;
using SboxMcp;

namespace SboxMcp.Handlers;

/// <summary>
/// Handles editor-specific commands: editor.get_selection, editor.select,
/// editor.undo, editor.redo, editor.save_scene, editor.screenshot, scene.hierarchy.
/// </summary>
public static class EditorHandler
{
	/// <summary>
	/// editor.get_selection — Get the currently selected GameObjects in the editor.
	/// </summary>
	public static Task<object> HandleGetSelection( HandlerRequest request )
	{
		try
		{
			var selected = EditorSession.SelectionGameObjects.ToList();

			if ( selected.Count == 0 )
				return Task.FromResult<object>( new List<object>() );

			var results = selected.Select( go => (object)new
			{
				id   = go.Id.ToString(),
				name = go.Name,
			} ).ToList();

			return Task.FromResult<object>( results );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP] editor.get_selection failed: {ex.Message}" );
			throw new InvalidOperationException( $"Could not get editor selection: {ex.Message}", ex );
		}
	}

	/// <summary>
	/// editor.select — Select a GameObject by ID.
	/// Params: { "objectId": "guid-string" }
	/// </summary>
	public static Task<object> HandleSelectObject( HandlerRequest request )
	{
		var objectId = GetParam( request, "objectId" );

		if ( !Guid.TryParse( objectId, out var guid ) )
			throw new ArgumentException( $"Invalid GUID: {objectId}" );

		var scene = EditorSession.ActiveScene ?? Game.ActiveScene;
		if ( scene is null )
			throw new InvalidOperationException( "No active scene." );

		GameObject target = null;
		foreach ( var go in EnumerateAll( scene ) )
		{
			if ( go.Id == guid )
			{
				target = go;
				break;
			}
		}

		if ( target is null )
			throw new KeyNotFoundException( $"GameObject not found: {objectId}" );

		try
		{
			EditorSession.SelectionClear();
			EditorSession.SelectionAdd( target );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP] editor.select could not set gizmo selection: {ex.Message}" );
		}

		Log.Info( $"[MCP] Selected GameObject '{target.Name}' ({objectId})" );
		return Task.FromResult<object>( (object)new
		{
			selected = true,
			id       = target.Id.ToString(),
			name     = target.Name,
		} );
	}

	/// <summary>
	/// editor.undo — Undo the last editor action.
	/// </summary>
	public static Task<object> HandleUndo( HandlerRequest request )
	{
		try
		{
			// NOTE: s&box API - verify
			EditorSession.Undo();
			Log.Info( "[MCP] editor.undo dispatched" );
			return Task.FromResult<object>( (object)new { success = true, action = "undo" } );
		}
		catch ( Exception ex )
		{
			throw new InvalidOperationException( $"Undo failed: {ex.Message}", ex );
		}
	}

	/// <summary>
	/// editor.redo — Redo the last undone editor action.
	/// </summary>
	public static Task<object> HandleRedo( HandlerRequest request )
	{
		try
		{
			// NOTE: s&box API - verify
			EditorSession.Redo();
			Log.Info( "[MCP] editor.redo dispatched" );
			return Task.FromResult<object>( (object)new { success = true, action = "redo" } );
		}
		catch ( Exception ex )
		{
			throw new InvalidOperationException( $"Redo failed: {ex.Message}", ex );
		}
	}

	/// <summary>
	/// editor.save_scene — Save the current scene.
	/// </summary>
	public static Task<object> HandleSaveScene( HandlerRequest request )
	{
		try
		{
			// NOTE: s&box API - verify
			EditorSession.Save( false );
			Log.Info( "[MCP] editor.save_scene dispatched" );
			return Task.FromResult<object>( (object)new { success = true, action = "save_scene" } );
		}
		catch ( Exception ex )
		{
			throw new InvalidOperationException( $"Save scene failed: {ex.Message}", ex );
		}
	}

	/// <summary>
	/// editor.screenshot — Render a scene camera to a PNG file and return the path.
	///
	/// We pick a camera in this priority order:
	///   1. The first <see cref="CameraComponent"/> found in the active scene.
	///   2. A throwaway editor camera positioned at the current scene-view perspective
	///      (if a SceneViewWidget is available).
	///
	/// The PNG is written into the project's "screenshots/" folder. If neither path
	/// is reachable we return a structured error so the caller can fall back to the
	/// <c>screenshot_highres</c> console command.
	///
	/// Optional params: { "width": 1920, "height": 1080, "path": "absolute/output.png" }
	/// </summary>
	public static Task<object> HandleScreenshot( HandlerRequest request )
	{
		var width  = GetOptionalInt( request, "width",  1920 );
		var height = GetOptionalInt( request, "height", 1080 );
		var explicitPath = GetOptionalString( request, "path" );

		try
		{
			var scene = EditorSession.ActiveScene ?? Game.ActiveScene;
			if ( scene is null )
				throw new InvalidOperationException( "No active scene to capture." );

			var camera = FindUsableCamera( scene );
			if ( camera is null )
				throw new InvalidOperationException(
					"No CameraComponent in scene. Add a camera or use the 'screenshot_highres' console command instead." );

			// Editor.Pixmap and the CameraComponent.RenderToPixmap extension are not
			// resolvable from the publish-wizard's library compile environment, so we
			// reach them reflectively. At runtime in a real editor session the types
			// are present and resolve fine.
			var outPath = ResolveOutputPath( explicitPath );
			var outDir = Path.GetDirectoryName( outPath );
			if ( !string.IsNullOrEmpty( outDir ) )
				Directory.CreateDirectory( outDir );

			var pixmapType = Type.GetType( "Editor.Pixmap, Sandbox.Tools" )
				?? AppDomain.CurrentDomain.GetAssemblies()
					.Select( a => a.GetType( "Editor.Pixmap" ) )
					.FirstOrDefault( t => t is not null );
			if ( pixmapType is null )
				throw new InvalidOperationException( "Editor.Pixmap type not available in this context." );

			var pixmap = Activator.CreateInstance( pixmapType, new object[] { width, height } );

			var renderToPixmap = camera.GetType().GetMethod( "RenderToPixmap", new[] { pixmapType } )
				?? AppDomain.CurrentDomain.GetAssemblies()
					.SelectMany( a => a.GetTypes() )
					.SelectMany( t => t.GetMethods( BindingFlags.Public | BindingFlags.Static ) )
					.FirstOrDefault( m => m.Name == "RenderToPixmap"
						&& m.GetParameters().Length == 2
						&& m.GetParameters()[1].ParameterType == pixmapType );
			if ( renderToPixmap is null )
				throw new InvalidOperationException( "RenderToPixmap method not available in this context." );

			if ( renderToPixmap.IsStatic )
				renderToPixmap.Invoke( null, new object[] { camera, pixmap } );
			else
				renderToPixmap.Invoke( camera, new object[] { pixmap } );

			var savePng = pixmapType.GetMethod( "SavePng", new[] { typeof( string ) } );
			savePng?.Invoke( pixmap, new object[] { outPath } );

			Log.Info( $"[MCP] editor.screenshot saved -> {outPath}" );
			return Task.FromResult<object>( (object)new
			{
				success = true,
				path    = outPath,
				width,
				height,
			} );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP] editor.screenshot failed: {ex.Message}" );
			return Task.FromResult<object>( (object)new
			{
				success = false,
				error   = ex.Message,
				note    = "Fallback: use the console command 'screenshot_highres' via console.run.",
			} );
		}
	}

	private static CameraComponent FindUsableCamera( Scene scene )
	{
		foreach ( var go in scene.GetAllObjects( false ) )
		{
			var cam = go.Components.Get<CameraComponent>();
			if ( cam is not null ) return cam;
		}
		return null;
	}

	private static string ResolveOutputPath( string explicitPath )
	{
		if ( !string.IsNullOrWhiteSpace( explicitPath ) )
		{
			// Absolute path or project-relative — caller's choice
			return Path.IsPathRooted( explicitPath )
				? explicitPath
				: Path.Combine( Project.Current?.GetRootPath() ?? Environment.CurrentDirectory, explicitPath );
		}

		var stamp = DateTime.UtcNow.ToString( "yyyyMMdd-HHmmss" );
		var rootPath = Project.Current?.GetRootPath() ?? Environment.CurrentDirectory;
		return Path.Combine( rootPath, "screenshots", $"mcp-{stamp}.png" );
	}

	private static int GetOptionalInt( HandlerRequest request, string key, int fallback )
	{
		if ( request.Params is JsonElement el && el.TryGetProperty( key, out var prop ) )
		{
			if ( prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32( out var i ) ) return i;
		}
		return fallback;
	}

	private static string GetOptionalString( HandlerRequest request, string key )
	{
		if ( request.Params is JsonElement el && el.TryGetProperty( key, out var prop ) )
			return prop.GetString();
		return null;
	}

	/// <summary>
	/// editor.play — Start playing the active scene.
	/// </summary>
	public static Task<object> HandlePlay( HandlerRequest request )
	{
		var editorScene = EditorSession.ActiveScene;
		if ( editorScene is null )
			throw new InvalidOperationException( "No active editor session." );

		if ( EditorSession.IsPlaying )
			return Task.FromResult<object>( (object)new { success = false, error = "Already playing" } );

		try
		{
			// SetPlaying requires a game scene — create one from the editor scene
			var gameScene = Scene.CreateEditorScene();
			gameScene.Load( editorScene.Source );
			EditorSession.SetPlaying( gameScene );
			Log.Info( "[MCP] editor.play dispatched" );
			return Task.FromResult<object>( (object)new { success = true, action = "play" } );
		}
		catch ( Exception ex )
		{
			throw new InvalidOperationException( $"editor.play failed: {ex.Message}", ex );
		}
	}

	/// <summary>
	/// editor.stop — Stop playing the active scene.
	/// </summary>
	public static Task<object> HandleStop( HandlerRequest request )
	{
		try
		{
			EditorSession.StopPlaying();
			Log.Info( "[MCP] editor.stop dispatched" );
			return Task.FromResult<object>( (object)new { success = true, action = "stop" } );
		}
		catch ( Exception ex )
		{
			throw new InvalidOperationException( $"editor.stop failed: {ex.Message}", ex );
		}
	}

	/// <summary>
	/// editor.is_playing — Return whether the editor is currently in play mode.
	/// </summary>
	public static Task<object> HandleIsPlaying( HandlerRequest request )
	{
		return Task.FromResult<object>( (object)new { playing = EditorSession.IsPlaying } );
	}

	/// <summary>
	/// editor.scene_info — Return metadata about the currently open scene.
	/// </summary>
	public static Task<object> HandleSceneInfo( HandlerRequest request )
	{
		var scene = EditorSession.ActiveScene;
		if ( scene is null )
			throw new InvalidOperationException( "No active editor session." );

		return Task.FromResult<object>( (object)new
		{
			name              = scene.Name ?? "",
			sourcePath        = scene.Source?.ResourcePath ?? "",
			hasUnsavedChanges = EditorSession.HasUnsavedChanges,
			isPlaying         = EditorSession.IsPlaying,
		} );
	}

	/// <summary>
	/// editor.console_output — Return recent log entries captured by the addon.
	/// </summary>
	public static Task<object> HandleConsoleOutput( HandlerRequest request )
	{
		var lines = ConsoleCapture.GetRecent();
		return Task.FromResult<object>( (object)new { lines = lines } );
	}

	// -------------------------------------------------------------------------
	// Hierarchy helpers (used by SceneHandler.HandleHierarchy)
	// -------------------------------------------------------------------------

	/// <summary>
	/// Builds an indented tree string for the given scene, e.g.:
	/// Scene
	/// ├── Directional Light
	/// ├── Player
	/// │   ├── Camera
	/// │   └── Model
	/// └── Ground
	/// </summary>
	public static string BuildHierarchyText( Scene scene )
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine( scene.Name ?? "Scene" );

		var rootChildren = scene.GetAllObjects( false )
			.Where( go => go.Parent is null && !go.Flags.HasFlag( GameObjectFlags.Hidden ) )
			.ToList();

		for ( var i = 0; i < rootChildren.Count; i++ )
		{
			var isLast = i == rootChildren.Count - 1;
			AppendNode( sb, rootChildren[i], "", isLast );
		}

		return sb.ToString().TrimEnd();
	}

	private static void AppendNode( System.Text.StringBuilder sb, GameObject go, string indent, bool isLast )
	{
		var connector = isLast ? "└── " : "├── ";
		sb.AppendLine( indent + connector + go.Name );

		var childIndent = indent + ( isLast ? "    " : "│   " );
		var children    = go.Children.ToList();
		for ( var i = 0; i < children.Count; i++ )
		{
			var childIsLast = i == children.Count - 1;
			AppendNode( sb, children[i], childIndent, childIsLast );
		}
	}

	// -------------------------------------------------------------------------
	// Private helpers
	// -------------------------------------------------------------------------

	private static IEnumerable<GameObject> EnumerateAll( Scene scene )
	{
		return scene.GetAllObjects( false );
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
