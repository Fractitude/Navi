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
	/// Goes up on every change, so UI can rebuild when it changes.
	/// </summary>
	public int Version { get; private set; }

	readonly List<NaviProcess> running = new();
	int topZIndex;

	readonly HashSet<string> downloading = new();

	/// <summary>
	/// App id to the Store package it came from.
	/// </summary>
	readonly Dictionary<string, string> appPackages = new();

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

	public bool IsInstalled( NaviAppInfo app ) => app.IsSystem || (InstalledApps?.Contains( app.Id ) ?? false);

	public bool IsRunning( string id ) => running.Any( x => x.App.Id == id );

	/// <summary>
	/// The open window of an app, or null.
	/// </summary>
	public NaviProcess GetProcess( string id ) => running.FirstOrDefault( x => x.App.Id == id );

	public bool IsPackageInstalled( string packageIdent ) => InstalledPackages?.Contains( packageIdent ) ?? false;

	public bool IsDownloading( string packageIdent ) => downloading.Contains( packageIdent );

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
		if ( app is not null && app.IsSystem )
			return false;

		if ( InstalledApps is null || !InstalledApps.Remove( id ) )
			return false;

		if ( appPackages.TryGetValue( id, out var packageIdent ) )
			InstalledPackages?.Remove( packageIdent );

		Close( id );
		Version++;
		return true;
	}

	/// <summary>
	/// Open an installed app, or restore it and bring it to the front if it is already open.
	/// </summary>
	public bool Launch( string id )
	{
		if ( IsRunning( id ) )
		{
			Focus( id );
			return true;
		}

		var app = NaviAppRegistry.Find( id );
		if ( app is null || !IsInstalled( app ) )
			return false;

		// Cascade new windows so they don't cover each other exactly.
		var offset = (running.Count % 6) * 16f;
		running.Add( new NaviProcess
		{
			App = app,
			Position = new Vector2( 16f + offset, 8f + offset ),
			ZIndex = ++topZIndex,
		} );

		Version++;
		return true;
	}

	public void Close( string id )
	{
		if ( running.RemoveAll( x => x.App.Id == id ) > 0 )
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
