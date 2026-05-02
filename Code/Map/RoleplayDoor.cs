using Facepunch;
using Sandbox.UI;
using System.Text.Json.Serialization;

public sealed class RoleplayDoor : Component
{
	const string GovernmentJobCategory = "Government";
	const float LockpickCooldownSeconds = 3.0f;
	public const bool DefaultAllowGovernmentLockpick = true;
	public const int MaxOwnedPerPlayer = 6;

	[RequireComponent] public Door Door { get; set; }

	[Property, Sync( SyncFlags.FromHost )]
	public int PurchasePrice { get; set; } = 500;

	[Property, Sync( SyncFlags.FromHost )]
	public bool IsGovernment { get; set; }

	[Property, Sync( SyncFlags.FromHost )]
	public bool IsPublic { get; set; }

	[Property, Sync( SyncFlags.FromHost ), Group( "Lockpick" )]
	public bool AllowGovernmentLockpick { get; set; } = DefaultAllowGovernmentLockpick;

	[Property, Group( "Sound" )]
	public SoundEvent LockSound { get; set; } = new( "entities/door/sounds/door_lock.sound" );

	[Property, Group( "Sound" )]
	public SoundEvent UnlockSound { get; set; } = new( "entities/door/sounds/door_unlock.sound" );

	[Sync( SyncFlags.FromHost )]
	private Guid _ownerId { get; set; }

	private TimeUntil _lockpickCooldown;

	[Property, ReadOnly, JsonIgnore]
	public Connection Owner
	{
		get => Connection.All.FirstOrDefault( x => x.Id == _ownerId );
		private set => _ownerId = value?.Id ?? Guid.Empty;
	}

	public bool IsOwned => Owner is not null;
	public bool CanBePurchased => !IsGovernment && !IsPublic && !IsOwned;
	bool CanLockpickGovernmentDoor => AllowGovernmentLockpick || DefaultAllowGovernmentLockpick;

	protected override void OnStart()
	{
		if ( !Networking.IsHost || !Door.IsValid() )
			return;

		if ( IsGovernment )
		{
			Door.IsLocked = true;
			return;
		}

		if ( IsPublic )
		{
			Door.IsLocked = false;
		}
	}

	public bool IsOwnedBy( Connection connection )
	{
		if ( connection is null )
			return false;

		return Owner == connection;
	}

	public static int CountOwnedBy( Connection owner )
	{
		if ( owner is null || Game.ActiveScene is null )
			return 0;

		return Game.ActiveScene.GetAllComponents<RoleplayDoor>()
			.Count( door => door.IsValid() && door.IsOwnedBy( owner ) );
	}

	public bool CanPress( IPressable.Event e, Door.DoorState state )
	{
		if ( state is not (Door.DoorState.Open or Door.DoorState.Closed) )
			return false;

		var player = GetPlayer( e );
		if ( !player.IsValid() )
			return false;

		if ( CanBePurchased )
			return true;

		if ( CanUseDoor( player ) )
			return true;

		return CanAttemptLockpick( player );
	}

	public bool Press( IPressable.Event e, Door.DoorState state )
	{
		if ( !CanPress( e, state ) )
			return false;

		var player = GetPlayer( e );
		if ( !player.IsValid() )
			return false;

		if ( CanBePurchased )
			return false;

		if ( !CanUseDoor( player ) )
			return false;

		Door.Toggle( e.Source.GameObject );
		return true;
	}

	public bool TryBuy( Player buyer, out string error )
	{
		error = null;

		if ( !Networking.IsHost || !buyer.IsValid() )
		{
			error = "Invalid door purchase request.";
			return false;
		}

		if ( IsGovernment )
		{
			error = "Government doors can't be bought.";
			return false;
		}

		if ( IsPublic )
		{
			error = "Public doors can't be bought.";
			return false;
		}

		if ( IsOwned )
		{
			error = "This door is already owned.";
			return false;
		}

		if ( CountOwnedBy( buyer.Network.Owner ) >= MaxOwnedPerPlayer )
		{
			error = $"You already own {MaxOwnedPerPlayer} doors.";
			return false;
		}

		var price = Math.Max( 0, PurchasePrice );
		if ( !buyer.TryTakeMoney( price ) )
		{
			error = "You don't have enough money.";
			return false;
		}

		Owner = buyer.Network.Owner;
		Door.IsLocked = false;
		return true;
	}

