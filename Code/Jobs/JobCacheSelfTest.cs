#if DEBUG
namespace Sandbox;

internal sealed class JobCacheSelfTest : GameObjectSystem<JobCacheSelfTest>
{
	public JobCacheSelfTest( Scene scene ) : base( scene )
	{
		RunCacheTest();
	}

	private static int _factoryCalls;

	private static List<string> Factory()
	{
		_factoryCalls++;
		return new List<string> { "alpha", "beta" };
	}

	private static void RunCacheTest()
	{
		_factoryCalls = 0;

		var first = JobCache.Get( "test-key", 0.5f, Factory );
		Log.Assert( first.Count == 2, "JobCache.Get returned wrong count on first call" );
		Log.Assert( _factoryCalls == 1, "JobCache.Get should call factory on first miss" );

		var second = JobCache.Get( "test-key", 0.5f, Factory );
		Log.Assert( _factoryCalls == 1, "JobCache.Get should hit cache within TTL (no second factory call)" );
		Log.Assert( second.Count == 2, "JobCache cached value count mismatch" );

		JobCache.Invalidate( "test-key" );
		var third = JobCache.Get( "test-key", 0.5f, Factory );
		Log.Assert( _factoryCalls == 2, "JobCache.Get should call factory after Invalidate" );
		Log.Assert( third.Count == 2, "JobCache post-invalidate count mismatch" );

		JobCache.Clear();
		Log.Info( "[JobCache] self-test PASSED" );
	}
}
#endif
