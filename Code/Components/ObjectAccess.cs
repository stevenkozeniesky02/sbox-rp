public static class ObjectAccess
{
	public static bool CanModifyOwnedObject( GameObject go, Connection caller, bool allowWorld = false )
	{
		if ( !go.IsValid() || go.IsProxy )
			return false;

		if ( allowWorld && go.Tags.Has( "world" ) )
			return true;

		if ( caller is null )
			return false;

		if ( caller.IsHost || AdminSystem.Current?.HasAdminAccess( caller ) == true )
			return true;

		return TryGetOwnable( go, out var ownable ) && ownable.Owner == caller;
	}

	public static bool CanLocalPlayerModifyOwnedObject( GameObject go, bool allowWorld = false )
	{
		if ( !go.IsValid() )
			return false;

		if ( allowWorld && go.Tags.Has( "world" ) )
			return true;

		var player = Player.FindLocalPlayer();
		if ( !player.IsValid() )
			return false;

		if ( player.HasAdminAccess )
			return true;

		return TryGetOwnable( go, out var ownable ) && ownable.Owner == player.Network.Owner;
	}

	public static bool TryGetOwnable( GameObject go, out Ownable ownable )
	{
		ownable = null;

		if ( !go.IsValid() )
			return false;

		if ( go.Components.TryGet( out ownable ) )
			return true;

		var parentOwnable = go.GetComponentInParent<Ownable>( true );
		if ( parentOwnable.IsValid() )
		{
			ownable = parentOwnable;
			return true;
		}

		var networkRoot = go.FindNetworkRoot();
		if ( networkRoot.IsValid() && networkRoot.Components.TryGet( out ownable ) )
			return true;

		var root = go.Root;
		if ( root.IsValid() && root.Components.TryGet( out ownable ) )
			return true;

		return false;
	}
}
