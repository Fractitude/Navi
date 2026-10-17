using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// An app that is open in a window.
/// </summary>
public sealed class NaviProcess
{
	public NaviAppInfo App { get; init; }

	/// <summary>
	/// Window position on the screen, in panel pixels.
	/// </summary>
	public Vector2 Position { get; set; }

	/// <summary>
	/// Window size in panel pixels, or null to fit the app's content. Set when the window is resized.
	/// </summary>
	public Vector2? Size { get; set; }

	/// <summary>
	/// Hidden, with only its taskbar icon left. The app keeps running.
	/// </summary>
	public bool IsMinimized { get; set; }

	/// <summary>
	/// Fills the whole screen above the taskbar.
	/// </summary>
	public bool IsMaximized { get; set; }

	/// <summary>
	/// Stacking order. The highest is in front.
	/// </summary>
	public int ZIndex { get; set; }

	/// <summary>
	/// The permissions the app has (<see cref="NaviPermissions"/>): what the player granted it, which the computer
	/// remembers per app (<see cref="NaviComputer.GrantedPermissions"/>), or all it declares for a system app.
	/// </summary>
	public HashSet<string> Permissions { get; } = new();

	/// <summary>
	/// Full system access: every permission. Only system apps the player accepted, or that are part of the OS.
	/// </summary>
	public bool IsRoot { get; init; }

	public bool HasPermission( string permission ) => IsRoot || Permissions.Contains( permission );
}

/// <summary>
/// A permission dialog on the desktop. Either a review of all the permissions an app declares, which sets exactly what it
/// has (before its first launch, or from its taskbar menu), or a running app asking for more (<c>Navi.RequestPermissions</c>).
/// </summary>
public sealed class NaviPermissionRequest
{
	public NaviAppInfo App { get; init; }

	/// <summary>
	/// The permissions in the dialog.
	/// </summary>
	public IReadOnlyList<string> Permissions { get; init; }

	/// <summary>
	/// The permissions ticked when the dialog opens.
	/// </summary>
	public IReadOnlyList<string> Ticked { get; init; }

	/// <summary>
	/// A review of everything the app declares: the ticked ones are what it has afterwards. Otherwise the app asked
	/// for more, and the ticked ones are added.
	/// </summary>
	public bool IsReview { get; init; }

	/// <summary>
	/// A system app asking for full system access (root), as it is installed or before it opens. Accept or cancel; there is
	/// nothing to tick.
	/// </summary>
	public bool IsRoot { get; init; }

	/// <summary>
	/// Open the app once the player answers. Cancelling doesn't open it.
	/// </summary>
	public bool Launch { get; init; }

	internal System.Threading.Tasks.TaskCompletionSource<string[]> Answer { get; } = new();
}

/// <summary>
/// The operating system of a Navi computer: which apps are installed and which are open.
/// Gameplay code can call <see cref="Install"/> / <see cref="Uninstall"/> directly.
/// </summary>
public sealed class NaviComputer : Component
{
	/// <summary>
	/// Ids of the installed apps. System apps are always installed and don't need to be listed.
	/// </summary>
	[Property] public List<string> InstalledApps { get; set; } = new() { "calculator" };

	/// <summary>
	/// Store packages (sbox.game idents, e.g. "org.notes") installed on this computer. They are downloaded when the game starts.
	/// </summary>
	[Property] public List<string> InstalledPackages { get; set; } = new();

	/// <summary>
	/// Permissions the player granted, as "app-id permission" pairs. System apps have theirs without being listed.
	/// </summary>
	[Property] public List<string> GrantedPermissions { get; set; } = new();

	/// <summary>
	/// Apps whose declared permissions the player has reviewed. An app that declares permissions shows the review on its
	/// first launch, before it runs.
	/// </summary>
	[Property] public List<string> ReviewedApps { get; set; } = new();

	/// <summary>
	/// Store system apps the player accepted full system access (root) for. Prepackaged system apps have it without this.
	/// </summary>
	[Property] public List<string> RootApps { get; set; } = new();

