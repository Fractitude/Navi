// The Navi client: how an app talks to the Navi computer it runs on. Every Navi app uses it, built-in or from the Store.
//
// It uses no Navi types, only .NET and engine ones, so a Store app copies this file into its own project and namespace
// instead of installing the Navi library (which would get published inside the app). Copy it as it is; only the
// namespace line changes.
//
// Calls go up to the computer as a "navi.call" panel event and are answered a frame or so later, so they are async.
// A call the app lacks the permission for throws UnauthorizedAccessException. Catch it, ask for the permission with
// RequestPermissions, and try again; WithPermission does all three.

using System;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.UI;

namespace Sandbox;

public static class Navi
{
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
	}

	/// <summary>
	/// Make a call to the computer. Prefer the typed methods below.
	/// </summary>
	public static Task<object> Call( Panel app, string method, params object[] args )
	{
		var reply = new TaskCompletionSource<object>();
		app.CreateEvent( new PanelEvent( "navi.call", app ) { Value = (method, args, reply) } );
		return reply.Task;
	}

	static async Task<T> Call<T>( Panel app, string method, params object[] args ) => (T)await Call( app, method, args );

	// The window the panel is in.

	/// <summary>
	/// Close the window this panel is in. Closing an app's main window closes the app and all its windows. Called from a
	/// headless app's service panel it quits the app.
	/// </summary>
	public static Task Close( Panel app ) => Call( app, "window.close" );

	public static Task<bool> Minimize( Panel app ) => Call<bool>( app, "window.minimize" );

	/// <summary>
	/// Maximize the window, or restore it if it is maximized. False if the app doesn't allow maximizing.
	/// </summary>
	public static Task<bool> ToggleMaximize( Panel app ) => Call<bool>( app, "window.maximize" );

	/// <summary>
	/// The id the .napp gave the window this panel is in, or empty for the app's main window.
	/// </summary>
	public static Task<string> WindowId( Panel app ) => Call<string>( app, "window.id" );

	/// <summary>
	/// Change what the title bar of the window this panel is in says after the app's name, up to 32 characters. Empty goes back
	/// to the title in the .napp. It is safe to call every frame. In a shared app only the host's copy should call it.
	/// </summary>
	public static Task SetTitle( Panel app, string title ) => Call( app, "window.title", title ?? "" );

	// More windows, and the system tray. An app can open the windows its .napp lists under Windows, one of each, and a
	// headless app (Headless in the .napp) starts with none: its main panel is a service that is never drawn and runs until
	// the player quits it from its tray menu. Call these from the main panel, or the service.

	/// <summary>
	/// Open one of the windows the .napp lists, by its id, or bring it to the front if it is open. False if the app has no
	/// such window. Needs no permission: an app can only open what its manifest says.
	/// </summary>
	public static Task<bool> OpenWindow( Panel app, string windowId ) => Call<bool>( app, "window.open", windowId );

	/// <summary>
	/// Close one of the app's other windows, by its id.
	/// </summary>
	public static Task CloseWindow( Panel app, string windowId ) => Call( app, "window.close-other", windowId );

	/// <summary>
	/// An entry in the tray menu: <paramref name="Id"/> comes back with <see cref="IsTrayClick"/>, <paramref name="Label"/> is
	/// what the player reads, <paramref name="Icon"/> is a Material icon name or empty. Ids are up to 32 characters, labels
	/// up to 48, and a menu holds 12.
	/// </summary>
	public readonly record struct TrayItem( string Id, string Label, string Icon = "" );

	/// <summary>
	/// Set this app's icon in the system tray: a Material icon name (empty uses the app's own), a tooltip, and the menu items
	/// shown on a right-click. The desktop puts the app's name before the tooltip and adds Quit after the items, so an app can
	/// neither pass for another nor stay when the player wants it gone. A headless app has an icon from the start.
	/// </summary>
	public static Task SetTray( Panel app, string icon, string tooltip, params TrayItem[] items ) =>
		Call( app, "tray.set", icon, tooltip, items.Select( x => (x.Id, x.Label, x.Icon ?? "") ).ToArray() );

	/// <summary>
	/// The id of the tray item the player chose, or <see cref="TrayPrimary"/> when they clicked the icon itself. Call it from
	/// the main panel's OnEvent.
	/// </summary>
	public static bool IsTrayClick( PanelEvent e, out string id )
	{
		if ( e.Name == "navi.tray" && e.Value is string item )
		{
			id = item;
			return true;
		}

		id = null;
		return false;
	}

	/// <summary>
	/// The id <see cref="IsTrayClick"/> gives for a click on the tray icon itself. Opening a headless app that is already
	/// running from the taskbar gives it too.
	/// </summary>
	public const string TrayPrimary = "";

	/// <summary>
	/// True while the app's window has the player's controls (it is in front while the player sits at the computer). Pause when it doesn't.
	/// </summary>
	public static bool HasInput( Panel app ) => app.HasClass( "navi-input" );

	// Permissions.

	/// <summary>
	/// The permissions this app has been granted.
	/// </summary>
	public static Task<string[]> GetPermissions( Panel app ) => Call<string[]>( app, "permissions.get" );

	/// <summary>
	/// Ask the player for permissions. They see a list and can grant all, some or none. Returns which were granted and which
	/// denied. Only permissions the app's .napp lists can be granted; already granted ones aren't asked again.
	/// </summary>
	public static Task<(string[] Granted, string[] Denied)> RequestPermissions( Panel app, params string[] permissions ) =>
		Call<(string[], string[])>( app, "permissions.request", new object[] { permissions } );

	/// <summary>
	/// True if this app has full system access (root): it is a system app the player accepted, or part of the OS.
	/// Root has every permission.
	/// </summary>
	public static Task<bool> HasRoot( Panel app ) => Call<bool>( app, "permissions.root" );

	/// <summary>
	/// The permission a call failed for, or null if it failed for another reason.
	/// </summary>
	public static string MissingPermission( Exception e )
	{
		const string prefix = "Missing permission: ";
		return e is UnauthorizedAccessException && e.Message.StartsWith( prefix ) ? e.Message[prefix.Length..] : null;
	}

	/// <summary>
	/// Make a call. If it fails for a missing permission, ask the player for it and, if they grant it, try once more.
	/// Still throws UnauthorizedAccessException if they deny it.
	/// </summary>
	public static async Task<T> WithPermission<T>( Panel app, Func<Task<T>> call )
	{
		string permission;

		try
		{
			return await call();
		}
		catch ( UnauthorizedAccessException e ) when ( MissingPermission( e ) is not null )
		{
			permission = MissingPermission( e );
		}

		var (granted, _) = await RequestPermissions( app, permission );
		if ( !granted.Contains( permission ) )
			throw new UnauthorizedAccessException( $"Missing permission: {permission}" );

		return await call();
	}

	// Apps.

	/// <summary>
	/// Every app on the computer, sorted by name. Type is "core" for apps that are part of the OS (always installed,
	/// can't be uninstalled), "system" for other apps with full system access, or "app".
	/// </summary>
	public static Task<(string Id, string Name, string Description, string Icon, string Color, bool Installed, string Type)[]> GetApps( Panel app ) =>
		Call<(string, string, string, string, string, bool, string)[]>( app, "apps.list" );

	/// <summary>
	/// Ids of the apps that came from the Store: every app whose manifest didn't ship with the game or a library.
	/// </summary>
	public static Task<string[]> GetStoreApps( Panel app ) => Call<string[]>( app, "apps.from-store" );

	/// <summary>
	/// Open an installed app, or bring it to the front.
	/// </summary>
	public static Task<bool> Launch( Panel app, string id ) => Call<bool>( app, "apps.launch", id );

	/// <summary>
	/// Install an app. Needs <see cref="Permission.InstallApps"/>.
	/// </summary>
	public static Task<bool> Install( Panel app, string id ) => Call<bool>( app, "apps.install", id );

	/// <summary>
	/// Uninstall an app. Needs <see cref="Permission.InstallApps"/>.
	/// </summary>
	public static Task<bool> Uninstall( Panel app, string id ) => Call<bool>( app, "apps.uninstall", id );

	/// <summary>
	/// Close an app's window. Needs <see cref="Permission.ManageApps"/>, unless it is this app.
	/// </summary>
	public static Task CloseApp( Panel app, string id ) => Call( app, "apps.close", id );

	/// <summary>
	/// Goes up whenever apps are installed, opened or closed, or packages change. Poll it to know when to refresh.
	/// </summary>
	public static Task<int> GetVersion( Panel app ) => Call<int>( app, "system.version" );

	// Settings.

	/// <summary>
	/// How big the desktop is drawn (1 is 100%), and the scales the player can pick from.
	/// </summary>
	public static Task<(float Scale, float[] Options)> GetDisplayScale( Panel app ) =>
		Call<(float, float[])>( app, "display.scale" );

	/// <summary>
	/// Draw the desktop at one of the scales <see cref="GetDisplayScale"/> lists. Needs <see cref="Permission.ChangeSettings"/>.
	/// False if the scale isn't one of them.
	/// </summary>
	public static Task<bool> SetDisplayScale( Panel app, float scale ) => Call<bool>( app, "display.set-scale", scale );

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
	/// The computer's wallpaper.
	/// </summary>
	public static async Task<Wallpaper> GetWallpaper( Panel app ) => (Wallpaper)await Call<int>( app, "wallpaper.get" );

	/// <summary>
	/// Draw a wallpaper behind the windows. Needs <see cref="Permission.ChangeSettings"/>.
	/// </summary>
	public static Task<bool> SetWallpaper( Panel app, Wallpaper wallpaper ) => Call<bool>( app, "wallpaper.set", (int)wallpaper );

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
	/// The computer's theme, color scheme, blur level and transparency level.
	/// </summary>
	public static async Task<(Theme Theme, ColorScheme Scheme, int Blur, int Transparency)> GetTheme( Panel app )
	{
		var (theme, scheme, blur, transparency) = await Call<(int, int, int, int)>( app, "theme.get" );
		return ((Theme)theme, (ColorScheme)scheme, blur, transparency);
	}

	/// <summary>
	/// Draw the computer in a theme. Needs <see cref="Permission.ChangeSettings"/>.
	/// </summary>
	public static Task<bool> SetTheme( Panel app, Theme theme ) => Call<bool>( app, "theme.set-theme", (int)theme );

	/// <summary>
	/// Draw the computer light or dark. Needs <see cref="Permission.ChangeSettings"/>.
	/// </summary>
	public static Task<bool> SetColorScheme( Panel app, ColorScheme scheme ) => Call<bool>( app, "theme.set-scheme", (int)scheme );

	/// <summary>
	/// How strongly the Glass theme blurs what is behind a surface, 0 to <see cref="MaxThemeLevel"/>. Needs
	/// <see cref="Permission.ChangeSettings"/>.
	/// </summary>
	public static Task<bool> SetBlurLevel( Panel app, int level ) => Call<bool>( app, "theme.set-blur", level );

	/// <summary>
	/// How much of what is behind a surface shows through it in the Glass theme, 0 (nothing: solid) to
	/// <see cref="MaxThemeLevel"/>. Needs <see cref="Permission.ChangeSettings"/>.
	/// </summary>
	public static Task<bool> SetTransparencyLevel( Panel app, int level ) => Call<bool>( app, "theme.set-transparency", level );

	/// <summary>
	/// The most <see cref="SetSnapDistance"/> accepts, in panel pixels.
	/// </summary>
	public const int MaxSnapDistance = 40;

	/// <summary>
	/// Whether windows snap to the screen's edges and to each other's, and how close in panel pixels an edge has to get.
	/// </summary>
	public static Task<(bool ScreenEdges, bool Windows, int Distance)> GetSnapping( Panel app ) =>
		Call<(bool, bool, int)>( app, "snap.get" );

	/// <summary>
	/// Let windows snap to the screen's edges or not. Needs <see cref="Permission.ChangeSettings"/>.
	/// </summary>
	public static Task<bool> SetSnapToScreenEdges( Panel app, bool snap ) => Call<bool>( app, "snap.set-screen", snap );

	/// <summary>
	/// Let windows snap to each other's edges or not. Needs <see cref="Permission.ChangeSettings"/>.
	/// </summary>
	public static Task<bool> SetSnapToWindows( Panel app, bool snap ) => Call<bool>( app, "snap.set-windows", snap );

	/// <summary>
	/// How close, in panel pixels, an edge has to get to snap, 1 to <see cref="MaxSnapDistance"/>. Needs
	/// <see cref="Permission.ChangeSettings"/>.
	/// </summary>
	public static Task<bool> SetSnapDistance( Panel app, int distance ) => Call<bool>( app, "snap.set-distance", distance );

	// The Store.

	/// <summary>
	/// One page of apps published to the Store that match <paramref name="text"/>, and how many match in all. The page
	/// leaves out the first <paramref name="skip"/> matches and holds at most <paramref name="take"/> of them. Official
	/// ones are by the Navi makers.
	/// </summary>
	public static Task<((Package Package, bool Official)[] Packages, int Total)> SearchStore( Panel app, string text = "", int skip = 0, int take = 20 ) =>
		Call<((Package, bool)[], int)>( app, "store.search", text, skip, take );

	/// <summary>
	/// Idents of the Store packages installed on this computer, and of those downloading now.
	/// </summary>
	public static Task<(string[] Installed, string[] Installing)> GetStoreStatus( Panel app ) =>
		Call<(string[], string[])>( app, "store.status" );

	/// <summary>
	/// Idents of the Store packages the game blocks. They can't be installed.
	/// </summary>
	public static Task<string[]> GetStoreBlacklist( Panel app ) => Call<string[]>( app, "store.blacklist" );

	/// <summary>
	/// Download a Store package and install its app. Needs <see cref="Permission.InstallFromStore"/>. False if it failed.
	/// </summary>
	public static Task<bool> InstallPackage( Panel app, string ident ) => Call<bool>( app, "store.install", ident );

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
	public static bool IsViewer( Panel app ) => app.HasClass( "navi-viewer" );

	static readonly System.Collections.Generic.Dictionary<Panel, object> published = new();

	/// <summary>
	/// The host's copy of a shared app: this is what every client draws now. Send a new object when something changes,
	/// such as a UiState record replaced whole; publishing the same object again sends nothing. Keep it small.
	/// </summary>
	public static void Publish( Panel app, object state )
	{
		if ( IsViewer( app ) || (published.TryGetValue( app, out var last ) && ReferenceEquals( last, state )) )
			return;

		Forget();
		published[app] = state;
		app.SetAttribute( "navi-published", Json.Serialize( state ) );
	}

	static readonly System.Collections.Generic.Dictionary<Panel, (string Json, object State)> received = new();

	/// <summary>
	/// A viewer's copy of a shared app: the state the host published last, or null before the first one arrives.
	/// </summary>
	public static T GetState<T>( Panel app ) where T : class
	{
		var json = app.GetAttribute( "navi-state", null );
		if ( string.IsNullOrEmpty( json ) )
			return null;

		if ( received.TryGetValue( app, out var cached ) && cached.Json == json && cached.State is T same )
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

		Forget();
		received[app] = (json, state);
		return state;
	}

	/// <summary>
	/// Drop what's kept for panels that are gone.
	/// </summary>
	static void Forget()
	{
		foreach ( var panel in published.Keys.Where( x => !x.IsValid ).ToArray() )
			published.Remove( panel );

		foreach ( var panel in received.Keys.Where( x => !x.IsValid ).ToArray() )
			received.Remove( panel );
	}

	/// <summary>
	/// The player did something in a shared app, such as press a button. The host's copy gets it as an action (see
	/// <see cref="IsAction"/>), from whichever client the player is on, a frame or so later. Only the player using the
	/// computer is listened to. Arguments are text: an action is what a player could have clicked, not data to trust.
	/// </summary>
	public static void Act( Panel app, string action, params string[] args ) =>
		app.CreateEvent( new PanelEvent( "navi.act", app ) { Value = (action, args ?? []) } );

	/// <summary>
	/// Is this event an action (<see cref="Act"/>) for the host's copy of the app? Call it from the panel's OnEvent.
	/// </summary>
	public static bool IsAction( PanelEvent e, out string action, out string[] args )
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
	/// Is the player holding this input action (e.g. "Forward") while the app has their controls? In a shared app it is
	/// the input of the player using the computer, from whichever client they're on. Use it instead of Input.Down.
	/// </summary>
	public static bool InputDown( Panel app, string action ) =>
		app.GetAttribute( "navi-input-down", null ) is { } down ? Has( down, action ) : HasInput( app ) && Input.Down( action );

	/// <summary>
	/// Did the player press this input action this frame? See <see cref="InputDown"/>.
	/// </summary>
	public static bool InputPressed( Panel app, string action ) =>
		app.GetAttribute( "navi-input-pressed", null ) is { } pressed ? Has( pressed, action ) : HasInput( app ) && Input.Pressed( action );

	static bool Has( string list, string action ) => list.Split( ',' ).Contains( action );
}
