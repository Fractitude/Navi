using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A window an app can have: its main one (<see cref="Id"/> empty), or one of the others its manifest lists.
/// </summary>
public sealed class NaviWindowInfo
{
	/// <summary>
	/// Empty for the app's main window, otherwise the id its manifest gave.
	/// </summary>
	public string Id { get; init; } = "";

	/// <summary>
	/// Shown after the app's name in the title bar, or empty.
	/// </summary>
	public string Title { get; init; } = "";

	public bool CanMinimize { get; init; }
	public bool CanMaximize { get; init; }
	public bool CanResize { get; init; }

	/// <summary>
	/// Width over height that the window's content keeps when resized, or 0 for any shape. See
	/// <see cref="NaviAppResource.AspectRatio"/>.
	/// </summary>
	public float AspectRatio { get; init; }

	public TypeDescription PanelType { get; init; }

	/// <summary>
	/// Create a new instance of the window's panel.
	/// </summary>
	public Panel Create() => PanelType.Create<Panel>();

	/// <summary>
	/// 1 to 32 lowercase letters, digits, '-' or '_'. Keeps a window's key (<see cref="NaviWindow.KeyOf"/>) unambiguous.
	/// </summary>
	public static bool IsValidId( string id ) =>
		id is { Length: >= 1 and <= 32 } && id.All( x => x is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' );

	/// <summary>
	/// The aspect ratio a manifest gives as width and height, as width over height. 0 for none, or for one so extreme that
	/// it can't be a window.
	/// </summary>
	internal static float RatioOf( Vector2 size ) => size.x > 0f && size.y > 0f ? CleanRatio( size.x / size.y ) : 0f;

	/// <summary>
	/// <paramref name="ratio"/> if it is a usable width over height, between 1:10 and 10:1, else 0.
	/// </summary>
	/// <summary>
	/// <paramref name="size"/> as a minimum window content size: each side finite and between 0 and 4000.
	/// </summary>
	internal static Vector2 CleanMin( Vector2 size ) => new(
		float.IsFinite( size.x ) ? System.Math.Clamp( size.x, 0f, 4000f ) : 0f,
		float.IsFinite( size.y ) ? System.Math.Clamp( size.y, 0f, 4000f ) : 0f );

	/// <summary>
	/// The smallest size the window's content can be resized to, in pixels, or zero for the desktop's own minimum.
	/// </summary>
	public Vector2 MinSize { get; init; }

	internal static float CleanRatio( float ratio ) => float.IsFinite( ratio ) && ratio is >= 0.1f and <= 10f ? ratio : 0f;

	internal NaviWindowDefinitionSnapshot ToSnapshot() => new()
	{
		Id = Id,
		Title = Title,
		CanMinimize = CanMinimize,
		CanMaximize = CanMaximize,
		CanResize = CanResize,
		AspectRatio = AspectRatio,
		PanelType = PanelType.FullName,
	};
}

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

	/// <summary>
	/// Width over height that the main window's content keeps when resized, or 0 for any shape.
	/// </summary>
	public float AspectRatio { get; init; }

	/// <summary>
	/// The smallest size the main window's content can be resized to, or zero. See <see cref="NaviAppResource.MinSize"/>.
	/// </summary>
	public Vector2 MinSize { get; init; }

	/// <summary>
	/// Starts without a window, in the system tray. See <see cref="NaviAppResource.Headless"/>.
	/// </summary>
	public bool Headless { get; init; }

	/// <summary>
	/// The windows the app can open besides its main one. Empty for most apps.
	/// </summary>
	public IReadOnlyList<NaviWindowInfo> Windows { get; init; } = [];

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
	/// The kinds of file the app opens, as lowercase extensions without the dot. See <see cref="NaviAppResource.FileTypes"/>.
	/// Only the host uses them, to pick the app that opens a file, so they aren't sent to clients.
	/// </summary>
	public IReadOnlyList<string> FileTypes { get; init; } = [];

	/// <summary>
	/// The tags of the computers that can install the app, lowercase; empty for any computer. See
	/// <see cref="NaviAppResource.ComputerTags"/>. Only the host installs apps, so they aren't sent to clients.
	/// </summary>
	public IReadOnlyList<string> ComputerTags { get; init; } = [];

	/// <summary>
	/// The app's panel class.
	/// </summary>
	public TypeDescription PanelType { get; init; }

	/// <summary>
	/// The app's main window: the one <see cref="PanelType"/> draws. For a headless app it is never drawn, and its panel is
	/// the app's service.
	/// </summary>
	public NaviWindowInfo MainWindow => mainWindow ??= new()
	{
		CanMinimize = CanMinimize,
		CanMaximize = CanMaximize,
		CanResize = CanResize,
		AspectRatio = AspectRatio,
		MinSize = MinSize,
		PanelType = PanelType,
	};

	NaviWindowInfo mainWindow;

	/// <summary>
	/// The window with this id (empty for the main one), or null if the app has no such window.
	/// </summary>
	public NaviWindowInfo FindWindow( string id ) =>
		string.IsNullOrEmpty( id ) ? MainWindow : Windows.FirstOrDefault( x => x.Id == id );

