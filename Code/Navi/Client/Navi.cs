// The Navi client: how an app talks to the Navi computer it runs on. Every Navi app uses it, built-in or from the Store.
//
// It uses no Navi types, only .NET and engine ones, so a Store app copies this file into its own project and namespace
// instead of installing the Navi library (which would get published inside the app). Copy it as it is; only the
// namespace line changes. A copy keeps working with newer computers: what it sends (method names, arguments) stays
// the same. Copy it again to get what was added since.
//
// An app's panel derives from NaviApp and uses its Navi property:
//
//     await Navi.Apps.LaunchAsync( "calculator" );
//     var theme = await Navi.Settings.GetThemeAsync();
//     await Navi.Notifications.SendAsync( "Download finished", "notes.txt is ready.", sound: true );
//     Navi.Act( "press", "7" );
//
// A view model takes the Navi, not the panel. Calls go up to the computer as a "navi.call" panel event and are
// answered a frame or so later, so they are async. A call the app lacks the permission for throws
// Navi.MissingPermissionException; WithPermissionAsync asks the player for it and tries again.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// A panel that is a Navi app: its <see cref="Navi"/> talks to the computer as this app, and the computer's events for
/// the app arrive as <see cref="OnAction"/> and <see cref="OnTrayClick"/>. Every window of an app, and a headless app's
/// service, is one.
/// </summary>
public abstract class NaviApp : Panel
{
	/// <summary>
	/// The computer this app runs on, as this app.
	/// </summary>
	public Navi Navi => field ??= new Navi( this );

	/// <summary>
	/// The player did something in this app (<see cref="Navi.Act"/>). In a shared app only the host's copy gets it, from
	/// whichever client the player is on. Turn it into a call on the view model, one case per control.
	/// </summary>
	protected virtual void OnAction( string action, string[] args )
	{
	}

	/// <summary>
	/// The player chose an item of this app's tray menu, or <see cref="Navi.TrayPrimary"/> when they clicked the icon
	/// itself. Only the main panel, or a headless app's service, gets it.
	/// </summary>
	protected virtual void OnTrayClick( string item )
	{
	}

	protected override void OnEvent( PanelEvent e )
	{
		if ( Navi.TryGetAction( e, out var action, out var args ) )
		{
			OnAction( action, args );
			e.StopPropagation();
			return;
		}

		if ( Navi.TryGetTrayClick( e, out var item ) )
		{
			OnTrayClick( item );
			e.StopPropagation();
			return;
		}

		base.OnEvent( e );
	}
}

/// <summary>
/// The computer an app runs on, as that app. Get it from <see cref="NaviApp.Navi"/>, or make one for any panel of the
/// app with <c>new Navi( this )</c>.
/// </summary>
public sealed class Navi
{
	readonly Panel panel;

	public Navi( Panel panel )
	{
		this.panel = panel ?? throw new ArgumentNullException( nameof(panel) );

		Window = new WindowCalls( this );
		Tray = new TrayCalls( this );
		Permissions = new PermissionCalls( this );
		Apps = new AppCalls( this );
		Settings = new SettingsCalls( this );
		Store = new StoreCalls( this );
		Notifications = new NotificationCalls( this );
		Input = new InputState( this );
	}

	/// <summary>
	/// The window this panel is in, and the app's other windows.
	/// </summary>
	public WindowCalls Window { get; }

	/// <summary>
	/// The app's icon in the system tray.
	/// </summary>
	public TrayCalls Tray { get; }

	/// <summary>
	/// What this app may do.
	/// </summary>
	public PermissionCalls Permissions { get; }

	/// <summary>
	/// The apps on the computer.
	/// </summary>
	public AppCalls Apps { get; }

	/// <summary>
	/// The computer's settings: display scale, theme, wallpaper, snapping.
	/// </summary>
	public SettingsCalls Settings { get; }

	/// <summary>
	/// The Store, where apps are published.
	/// </summary>
	public StoreCalls Store { get; }

	/// <summary>
	/// Notifications the app shows the player.
	/// </summary>
	public NotificationCalls Notifications { get; }

