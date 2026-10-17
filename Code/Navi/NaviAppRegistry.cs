using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// An app that exists in the game and can be installed, from a loaded <see cref="NaviAppResource"/>.
/// </summary>
public sealed class NaviAppInfo
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public string Icon { get; init; }
	public string Color { get; init; }
	public bool CanMinimize { get; init; }
	public bool CanMaximize { get; init; }
	public bool CanResize { get; init; }
	public bool CapturesInput { get; init; }

	/// <summary>
	/// Runs on the host, and other clients draw what it publishes. See <see cref="NaviAppResource.Shared"/>.
	/// </summary>
	public bool Shared { get; init; }

	/// <summary>
	/// Shipped with the game or a library: its manifest is in one of the <see cref="NaviAppRegistry.SystemAppFolders"/>, and
	/// it wasn't downloaded from the Store. Anything else, including a manifest from an unknown source, is not.
	/// </summary>
	public bool IsPrepackaged { get; init; }

	/// <summary>
	/// Asks for full system access (root). See <see cref="NaviAppResource.IsSystem"/>.
	/// </summary>
	public bool IsSystem { get; init; }

	/// <summary>
	/// A prepackaged system app: part of the OS. Always installed, can't be uninstalled, and has root without asking.
	/// </summary>
	public bool IsCore => IsSystem && IsPrepackaged;

	/// <summary>
	/// The permissions the app may ask for. See <see cref="NaviPermissions"/>.
	/// </summary>
	public IReadOnlyList<string> Permissions { get; init; }

	/// <summary>
	/// The app's panel class.
	/// </summary>
	public TypeDescription PanelType { get; init; }

	/// <summary>
	/// The resource that defines this app.
	/// </summary>
	public NaviAppResource Resource { get; init; }

	/// <summary>
	/// Create a new instance of the app's panel.
	/// </summary>
	public Panel Create() => PanelType.Create<Panel>();

	internal static NaviAppInfo FromResource( string id, NaviAppResource resource, TypeDescription panelType )
	{
		var prepackaged = NaviAppRegistry.IsPrepackaged( resource );

		return new()
		{
			Id = id,
			Name = resource.Title ?? id,
			Description = resource.Description ?? "",
			Icon = resource.Icon,
			Color = resource.Color,
			CanMinimize = resource.CanMinimize,
			CanMaximize = resource.CanMaximize,
			CanResize = resource.CanResize,
			CapturesInput = resource.CapturesInput,
			Shared = resource.Shared,
			IsPrepackaged = prepackaged,
			IsSystem = resource.IsSystem,
			Permissions = resource.Permissions?.Where( NaviPermissions.IsKnown ).Distinct().ToArray() ?? [],
			PanelType = panelType,
			Resource = resource,
		};
	}

	internal NaviAppSnapshot ToSnapshot() => new()
	{
		Id = Id,
		Name = Name,
		Description = Description,
		Icon = Icon,
		Color = Color,
		CanMinimize = CanMinimize,
		CanMaximize = CanMaximize,
		CanResize = CanResize,
		CapturesInput = CapturesInput,
		Shared = Shared,
		IsPrepackaged = IsPrepackaged,
		IsSystem = IsSystem,
		Permissions = Permissions.ToList(),
		PanelType = PanelType.FullName,
	};

	/// <summary>
	/// An app as the host describes it, for a client that doesn't have its manifest. Null if the panel class isn't in
	/// this client's code, or the id is a Store package's (those are downloaded, not taken from the host).
	/// </summary>
	internal static NaviAppInfo FromSnapshot( NaviAppSnapshot app )
	{
		if ( string.IsNullOrEmpty( app?.Id ) || app.Id.Any( char.IsWhiteSpace ) || NaviStore.NormalizeIdent( app.Id ) is not null )
			return null;

		var panelType = TypeLibrary.GetTypes<Panel>().FirstOrDefault( x => !x.IsAbstract && x.FullName == app.PanelType );
		if ( panelType is null )
			return null;

		return new()
		{
			Id = app.Id,
			Name = app.Name ?? app.Id,
			Description = app.Description ?? "",
			Icon = app.Icon,
			Color = app.Color,
			CanMinimize = app.CanMinimize,
			CanMaximize = app.CanMaximize,
			CanResize = app.CanResize,
			CapturesInput = app.CapturesInput,
			Shared = app.Shared,
			IsPrepackaged = app.IsPrepackaged,
			IsSystem = app.IsSystem,
			Permissions = app.Permissions?.Where( NaviPermissions.IsKnown ).Distinct().ToArray() ?? [],
			PanelType = panelType,
		};
	}
}

/// <summary>
/// Finds every app in the game: the loaded <see cref="NaviAppResource"/>s, from the game's and libraries' assets or downloaded
/// from the Store. Adding an app needs no registration code.
/// </summary>
public static class NaviAppRegistry
{
	/// <summary>
	/// The library's own apps, such as Packages and Calculator (<c>Assets/navi/apps/</c>).
	/// </summary>
	public const string BuiltInFolder = "navi/apps/";

