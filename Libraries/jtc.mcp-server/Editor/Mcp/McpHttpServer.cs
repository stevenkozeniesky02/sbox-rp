using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SboxMcp.Mcp;

/// <summary>
/// HTTP-based MCP server hosted inside the s&amp;box editor process.
///
/// Endpoints:
///   POST /mcp           — JSON-RPC request/response (Streamable HTTP transport)
///   GET  /sse           — SSE stream for legacy SSE transport (sends "endpoint" event,
///                         then keeps connection open while routing per-session POSTs)
///   POST /sse/message?session=...
///                       — SSE-paired POST endpoint (replies via the SSE stream)
///   GET  /              — Plain text health check
///
/// Lifecycle: process-wide singleton owned by <see cref="Instance"/>. The dock
/// widget *attaches* to the singleton; it doesn't own it. This keeps the listener
/// alive across hot-reloads (when s&amp;box's Roslyn pipeline rebuilds the addon
/// and reconstructs the dock) — otherwise each new dock would attempt to bind
/// the same port and lose to the still-running previous listener.
///
/// Use <see cref="GetOrStart"/> to obtain (and lazily start) the singleton.
/// Call <see cref="StopGlobal"/> to tear it down (e.g. user hits Stop in the dock,
/// or to restart on a different port — followed by another <see cref="GetOrStart"/>).
/// </summary>
public sealed class McpHttpServer : IDisposable
{
	public const int DefaultPort = 29015;

	private static McpHttpServer _instance;
	private static readonly object _instanceLock = new();

	public static McpHttpServer Instance
	{
		get { lock ( _instanceLock ) return _instance; }
	}

	private readonly int _port;
	private HttpListener _listener;
	private CancellationTokenSource _cts;
	private readonly Dictionary<string, SseSession> _sseSessions = new();
	private readonly object _sessionsLock = new();
	private int _requestCount;

	public bool IsListening => _listener?.IsListening == true;
	public int Port => _port;
	public string Url => $"http://localhost:{_port}";
	public int RequestCount => _requestCount;

	public int ClientCount
	{
		get { lock ( _sessionsLock ) return _sseSessions.Count; }
	}

	public event Action OnRequestCountChanged;
	public event Action OnClientCountChanged;

	private McpHttpServer( int port )
	{
		_port = port;
	}

	/// <summary>
	/// Return the existing singleton if it's listening; otherwise start a new one.
	/// Safe to call from many places — only one server is ever bound at a time.
	/// </summary>
	public static McpHttpServer GetOrStart( int port = DefaultPort )
	{
		lock ( _instanceLock )
		{
			if ( _instance is { IsListening: true } )
				return _instance;

			// Stale instance from a previous failed start? Wipe and retry.
			if ( _instance is not null )
			{
				try { _instance.StopInternal(); } catch { /* best-effort */ }
				_instance = null;
			}

			var server = new McpHttpServer( port );
			server.StartInternal();
			_instance = server;
			return server;
		}
	}

	/// <summary>
	/// Stop the singleton entirely. Used by the dock's Stop button and on full
	/// editor shutdown. Hot-reload should NOT call this — we want the listener
	/// to persist across file saves, otherwise clients lose their connection
	/// for a few seconds every time you save a tool definition.
	/// </summary>
	public static void StopGlobal()
	{
		lock ( _instanceLock )
		{
			if ( _instance is null ) return;
			try { _instance.StopInternal(); } catch { /* best-effort */ }
			_instance = null;
		}
	}

	private void StartInternal()
	{
		if ( _listener is not null ) return;

		_cts = new CancellationTokenSource();
		_listener = new HttpListener();
		_listener.Prefixes.Add( $"http://localhost:{_port}/" );

		try
		{
			_listener.Start();
		}
		catch ( HttpListenerException ex )
		{
			Log.Warning( $"[MCP] Could not bind to port {_port}: {ex.Message}" );
			try { _listener.Close(); } catch { /* best-effort */ }
			_listener = null;
			throw;
		}

		Log.Info( $"[MCP] HTTP listener up at {Url} — endpoints: POST /mcp, GET /sse" );
		_ = AcceptLoop( _cts.Token );
	}

	private void StopInternal()
	{
		_cts?.Cancel();

		lock ( _sessionsLock )
		{
			foreach ( var s in _sseSessions.Values ) s.Close();
			_sseSessions.Clear();
		}
		OnClientCountChanged?.Invoke();

		try { _listener?.Stop(); } catch { /* best-effort */ }
		try { _listener?.Close(); } catch { /* best-effort */ }
		_listener = null;

		_cts?.Dispose();
		_cts = null;

		Log.Info( "[MCP] Listener stopped." );
	}

	public void Dispose()
	{
		// Disposing a singleton instance is generally a no-op — the dock widget
		// shouldn't dispose the server when it goes away (hot-reload). Only
		// StopGlobal() actually tears it down.
	}

