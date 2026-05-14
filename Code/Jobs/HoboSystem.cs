/// <summary>
/// Per-job system for Hobo. Phase B pilot — minimal scope: logs lifecycle
/// so we can verify the JobSystem framework wires up correctly end-to-end.
/// Add real Hobo behavior here later (sermon-style chat, drift detection, etc).
/// </summary>
public sealed class HoboSystem : JobSystem<HoboSystem>
{
	public HoboSystem( Scene scene ) : base( scene ) { }

	public override string JobIdent => Player.HoboJobDefinitionPath;

	public override void OnBecameJob( PlayerData playerData )
	{
		Log.Info( $"[HoboSystem] {playerData.DisplayName} became Hobo." );
	}

	public override void OnLeftJob( PlayerData playerData )
	{
		Log.Info( $"[HoboSystem] {playerData.DisplayName} left Hobo." );
	}
}