	public bool TrySell( Player seller, out int refund, out string error )
	{
		refund = 0;
		error = null;

		if ( !Networking.IsHost || !seller.IsValid() )
		{
			error = "Invalid door sale request.";
			return false;
		}

		if ( IsGovernment )
		{
			error = "Government doors can't be sold.";
			return false;
		}

		if ( IsPublic )
		{
			error = "Public doors can't be sold.";
			return false;
		}

		if ( !IsOwnedBy( seller.Network.Owner ) )
		{
			error = IsOwned ? "Only the door owner can sell it." : "Buy this door first.";
			return false;
		}

		refund = Math.Max( 0, PurchasePrice );

		if ( Door.State is Door.DoorState.Open or Door.DoorState.Opening )
		{
			Door.CloseFromServer( seller.GameObject );
		}

		Door.IsLocked = false;
		Owner = null;

		if ( refund > 0 )
		{
			seller.GiveMoney( refund );
		}

		return true;
	}

	public bool TrySetLocked( Player actor, bool locked, out string error )
	{
		error = null;

		if ( !Networking.IsHost || !actor.IsValid() )
		{
			error = "Invalid door lock request.";
			return false;
		}

		if ( IsPublic )
		{
			error = "Public doors can't be locked.";
			return false;
		}

		if ( IsGovernment )
		{
			if ( !CanAccessGovernmentDoor( actor ) )
			{
				error = "Only government jobs can do that.";
				return false;
			}
		}
		else if ( !IsOwnedBy( actor.Network.Owner ) )
		{
			error = IsOwned ? "Only the door owner can do that." : "Buy this door first.";
			return false;
		}

		if ( Door.IsLocked == locked )
		{
			error = locked ? "Door is already locked." : "Door is already unlocked.";
			return false;
		}

		if ( locked && Door.State is Door.DoorState.Open or Door.DoorState.Opening )
		{
			Door.CloseFromServer( actor.GameObject );
		}

		Door.IsLocked = locked;

		var sound = locked ? LockSound : UnlockSound;
		if ( sound is not null )
		{
			PlayLockSound( sound );
		}

		return true;
	}

	public bool TryLockpick( Player actor, out string error )
	{
		error = null;

		if ( !CanAttemptLockpick( actor, out error ) )
			return false;

		if ( _lockpickCooldown > 0.0f )
		{
			error = "Lockpick is cooling down.";
			return false;
		}

		// Unlock the door
		Door.IsLocked = false;

		// Mark that this player just lockpicked this door (bypasses permission checks on toggle)
		actor.SetDoorLockpickBypass( this );

		// Toggle the door state directly
		if ( Door.State is Door.DoorState.Closed or Door.DoorState.Closing )
		{
			Door.OpenFromServer( actor.GameObject );
		}
		else if ( Door.State is Door.DoorState.Open or Door.DoorState.Opening )
		{
			Door.CloseFromServer( actor.GameObject );
		}

		_lockpickCooldown = LockpickCooldownSeconds;

		if ( UnlockSound is not null )
		{
			PlayLockSound( UnlockSound );
		}

		if ( Owner is { } ownerConnection && ownerConnection != actor.Network.Owner )
		{
			Notices.SendNotice( ownerConnection, "warning", Color.Orange, $"{actor.DisplayName} lockpicked your door.", 3 );
		}

		return true;
	}

	public bool CanAttemptLockpick( Player actor )
	{
		return CanAttemptLockpick( actor, out _ );
	}

	public bool CanAttemptLockpick( Player actor, out string error )
	{
		error = null;

		if ( !actor.IsValid() )
		{
			error = "Invalid lockpick request.";
			return false;
		}

		if ( !actor.IsThief )
		{
			error = "Only the thief can use lockpick.";
			return false;
		}

		if ( IsPublic )
		{
			error = "Public doors can't be lockpicked.";
			return false;
		}

		if ( IsGovernment )
		{
			if ( !CanLockpickGovernmentDoor )
			{
				error = "Government doors can't be lockpicked.";
				return false;
			}

			return true;
		}

		if ( !IsOwned )
		{
			error = "Only owned doors can be lockpicked.";
			return false;
		}

		if ( IsOwnedBy( actor.Network.Owner ) )
		{
			error = "This is your own door.";
			return false;
		}

		return true;
	}

