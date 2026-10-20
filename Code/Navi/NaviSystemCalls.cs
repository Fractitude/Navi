using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// The calls apps make to the computer, through the <c>Navi</c> client (<c>Code/Navi/Client/Navi.cs</c>).
/// A call is a <c>navi.call</c> panel event that bubbles up from the app's panel to its window, with a value of
/// <c>(string Method, object[] Args, TaskCompletionSource&lt;object&gt; Reply)</c>. Everything in it is a .NET or engine type,
/// so apps don't need Navi types and Store apps work in every game. The call runs as the app, from the window the panel is in: a call it lacks
/// the permission for fails with <see cref="System.UnauthorizedAccessException"/> (<see cref="NaviPermissions.Missing"/>), which the client
/// turns into <see cref="Navi.MissingPermissionException"/>.
/// Keep method names, arguments and results stable: published apps depend on them.
/// </summary>
public static class NaviSystemCalls
{
	public const string EventName = "navi.call";

	/// <summary>
	/// Run a call for <paramref name="window"/>'s app and answer it on <paramref name="reply"/>. The window is where the
	/// panel that made the call lives, which is what the calls about "this window" mean.
	/// </summary>
	public static async Task RunAsync( NaviComputer computer, NaviWindow window, string method, object[] args, TaskCompletionSource<object> reply )
	{
		try
		{
			reply.TrySetResult( await CallAsync( computer, window, method, args ?? [] ) );
		}
		catch ( System.Exception e )
		{
			reply.TrySetException( e );
		}
	}

