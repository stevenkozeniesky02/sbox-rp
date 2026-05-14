using Sandbox.Rendering;

/// <summary>
/// Abstract base for criminal / job interaction weapons that target another player,
/// perform a single action, then enter a per-Steam-ID cooldown.
///
/// Subclasses must implement:
///   float CooldownSeconds   — how long before the weapon can be used again
///   void  OnInteract(...)   — what actually happens to the target
///
/// The cooldown dictionary is shared across ALL subclasses, so a player on cooldown
/// cannot switch to a different interaction weapon to bypass the timer.
/// </summary>
public abstract partial class BaseInteractionWeapon : BaseCarryable
{
	/// <summary>
	/// Shared across all subclasses — keyed by SteamId (long).
	/// </summary>
	private static readonly Dictionary<long, RealTimeSince> _cooldowns = new();

	/// <summary>
	/// How many seconds the player must wait between uses.
	/// </summary>
	public abstract float CooldownSeconds { get; }

	/// <summary>
	/// Maximum reach for the aim trace, in Hammer units.
	/// </summary>
	public virtual float InteractionRange => 120f;

	/// <summary>
	/// Radius of the aim-trace sphere, in Hammer units.
	/// Wider than a pure ray so that near-misses still register.
	/// </summary>
	public virtual float InteractionTraceRadius => 8f;

	/// <summary>
	/// Returns true when <paramref name="target"/> is a valid candidate for
	/// <paramref name="attacker"/> to interact with.
	/// Default: target must be valid and must not be the attacker themselves.
	/// </summary>
	public virtual bool IsTargetEligible( Player attacker, Player target )
	{
		if ( !target.IsValid() ) return false;
		if ( target == attacker ) return false;
		return true;
	}

	/// <summary>
	/// Perform the actual interaction. Called on the owning client; use an
	/// [Rpc.Host] method inside the implementation for authoritative effects.
	/// </summary>
	protected abstract void OnInteract( Player attacker, Player target );

	// -------------------------------------------------------------------------
	// BaseCarryable overrides
	// -------------------------------------------------------------------------

	public override void OnControl( Player player )
	{
		base.OnControl( player );

		if ( !player.IsValid() ) return;
		if ( !Input.Pressed( "attack1" ) ) return;
		if ( IsOnCooldown( player.SteamId ) ) return;

		var target = FindAimedPlayer( player );
		if ( !IsTargetEligible( player, target ) ) return;

		StartCooldown( player.SteamId );
		OnInteract( player, target );
	}

	public override void DrawHud( HudPainter painter, Vector2 crosshair )
	{
		base.DrawHud( painter, crosshair );

		var local = Player.FindLocalPlayer();
		if ( !local.IsValid() ) return;

		var remaining = CooldownRemaining( local.SteamId );
		if ( remaining <= 0f ) return;

		var label = new TextRendering.Scope( $"{remaining:F1}s", Color.White, 14f );
		label.FontName = "Roboto";
		label.TextColor = Color.White.WithAlpha( 0.85f );
		label.FontWeight = 600;

		// Draw just below the crosshair.
		var rect = new Rect( crosshair.x - 40f, crosshair.y + 18f, 80f, 20f );
		painter.DrawText( label, rect, TextFlag.Center );
	}

	// -------------------------------------------------------------------------
	// Protected helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Fires a sphere-trace along the weapon's aim ray and returns the first
	/// <see cref="Player"/> hit within <see cref="InteractionRange"/>, or null.
	/// Mirrors the pattern used by <see cref="ArrestStickWeapon"/>.
	/// </summary>
	protected Player FindAimedPlayer( Player owner )
	{
		if ( !owner.IsValid() ) return null;

		var forward = owner.EyeTransform.Rotation.Forward;
		var ray = owner.EyeTransform.ForwardRay with { Forward = forward };

		var tr = Scene.Trace.Ray( ray, InteractionRange )
			.IgnoreGameObjectHierarchy( owner.GameObject )
			.WithoutTags( "playercontroller" )
			.Radius( InteractionTraceRadius )
			.UseHitboxes()
			.Run();

		if ( !tr.GameObject.IsValid() ) return null;

		var root = tr.GameObject.Root;
		if ( !root.IsValid() ) return null;

		return root.GetComponent<Player>();
	}

	/// <summary>
	/// Returns true while <paramref name="steamId"/> is still within their cooldown window.
	/// </summary>
	protected bool IsOnCooldown( long steamId )
	{
		if ( !_cooldowns.TryGetValue( steamId, out var since ) ) return false;
		return (float)since < CooldownSeconds;
	}

	/// <summary>
	/// Returns the seconds remaining on the cooldown, or 0 if not on cooldown.
	/// </summary>
	protected float CooldownRemaining( long steamId )
	{
		if ( !_cooldowns.TryGetValue( steamId, out var since ) ) return 0f;
		var remaining = CooldownSeconds - (float)since;
		return remaining > 0f ? remaining : 0f;
	}

	/// <summary>
	/// Stamps a fresh cooldown entry for <paramref name="steamId"/>.
	/// </summary>
	protected void StartCooldown( long steamId )
	{
		_cooldowns[steamId] = 0f;
	}
}