	public IPressable.Tooltip BuildTooltip( Player player, Door.DoorState state )
	{
		var isOwner = player.IsValid() && IsOwnedBy( player.Network.Owner );
		var title = Door.IsLocked ? "Locked" : state == Door.DoorState.Open ? "Close" : "Open";
		var icon = Door.IsLocked ? "lock" : "door_front";

		if ( IsPublic )
		{
			title = state == Door.DoorState.Open ? "Close" : "Open";
			return new IPressable.Tooltip( title, "door_front", "Public door" );
		}

		if ( IsGovernment )
		{
			if ( player.IsValid() && player.IsThief && CanLockpickGovernmentDoor && Door.IsLocked )
			{
				return new IPressable.Tooltip( "Lockpick", "key", "Hold attack" );
			}

			if ( CanAccessGovernmentDoor( player ) )
			{
				return new IPressable.Tooltip( title, icon, "Use keys" );
			}

			return new IPressable.Tooltip( "Government Door", "lock", "Government only" );
		}

		if ( !IsOwned )
		{
			var price = Math.Max( 0, PurchasePrice );
			var progress = player.IsValid() ? player.GetDoorPurchaseProgress( this ) : 0.0f;
			var description = $"E to open, Hold E to buy {price:n0}$";

			if ( progress > 0.0f )
			{
				var percent = (int)MathF.Round( progress * 100.0f );
				description = $"{BuildProgressBar( progress )} {percent}%";
			}

			return new IPressable.Tooltip( "Buy Door", "$", description );
		}

		if ( player.IsValid() && player.IsThief && !IsOwnedBy( player.Network.Owner ) )
		{
			if ( Door.IsLocked || player.IsDoorLockpickHolding && player.DoorLockpickTarget == this )
			{
				return new IPressable.Tooltip( "Lockpick", "key", "Hold attack" );
			}
		}

		if ( isOwner )
		{
			var sellPrice = Math.Max( 0, PurchasePrice );
			return new IPressable.Tooltip( title, icon, $"Use keys. R sell ${sellPrice:n0}" );
		}

		var ownerName = Owner?.DisplayName ?? "Unknown";
		var lockState = Door.IsLocked ? "locked" : "unlocked";
		return new IPressable.Tooltip( title, icon, $"{ownerName} - {lockState}" );
	}

	public bool CanControlLock( Player player )
	{
		if ( !player.IsValid() )
			return false;

		if ( IsGovernment )
			return CanAccessGovernmentDoor( player );

		if ( IsPublic )
			return false;

		return IsOwnedBy( player.Network.Owner );
	}

	public bool CanUseDoor( Player player )
	{
		if ( !player.IsValid() )
			return false;

		if ( Door.IsLocked )
			return IsPublic;

		if ( IsPublic )
			return true;

		if ( IsGovernment )
		{
			if ( CanAccessGovernmentDoor( player ) )
				return true;

			return false;
		}

		if ( !IsOwned )
			return true;

		return true;
	}

	public bool TryPlayLockpickAttemptSound( Player actor )
	{
		if ( !Networking.IsHost || !CanAttemptLockpick( actor ) )
			return false;

		if ( actor.IsValid() )
		{
			actor.PlayDoorLockpickAttemptSound();
		}

		return true;
	}

	static Player GetPlayer( IPressable.Event e )
	{
		if ( !e.Source.IsValid() )
			return null;

		return e.Source.GameObject.Root.GetComponent<Player>();
	}

	static bool CanAccessGovernmentDoor( Player player )
	{
		if ( !player.IsValid() )
			return false;

		var category = player.CurrentJobDefinition?.Category?.Trim();
		return string.Equals( category, GovernmentJobCategory, StringComparison.OrdinalIgnoreCase );
	}

	[Rpc.Broadcast]
	void PlayLockSound( SoundEvent sound )
	{
		if ( sound is null )
			return;

		GameObject.PlaySound( sound );
	}

	static string BuildProgressBar( float progress )
	{
		const int segments = 10;
		var clamped = Math.Clamp( progress, 0.0f, 1.0f );
		var filled = (int)MathF.Round( clamped * segments );
		return $"[{new string( '#', filled )}{new string( '-', segments - filled )}]";
	}
}
