using Sandbox;
using System.IO;
using SboxMcp;

namespace SboxMcp.Handlers;

/// <summary>
/// Handles file and project commands: file.read, file.write, file.list, project.info.
/// </summary>
public static class FileHandler
{
	/// <summary>
	/// file.read — Read a file relative to the project root.
	/// Params: { "path": "relative/path/to/file.txt" }
	/// </summary>
	public static Task<object> ReadFile( HandlerRequest request )
	{
		var path = GetParam( request, "path" );

		// Try the s&box mounted filesystem first, fall back to System.IO.
		string content;
		try
		{
			content = Sandbox.FileSystem.Mounted.ReadAllText( path );
		}
		catch
		{
			var fullPath = ResolveAbsolutePath( path );
			content = File.ReadAllText( fullPath );
		}

		return Task.FromResult<object>( (object)new { path, content } );
	}

	/// <summary>
	/// file.write — Write content to a file relative to the project root.
	/// Params: { "path": "relative/path", "content": "file content" }
	/// </summary>
	public static Task<object> WriteFile( HandlerRequest request )
	{
		var path    = GetParam( request, "path" );
		var content = GetParam( request, "content" );

		// Write to the active project directory — code files go in code/, assets in Assets/
		var projectRoot = Project.Current?.GetRootPath();
		if ( projectRoot is null )
			throw new InvalidOperationException( "No active project found." );

		string baseDir;
		if ( path.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) )
			baseDir = Path.Combine( projectRoot, "code" );
		else
			baseDir = Project.Current.GetAssetsPath();

		var fullPath = Path.Combine( baseDir, path );
		var dir = Path.GetDirectoryName( fullPath );
		if ( dir is not null )
			Directory.CreateDirectory( dir );
		File.WriteAllText( fullPath, content );

		Log.Info( $"[MCP] Wrote file: {fullPath}" );
		return Task.FromResult<object>( (object)new { path, written = true } );
	}

	/// <summary>
	/// file.list — List files in a directory, with optional glob pattern.
	/// Params: { "directory": "path/to/dir", "pattern"?: "*.cs" }
	/// </summary>
	public static Task<object> ListFiles( HandlerRequest request )
	{
		var directory = GetParam( request, "directory" );
		var pattern   = GetParamOptional( request, "pattern" ) ?? "*";

		var files = new List<string>();

		// Try s&box FileSystem first.
		try
		{
			var found = Sandbox.FileSystem.Mounted.FindFile( directory, pattern );
			files.AddRange( found );
		}
		catch
		{
			// Fall back to System.IO.
			var fullPath = ResolveAbsolutePath( directory );
			if ( Directory.Exists( fullPath ) )
			{
				foreach ( var f in Directory.GetFiles( fullPath, pattern, SearchOption.TopDirectoryOnly ) )
					files.Add( Path.GetRelativePath( fullPath, f ) );
			}
		}

		return Task.FromResult<object>( (object)new { directory, pattern, files } );
	}

	/// <summary>
	/// project.info — Return metadata about the current project.
	/// Params: (none)
	/// </summary>
	public static Task<object> ProjectInfo( HandlerRequest request )
	{
		string title       = Project.Current?.Config?.Title ?? "Unknown";
		string activeScene = "";
		int    objectCount = 0;

		try
		{
			// Editor session has the open .scene; Game.ActiveScene is play-mode only.
			var scene = EditorSession.ActiveScene ?? Game.ActiveScene;
			if ( scene is not null )
			{
				activeScene = scene.Name ?? "";
				foreach ( var _ in scene.GetAllObjects( false ) )
					objectCount++;
			}
		}
		catch { /* scene may be null */ }

		return Task.FromResult<object>( (object)new
		{
			title,
			activeScene,
			gameObjectCount = objectCount,
		} );
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Resolves a relative path against the current working directory.
	/// </summary>
	private static string ResolveAbsolutePath( string relativePath )
	{
		string root;
		try
		{
			root = Directory.GetCurrentDirectory();
		}
		catch
		{
			root = ".";
		}

		return Path.Combine( root, relativePath );
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

	private static string GetParamOptional( HandlerRequest request, string key )
	{
		if ( request.Params is JsonElement el && el.TryGetProperty( key, out var prop ) )
			return prop.GetString();
		return null;
	}
}
