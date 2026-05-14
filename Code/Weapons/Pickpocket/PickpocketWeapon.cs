using Sandbox.UI;

/// <summary>
/// Thief tool. Aim at another player, attack1 → server takes a percentage of
/// their cash (capped) and gives it to the thief. 3-minute cooldown (shared
/// across BaseInteractionWeapon, so it can't be combo'd with BeggingTool etc).
/// Phase D.1 — closes the criminal loop by adding theft pressure.
/// Pattern from MauveRP STUDIED #34.
/// </summary>
public sealed partial class PickpocketWeapon : BaseInteractionWeapon
{
	public override float CooldownSeconds => 180f;        // 3 minutes
	public override float InteractionRange => 80f;        // close — needs to be near

	private const float StealFraction = 0.10f;            // 10% of target's money
	private const int StealCap = 500;                     // capped at $500 per attempt
	private const int MinTargetMoney = 50;                // don't waste a cooldown on broke targets

	public override bool IsTargetEligible( Player attacker, Player target )
	{
		if ( !base.IsTargetEligible( attacker, target ) ) return false;
		if ( target.Money < MinTargetMoney ) return false;
		return true;
	}

	protected override void OnInteract( Player attacker, Player target )
	{
		RpcServerPickpocket( attacker.GameObject, target.GameObject );
	}

	[Rpc.Host]
	private static void RpcServerPickpocket( GameObject attackerGo, GameObject targetGo )
	{
		var attacker = attackerGo?.GetComponent<Player>();
		var target = targetGo?.GetComponent<Player>();
		if ( !attacker.IsValid() || !target.IsValid() ) return;

		var fraction = (int)System.MathF.Floor( target.Money * StealFraction );
		var amount = System.Math.Min( fraction, StealCap );
		if ( amount <= 0 ) return;

		if ( !target.TryTakeMoney( amount ) ) return;
		attacker.GiveMoney( amount );

		Log.Info( $"[Pickpocket] {attacker.DisplayName} stole ${amount} from {target.DisplayName}." );

		// Tell the victim they got pickpocketed (they should KNOW so it feels like a real theft mechanic)
		var victimConnection = target.Network?.Owner;
		if ( victimConnection is not null )
		{
			Notices.SendNotice( victimConnection, "warning", Color.Red, $"You were pickpocketed for ${amount}!", 3 );
		}
	}
}