	/// <summary>
	/// The player's keys, while the app has them. Use it instead of the engine's Input.
	/// </summary>
	public InputState Input { get; }

	/// <summary>
	/// True while the app's window has the player's controls (it is in front while the player sits at the computer).
	/// Pause when it doesn't.
	/// </summary>
	public bool HasInput => panel.HasClass( "navi-input" );

	/// <summary>
	/// Goes up whenever apps are installed, opened or closed, or packages change. Poll it to know when to refresh.
	/// </summary>
	public Task<int> GetVersionAsync() => CallAsync<int>( "system.version" );

	// Calls.

	const string MissingPrefix = "Missing permission: ";

	/// <summary>
	/// Make a call to the computer by its method name. Prefer the typed calls: <see cref="Apps"/>, <see cref="Settings"/>
	/// and the rest.
	/// </summary>
	public async Task<object> CallAsync( string method, params object[] args )
	{
		var reply = new TaskCompletionSource<object>();
		panel.CreateEvent( new PanelEvent( "navi.call", panel ) { Value = (method, args, reply) } );

		try
		{
			return await reply.Task;
		}
		catch ( UnauthorizedAccessException e )
			when ( e is not MissingPermissionException && e.Message.StartsWith( MissingPrefix ) )
		{
			throw new MissingPermissionException( e.Message[MissingPrefix.Length..], e );
		}
	}

	async Task<T> CallAsync<T>( string method, params object[] args ) => (T)await CallAsync( method, args );

	/// <summary>
	/// A call failed because the app lacks <see cref="Permission"/>. Ask for it with
	/// <see cref="PermissionCalls.RequestAsync"/>, or make the call through <see cref="WithPermissionAsync{T}"/>.
	/// </summary>
	public sealed class MissingPermissionException( string permission, Exception inner = null )
		: UnauthorizedAccessException( MissingPrefix + permission, inner )
	{
		public string Permission { get; } = permission;
	}

	/// <summary>
	/// Make a call. If it fails for a missing permission, ask the player for it and, if they grant it, try once more.
	/// Still throws <see cref="MissingPermissionException"/> if they deny it.
	/// </summary>
	public async Task<T> WithPermissionAsync<T>( Func<Task<T>> call )
	{
		string permission;

		try
		{
			return await call();
		}
		catch ( MissingPermissionException e )
		{
			permission = e.Permission;
		}

		var request = await Permissions.RequestAsync( permission );
		if ( !request.Granted.Contains( permission ) )
			throw new MissingPermissionException( permission );

		return await call();
	}

	/// <inheritdoc cref="WithPermissionAsync{T}"/>
	public Task WithPermissionAsync( Func<Task> call ) => WithPermissionAsync( async () =>
	{
		await call();
		return true;
	} );

	// Multiplayer. An app with Shared in its .napp runs on the host, and every other client only draws it: the host's
	// copy publishes its state, the others draw that, and what the player at the screen does goes to the host as an
	// action. Offline, the one copy is the host. See docs/app-architecture.md.

	/// <summary>
	/// The longest action name, and the most text an action's arguments can hold in all.
	/// </summary>
	public const int MaxActionName = 64, MaxActionArgs = 4096;

	/// <summary>
	/// True on a client whose copy of a shared app only draws. It makes no calls, runs no logic, and draws
	/// <see cref="GetState{T}"/>.
	/// </summary>
	public bool IsViewer => panel.HasClass( "navi-viewer" );

	object published;

	/// <summary>
	/// The host's copy of a shared app: this is what every client draws now. Send a new object when something changes,
	/// such as a UiState record replaced whole; publishing the same object again sends nothing. Keep it small. A viewer's
	/// copy publishes nothing.
	/// </summary>
	public void Publish( object state )
	{
		if ( IsViewer || ReferenceEquals( published, state ) )
			return;

		published = state;
		panel.SetAttribute( "navi-published", Json.Serialize( state ) );
	}

	string receivedJson;
	object received;

