using Sandbox.UI;

/// <summary>
/// Owner-only money storage entity. Press E as owner to open the deposit/withdraw
/// popup. Non-owners get no interaction (future Phase D extension: lockpick crack).
/// Phase D.2 — the response to PickpocketWeapon pressure. Players move large
/// cash piles into safes to protect them from theft.
/// </summary>
public sealed class MoneySafe : Component, Component.IPressable
{
	public const string PrefabPath = "entities/misc/money_safe.prefab";
	public const int MaxOwnedPerPlayer = 2;
	public const int MaxStorage = 100_000;

	List<TextRenderer> Labels = new();

	[Property, Sync( SyncFlags.FromHost ), Change( nameof( OnStoredMoneyChanged ) )]
	public int StoredMoney { get; set; }

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		var player = GetPlayer( e.Source.GameObject );
		if ( !player.IsValid() ) return null;

		if ( !IsOwner( player ) )
		{
			var ownerName = GetOwnerName();
			return new IPressable.Tooltip( "Locked", "lock", $"{ownerName}'s safe." );
		}

		var description = StoredMoney > 0
			? $"Safe - ${StoredMoney:n0} stored"
			: "Safe - Empty";
		return new IPressable.Tooltip( "Open", "$", description );
	}

	bool IPressable.CanPress( IPressable.Event e )
	{
		var player = GetPlayer( e.Source.GameObject );
		return player.IsValid() && IsOwner( player );
	}

	bool IPressable.Press( IPressable.Event e )
	{
		var player = GetPlayer( e.Source.GameObject );
		if ( !player.IsValid() || !IsOwner( player ) ) return false;

		OpenSafePopup( player.Network.Owner );
		return true;
	}

	[Rpc.Host]
	public void RequestDeposit( int amount )
	{
		if ( amount <= 0 ) return;
		if ( Rpc.Caller is null ) return;

		var player = Player.FindForConnection( Rpc.Caller );
		if ( !player.IsValid() || !IsOwner( player ) ) return;

		// Cap by current safe space.
		var space = MaxStorage - StoredMoney;
		if ( space <= 0 ) return;
		amount = System.Math.Min( amount, space );

		if ( !player.TryTakeMoney( amount ) ) return;
		StoredMoney += amount;
		Notices.SendNotice( Rpc.Caller, "$", Color.Green, $"Deposited ${amount:n0} to safe.", 2 );
	}

	[Rpc.Host]
	public void RequestWithdraw( int amount )
	{
		if ( amount <= 0 ) return;
		if ( Rpc.Caller is null ) return;

		var player = Player.FindForConnection( Rpc.Caller );
		if ( !player.IsValid() || !IsOwner( player ) ) return;

		amount = System.Math.Min( amount, StoredMoney );
		if ( amount <= 0 ) return;

		StoredMoney -= amount;
		player.GiveMoney( amount );
		Notices.SendNotice( Rpc.Caller, "$", Color.Green, $"Withdrew ${amount:n0} from safe.", 2 );
	}

	protected override void OnStart()
	{
		CacheLabels();
		RefreshLabels();
	}

	void OnStoredMoneyChanged( int oldAmount, int newAmount )
	{
		RefreshLabels();
	}

	void OpenSafePopup( Connection caller )
	{
		if ( caller is null ) return;
		BroadcastOpenSafePopup( caller );
	}

	[Rpc.Broadcast]
	void BroadcastOpenSafePopup( Connection target )
	{
		if ( target != Connection.Local ) return;
		MoneySafePopup.Open( this );
	}

	void CacheLabels()
	{
		Labels = EnumerateSelfAndDescendants( GameObject )
			.SelectMany( x => x.Components.GetAll<TextRenderer>() )
			.Where( x => x.IsValid() )
			.ToList();
	}

	void RefreshLabels()
	{
		if ( Labels.Count == 0 ) CacheLabels();

		var labelText = StoredMoney > 0
			? $"${MoneyFormatter.FormatCompact( StoredMoney )}"
			: "$0";

		foreach ( var label in Labels )
		{
			if ( !label.IsValid() ) continue;
			var textScope = label.TextScope;
			if ( textScope.Text == labelText ) continue;
			textScope.Text = labelText;
			label.TextScope = textScope;
		}
	}

	public Connection GetOwnerConnection()
	{
		return GameObject.GetComponent<Ownable>()?.Owner;
	}

	public string GetOwnerName()
	{
		var connection = GetOwnerConnection();
		return connection?.DisplayName ?? "Someone";
	}

	bool IsOwner( Player player )
	{
		return player.IsValid() && GetOwnerConnection() == player.Network.Owner;
	}

	static Player GetPlayer( GameObject obj )
	{
		return obj?.Root?.GetComponent<Player>();
	}

	static IEnumerable<GameObject> EnumerateSelfAndDescendants( GameObject root )
	{
		yield return root;
		foreach ( var child in root.Children )
		{
			foreach ( var nested in EnumerateSelfAndDescendants( child ) )
				yield return nested;
		}
	}

	public static int CountOwned( Connection owner )
	{
		if ( owner is null || Game.ActiveScene is null ) return 0;
		return Game.ActiveScene.GetAllComponents<MoneySafe>()
			.Count( x => x.IsValid() && x.GameObject.GetComponent<Ownable>()?.Owner == owner );
	}

	public static bool TrySpawn( Player owner )
	{
		if ( !Networking.IsHost || !owner.IsValid() ) return false;

		var prefab = GameObject.GetPrefab( PrefabPath );
		if ( prefab is null ) return false;

		var eyes = owner.EyeTransform;
		var trace = Game.SceneTrace.Ray( eyes.Position, eyes.Position + eyes.Forward * 200f )
			.IgnoreGameObject( owner.GameObject )
			.WithoutTags( "player" )
			.Run();

		var up = trace.Normal.Length.AlmostEqual( 0f ) ? Vector3.Up : trace.Normal;
		var spawnTransform = new Transform( trace.EndPosition + up * 1f, owner.WorldRotation );

		var spawned = GameObject.Clone( prefab, new CloneConfig
		{
			Transform = spawnTransform,
			StartEnabled = false
		} );

		var safe = spawned.GetComponent<MoneySafe>( true );
		if ( !safe.IsValid() ) { spawned.Destroy(); return false; }

		safe.StoredMoney = 0;

		spawned.Tags.Add( "removable" );
		Ownable.Set( spawned, owner.Network.Owner );
		spawned.NetworkSpawn();
		return true;
	}
}