	/// <summary>
	/// Asset folders of this game whose app manifests ship with it, so their system apps are part of the OS: always installed,
	/// root without asking. Manifests anywhere else (except the library's own <see cref="NaviAppRegistry.BuiltInFolder"/>)
	/// are normal apps, and a system one asks the player for root. Folders are trusted from when the computer is enabled.
	/// </summary>
	[Property] public List<string> SystemAppFolders { get; set; } = new() { "navi/system/" };

	/// <summary>
	/// The display scales the player can pick, like Windows' display scaling. 1 is the panel's own size.
	/// </summary>
	public static IReadOnlyList<float> DisplayScales { get; } = [0.5f, 0.75f, 1f, 1.25f, 1.5f];

	/// <summary>
	/// How big the desktop and its windows are drawn: one of <see cref="DisplayScales"/>. Bigger shows less on the screen.
	/// <see cref="NaviDesktop"/> applies it to its panel, on top of the scale the panel started with.
	/// </summary>
	[Property] public float DisplayScale { get; set; } = 1f;

	/// <summary>
	/// Set <see cref="DisplayScale"/>. Returns false if the scale isn't one of <see cref="DisplayScales"/>.
	/// </summary>
	public bool SetDisplayScale( float scale )
	{
		if ( !DisplayScales.Any( x => System.MathF.Abs( x - scale ) < 0.001f ) )
			return false;

		DisplayScale = scale;
		Version++;
		return true;
	}

	/// <summary>
	/// Goes up on every change, so UI can rebuild when it changes.
	/// </summary>
	public int Version { get; private set; }

	readonly List<NaviProcess> running = new();
	int topZIndex;

	readonly List<NaviPermissionRequest> permissionRequests = new();

	readonly HashSet<string> downloading = new();

	/// <summary>
	/// App id to the Store package it came from.
	/// </summary>
	readonly Dictionary<string, string> appPackages = new();

	protected override void OnEnabled()
	{
		foreach ( var folder in SystemAppFolders ?? [] )
			NaviAppRegistry.AddSystemAppFolder( folder );
	}

	protected override void OnStart()
	{
		_ = DownloadInstalledPackagesAsync();
	}

	/// <summary>
	/// Download the Store apps in <see cref="InstalledPackages"/>, so their code is loaded and they can run.
	/// </summary>
	async Task DownloadInstalledPackagesAsync()
	{
		foreach ( var ident in InstalledPackages?.ToArray() ?? [] )
		{
			if ( await DownloadPackageAsync( ident ) is { } app && !IsInstalled( app ) )
			{
				InstalledApps ??= new();
				InstalledApps.Add( app.Id );
				Version++;
			}
		}
	}

	/// <summary>
	/// Open apps, in the order they were opened.
	/// </summary>
	public IReadOnlyList<NaviProcess> Running => running;

	/// <summary>
	/// Permission requests waiting for the player, oldest first. The desktop shows the first one.
	/// </summary>
	public IReadOnlyList<NaviPermissionRequest> PermissionRequests => permissionRequests;

	/// <summary>
	/// The visible app in front, or null.
	/// </summary>
	public NaviProcess Focused => running.Where( x => !x.IsMinimized ).OrderByDescending( x => x.ZIndex ).FirstOrDefault();

	/// <summary>
	/// The window that has the player's input: the window in front, if its app captures input. Otherwise null.
	/// See <see cref="NaviInputCapture"/>.
	/// </summary>
	public NaviProcess InputCapture => Focused is { App.CapturesInput: true } focused ? focused : null;

	/// <summary>
	/// Installed apps, sorted by name.
	/// </summary>
	public IEnumerable<NaviAppInfo> Installed => NaviAppRegistry.All.Where( IsInstalled );

	public bool IsInstalled( string id ) => NaviAppRegistry.Find( id ) is { } app && IsInstalled( app );

	public bool IsInstalled( NaviAppInfo app ) => app.IsCore || (InstalledApps?.Contains( app.Id ) ?? false);

