using Sandbox;
using System.ComponentModel;
using System.Threading.Tasks;
using SboxMcp.Handlers;

namespace SboxMcp.Mcp.Tools;

[McpToolGroup]
public static class AssetTools
{
	[McpTool( "asset_search", Description = "Search the s&box asset library" )]
	public static Task<object> SearchAssets(
		[Description( "Search query string" )] string query,
		[Description( "Optional asset type filter" )] string type = null,
		[Description( "Optional maximum number of results" )] string amount = null ) =>
		HandlerDispatcher.InvokeAsync( "asset.search", new { query, type, amount }, AssetHandler.SearchAssets );

	[McpTool( "asset_fetch", Description = "Fetch detailed information about an asset by its identifier" )]
	public static Task<object> FetchAsset(
		[Description( "The asset identifier" )] string ident ) =>
		HandlerDispatcher.InvokeAsync( "asset.fetch", new { ident }, AssetHandler.FetchAsset );

	[McpTool( "asset_mount", Description = "Mount an asset into the current project; auto-adds to .sbproj PackageReferences" )]
	public static Task<object> MountAsset(
		[Description( "The asset identifier to mount" )] string ident ) =>
		HandlerDispatcher.InvokeAsync( "asset.mount", new { ident }, AssetHandler.MountAsset );

	[McpTool( "asset_browse_local", Description = "Browse local project assets by directory and/or file extension" )]
	public static Task<object> BrowseLocalAssets(
		[Description( "Optional directory path to browse" )] string directory = null,
		[Description( "Optional file extension filter (e.g. vmat, vmdl)" )] string extension = null ) =>
		HandlerDispatcher.InvokeAsync( "asset.browse_local", new { directory, extension }, AssetHandler.BrowseLocalAssets );
}
