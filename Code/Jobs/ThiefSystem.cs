/// <summary>
/// Per-job system for Thief. Phase D.1 — logs lifecycle for now;
/// hook real Thief behavior (pickpocket cooldown reductions on level-up,
/// stolen-money accumulator, etc.) when those exist.
/// </summary>
public sealed class ThiefSystem : JobSystem<ThiefSystem>
{
	public ThiefSystem( Scene scene ) : base( scene ) { }

	public override string JobIdent => Player.ThiefJobDefinitionPath;

	public override void OnBecameJob( PlayerData playerData )
	{
		Log.Info( $"[ThiefSystem] {playerData.DisplayName} became Thief." );
	}

	public override void OnLeftJob( PlayerData playerData )
	{
		Log.Info( $"[ThiefSystem] {playerData.DisplayName} left Thief." );
	}
}