	static async Task<object> CallAsync( NaviComputer computer, NaviWindow window, string method, object[] args )
	{
		var process = window.Process;
		var self = process.App.Id;
		var windowId = window.Id;

		// On a client that isn't the host, a copy of an app that isn't shared runs here alone. It can read the computer
		// as the host has it, and work its own window as the local player would. Everything else that changes the
		// computer does nothing here.
		if ( !computer.IsAuthority )
		{
			switch ( method )
			{
				case "window.close":
					computer.UserCloseWindow( self, windowId );
					return null;

				case "window.minimize":
					computer.UserMinimize( self, windowId );
					return window.Info.CanMinimize && !window.IsService;

				case "window.maximize":
					computer.UserToggleMaximized( self, windowId );
					return window.Info.CanMaximize && !window.IsService;

				case "window.open":
					computer.UserOpenWindow( self, Arg<string>( args, 0, method ) );
					return process.App.FindWindow( Arg<string>( args, 0, method ) ) is { Id: not "" };

				case "window.close-other":
					computer.UserCloseWindow( self, Arg<string>( args, 0, method ) );
					return null;
			}
		}

		// The camera on top of the screen. Every client has its own, since it looks at the world around the screen.
		if ( method is "camera.view" or "camera.photo" )
		{
			Require( process, NaviPermissions.Camera );

			return method == "camera.view"
				? computer.GetCameraView( Arg<int>( args, 0, method ), Arg<int>( args, 1, method ) )
				: computer.TakeCameraPhoto();
		}

		// The system file picker: any app may ask the player for a file, and gets only the one chosen. The File Picker
		// window itself answers with the picker.* calls, which no other app may make.
		if ( method == "files.pick" )
			return await computer.PickFileAsync( process, Opt<string[]>( args, 0, [] ), Opt( args, 1, "/" ) );

		if ( method.StartsWith( "picker." ) )
		{
			if ( self != NaviAppRegistry.FilePickerApp || !computer.IsAuthority )
				throw new System.NotSupportedException( $"Navi has no call '{method}'." );

			switch ( method )
			{
				case "picker.request":
					return computer.PickRequest is { } request
						? (request.Caller.App.Name, request.Types.ToArray(), request.StartFolder)
						: null;

				case "picker.choose":
					return computer.AnswerPick( Arg<string>( args, 0, method ) );

				case "picker.cancel":
					computer.CancelPick();
					return null;
			}
		}

		// Direct messages. The host has them, and they are the seated player's.
		if ( method.StartsWith( "messages." ) )
			return NaviMessageCalls.Run( computer, process, method, args );

		// Files. The host has them; a client's own copy of an app gets an error.
		if ( method.StartsWith( "files." ) )
			return NaviFileCalls.Run( computer, process, method, args );

		switch ( method )
		{
			// The window the panel is in.
			case "window.close":
				computer.CloseWindow( self, windowId );
				return null;

			case "window.minimize":
				return computer.Minimize( self, windowId );

			case "window.maximize":
				return computer.ToggleMaximized( self, windowId );

			// The id of the window the panel is in: empty for the main window.
			case "window.id":
				return windowId;

			// The title of the window the panel is in. The desktop puts the app's name before it.
			case "window.title":
				return computer.SetTitle( self, windowId, Arg<string>( args, 0, method ) );

			// Ask before the player closes the window the panel is in. The app is only asked; it can't keep the window open.
			case "window.close-guard":
				return computer.SetCloseGuard( self, windowId, Arg<bool>( args, 0, method ) );

			// The app's other windows, which only its manifest can define.
			case "window.open":
				return computer.OpenWindow( self, Arg<string>( args, 0, method ) );

			case "window.close-other":
				return computer.CloseWindow( self, Arg<string>( args, 0, method ) );

			// The system tray. The app has its own icon, and only its own.
			case "tray.set":
				return computer.SetTray( self, Opt( args, 0, "" ), Opt( args, 1, "" ), Opt<(string, string, string)[]>( args, 2, [] ) );

			// Notifications. The desktop draws them with the app's name and icon, so an app sends them only as itself, and
			// dismisses only its own.
			case "notifications.send":
				Require( process, NaviPermissions.Notify );
				return computer.Notify( self, Opt( args, 0, "" ), Opt( args, 1, "" ), Opt( args, 2, false ) );

			case "notifications.dismiss":
				return computer.DismissNotification( Arg<int>( args, 0, method ), self );

			case "notifications.apps":
				return computer.Installed.Select( x => (x.Id, computer.IsNotificationsMuted( x.Id )) ).ToArray();

			case "notifications.set-muted":
			{
				Require( process, NaviPermissions.ChangeSettings );

				// Muting is the player's word against an app, so no app can take it back for itself. Core apps are the OS,
				// and Settings has to reach its own row like any other.
				var id = Arg<string>( args, 0, method );
				return (id != self || process.App.IsCore) && computer.SetNotificationsMuted( id, Arg<bool>( args, 1, method ) );
			}

			// Permissions.
			case "permissions.get":
				return process.IsRoot ? NaviPermissions.All.Select( x => x.Id ).ToArray() : process.Permissions.ToArray();

			case "permissions.root":
				return process.IsRoot;

			case "permissions.request":
			{
				var asked = Arg<string[]>( args, 0, method );
				var granted = await computer.RequestPermissionsAsync( process, asked );
				return (granted, asked.Except( granted ).ToArray());
			}

			// Apps.
			case "system.version":
				return computer.Version;

			case "apps.list":
				// An app this computer can't install is left out, unless it is already installed.
				return NaviAppRegistry.All
					.Where( x => computer.CanInstall( x ) || computer.IsInstalled( x ) )
					.Select( x => (x.Id, x.Name, x.Description, x.Icon, x.Color, computer.IsInstalled( x ), x.IsCore ? "core" : x.IsSystem ? "system" : "app") )
					.ToArray();

			case "apps.from-store":
				// The apps whose manifest didn't ship with the game or a library, which in a published game means the ones
				// installed from the Store. It isn't "downloaded this session": a Store app uninstalled a moment ago is
				// still loaded, and in the editor an app lives in an addon project rather than in the package cache.
				return NaviAppRegistry.All.Where( x => !x.IsPrepackaged ).Select( x => x.Id ).ToArray();

			case "apps.launch":
				return computer.Launch( Arg<string>( args, 0, method ) );

			case "apps.install":
				Require( process, NaviPermissions.InstallApps );
				return computer.Install( Arg<string>( args, 0, method ) );

			case "apps.uninstall":
				Require( process, NaviPermissions.InstallApps );
				return computer.Uninstall( Arg<string>( args, 0, method ) );

			case "apps.close":
			{
				var id = Arg<string>( args, 0, method );
				if ( id != self )
					Require( process, NaviPermissions.ManageApps );

				computer.Close( id );
				return null;
			}

			// Settings.
			case "display.scale":
				return (computer.DisplayScale, NaviComputer.DisplayScales.ToArray());

			case "display.set-scale":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetDisplayScale( Arg<float>( args, 0, method ) );

			case "theme.get":
				return ((int)computer.Theme, (int)computer.ColorScheme, computer.BlurLevel, computer.TransparencyLevel);

			case "theme.set-theme":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetTheme( (Navi.Theme)Arg<int>( args, 0, method ) );

			case "theme.set-scheme":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetColorScheme( (Navi.ColorScheme)Arg<int>( args, 0, method ) );

			case "theme.set-blur":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetBlurLevel( Arg<int>( args, 0, method ) );

			case "theme.set-transparency":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetTransparencyLevel( Arg<int>( args, 0, method ) );

			case "wallpaper.get":
				return (int)computer.Wallpaper;

			case "wallpaper.set":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetWallpaper( (Navi.Wallpaper)Arg<int>( args, 0, method ) );

			case "snap.get":
				return (computer.SnapToScreenEdges, computer.SnapToWindows, computer.SnapDistance);

			case "snap.set-screen":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetSnapToScreenEdges( Arg<bool>( args, 0, method ) );

			case "snap.set-windows":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetSnapToWindows( Arg<bool>( args, 0, method ) );

			case "snap.set-distance":
				Require( process, NaviPermissions.ChangeSettings );
				return computer.SetSnapDistance( Arg<int>( args, 0, method ) );

			// The Store.
			case "store.search":
			{
				var (packages, total) = await NaviStore.SearchAsync(
					Opt( args, 0, "" ), Opt( args, 1, 0 ), Opt( args, 2, NaviStore.PageSize ) );

				return (packages.Select( x => (x, NaviStore.IsOfficial( x )) ).ToArray(), total);
			}

			case "store.status":
				return (computer.InstalledPackages?.ToArray() ?? [], computer.DownloadingPackages.ToArray());

			case "store.blacklist":
				return NaviStore.Blacklisted;

			case "store.install":
				Require( process, NaviPermissions.InstallFromStore );
				return await computer.InstallPackageAsync( Arg<string>( args, 0, method ) );

			case "store.uninstall":
				Require( process, NaviPermissions.InstallApps );
				return computer.UninstallPackage( Arg<string>( args, 0, method ) );
		}

		throw new System.NotSupportedException( $"Navi has no call '{method}'." );
	}

	static void Require( NaviProcess process, string permission )
	{
		if ( !process.HasPermission( permission ) )
			throw NaviPermissions.Missing( permission );
	}

	/// <summary>
	/// The argument at <paramref name="index"/>, or <paramref name="fallback"/> when the app left it out.
	/// </summary>
	static T Opt<T>( object[] args, int index, T fallback ) =>
		index < args.Length && args[index] is T value ? value : fallback;

	static T Arg<T>( object[] args, int index, string method )
	{
		if ( index < args.Length && args[index] is T value )
			return value;

		throw new System.ArgumentException( $"'{method}' needs a {typeof( T ).Name} as argument {index + 1}." );
	}
}