	/// <summary>
	/// A viewer's copy of a shared app: the state the host published last, or null before the first one arrives.
	/// </summary>
	public T GetState<T>() where T : class
	{
		var json = panel.GetAttribute( "navi-state", null );
		if ( string.IsNullOrEmpty( json ) )
			return null;

		if ( json == receivedJson && received is T same )
			return same;

		T state;
		try
		{
			state = Json.Deserialize<T>( json );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Navi: couldn't read the host's state as {typeof( T ).Name}: {e.Message}" );
			return null;
		}

		receivedJson = json;
		received = state;
		return state;
	}

	/// <summary>
	/// The player did something in the app, such as press a button. The host's copy gets it as an action
	/// (<see cref="NaviApp.OnAction"/>), from whichever client the player is on, a frame or so later. Only the player
	/// using the computer is listened to. Arguments are text: an action is what a player could have clicked, not data to
	/// trust.
	/// </summary>
	public void Act( string action, params string[] args ) =>
		panel.CreateEvent( new PanelEvent( "navi.act", panel ) { Value = (action, args ?? []) } );

	/// <summary>
	/// Is this event an action (<see cref="Act"/>) for the app? <see cref="NaviApp"/> calls it; a panel that isn't one
	/// calls it from its OnEvent.
	/// </summary>
	public static bool TryGetAction( PanelEvent e, out string action, out string[] args )
	{
		if ( e.Name == "navi.action" && e.Value is ValueTuple<string, string[]> value )
		{
			(action, args) = value;
			return true;
		}

		action = null;
		args = null;
		return false;
	}

	/// <summary>
	/// Is this event a click on the app's tray icon or menu? See <see cref="TryGetAction"/>.
	/// </summary>
	public static bool TryGetTrayClick( PanelEvent e, out string item )
	{
		if ( e.Name == "navi.tray" && e.Value is string value )
		{
			item = value;
			return true;
		}

		item = null;
		return false;
	}

	/// <summary>
	/// The tray item a click on the tray icon itself gives (<see cref="NaviApp.OnTrayClick"/>). Opening a headless app that
	/// is already running from the taskbar gives it too.
	/// </summary>
	public const string TrayPrimary = "";

	/// <summary>
	/// The window this panel is in, and the app's other windows. An app can open the windows its .napp lists under
	/// Windows, one of each. A headless app (Headless in the .napp) starts with none: its main panel is a service that is
	/// never drawn and runs until the player quits it from its tray menu.
	/// </summary>
	public sealed class WindowCalls( Navi navi )
	{
		/// <summary>
		/// Close the window this panel is in. Closing an app's main window closes the app and all its windows. Called
		/// from a headless app's service it quits the app.
		/// </summary>
		public Task CloseAsync() => navi.CallAsync( "window.close" );


		public Task<bool> MinimizeAsync() => navi.CallAsync<bool>( "window.minimize" );


		/// <summary>
		/// Maximize the window, or restore it if it is maximized. False if the app doesn't allow maximizing.
		/// </summary>
		public Task<bool> ToggleMaximizeAsync() => navi.CallAsync<bool>( "window.maximize" );


		/// <summary>
		/// The id the .napp gave the window this panel is in, or empty for the app's main window.
		/// </summary>
		public Task<string> GetIdAsync() => navi.CallAsync<string>( "window.id" );


		/// <summary>
		/// Change what the title bar says after the app's name, up to 32 characters. Empty goes back to the title in the
		/// .napp. It is safe to call every frame. In a shared app only the host's copy should call it.
		/// </summary>
		public Task SetTitleAsync( string title ) => navi.CallAsync( "window.title", title ?? "" );


		/// <summary>
		/// Open one of the windows the .napp lists, by its id, or bring it to the front if it is open. False if the app
		/// has no such window. Needs no permission: an app can only open what its manifest says.
		/// </summary>
		public Task<bool> OpenAsync( string windowId ) => navi.CallAsync<bool>( "window.open", windowId );


		/// <summary>
		/// Close one of the app's other windows, by its id.
		/// </summary>
		public Task CloseAsync( string windowId ) => navi.CallAsync( "window.close-other", windowId );

	}