	private async Task AcceptLoop( CancellationToken ct )
	{
		while ( !ct.IsCancellationRequested && _listener is { IsListening: true } )
		{
			HttpListenerContext ctx;
			try
			{
				ctx = await _listener.GetContextAsync();
			}
			catch ( ObjectDisposedException ) { break; }
			catch ( HttpListenerException ) { break; }
			catch ( Exception ex )
			{
				Log.Warning( $"[MCP] Accept failed: {ex.Message}" );
				continue;
			}

			_ = Task.Run( () => HandleRequestAsync( ctx, ct ), CancellationToken.None );
		}
	}

	private async Task HandleRequestAsync( HttpListenerContext ctx, CancellationToken ct )
	{
		try
		{
			var path = ctx.Request.Url?.AbsolutePath ?? "/";
			var method = ctx.Request.HttpMethod;

			if ( method == "GET" && (path == "/" || path == "/health") )
			{
				await WriteText( ctx.Response, 200, "text/plain", "sbox-mcp ok\n" );
				return;
			}

			if ( method == "POST" && path == "/mcp" )
			{
				await HandleStreamableHttpAsync( ctx, ct );
				return;
			}

			if ( method == "GET" && path == "/sse" )
			{
				await HandleSseStreamAsync( ctx, ct );
				return;
			}

			if ( method == "POST" && path == "/sse/message" )
			{
				await HandleSsePostAsync( ctx, ct );
				return;
			}

			await WriteText( ctx.Response, 404, "text/plain", "Not found\n" );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP] Request handling crashed: {ex.Message}" );
			try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* best-effort */ }
		}
	}

	// ------------------------------------------------------------------
	// Streamable HTTP transport (POST /mcp, JSON in / JSON out)
	// ------------------------------------------------------------------

	private async Task HandleStreamableHttpAsync( HttpListenerContext ctx, CancellationToken ct )
	{
		string body;
		using ( var reader = new StreamReader( ctx.Request.InputStream, Encoding.UTF8 ) )
			body = await reader.ReadToEndAsync();

		var request = ParseRequest( body );
		if ( request is null )
		{
			var bytes = SerializeResponse( JsonRpcResponse.Fail( default, McpErrorCodes.ParseError, "Invalid JSON" ) );
			await WriteBytes( ctx.Response, 400, "application/json", bytes );
			return;
		}

		Interlocked.Increment( ref _requestCount );
		OnRequestCountChanged?.Invoke();

		var response = await DispatchAsync( request );
		if ( request.IsNotification )
		{
			ctx.Response.StatusCode = 204;
			ctx.Response.Close();
			return;
		}

		var payload = SerializeResponse( response );
		await WriteBytes( ctx.Response, 200, "application/json", payload );
	}

	// ------------------------------------------------------------------
	// SSE transport (legacy two-endpoint flow)
	// ------------------------------------------------------------------

	private async Task HandleSseStreamAsync( HttpListenerContext ctx, CancellationToken ct )
	{
		// HttpListener manages Connection automatically — setting it via Headers
		// throws. Same restriction applies to a few other "restricted" headers.
		ctx.Response.StatusCode = 200;
		ctx.Response.ContentType = "text/event-stream";
		ctx.Response.Headers["Cache-Control"] = "no-cache";
		ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
		ctx.Response.SendChunked = true;
		ctx.Response.KeepAlive = true;

		var sessionId = Guid.NewGuid().ToString( "N" );
		var session = new SseSession( sessionId, ctx.Response );

		lock ( _sessionsLock )
			_sseSessions[sessionId] = session;
		OnClientCountChanged?.Invoke();
		Log.Info( $"[MCP] SSE client connected (session {sessionId})" );

		try
		{
			// MCP SSE handshake: tell the client which URL to POST JSON-RPC to.
			// Use an absolute URL — some MCP clients reject relative paths here.
			var host = ctx.Request.Url?.Host ?? "localhost";
			var endpointPayload = $"http://{host}:{_port}/sse/message?session={sessionId}";
			await session.SendEventAsync( "endpoint", endpointPayload );

			// Hold the connection open until the client closes it or we shut down.
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource( ct, session.CancellationToken );
			while ( !linkedCts.IsCancellationRequested && session.IsAlive )
			{
				await Task.Delay( 5000, linkedCts.Token );
				try { await session.SendCommentAsync( "keepalive" ); }
				catch { break; }
			}
		}
		catch ( OperationCanceledException ) { /* shutdown */ }
		catch ( Exception ex )
		{
			Log.Info( $"[MCP] SSE session {sessionId} ended: {ex.Message}" );
		}
		finally
		{
			lock ( _sessionsLock ) _sseSessions.Remove( sessionId );
			OnClientCountChanged?.Invoke();
			session.Close();
		}
	}

	private async Task HandleSsePostAsync( HttpListenerContext ctx, CancellationToken ct )
	{
		var sessionId = ctx.Request.QueryString["session"];
		SseSession session;
		lock ( _sessionsLock )
			_sseSessions.TryGetValue( sessionId ?? "", out session );

		if ( session is null )
		{
			await WriteText( ctx.Response, 404, "text/plain", "Unknown SSE session.\n" );
			return;
		}

		string body;
		using ( var reader = new StreamReader( ctx.Request.InputStream, Encoding.UTF8 ) )
			body = await reader.ReadToEndAsync();

		var request = ParseRequest( body );
		if ( request is null )
		{
			await WriteText( ctx.Response, 400, "text/plain", "Invalid JSON\n" );
			return;
		}

		Interlocked.Increment( ref _requestCount );
		OnRequestCountChanged?.Invoke();

		// Acknowledge the POST immediately; the actual response goes via the SSE stream.
		ctx.Response.StatusCode = 202;
		ctx.Response.Close();

		var response = await DispatchAsync( request );
		if ( request.IsNotification ) return;

		try
		{
			var payload = Encoding.UTF8.GetString( SerializeResponse( response ) );
			await session.SendEventAsync( "message", payload );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP] Failed to send SSE response: {ex.Message}" );
		}
	}

	// ------------------------------------------------------------------
	// MCP method dispatch
	// ------------------------------------------------------------------

	private static async Task<JsonRpcResponse> DispatchAsync( JsonRpcRequest request )
	{
		var id = request.Id ?? default;

		try
		{
			switch ( request.Method )
			{
				case "initialize":
					return JsonRpcResponse.Ok( id, new InitializeResult() );

				case "notifications/initialized":
				case "notifications/cancelled":
					return JsonRpcResponse.Ok( id, new { } );

				case "ping":
					return JsonRpcResponse.Ok( id, new { } );

				case "tools/list":
					return JsonRpcResponse.Ok( id, new ToolListResult { Tools = new List<ToolDescriptor>( ToolRegistry.List() ) } );

				case "tools/call":
				{
					if ( !request.Params.HasValue )
						return JsonRpcResponse.Fail( id, McpErrorCodes.InvalidParams, "Missing params" );

					var p = JsonSerializer.Deserialize<ToolCallParams>( request.Params.Value.GetRawText() );
					if ( p is null || string.IsNullOrEmpty( p.Name ) )
						return JsonRpcResponse.Fail( id, McpErrorCodes.InvalidParams, "Missing tool name" );

					var result = await ToolRegistry.InvokeAsync( p.Name, p.Arguments );
					return JsonRpcResponse.Ok( id, result );
				}

				default:
					return JsonRpcResponse.Fail( id, McpErrorCodes.MethodNotFound, $"Method not found: {request.Method}" );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP] Dispatch error for '{request.Method}': {ex.Message}" );
			return JsonRpcResponse.Fail( id, McpErrorCodes.InternalError, ex.Message );
		}
	}

	// ------------------------------------------------------------------
	// HTTP helpers
	// ------------------------------------------------------------------

	private static JsonRpcRequest ParseRequest( string body )
	{
		try
		{
			return JsonSerializer.Deserialize<JsonRpcRequest>( body );
		}
		catch
		{
			return null;
		}
	}

	private static byte[] SerializeResponse( JsonRpcResponse response )
	{
		var json = JsonSerializer.Serialize( response );
		return Encoding.UTF8.GetBytes( json );
	}

	private static async Task WriteText( HttpListenerResponse resp, int status, string contentType, string body )
	{
		var bytes = Encoding.UTF8.GetBytes( body );
		await WriteBytes( resp, status, contentType, bytes );
	}

	private static async Task WriteBytes( HttpListenerResponse resp, int status, string contentType, byte[] bytes )
	{
		resp.StatusCode = status;
		resp.ContentType = contentType;
		resp.ContentLength64 = bytes.Length;
		resp.Headers["Access-Control-Allow-Origin"] = "*";
		await resp.OutputStream.WriteAsync( bytes, 0, bytes.Length );
		resp.Close();
	}
}