	/// <summary>
	/// Has full system access: a system app that is part of the OS, or that the player accepted.
	/// </summary>
	public bool HasRoot( NaviAppInfo app ) => app.IsSystem && (app.IsPrepackaged || (RootApps?.Contains( app.Id ) ?? false));

	public bool IsRunning( string id ) => running.Any( x => x.App.Id == id );

	/// <summary>
	/// The open window of an app, or null.
	/// </summary>
	public NaviProcess GetProcess( string id ) => running.FirstOrDefault( x => x.App.Id == id );

	public bool IsPackageInstalled( string packageIdent ) => InstalledPackages?.Contains( packageIdent ) ?? false;

	public bool IsDownloading( string packageIdent ) => downloading.Contains( packageIdent );

	/// <summary>
	/// Store packages being downloaded right now.
	/// </summary>
	public IEnumerable<string> DownloadingPackages => downloading;

	/// <summary>
	/// Download an app from the Store and install it. Returns false if the download failed.
	/// </summary>
	public async Task<bool> InstallPackageAsync( string packageIdent )
	{
		if ( IsPackageInstalled( packageIdent ) || IsDownloading( packageIdent ) )
			return false;

		var app = await DownloadPackageAsync( packageIdent );
		if ( app is null )
			return false;

		// A system app asks for root before it is installed. The manifest can only be read once it is downloaded.
		if ( app.IsSystem && !HasRoot( app ) )
		{
			downloading.Add( packageIdent );
			var accepted = await RequestRootAsync( app, launch: false );
			downloading.Remove( packageIdent );
			Version++;

			if ( !accepted )
				return false;
		}

		InstalledPackages ??= new();
		InstalledPackages.Add( packageIdent );
		Install( app.Id );
		Version++;
		return true;
	}

	async Task<NaviAppInfo> DownloadPackageAsync( string packageIdent )
	{
		downloading.Add( packageIdent );
		Version++;

		try
		{
			var app = await NaviStore.DownloadAsync( packageIdent );
			if ( app is not null )
				appPackages[app.Id] = packageIdent;

			return app;
		}
		finally
		{
			downloading.Remove( packageIdent );
			Version++;
		}
	}

	/// <summary>
	/// Install an app. Returns false if it doesn't exist or is already installed.
	/// </summary>
	public bool Install( string id )
	{
		var app = NaviAppRegistry.Find( id );
		if ( app is null )
		{
			Log.Warning( $"NaviComputer: no app with id '{id}'" );
			return false;
		}

		if ( IsInstalled( app ) )
			return false;

		InstalledApps ??= new();
		InstalledApps.Add( id );
		Version++;
		return true;
	}

	/// <summary>
	/// Uninstall an app, closing it if it is open. Returns false if it isn't installed or is a system app.
	/// A Store app's code stays loaded until the game restarts, but it won't be downloaded again.
	/// </summary>
	public bool Uninstall( string id )
	{
		var app = NaviAppRegistry.Find( id );
		if ( app is not null && app.IsCore )
			return false;

		if ( InstalledApps is null || !InstalledApps.Remove( id ) )
			return false;

		if ( appPackages.TryGetValue( id, out var packageIdent ) )
			InstalledPackages?.Remove( packageIdent );

		Close( id );

		// A reinstall starts over, and asks again.
		GrantedPermissions?.RemoveAll( x => x.StartsWith( id + " " ) );
		ReviewedApps?.Remove( id );
		RootApps?.Remove( id );
		Version++;
		return true;
	}

	/// <summary>
	/// Open an installed app, or restore it and bring it to the front if it is already open. An app that declares
	/// permissions the player hasn't reviewed yet shows the review first, and opens once they answer it.
	/// </summary>
	public bool Launch( string id )
	{
		if ( GetProcess( id ) is not null )
		{
			Focus( id );
			return true;
		}

		var app = NaviAppRegistry.Find( id );
		if ( app is null || !IsInstalled( app ) )
			return false;

		// A Store system app whose root the player hasn't accepted, e.g. because an update made it one, asks first.
		if ( app.IsSystem && !HasRoot( app ) )
		{
			_ = RequestRootAsync( app, launch: true );
			return true;
		}

		if ( NeedsReview( app ) )
		{
			ReviewPermissions( id );
			return true;
		}

		Open( app );
		return true;
	}