	/// <summary>
	/// An entry in the tray menu: <paramref name="Id"/> comes back to <see cref="NaviApp.OnTrayClick"/>,
	/// <paramref name="Label"/> is what the player reads, <paramref name="Icon"/> is a Material icon name or empty. Ids
	/// are up to 32 characters, labels up to 48, and a menu holds 12.
	/// </summary>
	public readonly record struct TrayItem( string Id, string Label, string Icon = "" );

	/// <summary>
	/// The app's icon in the system tray. Call it from the main panel, or a headless app's service.
	/// </summary>
	public sealed class TrayCalls( Navi navi )
	{
		/// <summary>
		/// Set the icon: a Material icon name (empty uses the app's own), a tooltip, and the menu items shown on a
		/// right-click. The desktop puts the app's name before the tooltip and adds Quit after the items, so an app can
		/// neither pass for another nor stay when the player wants it gone. A headless app has an icon from the start.
		/// </summary>
		public Task SetAsync( string icon, string tooltip, params TrayItem[] items ) =>
			navi.CallAsync( "tray.set", icon, tooltip, items.Select( x => (x.Id, x.Label, x.Icon ?? "") ).ToArray() );
	}

	/// <summary>
	/// Permissions an app can ask for. List the ones an app may ask for in its .napp, under Permissions.
	/// </summary>
	public static class Permission
	{
		/// <summary>
		/// Install and uninstall apps.
		/// </summary>
		public const string InstallApps = "navi.apps.install";

		/// <summary>
		/// Download and install apps from the Store.
		/// </summary>
		public const string InstallFromStore = "navi.store.install";

		/// <summary>
		/// Close other apps' windows.
		/// </summary>
		public const string ManageApps = "navi.apps.manage";

		/// <summary>
		/// Change the computer's settings, such as the display scale.
		/// </summary>
		public const string ChangeSettings = "navi.settings.change";

		/// <summary>
		/// Send the player notifications.
		/// </summary>
		public const string Notify = "navi.notifications.post";
	}

	/// <summary>
	/// Which of the permissions an app asked for the player granted, and which they denied.
	/// </summary>
	public sealed record PermissionRequest( IReadOnlyList<string> Granted, IReadOnlyList<string> Denied );

	/// <summary>
	/// What the app may do. The ids are in <see cref="Permission"/>.
	/// </summary>
	public sealed class PermissionCalls( Navi navi )
	{
		/// <summary>
		/// The permissions this app has been granted.
		/// </summary>
		public async Task<IReadOnlyList<string>> GetAsync() => await navi.CallAsync<string[]>( "permissions.get" );


		/// <summary>
		/// Ask the player for permissions. They see a list and can grant all, some or none. Only permissions the app's
		/// .napp lists can be granted; already granted ones aren't asked again.
		/// </summary>
		public async Task<PermissionRequest> RequestAsync( params string[] permissions )
		{
			var (granted, denied) = await navi.CallAsync<(string[], string[])>(
				"permissions.request", new object[] { permissions } );
			return new PermissionRequest( granted, denied );
		}

		/// <summary>
		/// True if this app has full system access (root): it is a system app the player accepted, or part of the OS.
		/// Root has every permission.
		/// </summary>
		public Task<bool> HasRootAsync() => navi.CallAsync<bool>( "permissions.root" );

	}

	/// <summary>
	/// What an app is to the computer.
	/// </summary>
	public enum AppKind
	{
		/// <summary>An app with no system access.</summary>
		App,

		/// <summary>An app with full system access (root), which the player accepted.</summary>
		System,

		/// <summary>Part of the OS: always installed, can't be uninstalled, has root.</summary>
		Core,
	}

	/// <summary>
	/// An app on the computer.
	/// </summary>
	public sealed record AppInfo(
		string Id,
		string Name,
		string Description,
		string Icon,
		string Color,
		bool Installed,
		AppKind Kind );

