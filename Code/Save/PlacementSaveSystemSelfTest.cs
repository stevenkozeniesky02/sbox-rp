#if DEBUG
namespace Sandbox;

internal sealed class PlacementSaveSystemSelfTest : GameObjectSystem<PlacementSaveSystemSelfTest>
{
	public PlacementSaveSystemSelfTest( Scene scene ) : base( scene )
	{
		RunRoundTripTest();
	}

	private sealed class FakeData
	{
		public int Value { get; set; }
		public string Tag { get; set; }
	}

	private sealed class FakeSaveSystem : PlacementSaveSystem<FakeData>
	{
		public FakeSaveSystem() : base( "selftest/fakesave.json" ) { }
		protected override void SpawnFromSave( FakeData data ) { /* no-op for round-trip */ }
	}

	private static void RunRoundTripTest()
	{
		var fake = new FakeSaveSystem();
		var sample = new List<FakeData>
		{
			new() { Value = 7, Tag = "alpha" },
			new() { Value = 42, Tag = "beta" }
		};

		fake.Save( sample );
		var loaded = fake.Load();

		Assert.True( loaded.Count == 2, "PlacementSaveSystem round-trip count mismatch" );
		Assert.True( loaded[0].Value == 7, "PlacementSaveSystem round-trip Value mismatch" );
		Assert.True( loaded[1].Tag == "beta", "PlacementSaveSystem round-trip Tag mismatch" );

		FileSystem.Data.DeleteFile( "selftest/fakesave.json" );
		Log.Info( "[PlacementSaveSystem] self-test PASSED" );
	}
}
#endif