internal sealed class SseSession
{
	private readonly HttpListenerResponse _response;
	private readonly CancellationTokenSource _cts = new();
	private readonly SemaphoreSlim _writeLock = new( 1, 1 );
	private bool _closed;

	public string SessionId { get; }
	public bool IsAlive => !_closed;
	public CancellationToken CancellationToken => _cts.Token;

	public SseSession( string id, HttpListenerResponse response )
	{
		SessionId = id;
		_response = response;
	}

	public async Task SendEventAsync( string eventName, string data )
	{
		if ( _closed ) return;
		var payload = $"event: {eventName}\ndata: {data}\n\n";
		var bytes = Encoding.UTF8.GetBytes( payload );

		await _writeLock.WaitAsync();
		try
		{
			await _response.OutputStream.WriteAsync( bytes, 0, bytes.Length );
			await _response.OutputStream.FlushAsync();
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public async Task SendCommentAsync( string comment )
	{
		if ( _closed ) return;
		var payload = $": {comment}\n\n";
		var bytes = Encoding.UTF8.GetBytes( payload );

		await _writeLock.WaitAsync();
		try
		{
			await _response.OutputStream.WriteAsync( bytes, 0, bytes.Length );
			await _response.OutputStream.FlushAsync();
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public void Close()
	{
		if ( _closed ) return;
		_closed = true;
		_cts.Cancel();
		try { _response.OutputStream.Close(); } catch { /* best-effort */ }
		try { _response.Close(); } catch { /* best-effort */ }
	}
}
