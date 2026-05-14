using Sandbox;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SboxMcp;

/// <summary>
/// Legacy command envelope that the existing <see cref="Handlers"/> still consume.
/// Kept after the migration to in-editor MCP so handler bodies didn't have to change —
/// <see cref="Mcp.HandlerDispatcher"/> wraps typed tool arguments into this shape before
/// dispatching them on the main thread.
/// </summary>
public class HandlerRequest
{
	[JsonPropertyName( "id" )]
	public string Id { get; set; } = "";

	[JsonPropertyName( "command" )]
	public string Command { get; set; } = "";

	[JsonPropertyName( "params" )]
	public JsonElement? Params { get; set; }
}

/// <summary>
/// Response shape returned by handlers. Most handlers return a plain anonymous
/// object; <see cref="Mcp.HandlerDispatcher"/> serialises that into the MCP tool result.
/// Kept for handlers that still call <see cref="Ok"/> / <see cref="Fail"/> helpers.
/// </summary>
public class HandlerResponse
{
	[JsonPropertyName( "id" )]
	public string Id { get; set; } = "";

	[JsonPropertyName( "success" )]
	public bool Success { get; set; }

	[JsonPropertyName( "data" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public object Data { get; set; }

	[JsonPropertyName( "error" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public string Error { get; set; }

	public static HandlerResponse Ok( string id, object data = null ) =>
		new() { Id = id, Success = true, Data = data };

	public static HandlerResponse Fail( string id, string error ) =>
		new() { Id = id, Success = false, Error = error };
}