	/// <summary>
	/// The apps on the computer.
	/// </summary>
	public sealed class AppCalls( Navi navi )
	{
		/// <summary>
		/// Every app on the computer, sorted by name.
		/// </summary>
		public async Task<IReadOnlyList<AppInfo>> ListAsync()
		{
			var apps = await navi.CallAsync<(string, string, string, string, string, bool, string)[]>( "apps.list" );
			return apps.Select( x => new AppInfo( x.Item1, x.Item2, x.Item3, x.Item4, x.Item5, x.Item6, x.Item7 switch
			{
				"core" => AppKind.Core,
				"system" => AppKind.System,
				_ => AppKind.App,
			} ) ).ToArray();
		}

		/// <summary>
		/// Ids of the apps that came from the Store: every app whose manifest didn't ship with the game or a library.
		/// </summary>
		public async Task<IReadOnlyList<string>> ListFromStoreAsync() =>
			await navi.CallAsync<string[]>( "apps.from-store" );

		/// <summary>
		/// Open an installed app, or bring it to the front.
		/// </summary>
		public Task<bool> LaunchAsync( string id ) => navi.CallAsync<bool>( "apps.launch", id );


		/// <summary>
		/// Install an app. Needs <see cref="Permission.InstallApps"/>.
		/// </summary>
		public Task<bool> InstallAsync( string id ) => navi.CallAsync<bool>( "apps.install", id );


		/// <summary>
		/// Uninstall an app. Needs <see cref="Permission.InstallApps"/>.
		/// </summary>
		public Task<bool> UninstallAsync( string id ) => navi.CallAsync<bool>( "apps.uninstall", id );


		/// <summary>
		/// Close an app's windows. Needs <see cref="Permission.ManageApps"/>, unless it is this app.
		/// </summary>
		public Task CloseAsync( string id ) => navi.CallAsync( "apps.close", id );

	}

	/// <summary>
	/// How the desktop, its windows and the apps in them are drawn.
	/// </summary>
	public enum Theme
	{
		/// <summary>Flat, opaque, with hard edges and hard shadows.</summary>
		Paper,

		/// <summary>Rounded, translucent surfaces that blur what is behind them.</summary>
		Glass,
	}

	/// <summary>
	/// The picture behind the windows. Independent of the theme; each is drawn for both color schemes.
	/// </summary>
	public enum Wallpaper
	{
		/// <summary>A flat, dark color.</summary>
		Paper,

		/// <summary>A gradient with soft colored shapes, for Glass surfaces to blur.</summary>
		Glass,
	}

	/// <summary>
	/// Whether a theme is drawn light or dark. Every theme has both.
	/// </summary>
	public enum ColorScheme
	{
		Light,
		Dark,
	}

	/// <summary>
	/// The highest blur and transparency level. Both go from 0 (none) to this.
	/// </summary>
	public const int MaxThemeLevel = 4;

	/// <summary>
	/// The most <see cref="SettingsCalls.SetSnapDistanceAsync"/> accepts, in panel pixels.
	/// </summary>
	public const int MaxSnapDistance = 40;

	/// <summary>
	/// How big the desktop is drawn (1 is 100%), and the scales the player can pick from.
	/// </summary>
	public sealed record DisplayScale( float Scale, IReadOnlyList<float> Options );

	/// <summary>
	/// The computer's theme, color scheme, blur level and transparency level.
	/// </summary>
	public sealed record ThemeSettings( Theme Theme, ColorScheme Scheme, int Blur, int Transparency );

	/// <summary>
	/// Whether windows snap to the screen's edges and to each other's, and how close in panel pixels an edge has to get.
	/// </summary>
	public sealed record SnapSettings( bool ScreenEdges, bool Windows, int Distance );

	/// <summary>
	/// The computer's settings. Reading them needs no permission; every change needs
	/// <see cref="Permission.ChangeSettings"/>, and returns false if the value isn't one the computer accepts.
	/// </summary>
	public sealed class SettingsCalls( Navi navi )
	{
		public async Task<DisplayScale> GetDisplayScaleAsync()
		{
			var (scale, options) = await navi.CallAsync<(float, float[])>( "display.scale" );
			return new DisplayScale( scale, options );
		}

		/// <summary>
		/// Draw the desktop at one of the scales <see cref="DisplayScale.Options"/> lists.
		/// </summary>
		public Task<bool> SetDisplayScaleAsync( float scale ) => navi.CallAsync<bool>( "display.set-scale", scale );


