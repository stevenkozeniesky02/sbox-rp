#if DEBUG
namespace Sandbox;

internal sealed class PaydaySystemSelfTest : GameObjectSystem<PaydaySystemSelfTest>
{
	public PaydaySystemSelfTest( Scene scene ) : base( scene )
	{
		Assert.True(
			PaydaySystem.ComputeMultiplierFor( isDoublePaycheck: false ) == 1f,
			"PaydaySystem multiplier should be 1× when no events are active"
		);

		Assert.True(
			PaydaySystem.ComputeMultiplierFor( isDoublePaycheck: true ) == 2f,
			"PaydaySystem multiplier should be 2× when DoublePaycheck is active"
		);

		Log.Info( "[PaydaySystem] self-test PASSED" );
	}
}
#endif
