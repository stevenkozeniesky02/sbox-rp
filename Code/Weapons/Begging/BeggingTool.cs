/// <summary>
/// Hobo-only interaction weapon. Aim at a player, attack1 → server takes $10
/// from them and gives it to the Hobo. 10-second cooldown per Hobo.
/// Phase B pilot — uses BaseInteractionWeapon (cooldown + aim trace +
/// HUD overlay) and JobSystemRegistry hooks via HoboSystem.
/// </summary>
public sealed partial class BeggingTool : BaseInteractionWeapon
{
	public override float CooldownSeconds => 10f;
	public override float InteractionRange => 100f;

	private const int BegAmount = 10;

	public override bool IsTargetEligible( Player attacker, Player target )
	{
		if ( !base.IsTargetEligible( attacker, target ) ) return false;
		// Skip targets who can't afford it — otherwise the cooldown burns for nothing.
		if ( target.Money < BegAmount ) return false;
		return true;
	}

	protected override void OnInteract( Player attacker, Player target )
	{
		RpcServerBeg( attacker.GameObject, target.GameObject );
	}

	[Rpc.Host]
	private static void RpcServerBeg( GameObject attackerGo, GameObject targetGo )
	{
		var attacker = attackerGo?.GetComponent<Player>();
		var target = targetGo?.GetComponent<Player>();
		if ( !attacker.IsValid() || !target.IsValid() ) return;

		if ( !target.TryTakeMoney( BegAmount ) ) return;
		attacker.GiveMoney( BegAmount );

		Log.Info( $"[BeggingTool] {attacker.DisplayName} begged ${BegAmount} from {target.DisplayName}." );
	}
}