		public async Task<ThemeSettings> GetThemeAsync()
		{
			var (theme, scheme, blur, transparency) = await navi.CallAsync<(int, int, int, int)>( "theme.get" );
			return new ThemeSettings( (Theme)theme, (ColorScheme)scheme, blur, transparency );
		}

		public Task<bool> SetThemeAsync( Theme theme ) => navi.CallAsync<bool>( "theme.set-theme", (int)theme );


		public Task<bool> SetColorSchemeAsync( ColorScheme scheme ) =>
			navi.CallAsync<bool>( "theme.set-scheme", (int)scheme );

		/// <summary>
		/// How strongly the Glass theme blurs what is behind a surface, 0 to <see cref="MaxThemeLevel"/>.
		/// </summary>
		public Task<bool> SetBlurLevelAsync( int level ) => navi.CallAsync<bool>( "theme.set-blur", level );


		/// <summary>
		/// How much of what is behind a surface shows through it in the Glass theme, 0 (nothing: solid) to
		/// <see cref="MaxThemeLevel"/>.
		/// </summary>
		public Task<bool> SetTransparencyLevelAsync( int level ) =>
			navi.CallAsync<bool>( "theme.set-transparency", level );

		public async Task<Wallpaper> GetWallpaperAsync() => (Wallpaper)await navi.CallAsync<int>( "wallpaper.get" );


		public Task<bool> SetWallpaperAsync( Wallpaper wallpaper ) =>
			navi.CallAsync<bool>( "wallpaper.set", (int)wallpaper );

		public async Task<SnapSettings> GetSnappingAsync()
		{
			var (screenEdges, windows, distance) = await navi.CallAsync<(bool, bool, int)>( "snap.get" );
			return new SnapSettings( screenEdges, windows, distance );
		}

		public Task<bool> SetSnapToScreenEdgesAsync( bool snap ) => navi.CallAsync<bool>( "snap.set-screen", snap );


		public Task<bool> SetSnapToWindowsAsync( bool snap ) => navi.CallAsync<bool>( "snap.set-windows", snap );


		/// <summary>
		/// How close, in panel pixels, an edge has to get to snap, 1 to <see cref="MaxSnapDistance"/>.
		/// </summary>
		public Task<bool> SetSnapDistanceAsync( int distance ) =>
			navi.CallAsync<bool>( "snap.set-distance", distance );

		/// <summary>
		/// Ask the secure layer to show the lock screen's setup dialog (password or pattern) to the player. Only core apps
		/// are listened to. Call it from a click: the password is typed in the dialog, never in the app.
		/// </summary>
		public void OpenLockSetup() => navi.panel.CreateEvent( new PanelEvent( "navi.lock-setup", navi.panel ) );

	}

	/// <summary>
	/// A Store package, and whether it is official: by the Navi makers.
	/// </summary>
	public sealed record StoreListing( Package Package, bool Official );

	/// <summary>
	/// One page of a Store search, and how many packages match in all.
	/// </summary>
	public sealed record StorePage( IReadOnlyList<StoreListing> Listings, int Total );

	/// <summary>
	/// Idents of the Store packages installed on this computer, and of those downloading now.
	/// </summary>
	public sealed record StoreStatus( IReadOnlyList<string> Installed, IReadOnlyList<string> Installing );

	/// <summary>
	/// The Store, where apps are published.
	/// </summary>
	public sealed class StoreCalls( Navi navi )
	{
		/// <summary>
		/// One page of apps published to the Store that match <paramref name="text"/>. The page leaves out the first
		/// <paramref name="skip"/> matches and holds at most <paramref name="take"/> of them.
		/// </summary>
		public async Task<StorePage> SearchAsync( string text = "", int skip = 0, int take = 20 )
		{
			var (packages, total) = await navi.CallAsync<((Package, bool)[], int)>( "store.search", text, skip, take );
			return new StorePage( packages.Select( x => new StoreListing( x.Item1, x.Item2 ) ).ToArray(), total );
		}

