using System;

namespace SboxMcp.Mcp;

/// <summary>
/// Decouples the Code/ assembly from the Editor/ assembly. Code/ holds the
/// runtime MCP machinery; Editor/ holds the dock UI. The dock cannot be
/// directly referenced from runtime code (Editor compiles AFTER Code, not
/// the other way round, and library publish strips the Editor assembly
/// entirely from the runtime context).
///
/// The dock subscribes to these static callbacks when it constructs;
/// runtime code invokes them via null-conditional. When no dock is loaded
/// (e.g. headless / library-only deploy), every call is a no-op.
/// </summary>
public static class EditorBridge
{
	/// <summary>Append a line to the dock's activity log. No-op if no dock.</summary>
	public static Action<string> LogMessage;

	/// <summary>Increment the dock's request counter.</summary>
	public static Action IncrementRequestCount;

	/// <summary>Return a one-line server status string for get_server_status.</summary>
	public static Func<string> GetStatus;
}
