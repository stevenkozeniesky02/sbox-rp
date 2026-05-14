/// <summary>
/// Auto-registers concrete <see cref="ServerEvent"/> subclasses with
/// <see cref="ServerEventRegistry"/> at scene start. Add new events
/// to the constructor as they're created.
/// </summary>
internal sealed class ServerEventBootstrap : GameObjectSystem<ServerEventBootstrap>
{
	public ServerEventBootstrap( Scene scene ) : base( scene )
	{
		// Clear first so hot-reload doesn't leave stale entries from a previous build.
		ServerEventRegistry.Clear();

		ServerEventRegistry.Register( new DoublePaycheckEvent() );
	}
}
