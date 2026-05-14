#if DEBUG
namespace Sandbox;

/// <summary>
/// Phase A probe — discover what's actually in the lockpick + duplicator addons
/// via runtime Package.Fetch (Cloud.Model failed at compile time because the
/// primary assets aren't Models). Logs results at scene start.
/// Delete this file once Phase D actually uses the assets.
/// </summary>
internal sealed class CloudAssetProbe : GameObjectSystem<CloudAssetProbe>
{
	public CloudAssetProbe( Scene scene ) : base( scene )
	{
		_ = TryFetchPackageAsync( "sanboxstore.realistic_lockpick" );
		_ = TryFetchPackageAsync( "null.duplicator" );
	}

	private static async System.Threading.Tasks.Task TryFetchPackageAsync( string ident )
	{
		try
		{
			var package = await Package.Fetch( ident, false );
			if ( package is null )
			{
				Log.Warning( $"[CloudAssetProbe] Package.Fetch(\"{ident}\") returned null." );
				return;
			}

			var primaryAsset = package.GetMeta( "PrimaryAsset", "(none)" );
			var packageType = package.PackageType.ToString();
			var assetType = package.GetMeta( "Type", "(unknown)" );
			Log.Info( $"[CloudAssetProbe] {ident}: type={packageType} assetType={assetType} primary={primaryAsset} rev={package.Revision}" );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[CloudAssetProbe] Package.Fetch(\"{ident}\") threw: {ex.GetType().Name}: {ex.Message}" );
		}
	}
}
#endif
