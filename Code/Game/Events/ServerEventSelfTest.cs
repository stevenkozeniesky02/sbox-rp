#if DEBUG
namespace Sandbox;

internal sealed class ServerEventSelfTest : GameObjectSystem<ServerEventSelfTest>
{
	public ServerEventSelfTest( Scene scene ) : base( scene )
	{
		// Bootstrap should have registered at least the DoublePaycheckEvent by now.
		Assert.True(
			ServerEventRegistry.All.Count >= 1,
			"ServerEventRegistry should contain at least one event after bootstrap"
		);

		var doublePaycheck = FindRegisteredOfType<DoublePaycheckEvent>();
		Assert.True( doublePaycheck is not null, "DoublePaycheckEvent should be registered" );
		Assert.True( doublePaycheck.DurationSeconds == 300f, "DoublePaycheckEvent duration should be 300s" );
		Assert.True( doublePaycheck.DisplayName == "Double Paycheck", "DoublePaycheckEvent DisplayName should be 'Double Paycheck'" );

		// Lifecycle: OnStart should set IsDoublePaycheck on the system; OnEnd should clear it.
		var system = ServerEventSystem.Current;
		if ( system is not null )
		{
			var wasActive = system.IsDoublePaycheck;
			doublePaycheck.OnStart( system );
			Assert.True( system.IsDoublePaycheck, "DoublePaycheckEvent.OnStart should set IsDoublePaycheck = true" );
			doublePaycheck.OnEnd( system );
			Assert.True( !system.IsDoublePaycheck, "DoublePaycheckEvent.OnEnd should clear IsDoublePaycheck" );
			system.IsDoublePaycheck = wasActive; // restore
		}

		Log.Info( "[ServerEventSystem] self-test PASSED" );
	}

	private static T FindRegisteredOfType<T>() where T : ServerEvent
	{
		foreach ( var ev in ServerEventRegistry.All )
			if ( ev is T t ) return t;
		return null;
	}
}
#endif