	/// <summary>
	/// Id of the built-in Packages app, which the NAVI button opens.
	/// </summary>
	public const string PackagesApp = BuiltInFolder + "packages.napp";

	static readonly HashSet<string> systemAppFolders = new( System.StringComparer.OrdinalIgnoreCase ) { BuiltInFolder };

	/// <summary>
	/// Asset folders whose manifests count as shipped with the game or a library, so their system apps are part of the OS.
	/// <see cref="BuiltInFolder"/>, plus the folders games list in <see cref="NaviComputer.SystemAppFolders"/>.
	/// </summary>
	public static IReadOnlyCollection<string> SystemAppFolders => systemAppFolders;

	/// <summary>
	/// Trust manifests in an asset folder, e.g. "navi/system/", as shipped with the game. Ignored if it isn't a plain
	/// relative folder.
	/// </summary>
	public static void AddSystemAppFolder( string folder )
	{
		if ( NormalizeFolder( folder ) is { } normalized )
			systemAppFolders.Add( normalized );
	}

	/// <summary>
	/// Was this manifest shipped with the game or a library? Only if it is in one of the <see cref="SystemAppFolders"/> and
	/// wasn't downloaded from the Store. Anything else fails safe: it is a normal app, and a system one has to ask for root.
	/// </summary>
	public static bool IsPrepackaged( NaviAppResource resource )
	{
		if ( resource?.ResourcePath is not { } path || NaviStore.IsDownloaded( resource ) )
			return false;

		path = path.Replace( '\\', '/' ).TrimStart( '/' );
		if ( path.Split( '/' ).Contains( ".." ) )
			return false;

		return systemAppFolders.Any( x => path.StartsWith( x, System.StringComparison.OrdinalIgnoreCase ) );
	}

	static string NormalizeFolder( string folder )
	{
		var normalized = folder?.Trim().Replace( '\\', '/' ).Trim( '/' );
		if ( string.IsNullOrEmpty( normalized ) || normalized.Contains( ':' ) || normalized.Split( '/' ).Any( x => x is "" or "." or ".." ) )
			return null;

		return normalized + "/";
	}

	/// <summary>
	/// An app's id, which nothing in its manifest can choose. A Store app's is the package ident it was downloaded
	/// from, e.g. "fractitude.napp_counter": unique on sbox.game, and only the package's org can publish under it. Any
	/// other app's is its resource path, e.g. "navi/apps/packages.napp", so moving or renaming the manifest changes
	/// it. Idents have no '/' and paths do, so the two can't meet. Null for a path with whitespace, which can't be an
	/// id because <see cref="NaviComputer.GrantedPermissions"/> separates the id from the permission with a space.
	/// </summary>
	public static string IdOf( NaviAppResource resource )
	{
		if ( NaviStore.PackageOf( resource ) is { } ident )
			return ident;

		var path = resource?.ResourcePath?.Replace( '\\', '/' ).TrimStart( '/' ).ToLowerInvariant();
		if ( string.IsNullOrEmpty( path ) || path.Any( char.IsWhiteSpace ) )
			return null;

		return path;
	}

	/// <summary>
	/// Every app, sorted by name. Ids are unique, but if two resources ever share one, a core app wins, then a
	/// prepackaged one, so a Store app can never replace an app that ships with the game.
	/// </summary>
	public static IEnumerable<NaviAppInfo> All => ResourceLibrary.GetAll<NaviAppResource>()
		.Select( x => (Resource: x, Id: IdOf( x ), Panel: x.FindPanelType()) )
		.Where( x => x.Id is not null && x.Panel is not null )
		.Select( x => NaviAppInfo.FromResource( x.Id, x.Resource, x.Panel ) )
		.OrderBy( x => x.IsCore ? 0 : x.IsPrepackaged ? 1 : 2 )
		.Concat( hostApps.Values )
		.DistinctBy( x => x.Id )
		.OrderBy( x => x.Name );

	/// <summary>
	/// On a client: apps the host described (<see cref="NaviComputerSnapshot.Apps"/>) because this client has no
	/// manifest for them. A manifest of its own always wins.
	/// </summary>
	static readonly Dictionary<string, NaviAppInfo> hostApps = new();

	internal static void AddHostApps( IEnumerable<NaviAppSnapshot> apps )
	{
		foreach ( var app in apps ?? [] )
		{
			if ( app?.Id is { } id && !hostApps.ContainsKey( id ) && NaviAppInfo.FromSnapshot( app ) is { } info )
				hostApps[id] = info;
		}
	}

	/// <summary>
	/// The app with this id, or null.
	/// </summary>
	public static NaviAppInfo Find( string id ) => All.FirstOrDefault( x => x.Id == id );
}
