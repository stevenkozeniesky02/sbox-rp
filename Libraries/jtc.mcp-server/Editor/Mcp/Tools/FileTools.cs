using Sandbox;
using System.ComponentModel;
using System.Threading.Tasks;
using SboxMcp.Handlers;

namespace SboxMcp.Mcp.Tools;

[McpToolGroup]
public static class FileTools
{
	[McpTool( "file_read", Description = "Read a file from the s&box project" )]
	public static Task<object> ReadFile(
		[Description( "Path to the file relative to the project root" )] string path ) =>
		HandlerDispatcher.InvokeAsync( "file.read", new { path }, FileHandler.ReadFile );

	[McpTool( "file_write", Description = "Write content to a file in the s&box project (.cs files auto-route to code/)" )]
	public static Task<object> WriteFile(
		[Description( "Path to the file relative to the project root" )] string path,
		[Description( "Content to write to the file" )] string content ) =>
		HandlerDispatcher.InvokeAsync( "file.write", new { path, content }, FileHandler.WriteFile );

	[McpTool( "file_list", Description = "List files in the s&box project directory; supports glob patterns" )]
	public static Task<object> ListFiles(
		[Description( "Directory to list (default: project root)" )] string directory = null,
		[Description( "Optional glob pattern, e.g. *.cs" )] string pattern = null ) =>
		HandlerDispatcher.InvokeAsync( "file.list", new { directory, pattern }, FileHandler.ListFiles );

	[McpTool( "project_info", Description = "Get s&box project metadata and status" )]
	public static Task<object> ProjectInfo() =>
		HandlerDispatcher.InvokeAsync( "project.info", null, FileHandler.ProjectInfo );
}
