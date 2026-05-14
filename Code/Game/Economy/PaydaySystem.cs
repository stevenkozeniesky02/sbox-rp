/// <summary>
/// Host-only periodic salary tick. Every <see cref="PaydayIntervalSeconds"/> seconds
/// each online player is paid their <see cref="JobDefinition.Salary"/> multiplied by
/// the active event multiplier.
///
/// Phase C / Task 1. ServerEventSystem (Task 4) will replace the hardcoded multiplier
/// by wiring into <see cref="ComputeMultiplierFor"/>.
/// </summary>
public sealed class PaydaySystem : GameObjectSystem<PaydaySystem>
{
	/// <summary>Payday interval in seconds. Tune via this constant.</summary>
	public const float PaydayIntervalSeconds = 180f;

	private TimeSince _sinceLastPayday;

	public PaydaySystem( Scene scene ) : base( scene )
	{
		_sinceLastPayday = 0f;
		Listen( Stage.StartFixedUpdate, 10, OnTick, "PaydaySystem" );
	}

	private void OnTick()
	{
		if ( !Networking.IsHost ) return;
		if ( (float)_sinceLastPayday < PaydayIntervalSeconds ) return;

		_sinceLastPayday = 0f;
		RunPayday();
	}

	private void RunPayday()
	{
		// TODO: when ServerEventSystem lands in Task 4, switch to:
		//   var isDouble = ServerEventSystem.Instance?.IsDoublePaycheck ?? false;
		var isDouble = false;
		var multiplier = ComputeMultiplierFor( isDouble );

		foreach ( var player in Scene.GetAll<Player>() )
		{
			if ( !player.IsValid() ) continue;
			if ( player.Network?.Owner is null ) continue;
			TryPayPlayer( player, multiplier );
		}

		BroadcastPaydayNotice( multiplier );
	}

	/// <summary>
	/// Pure helper — computes the payday salary multiplier from active event flags.
	/// Kept static so Task 2 can unit-test the math without instantiating this system.
	/// </summary>
	public static float ComputeMultiplierFor( bool isDoublePaycheck )
	{
		var mult = 1f;
		if ( isDoublePaycheck ) mult *= 2f;
		return mult;
	}

	private static void TryPayPlayer( Player player, float multiplier )
	{
		var jobDef = player.CurrentJobDefinition;
		if ( jobDef is null ) return;

		var amount = (int)System.MathF.Round( jobDef.Salary * multiplier );
		if ( amount <= 0 ) return;

		player.GiveMoney( amount );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void BroadcastPaydayNotice( float multiplier )
	{
		var local = Player.FindLocalPlayer();
		var jobDef = local?.CurrentJobDefinition;
		if ( jobDef is null ) return;

		var amount = (int)System.MathF.Round( jobDef.Salary * multiplier );
		var prefix = multiplier > 1.01f ? $"PAYDAY (x{multiplier:0.#}) " : "Payday ";
		Log.Info( $"{prefix}— received ${amount}" );
	}
}