		public async Task<StoreStatus> GetStatusAsync()
		{
			var (installed, installing) = await navi.CallAsync<(string[], string[])>( "store.status" );
			return new StoreStatus( installed, installing );
		}

		/// <summary>
		/// Idents of the Store packages the game blocks. They can't be installed.
		/// </summary>
		public async Task<IReadOnlyList<string>> GetBlacklistAsync() =>
			await navi.CallAsync<string[]>( "store.blacklist" );

		/// <summary>
		/// Download a Store package and install its app. Needs <see cref="Permission.InstallFromStore"/>. False if it
		/// failed.
		/// </summary>
		public Task<bool> InstallAsync( string ident ) => navi.CallAsync<bool>( "store.install", ident );

		/// <summary>
		/// Remove an installed Store package, even one whose app didn't load. Needs <see cref="Permission.InstallApps"/>.
		/// </summary>
		public Task<bool> UninstallAsync( string ident ) => navi.CallAsync<bool>( "store.uninstall", ident );

	}

	/// <summary>
	/// The longest a notification's title and text can be, in characters. More is cut off.
	/// </summary>
	public const int MaxNotificationTitle = 64, MaxNotificationText = 280;

	/// <summary>
	/// An app that can send notifications, and whether the player muted it.
	/// </summary>
	public sealed record NotificationApp( string AppId, bool Muted );

	/// <summary>
	/// Notifications: the computer shows them in a banner, with the app's name and icon, and keeps them in its notification
	/// list until the player dismisses them. The player can mute an app; its notifications then go to the list quietly.
	/// </summary>
	public sealed class NotificationCalls( Navi navi )
	{
		/// <summary>
		/// Show the player a notification, with a title (one line) and text, and the notification sound if
		/// <paramref name="sound"/>. Needs <see cref="Permission.Notify"/>. Returns its id, for <see cref="DismissAsync"/>,
		/// or 0 if the computer didn't take it (both title and text were empty). In a shared app only the host's copy
		/// should call it.
		/// </summary>
		public Task<int> SendAsync( string title, string text = "", bool sound = false ) =>
			navi.CallAsync<int>( "notifications.send", title ?? "", text ?? "", sound );

		/// <summary>
		/// Take one of this app's notifications out of the list, e.g. once what it said is no longer true. False if it
		/// isn't there, or isn't this app's.
		/// </summary>
		public Task<bool> DismissAsync( int id ) => navi.CallAsync<bool>( "notifications.dismiss", id );

		/// <summary>
		/// The installed apps that can send notifications, and whether each is muted.
		/// </summary>
		public async Task<IReadOnlyList<NotificationApp>> ListAppsAsync()
		{
			var apps = await navi.CallAsync<(string, bool)[]>( "notifications.apps" );
			return apps.Select( x => new NotificationApp( x.Item1, x.Item2 ) ).ToArray();
		}

		/// <summary>
		/// Mute another app's notifications, or unmute them. Needs <see cref="Permission.ChangeSettings"/>. False if the app
		/// isn't installed, or is this one.
		/// </summary>
		public Task<bool> SetMutedAsync( string appId, bool muted ) =>
			navi.CallAsync<bool>( "notifications.set-muted", appId, muted );
	}

	/// <summary>
	/// The player's keys, while the app has their controls. In a shared app it is the input of the player using the
	/// computer, from whichever client they're on.
	/// </summary>
	public sealed class InputState( Navi navi )
	{
		/// <summary>
		/// Is the player holding this input action (e.g. "Forward")?
		/// </summary>
		public bool Down( string action ) =>
			navi.panel.GetAttribute( "navi-input-down", null ) is { } down
				? Has( down, action )
				: navi.HasInput && Sandbox.Input.Down( action );

		/// <summary>
		/// Did the player press this input action this frame?
		/// </summary>
		public bool Pressed( string action ) =>
			navi.panel.GetAttribute( "navi-input-pressed", null ) is { } pressed
				? Has( pressed, action )
				: navi.HasInput && Sandbox.Input.Pressed( action );

		static bool Has( string list, string action ) => list.Split( ',' ).Contains( action );
	}
}
