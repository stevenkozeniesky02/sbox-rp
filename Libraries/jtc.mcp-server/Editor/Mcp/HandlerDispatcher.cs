using Sandbox;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace SboxMcp.Mcp;

/// <summary>
/// Adapts typed MCP tool arguments to the existing <see cref="HandlerRequest"/>-shaped
/// editor handlers, dispatching the call onto the s&amp;box main thread (required
/// for nearly all editor APIs).
///
/// Also drives the dock UI side-effects (command count, toast, activity log) so each
/// tool method stays a one-liner.
/// </summary>
public static class HandlerDispatcher
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	/// <summary>
	/// Invoke an existing <c>Handler( HandlerRequest )</c> by serialising the typed
	/// args into the legacy <c>JsonElement Params</c> envelope. Runs the handler on
	/// the main thread and surfaces the result through the dock UI.
	/// </summary>
	public static Task<object> InvokeAsync( string commandName, object args, Func<HandlerRequest, Task<object>> handler )
	{
		var request = BuildRequest( commandName, args );
		return RunOnMainThread( commandName, () => handler( request ) );
	}

	/// <summary>
	/// Run a handler on the main thread, with dock UI side-effects, but without
	/// the HandlerRequest envelope. Use when the handler doesn't need params.
	/// </summary>
	public static Task<object> RunOnMainThread( string commandName, Func<Task<object>> body )
	{
		// Dock UI updates MUST happen on the main thread — Qt widgets crash if
		// touched from background threads (and our exceptions used to be silently
		// swallowed by try/catch, leaving the activity log permanently empty after
		// the first request). Queue all UI side-effects onto MainThread.
		PostUi( () => EditorBridge.LogMessage?.Invoke( $"→ {commandName}" ) );

		var tcs = new TaskCompletionSource<object>();
		MainThread.Queue( async () =>
		{
			try
			{
				var result = await body();
				tcs.SetResult( result );
			}
			catch ( Exception ex )
			{
				tcs.SetException( ex );
			}
		} );

		return Finalise( commandName, tcs.Task );
	}

	private static async Task<object> Finalise( string commandName, Task<object> task )
	{
		try
		{
			var result = await task;
			PostUi( () =>
			{
				EditorBridge.IncrementRequestCount?.Invoke();
				EditorBridge.LogMessage?.Invoke( $"✓ {commandName}" );
			} );
			return result;
		}
		catch ( Exception ex )
		{
			PostUi( () => EditorBridge.LogMessage?.Invoke( $"✗ {commandName}: {ex.Message}" ) );
			throw;
		}
	}

	/// <summary>
	/// Queue a UI-touching action onto the s&amp;box editor main thread.
	/// Outer try/catch keeps a runaway tool call from poisoning the dispatcher.
	/// </summary>
	private static void PostUi( Action body )
	{
		try
		{
			MainThread.Queue( () => { try { body(); } catch { /* swallow inner UI errors */ } } );
		}
		catch { /* if even the queue blew up, nothing we can do */ }
	}

	private static HandlerRequest BuildRequest( string commandName, object args )
	{
		JsonElement? paramsEl = null;
		if ( args is not null )
		{
			var json = JsonSerializer.Serialize( args, JsonOpts );
			using var doc = JsonDocument.Parse( json );
			paramsEl = doc.RootElement.Clone();
		}

		return new HandlerRequest
		{
			Id = Guid.NewGuid().ToString(),
			Command = commandName,
			Params = paramsEl,
		};
	}
}
