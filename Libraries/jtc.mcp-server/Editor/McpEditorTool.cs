// Disambiguate: both Editor.* and Sandbox.UI.* define these widget types.
// We want the Editor (Qt-style) variants here.
using Editor;
using Sandbox;
using Label = Editor.Label;
using LineEdit = Editor.LineEdit;
using Button = Editor.Button;
using Widget = Editor.Widget;
using Layout = Editor.Layout;
using ScrollArea = Editor.ScrollArea;

using System;
using System.Collections.Generic;
using SboxMcp.Mcp;
using SboxMcp.Mcp.Docs;

namespace SboxMcp;

/// <summary>
/// s&amp;box Editor Dock entry point for the in-editor MCP server.
///
/// The dock owns a permanent <see cref="McpHttpServer"/> on port 29015 and the
/// <see cref="DocsService"/> singleton. While the editor is open, MCP clients
/// (Claude Code via SSE / Streamable HTTP) can connect to it and dispatch tool
/// calls directly — there's no separate dotnet sub-process and no extra IPC.
/// </summary>
[Dock( "Editor", "MCP Server", "smart_toy" )]
public class McpServerDock : Widget
{
	public static McpServerDock Current { get; private set; }

	public int CommandCount { get; set; }

	private McpHttpServer _server;
	private int _port = McpHttpServer.DefaultPort;
	private readonly List<string> _logEntries = new();
	private const int MaxLogEntries = 50;
	private DateTime _startedAt;

	private Label _statusDot;
	private Label _statusText;
	private Label _urlLabel;
	private Button _toggleButton;
	private Label _commandCountLabel;
	private Label _uptimeLabel;
	private Layout _logLayout;
	private LineEdit _portField;

	public McpServerDock( Widget parent ) : base( parent )
	{
		Current = this;

		ConsoleCapture.EnsureHooked();

		// Register with the runtime->editor bridge so HandlerDispatcher (in Code/)
		// can push log/counter updates without a direct compile-time reference.
		EditorBridge.LogMessage = AddLog;
		EditorBridge.IncrementRequestCount = () => CommandCount++;
		EditorBridge.GetStatus = GetStatus;

		MinimumSize = 200;
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 8;

		BuildHeader();
		BuildStats();
		BuildLog();

		StartServer();

		// Kick off docs/api crawling in the background so first MCP call is fast.
		try { DocsService.GetOrCreate(); } catch ( Exception ex ) { AddLog( $"Docs init error: {ex.Message}" ); }
	}

	public override void OnDestroyed()
	{
		if ( Current == this ) Current = null;

		// Clear bridge callbacks so post-destroy events don't try to touch us.
		if ( EditorBridge.LogMessage == (Action<string>)AddLog ) EditorBridge.LogMessage = null;
		EditorBridge.IncrementRequestCount = null;
		EditorBridge.GetStatus = null;

		// Unsubscribe so post-destroy events don't try to update a dead widget.
		if ( _server is not null )
			_server.OnClientCountChanged -= OnServerClientCountChanged;

		// Don't dispose the server itself — it's a process-wide singleton that
		// must survive hot-reloads. It tears down with the editor process.
		base.OnDestroyed();
	}

	public string GetStatus()
	{
		if ( _server is null ) return "MCP server not running.";
		return _server.IsListening
			? $"Listening on {_server.Url} | requests: {_server.RequestCount} | clients: {_server.ClientCount}"
			: "MCP server stopped.";
	}

	private void BuildHeader()
	{
		// Single header row: [● status text + url] [stretch] [Port: ___] [Stop/Start]
		var header = Layout.AddRow();
		header.Spacing = 8;

		_statusDot = new Label( "●", this );
		_statusDot.SetStyles( "color: #e05252; font-size: 18px;" );
		header.Add( _statusDot );

		var textCol = header.AddColumn();
		textCol.Spacing = 2;

		_statusText = new Label( "Stopped", this );
		textCol.Add( _statusText );

		_urlLabel = new Label( $"http://localhost:{_port}/mcp", this );
		_urlLabel.SetStyles( "color: #888888; font-size: 11px;" );
		textCol.Add( _urlLabel );

		header.AddStretchCell();

		var portLabel = new Label( "Port:", this );
		portLabel.SetStyles( "color: #aaaaaa; font-size: 11px;" );
		header.Add( portLabel );

		_portField = new LineEdit( this );
		_portField.Text = _port.ToString();
		_portField.MinimumWidth = 70;
		_portField.MaximumWidth = 80;
		_portField.TextEdited += v =>
		{
			if ( int.TryParse( v, out var p ) )
			{
				_port = p;
				_urlLabel.Text = $"http://localhost:{_port}/mcp";
			}
		};
		header.Add( _portField );

		_toggleButton = new Button( "Stop", this );
		_toggleButton.Icon = "stop";
		_toggleButton.ToolTip = "Stop or start the MCP listener. Edit the port first if you want to bind a different port.";
		_toggleButton.Clicked = () =>
		{
			if ( McpHttpServer.Instance is { IsListening: true } )
			{
				McpHttpServer.StopGlobal();
				_server = null;
				_startedAt = default;
				AddLog( "Stopped." );
			}
			else
			{
				StartServer();
			}
		};
		header.Add( _toggleButton );
	}

