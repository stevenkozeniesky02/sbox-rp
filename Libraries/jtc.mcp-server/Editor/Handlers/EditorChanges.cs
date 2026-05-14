namespace SboxMcp.Handlers;

/// <summary>
/// Marks the editor's scene as having unsaved changes after programmatic
/// mutations. Without this, closing the editor doesn't prompt to save and
/// the LLM's work vanishes on next launch.
///
/// Routed through <see cref="EditorSession"/>'s reflective wrapper so the
/// addon compiles even in publish-wizard contexts where Sandbox.Tools.dll
/// isn't linked.
/// </summary>
public static class EditorChanges
{
	public static void MarkDirty( string label = "MCP edit" )
	{
		// FullUndoSnapshot pushes a named undo step AND flips HasUnsavedChanges
		// in most s&box builds — both at once.
		EditorSession.FullUndoSnapshot( label );

		// OnEdited is the canonical "scene was edited externally" hook (not
		// always public; reflectively invoked when accessible).
		EditorSession.OnEdited();

		// Belt-and-braces: force HasUnsavedChanges true via the wrapper's setter,
		// which uses reflection to handle private setters / backing fields.
		EditorSession.HasUnsavedChanges = true;
	}
}
