using Sandbox;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SboxMcp.Mcp;

// ----------------------------------------------------------------------------
// JSON-RPC 2.0 envelopes (MCP wire format)
// ----------------------------------------------------------------------------

public class JsonRpcRequest
{
	[JsonPropertyName( "jsonrpc" )] public string Version { get; set; } = "2.0";
	[JsonPropertyName( "id" )] public JsonElement? Id { get; set; }
	[JsonPropertyName( "method" )] public string Method { get; set; } = "";
	[JsonPropertyName( "params" )] public JsonElement? Params { get; set; }

	[JsonIgnore] public bool IsNotification => !Id.HasValue || Id.Value.ValueKind == JsonValueKind.Null || Id.Value.ValueKind == JsonValueKind.Undefined;
}

public class JsonRpcResponse
{
	[JsonPropertyName( "jsonrpc" )] public string Version { get; set; } = "2.0";
	[JsonPropertyName( "id" )] public JsonElement Id { get; set; }

	[JsonPropertyName( "result" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public object Result { get; set; }

	[JsonPropertyName( "error" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public JsonRpcError Error { get; set; }

	public static JsonRpcResponse Ok( JsonElement id, object result ) =>
		new() { Id = id, Result = result };

	public static JsonRpcResponse Fail( JsonElement id, int code, string message ) =>
		new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };
}

public class JsonRpcError
{
	[JsonPropertyName( "code" )] public int Code { get; set; }
	[JsonPropertyName( "message" )] public string Message { get; set; } = "";

	[JsonPropertyName( "data" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public object Data { get; set; }
}

// ----------------------------------------------------------------------------
// MCP-specific message shapes
// ----------------------------------------------------------------------------

public static class McpErrorCodes
{
	public const int ParseError = -32700;
	public const int InvalidRequest = -32600;
	public const int MethodNotFound = -32601;
	public const int InvalidParams = -32602;
	public const int InternalError = -32603;
}

public class InitializeResult
{
	[JsonPropertyName( "protocolVersion" )] public string ProtocolVersion { get; set; } = "2024-11-05";
	[JsonPropertyName( "capabilities" )] public ServerCapabilities Capabilities { get; set; } = new();
	[JsonPropertyName( "serverInfo" )] public ServerInfo ServerInfo { get; set; } = new();
}

public class ServerCapabilities
{
	[JsonPropertyName( "tools" )] public ToolsCapability Tools { get; set; } = new();
}

public class ToolsCapability
{
	[JsonPropertyName( "listChanged" )] public bool ListChanged { get; set; } = false;
}

public class ServerInfo
{
	[JsonPropertyName( "name" )] public string Name { get; set; } = "sbox-mcp";
	[JsonPropertyName( "version" )] public string Version { get; set; } = "2.0.0";
}

public class ToolListResult
{
	[JsonPropertyName( "tools" )] public List<ToolDescriptor> Tools { get; set; } = new();
}

public class ToolDescriptor
{
	[JsonPropertyName( "name" )] public string Name { get; set; } = "";
	[JsonPropertyName( "description" )] public string Description { get; set; } = "";
	[JsonPropertyName( "inputSchema" )] public object InputSchema { get; set; }
}

public class ToolCallParams
{
	[JsonPropertyName( "name" )] public string Name { get; set; } = "";
	[JsonPropertyName( "arguments" )] public JsonElement? Arguments { get; set; }
}

public class ToolCallResult
{
	[JsonPropertyName( "content" )] public List<ToolContent> Content { get; set; } = new();
	[JsonPropertyName( "isError" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public bool IsError { get; set; }
}

public class ToolContent
{
	[JsonPropertyName( "type" )] public string Type { get; set; } = "text";
	[JsonPropertyName( "text" )] public string Text { get; set; } = "";

	public static ToolContent FromText( string text ) => new() { Text = text ?? "" };
}