	private void BuildStats()
	{
		// Two equal-weight columns: Requests counter + Uptime.
		// SSE-stream counter dropped — Streamable HTTP (Claude's default) doesn't
		// hold persistent connections, the number was always 0 and confused users.
		var statsRow = Layout.AddRow();
		statsRow.Spacing = 12;

		var cmdCol = statsRow.AddColumn();
		cmdCol.Spacing = 2;
		cmdCol.Add( new Label( "Requests", this ) );
		_commandCountLabel = new Label( "0", this );
		_commandCountLabel.SetStyles( "font-size: 18px; font-weight: bold;" );
		cmdCol.Add( _commandCountLabel );

		var uptimeCol = statsRow.AddColumn();
		uptimeCol.Spacing = 2;
		uptimeCol.Add( new Label( "Uptime", this ) );
		_uptimeLabel = new Label( "--", this );
		_uptimeLabel.SetStyles( "font-size: 18px; font-weight: bold;" );
		uptimeCol.Add( _uptimeLabel );
	}

	private void BuildLog()
	{
		Layout.Add( new Label( "Log", this ) );

		// Single styled container holds the full scrollable log. Each entry is a
		// plain Label (transparent background) so the box looks unified.
		var box = new Widget( this );
		box.SetStyles( "background-color: #161616; border: 1px solid #2a2a2a; border-radius: 4px;" );
		box.Layout = Layout.Column();
		box.Layout.Margin = 0;

		var scroll = new ScrollArea( box );
		var canvas = new Widget( box );
		canvas.SetStyles( "background-color: transparent;" );
		canvas.Layout = Layout.Column();
		canvas.Layout.Spacing = 2;
		canvas.Layout.Margin = 8;
		scroll.Canvas = canvas;

		// AddLog rebuilds this layout (newest -> oldest, then a trailing stretch
		// cell to keep entries anchored to the top in an under-filled box).
		_logLayout = canvas.Layout;
		box.Layout.Add( scroll, 1 );

		Layout.Add( box, 1 );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( !_statusDot.IsValid() || !_statusText.IsValid() || !_toggleButton.IsValid() )
			return;

		// Always read from the singleton — survives our local _server reference being null
		// across hot-reloads / Stop+Start cycles.
		var live = McpHttpServer.Instance;
		var listening = live is { IsListening: true };
		var requests = live?.RequestCount ?? 0;

		_toggleButton.Text = listening ? "Stop" : "Start";
		_toggleButton.Icon = listening ? "stop" : "play_arrow";

		_statusDot.SetStyles( listening
			? "color: #52e052; font-size: 18px;"
			: "color: #e05252; font-size: 18px;" );

		_statusText.Text = listening
			? (requests > 0 ? $"Listening — {requests} request(s) handled" : "Listening — waiting for requests")
			: "Stopped";

		_commandCountLabel.Text = (requests + CommandCount).ToString();
		_portField.ReadOnly = listening;

		if ( listening && _startedAt != default )
		{
			var elapsed = DateTime.Now - _startedAt;
			_uptimeLabel.Text = elapsed.TotalHours >= 1
				? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m"
				: $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
		}
		else
		{
			_uptimeLabel.Text = "--";
		}
	}

	public void AddLog( string message )
	{
		var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
		_logEntries.Insert( 0, entry );
		if ( _logEntries.Count > MaxLogEntries )
			_logEntries.RemoveAt( _logEntries.Count - 1 );

		// Wipe and rebuild: Labels first (newest -> oldest), then a stretch cell
		// to keep them anchored to the top of the box when there are few entries.
		_logLayout.Clear( true );
		foreach ( var e in _logEntries )
		{
			var lbl = new Label( e, this );
			lbl.SetStyles( "background-color: transparent; border: none; padding: 0; font-size: 11px; color: #c0c0c0;" );
			_logLayout.Add( lbl );
		}
		_logLayout.AddStretchCell();
	}

	private void StartServer()
	{
		try
		{
			// Attach to the process-wide singleton — survives hot-reloads.
			_server = McpHttpServer.GetOrStart( _port );
			_startedAt = DateTime.Now;

			// Subscribe (idempotent re-subscribes are harmless — events fire once per real change).
			_server.OnClientCountChanged += OnServerClientCountChanged;

			AddLog( $"Listening on http://localhost:{_port}/mcp" );
			AddLog( $"Tools registered: {ToolRegistry.List().Count}" );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"[MCP] Could not start server: {ex.Message}" );
			AddLog( $"Error: {ex.Message}" );
			_server = null;
		}
	}

	private void OnServerClientCountChanged()
	{
		// Fired from the HTTP accept loop background thread — bounce onto the
		// main thread before touching widgets, otherwise AddLog crashes silently.
		var server = _server;
		if ( server is null ) return;
		var count = server.ClientCount;
		MainThread.Queue( () =>
		{
			try { AddLog( $"Streams: {count}" ); } catch { /* dock may be torn down */ }
		} );
	}

}
