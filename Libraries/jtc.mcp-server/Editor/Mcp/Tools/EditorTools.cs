using Sandbox;
using System.ComponentModel;
using System.Threading.Tasks;
using SboxMcp.Handlers;

namespace SboxMcp.Mcp.Tools;

[McpToolGroup]
public static class EditorTools
{
	[McpTool( "editor_get_selection", Description = "Get the currently selected GameObjects in the editor" )]
	public static Task<object> GetSelection() =>
		HandlerDispatcher.InvokeAsync( "editor.get_selection", null, EditorHandler.HandleGetSelection );

	[McpTool( "editor_select_object", Description = "Select a GameObject in the editor by ID" )]
	public static Task<object> SelectObject(
		[Description( "The ID of the GameObject to select" )] string objectId ) =>
		HandlerDispatcher.InvokeAsync( "editor.select", new { objectId }, EditorHandler.HandleSelectObject );

	[McpTool( "editor_undo", Description = "Undo the last editor action" )]
	public static Task<object> Undo() =>
		HandlerDispatcher.InvokeAsync( "editor.undo", null, EditorHandler.HandleUndo );

	[McpTool( "editor_redo", Description = "Redo the last undone editor action" )]
	public static Task<object> Redo() =>
		HandlerDispatcher.InvokeAsync( "editor.redo", null, EditorHandler.HandleRedo );

	[McpTool( "editor_save_scene", Description = "Save the current scene" )]
	public static Task<object> SaveScene() =>
		HandlerDispatcher.InvokeAsync( "editor.save_scene", null, EditorHandler.HandleSaveScene );

	[McpTool( "editor_take_screenshot",
		Description = "Render a CameraComponent in the active scene to a PNG and return the file path. Falls back with a hint to use console.run('screenshot_highres') if no camera is found." )]
	public static Task<object> TakeScreenshot(
		[Description( "Optional output width in pixels (default 1920)" )] int? width = null,
		[Description( "Optional output height in pixels (default 1080)" )] int? height = null,
		[Description( "Optional output path (absolute or project-relative)" )] string path = null ) =>
		HandlerDispatcher.InvokeAsync( "editor.screenshot", new { width, height, path }, EditorHandler.HandleScreenshot );

	[McpTool( "editor_play", Description = "Start playing the current scene in the editor" )]
	public static Task<object> Play() =>
		HandlerDispatcher.InvokeAsync( "editor.play", null, EditorHandler.HandlePlay );

	[McpTool( "editor_stop", Description = "Stop playing the current scene in the editor" )]
	public static Task<object> Stop() =>
		HandlerDispatcher.InvokeAsync( "editor.stop", null, EditorHandler.HandleStop );

	[McpTool( "editor_is_playing", Description = "Check whether the editor is currently in play mode" )]
	public static Task<object> IsPlaying() =>
		HandlerDispatcher.InvokeAsync( "editor.is_playing", null, EditorHandler.HandleIsPlaying );

	[McpTool( "editor_scene_info", Description = "Get information about the current scene (name, path, dirty state)" )]
	public static Task<object> SceneInfo() =>
		HandlerDispatcher.InvokeAsync( "editor.scene_info", null, EditorHandler.HandleSceneInfo );

	[McpTool( "editor_console_output", Description = "Get recent console output from the s&box editor" )]
	public static Task<object> ConsoleOutput() =>
		HandlerDispatcher.InvokeAsync( "editor.console_output", null, EditorHandler.HandleConsoleOutput );
}