	void Open( NaviAppInfo app )
	{
		// Cascade new windows so they don't cover each other exactly.
		var offset = (running.Count % 6) * 16f;
		var process = new NaviProcess
		{
			App = app,
			Position = new Vector2( 16f + offset, 8f + offset ),
			ZIndex = ++topZIndex,
			IsRoot = HasRoot( app ),
		};

		process.Permissions.UnionWith( GetPermissions( app ) );
		running.Add( process );
		Version++;
	}

	public void Close( string id )
	{
		if ( running.RemoveAll( x => x.App.Id == id ) == 0 )
			return;

		// Nobody is left to answer these.
		foreach ( var request in permissionRequests.Where( x => x.App.Id == id && !x.IsReview ).ToList() )
			DismissPermissionRequest( request );

		Version++;
	}

	/// <summary>
	/// Declares permissions, isn't a system app, and the player hasn't reviewed them yet.
	/// </summary>
	public bool NeedsReview( NaviAppInfo app ) => !app.IsSystem && app.Permissions.Count > 0 && !(ReviewedApps?.Contains( app.Id ) ?? false);

	/// <summary>
	/// The permissions an app has: all of them with root, otherwise those the player granted.
	/// </summary>
	public IEnumerable<string> GetPermissions( NaviAppInfo app )
	{
		if ( HasRoot( app ) )
			return NaviPermissions.All.Select( x => x.Id );

		var prefix = app.Id + " ";
		return GrantedPermissions?.Where( x => x.StartsWith( prefix ) ).Select( x => x[prefix.Length..] ).Where( app.Permissions.Contains ) ?? [];
	}

	/// <summary>
	/// Set exactly which permissions an app has, remembered for later launches. Only the ones it declares count.
	/// </summary>
	public void SetPermissions( NaviAppInfo app, IEnumerable<string> permissions )
	{
		var granted = permissions?.Where( app.Permissions.Contains ).Distinct().ToArray() ?? [];

		GrantedPermissions ??= new();
		GrantedPermissions.RemoveAll( x => x.StartsWith( app.Id + " " ) );
		GrantedPermissions.AddRange( granted.Select( x => $"{app.Id} {x}" ) );

		if ( GetProcess( app.Id ) is { } process && !process.IsRoot )
		{
			process.Permissions.Clear();
			process.Permissions.UnionWith( granted );
		}

		Version++;
	}

	/// <summary>
	/// Ask the player to grant a running app more permissions, with a dialog. Returns the ones it has afterwards, out of
	/// those asked for. Permissions it already has, or that it doesn't declare, aren't shown; the latter are never granted.
	/// </summary>
	public async Task<string[]> RequestPermissionsAsync( NaviProcess process, IEnumerable<string> permissions )
	{
		var asked = permissions?.Distinct().ToArray() ?? [];
		var missing = asked.Where( x => process.App.Permissions.Contains( x ) && !process.HasPermission( x ) ).ToArray();

		if ( missing.Length > 0 && running.Contains( process ) )
		{
			var request = new NaviPermissionRequest { App = process.App, Permissions = missing, Ticked = missing };
			permissionRequests.Add( request );
			Version++;

			await request.Answer.Task;
		}

		return asked.Where( process.HasPermission ).ToArray();
	}

	/// <summary>
	/// Show the player every permission an app declares, ticked as it has them (all ticked the first time), so they can
	/// change them. Opens the app afterwards if it isn't running. The taskbar menu's "Permissions..." does this.
	/// </summary>
	public void ReviewPermissions( string id )
	{
		var app = NaviAppRegistry.Find( id );
		if ( app is null || !IsInstalled( app ) || app.IsSystem || app.Permissions.Count == 0 )
			return;

		if ( permissionRequests.Any( x => x.App.Id == id && x.IsReview ) )
			return;

		permissionRequests.Add( new NaviPermissionRequest
		{
			App = app,
			Permissions = app.Permissions,
			Ticked = NeedsReview( app ) ? app.Permissions : GetPermissions( app ).ToArray(),
			IsReview = true,
			Launch = !IsRunning( id ),
		} );

		Version++;
	}

