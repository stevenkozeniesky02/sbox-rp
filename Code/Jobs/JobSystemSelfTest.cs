#if DEBUG
namespace Sandbox;

internal sealed class JobSystemSelfTest : GameObjectSystem<JobSystemSelfTest>
{
	private sealed class FakeJobSystem : IJobSystem
	{
		public string JobIdent => "test/fake.jobdef";
		public int BecameCount;
		public int LeftCount;
		public void OnBecameJob( PlayerData pd ) => BecameCount++;
		public void OnLeftJob( PlayerData pd ) => LeftCount++;
	}

	public JobSystemSelfTest( Scene scene ) : base( scene )
	{
		var fake = new FakeJobSystem();
		JobSystemRegistry.Register( fake );

		var found = JobSystemRegistry.Find( "test/fake.jobdef" );
		Assert.True( ReferenceEquals( fake, found ), "JobSystemRegistry.Find returned wrong instance" );

		var caseInsensitive = JobSystemRegistry.Find( "TEST/FAKE.JOBDEF" );
		Assert.True( ReferenceEquals( fake, caseInsensitive ), "JobSystemRegistry should be case-insensitive" );

		var missing = JobSystemRegistry.Find( "test/does-not-exist.jobdef" );
		Assert.True( missing is null, "Missing job should return null, not throw" );

		Log.Info( "[JobSystemRegistry] self-test PASSED" );
	}
}
#endif
