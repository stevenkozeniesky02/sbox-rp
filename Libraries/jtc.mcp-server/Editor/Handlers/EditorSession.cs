using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SboxMcp.Handlers;

/// <summary>
/// Reflective wrapper around <c>Editor.SceneEditorSession</c>. Direct compile-time
/// references to that type fail in the publish-wizard's library compile context
/// because Sandbox.Tools.dll isn't linked there — even though the type IS resolvable
/// when the editor mounts the addon at runtime.
///
/// All access goes through <see cref="ActiveObject"/> which lazily looks up
/// <c>Editor.SceneEditorSession.Active</c> via reflection. If the editor isn't
/// running (or the assembly isn't loaded), every property/method here returns a
/// safe default and methods become no-ops.
/// </summary>
public static class EditorSession
{
	private static readonly Lazy<Type> _sessionType = new( ResolveSessionType );

	private static Type ResolveSessionType()
	{
		// First try the well-known assembly-qualified name.
		var t = Type.GetType( "Editor.SceneEditorSession, Sandbox.Tools" );
		if ( t is not null ) return t;

		// Fall back to scanning all loaded assemblies — Sandbox.Tools.dll is
		// loaded but might be ResolveAssembly-named differently in some builds.
		foreach ( var asm in AppDomain.CurrentDomain.GetAssemblies() )
		{
			t = asm.GetType( "Editor.SceneEditorSession" );
			if ( t is not null ) return t;
		}
		return null;
	}

	/// <summary>The active <c>SceneEditorSession</c> as a boxed object, or null if no editor session.</summary>
	public static object ActiveObject
	{
		get
		{
			var t = _sessionType.Value;
			if ( t is null ) return null;
			return t.GetProperty( "Active", BindingFlags.Public | BindingFlags.Static )?.GetValue( null );
		}
	}

	public static Scene ActiveScene
	{
		get
		{
			var session = ActiveObject;
			if ( session is null ) return null;
			return session.GetType().GetProperty( "Scene" )?.GetValue( session ) as Scene;
		}
	}

	public static bool IsPlaying
	{
		get
		{
			var session = ActiveObject;
			if ( session is null ) return false;
			return session.GetType().GetProperty( "IsPlaying" )?.GetValue( session ) as bool? ?? false;
		}
	}

	public static bool HasUnsavedChanges
	{
		get
		{
			var session = ActiveObject;
			if ( session is null ) return false;
			return session.GetType().GetProperty( "HasUnsavedChanges" )?.GetValue( session ) as bool? ?? false;
		}
		set
		{
			var session = ActiveObject;
			if ( session is null ) return;
			var prop = session.GetType().GetProperty( "HasUnsavedChanges" );
			if ( prop is not null && prop.CanWrite ) prop.SetValue( session, value );
		}
	}

	public static IEnumerable<GameObject> SelectionGameObjects
	{
		get
		{
			var session = ActiveObject;
			if ( session is null ) return Enumerable.Empty<GameObject>();
			var sel = session.GetType().GetProperty( "Selection" )?.GetValue( session );
			if ( sel is not IEnumerable e ) return Enumerable.Empty<GameObject>();
			return e.OfType<GameObject>();
		}
	}

	public static void SelectionClear()
	{
		var session = ActiveObject;
		if ( session is null ) return;
		var sel = session.GetType().GetProperty( "Selection" )?.GetValue( session );
		sel?.GetType().GetMethod( "Clear", Type.EmptyTypes )?.Invoke( sel, null );
	}

	public static void SelectionAdd( object item )
	{
		var session = ActiveObject;
		if ( session is null ) return;
		var sel = session.GetType().GetProperty( "Selection" )?.GetValue( session );
		if ( sel is null ) return;
		var add = sel.GetType().GetMethods()
			.FirstOrDefault( m => m.Name == "Add" && m.GetParameters().Length == 1 );
		add?.Invoke( sel, new[] { item } );
	}

	public static void Undo()
	{
		var session = ActiveObject;
		var undoSys = session?.GetType().GetProperty( "UndoSystem" )?.GetValue( session );
		undoSys?.GetType().GetMethod( "Undo", Type.EmptyTypes )?.Invoke( undoSys, null );
	}

	public static void Redo()
	{
		var session = ActiveObject;
		var undoSys = session?.GetType().GetProperty( "UndoSystem" )?.GetValue( session );
		undoSys?.GetType().GetMethod( "Redo", Type.EmptyTypes )?.Invoke( undoSys, null );
	}

	public static void Save( bool saveAs = false )
	{
		var session = ActiveObject;
		session?.GetType().GetMethod( "Save", new[] { typeof( bool ) } )?.Invoke( session, new object[] { saveAs } );
	}

	public static void StopPlaying()
	{
		var session = ActiveObject;
		session?.GetType().GetMethod( "StopPlaying", Type.EmptyTypes )?.Invoke( session, null );
	}

	public static void SetPlaying( Scene scene )
	{
		var session = ActiveObject;
		session?.GetType().GetMethod( "SetPlaying", new[] { typeof( Scene ) } )?.Invoke( session, new object[] { scene } );
	}

	public static void OnEdited()
	{
		var session = ActiveObject;
		session?.GetType().GetMethod( "OnEdited",
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
			binder: null, types: Type.EmptyTypes, modifiers: null )
			?.Invoke( session, null );
	}

	public static void FullUndoSnapshot( string label )
	{
		var session = ActiveObject;
		session?.GetType().GetMethod( "FullUndoSnapshot", new[] { typeof( string ) } )
			?.Invoke( session, new object[] { label } );
	}

	public static bool CreateFromPath( string path )
	{
		var t = _sessionType.Value;
		if ( t is null ) return false;
		var m = t.GetMethod( "CreateFromPath", BindingFlags.Public | BindingFlags.Static );
		if ( m is null ) return false;
		m.Invoke( null, new object[] { path } );
		return true;
	}
}