	/// <summary>
	/// Ask the player to accept full system access (root) for a system app. Returns whether they did. With
	/// <paramref name="launch"/>, the app opens if they do.
	/// </summary>
	public async Task<bool> RequestRootAsync( NaviAppInfo app, bool launch )
	{
		if ( HasRoot( app ) )
			return true;

		var request = permissionRequests.FirstOrDefault( x => x.App.Id == app.Id && x.IsRoot );
		if ( request is null )
		{
			request = new NaviPermissionRequest { App = app, Permissions = [], Ticked = [], IsRoot = true, Launch = launch };
			permissionRequests.Add( request );
			Version++;
		}

		return (await request.Answer.Task).Length > 0;
	}

	/// <summary>
	/// The player closed a permission dialog without answering: nothing changes, an app that asked gets nothing more,
	/// and an app waiting to open doesn't open.
	/// </summary>
	public void DismissPermissionRequest( NaviPermissionRequest request )
	{
		if ( !permissionRequests.Remove( request ) )
			return;

		request.Answer.TrySetResult( [] );
		Version++;
	}

	/// <summary>
	/// The player answered a permission dialog. <paramref name="ticked"/> are the permissions they ticked.
	/// </summary>
	public void AnswerPermissionRequest( NaviPermissionRequest request, IEnumerable<string> ticked )
	{
		if ( !permissionRequests.Remove( request ) )
			return;

		var answer = ticked?.Where( request.Permissions.Contains ).ToArray() ?? [];
		var app = request.App;

		if ( request.IsRoot )
		{
			RootApps ??= new();
			if ( !RootApps.Contains( app.Id ) )
				RootApps.Add( app.Id );

			if ( request.Launch && !IsRunning( app.Id ) && IsInstalled( app ) )
				Open( app );

			request.Answer.TrySetResult( NaviPermissions.All.Select( x => x.Id ).ToArray() );
			Version++;
			return;
		}

		if ( request.IsReview )
		{
			ReviewedApps ??= new();
			if ( !ReviewedApps.Contains( app.Id ) )
				ReviewedApps.Add( app.Id );

			SetPermissions( app, answer );

			if ( request.Launch && !IsRunning( app.Id ) && IsInstalled( app ) )
				Open( app );
		}
		else
		{
			SetPermissions( app, GetPermissions( app ).Concat( answer ) );
		}

		request.Answer.TrySetResult( answer );
		Version++;
	}

	/// <summary>
	/// Bring an app's window to the front, restoring it if it is minimized.
	/// </summary>
	public void Focus( string id )
	{
		var process = GetProcess( id );
		if ( process is null || process == Focused )
			return;

		process.IsMinimized = false;
		process.ZIndex = ++topZIndex;
		Version++;
	}

	/// <summary>
	/// Hide an app's window. Returns false if the app doesn't allow it.
	/// </summary>
	public bool Minimize( string id )
	{
		var process = GetProcess( id );
		if ( process is null || process.IsMinimized || !process.App.CanMinimize )
			return false;

		process.IsMinimized = true;
		Version++;
		return true;
	}

	/// <summary>
	/// Maximize or restore an app's window. Returns false if the app doesn't allow it.
	/// </summary>
	public bool SetMaximized( string id, bool maximized )
	{
		var process = GetProcess( id );
		if ( process is null || !process.App.CanMaximize )
			return false;

		Focus( id );

		if ( process.IsMaximized != maximized )
		{
			process.IsMaximized = maximized;
			Version++;
		}

		return true;
	}

	public bool ToggleMaximized( string id ) => GetProcess( id ) is { } process && SetMaximized( id, !process.IsMaximized );
}