	/// <summary>
	/// The resource that defines this app.
	/// </summary>
	public NaviAppResource Resource { get; init; }

	/// <summary>
	/// Create a new instance of the app's main panel.
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
			AspectRatio = NaviWindowInfo.RatioOf( resource.AspectRatio ),
			MinSize = NaviWindowInfo.CleanMin( resource.MinSize ),
			Headless = resource.Headless,
			Windows = ReadWindows( resource.Windows?.Select( x =>
				(x?.Id, x?.Title, x?.PanelType, x?.CanMinimize ?? true, x?.CanMaximize ?? true, x?.CanResize ?? true,
					NaviWindowInfo.RatioOf( x?.AspectRatio ?? default )) ) ),
			Shared = resource.Shared,
			IsPrepackaged = prepackaged,
			IsSystem = resource.IsSystem,
			Permissions = resource.Permissions?.Where( NaviPermissions.IsKnown ).Distinct().ToArray() ?? [],
			FileTypes = resource.FileTypes?.Where( x => !string.IsNullOrWhiteSpace( x ) )
				.Select( x => x.Trim().TrimStart( '.' ).ToLowerInvariant() )
				.Distinct()
				.ToArray() ?? [],
			PanelType = panelType,
			Resource = resource,
			ComputerTags = NaviComputer.CleanTags( resource.ComputerTags ),
		};
	}

	/// <summary>
	/// The windows a manifest lists. One with a bad or repeated id, or a panel class that isn't loaded, is left out.
	/// </summary>
	static NaviWindowInfo[] ReadWindows(
		IEnumerable<(string Id, string Title, string PanelType, bool CanMinimize, bool CanMaximize, bool CanResize, float AspectRatio)> windows )
	{
		var found = new List<NaviWindowInfo>();

		foreach ( var window in windows ?? [] )
		{
			if ( !NaviWindowInfo.IsValidId( window.Id ) || found.Any( x => x.Id == window.Id ) || NaviAppResource.FindPanelType( window.PanelType ) is not { } panel )
				continue;

			found.Add( new NaviWindowInfo
			{
				Id = window.Id,
				Title = Cap( window.Title, NaviWindow.MaxTitle ),
				CanMinimize = window.CanMinimize,
				CanMaximize = window.CanMaximize,
				CanResize = window.CanResize,
				AspectRatio = NaviWindowInfo.CleanRatio( window.AspectRatio ),
				PanelType = panel,
			} );
		}

		return found.ToArray();
	}

	internal static string Cap( string text, int length ) =>
		text is null ? "" : text.Length > length ? text[..length] : text;

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
		AspectRatio = AspectRatio,
		MinWidth = MinSize.x,
		MinHeight = MinSize.y,
		Headless = Headless,
		Windows = Windows.Select( x => x.ToSnapshot() ).ToList(),
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
		if ( string.IsNullOrEmpty( app?.Id ) || app.Id.Any( x => char.IsWhiteSpace( x ) || x == NaviWindow.KeySeparator ) || NaviStore.NormalizeIdent( app.Id ) is not null )
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
			AspectRatio = NaviWindowInfo.CleanRatio( app.AspectRatio ),
			MinSize = NaviWindowInfo.CleanMin( new Vector2( app.MinWidth, app.MinHeight ) ),
			Headless = app.Headless,
			Windows = ReadWindows( app.Windows?.Select( x =>
				(x?.Id, x?.Title, x?.PanelType, x?.CanMinimize ?? true, x?.CanMaximize ?? true, x?.CanResize ?? true, x?.AspectRatio ?? 0f) ) ),
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

	/// <summary>
	/// Id of the built-in Settings app, which the start menu has a button for.
	/// </summary>
	public const string SettingsApp = BuiltInFolder + "settings.napp";

	static HashSet<string> systemAppFolders = new( System.StringComparer.OrdinalIgnoreCase ) { BuiltInFolder };

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
	/// id because <see cref="NaviComputer.GrantedPermissions"/> separates the id from the permission with a space, nor
	/// for one with a '#', which <see cref="NaviWindow.KeyOf"/> separates the id from a window's id with.
	/// </summary>
	public static string IdOf( NaviAppResource resource )
	{
		if ( NaviStore.PackageOf( resource ) is { } ident )
			return ident;

		var path = resource?.ResourcePath?.Replace( '\\', '/' ).TrimStart( '/' ).ToLowerInvariant();
		if ( string.IsNullOrEmpty( path ) || path.Any( x => char.IsWhiteSpace( x ) || x == NaviWindow.KeySeparator ) )
			return null;

		return path;
	}

	/// <summary>
	/// Every app, sorted by name. Ids are unique, but if two resources ever share one, a core app wins, then a
	/// prepackaged one, so a Store app can never replace an app that ships with the game.
	/// </summary>
	public static IEnumerable<NaviAppInfo> All => ResourceLibrary.GetAll<NaviAppResource>()
		.Concat( NaviStore.DownloadedResources )
		.Distinct()
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
	static Dictionary<string, NaviAppInfo> hostApps = new();

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
