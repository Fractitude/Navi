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
	/// Allow only Store apps from the <see cref="OfficialOrgs"/> and the <see cref="AllowedOrgs"/>, on every computer in
	/// the game. Others don't show in the Store and are never downloaded, so their code never loads: not from a save, nor
	/// on a client whose host has them. Off by default. A game sets it in its own code, before its computers start (e.g.
	/// in a <see cref="GameObjectSystem"/>'s constructor), so it holds on the host and on every client.
	/// </summary>
	public static bool OfficialOnly { get; set; }

	/// <summary>
	/// sbox.game organisations whose apps are allowed besides the official ones when <see cref="OfficialOnly"/> is on.
	/// They don't get the official mark. Ignored while it is off, when every org is allowed.
	/// </summary>
	public static HashSet<string> AllowedOrgs { get; } = new( System.StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The orgs whose apps are allowed, or null if every org is.
	/// </summary>
	static string[] AllowedOrgIdents => OfficialOnly
		? OfficialOrgs.Concat( AllowedOrgs )
			.Select( x => x?.Trim().ToLowerInvariant() )
			.Where( x => !string.IsNullOrEmpty( x ) && System.Text.RegularExpressions.Regex.IsMatch( x, @"^[a-z0-9_\-]+$" ) )
			.Distinct()
			.ToArray()
		: null;

	/// <summary>
	/// May this package be downloaded? Its org is the first part of its ident, which only the org's members can publish
	/// under. See <see cref="OfficialOnly"/>.
	/// </summary>
	public static bool IsAllowed( string packageIdent )
	{
		if ( NormalizeIdent( packageIdent ) is not { } ident )
			return false;

		return AllowedOrgIdents is not { } orgs || orgs.Contains( ident.Split( '.' )[0] );
	}

	/// <summary>
	/// Resource paths of the apps downloaded from the Store, to the package each came from. They are never trusted as
	/// shipped with the game, even in one of the <see cref="NaviAppRegistry.SystemAppFolders"/>.
	/// </summary>
	static readonly Dictionary<string, string> downloaded = new( System.StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Was this app downloaded from the Store? A false doesn't mean it shipped with the game: see <see cref="NaviAppRegistry.IsPrepackaged"/>.
	/// </summary>
	public static bool IsDownloaded( NaviAppResource resource ) => PackageOf( resource ) is not null;

	/// <summary>
	/// The ident of the Store package this app was downloaded from, e.g. "fractitude.napp_counter", or null if it
	/// wasn't.
	/// </summary>
	public static string PackageOf( NaviAppResource resource ) =>
		resource?.ResourcePath is { } path && downloaded.TryGetValue( path, out var ident ) ? ident : null;

	const string IdentPattern = @"^[a-z0-9_\-]+\.[a-z0-9_\-]+$";

	/// <summary>
	/// A package ident as an app id: "org.name", lowercase, without a version. Null if it isn't a package ident, so it
	/// can't pass for a resource path or another package.
	/// </summary>
	public static string NormalizeIdent( string ident )
	{
		var normalized = ident?.Split( '#' )[0].Trim().ToLowerInvariant();
		if ( normalized is null || !System.Text.RegularExpressions.Regex.IsMatch( normalized, IdentPattern ) )
			return null;

		return normalized;
	}

	/// <summary>
	/// Store apps fetched by a call that doesn't ask for a size.
	/// </summary>
	public const int PageSize = 20;

	/// <summary>
	/// One page of published apps matching <paramref name="text"/>, and how many match in all. The page leaves out
	/// the first <paramref name="skip"/> matches and holds at most <paramref name="pageSize"/> of them. Only apps that
	/// are allowed (<see cref="OfficialOnly"/>) are listed.
	/// </summary>
	public static async Task<(List<Package> Packages, int Total)> SearchAsync( string text = "", int skip = 0, int pageSize = PageSize )
	{
		skip = System.Math.Max( skip, 0 );
		pageSize = System.Math.Max( pageSize, 0 );

		if ( AllowedOrgIdents is not { } orgs )
			return await FindAsync( text, skip, pageSize );

		// The backend filters by one org: with two org filters only the last counts, and "org:a|b" filters nothing. So
		// one org is searched there, page and all, and several are searched one by one, enough of each for the page,
		// and paged here. Either way the backend returns no other org's apps.
		if ( orgs.Length <= 1 )
			return orgs.Length == 0 ? (new(), 0) : await FindAsync( $"org:{orgs[0]} {text}", skip, pageSize );

		var packages = new List<Package>();
		var total = 0;
		foreach ( var org in orgs )
		{
			var (found, count) = await FindAsync( $"org:{org} {text}", 0, skip + pageSize );
			packages.AddRange( found );
			total += count;
		}

		return (packages.Skip( skip ).Take( pageSize ).ToList(), System.Math.Max( total, packages.Count ));
	}

	static async Task<(List<Package> Packages, int Total)> FindAsync( string text, int skip, int pageSize )
	{
		// Package.FindAsync takes the page size before the offset, and doesn't search for a page past the last one.
		var result = await Package.FindAsync( $"type:{PackageType} {text}".Trim(), pageSize, skip );

		// The backend ignores a type filter until a package of that type exists, so check the type here too. The org is
		// checked too, in case the backend let another through.
		var packages = result?.Packages?.Where( x => x.TypeName == PackageType && IsAllowed( x.FullIdent ) ).ToList() ?? new();

		// Never report fewer matches than the page holds, in case the filter let some other type through.
		return (packages, System.Math.Max( result?.TotalCount ?? 0, skip + packages.Count ));
	}

	/// <summary>
	/// Download a published app and load its code. Returns the app, whose id is the package ident, or null if the package
	/// isn't a working Navi app.
	/// </summary>
	public static async Task<NaviAppInfo> DownloadAsync( string packageIdent )
	{
		if ( NormalizeIdent( packageIdent ) is not { } ident )
		{
			Log.Warning( $"NaviStore: '{packageIdent}' is not a package ident" );
			return null;
		}

		if ( !IsAllowed( ident ) )
		{
			Log.Warning( $"NaviStore: '{ident}' isn't from an allowed org; this game allows only official apps" );
			return null;
		}

		var resource = await Cloud.Load<NaviAppResource>( ident, withCode: true );
		if ( resource?.ResourcePath is null )
		{
			Log.Warning( $"NaviStore: '{ident}' is not a Navi app" );
			return null;
		}

		downloaded[resource.ResourcePath] = ident;

		var app = NaviAppRegistry.Find( ident );
		if ( app is null )
		{
			Log.Warning( $"NaviStore: '{packageIdent}' has no panel class '{resource.PanelType}' in its code" );
		}

		return app;
	}
}
