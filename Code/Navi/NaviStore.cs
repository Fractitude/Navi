using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Finds and downloads Navi apps that players published to sbox.game as <see cref="NaviAppResource"/>s.
/// </summary>
public static class NaviStore
{
	/// <summary>
	/// Package type of published apps. It is the file extension of <see cref="NaviAppResource"/>.
	/// </summary>
	public const string PackageType = "napp";

	/// <summary>
	/// sbox.game organisations whose apps the Store marks as official. Only an org's members can publish under its
	/// ident, so the mark can't be faked by other publishers.
	/// </summary>
	public static HashSet<string> OfficialOrgs { get; } = new( System.StringComparer.OrdinalIgnoreCase ) { "fractitude" };

	/// <summary>
	/// Was this package published by one of the <see cref="OfficialOrgs"/>?
	/// </summary>
	public static bool IsOfficial( Package package ) => package?.Org?.Ident is { } org && OfficialOrgs.Contains( org );

	/// <summary>
	/// Resource paths of the apps downloaded from the Store. Every other app shipped with the game or a library.
	/// </summary>
	static readonly HashSet<string> downloaded = new( System.StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Was this app downloaded from the Store, rather than shipped with the game or a library?
	/// </summary>
	public static bool IsDownloaded( NaviAppResource resource ) => resource?.ResourcePath is { } path && downloaded.Contains( path );

	/// <summary>
	/// Store apps fetched by a call that doesn't ask for a size.
	/// </summary>
	public const int PageSize = 20;

	/// <summary>
	/// One page of published apps matching <paramref name="text"/>, and how many match in all. The page leaves out
	/// the first <paramref name="skip"/> matches and holds at most <paramref name="pageSize"/> of them.
	/// </summary>
	public static async Task<(List<Package> Packages, int Total)> SearchAsync( string text = "", int skip = 0, int pageSize = PageSize )
	{
		// Package.FindAsync takes the page size before the offset, and doesn't search for a page past the last one.
		var result = await Package.FindAsync( $"type:{PackageType} {text}".Trim(), pageSize, skip );

		// The backend ignores a type filter until a package of that type exists, so check the type here too.
		var packages = result?.Packages?.Where( x => x.TypeName == PackageType ).ToList() ?? new();

		// Never report fewer matches than the page holds, in case the filter let some other type through.
		return (packages, System.Math.Max( result?.TotalCount ?? 0, skip + packages.Count ));
	}

	/// <summary>
	/// Download a published app and load its code. Returns the app, or null if the package isn't a working Navi app.
	/// </summary>
	public static async Task<NaviAppInfo> DownloadAsync( string packageIdent )
	{
		var resource = await Cloud.Load<NaviAppResource>( packageIdent, withCode: true );
		if ( resource is null )
		{
			Log.Warning( $"NaviStore: '{packageIdent}' is not a Navi app" );
			return null;
		}

		downloaded.Add( resource.ResourcePath );

		var app = NaviAppRegistry.Find( resource.AppId );
		if ( app is null )
		{
			Log.Warning( $"NaviStore: '{packageIdent}' has no panel class '{resource.PanelType}' in its code" );
		}

		return app;
	}
}
