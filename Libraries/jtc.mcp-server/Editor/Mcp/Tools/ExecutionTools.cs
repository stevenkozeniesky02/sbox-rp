using Sandbox;
using System.ComponentModel;
using System.Threading.Tasks;
using SboxMcp.Handlers;

namespace SboxMcp.Mcp.Tools;

[McpToolGroup]
public static class ExecutionTools
{
	[McpTool( "execute_csharp",
		Description = "Evaluate a C# expression or statement block in the s&box editor context using Roslyn scripting. Returns the result or a structured error if the scripting assembly isn't loaded." )]
	public static Task<object> ExecuteCSharp(
		[Description( "C# expression or statement block to evaluate" )] string code,
		[Description( "Optional comma-separated namespaces to import (e.g. 'Sandbox.UI, Editor.Inspector')" )] string imports = null ) =>
		HandlerDispatcher.InvokeAsync( "execute.csharp", new { code, imports }, ExecutionHandler.ExecuteCSharp );

	[McpTool( "console_run", Description = "Run a console command in the s&box console" )]
	public static Task<object> ConsoleRun(
		[Description( "Console command to run" )] string command ) =>
		HandlerDispatcher.InvokeAsync( "console.run", new { command }, ExecutionHandler.RunConsoleCommand );

	[McpTool( "get_server_status", Description = "Report the in-editor MCP server status: listener URL, request count, connected clients" )]
	public static Task<object> GetServerStatus()
	{
		var status = EditorBridge.GetStatus?.Invoke() ?? "MCP Server dock not open in this editor instance.";
		return Task.FromResult<object>( new { status } );
	}
}
