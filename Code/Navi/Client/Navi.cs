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

	/// <summary>
	/// The player pressed a keyboard shortcut while this app's window was in front, e.g. "ctrl+c", "delete", "f2" or
	/// "shift+down": "ctrl+", "shift+" and "alt+" in that order, then the key (a letter or digit, "enter", "backspace",
	/// "delete", "tab", "space", "up", "down", "left", "right", "home", "end", "pageup", "pagedown", "f1" to "f12", "mouse4", "mouse5"). While
	/// a text field has the keys, only Ctrl with a letter or digit the field doesn't use (not A, C, X, V, Z or Y) comes,
	/// e.g. "ctrl+s". Never Escape, which stands the player up. A shared app gets it on the host's
	/// copy, any other app on the player's own, like clicks.
	/// </summary>
	protected virtual void OnShortcut( string keys )
	{
	}

	/// <summary>
	/// The player is closing this window (its close button, the taskbar's Close, the tray's Quit), and the app asked to be
	/// told first with <see cref="Navi.WindowCalls.SetCloseGuardAsync"/>. The window stays open: ask the player in it
	/// (e.g. Save / Don't save / Cancel), then close it with <see cref="Navi.WindowCalls.CloseAsync()"/>, or don't. If the
	/// player closes it again within a few seconds, without clicking in the window, it closes anyway. A shared app gets it
	/// on the host's copy.
	/// </summary>
	protected virtual void OnCloseRequested()
	{
	}

	/// <summary>
	/// Hardware was installed in or removed from the computer. Ask <see cref="Navi.Hardware"/>, <see cref="Navi.Network"/>
	/// or <see cref="Navi.Audio"/> what it has now. Every window of every app gets it, on the copy that runs here.
	/// </summary>
	protected virtual void OnHardwareChanged()
	{
	}

	/// <summary>
	/// The computer went online or offline: a network card was installed or taken out. Offline, every call that goes out
	/// (the Store, Messages) throws <see cref="Navi.NoNetworkException"/>, and the app should stop trying until it is
	/// online again. Comes after <see cref="OnHardwareChanged"/>.
	/// </summary>
	protected virtual void OnNetworkChanged( bool online )
	{
	}

	/// <summary>
	/// The player is dragging something over a drop zone of this app (<see cref="Navi.DragCalls.AcceptDrops"/>) for the first
	/// time. <see cref="Navi.DragEvent.Zone"/> is the zone, and <see cref="Navi.DragEvent.Effect"/> what a drop there would do.
	/// Comes on the copy of the app on the player's client, a shared app's viewer included. Treat what is dragged like
	/// anything the player typed.
	/// </summary>
	protected virtual void OnDragEnter( Navi.DragEvent e )
	{
	}

	/// <summary>
	/// The drag moved over the zone, or its effect changed (the player pressed Ctrl, say). Every frame while it is over.
	/// <see cref="Navi.DragEvent.Position"/> is in the zone's panel pixels from its top left.
	/// </summary>
	protected virtual void OnDragOver( Navi.DragEvent e )
	{
	}

	/// <summary>
	/// The drag left the zone without a drop, or ended: take down what <see cref="OnDragEnter"/> showed.
	/// </summary>
	protected virtual void OnDragLeave( Navi.DragEvent e )
	{
	}

	/// <summary>
	/// The player let go over the zone. <see cref="Navi.DragEvent.Effect"/> is what to do: copy, move or link.
	/// </summary>
	protected virtual void OnDrop( Navi.DragEvent e )
	{
	}

	/// <summary>
	/// A drag this app started is over. <paramref name="effect"/> is what the target did with it, or
	/// <see cref="Navi.DragEffects.None"/> if nothing took it. After a move the app removes what it moved.
	/// </summary>
	protected virtual void OnDragEnded( Navi.DragEffects effect )
	{
	}

	protected override void OnEvent( PanelEvent e )
	{
		if ( Navi.TryGetDragEvent( e, out var phase, out var drag ) )
		{
			switch ( phase )
			{
				case "enter": OnDragEnter( drag ); break;
				case "over": OnDragOver( drag ); break;
				case "leave": OnDragLeave( drag ); break;
				default: OnDrop( drag ); break;
			}

			e.StopPropagation();
			return;
		}

		if ( Navi.TryGetDragEnded( e, out var ended ) )
		{
			OnDragEnded( ended );
			e.StopPropagation();
			return;
		}

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

		if ( Navi.TryGetShortcut( e, out var keys ) )
		{
			OnShortcut( keys );
			e.StopPropagation();
			return;
		}

		if ( e.Name == "navi.close-request" )
		{
			OnCloseRequested();
			e.StopPropagation();
			return;
		}

		if ( Navi.TryGetHardwareChange( e ) )
		{
			OnHardwareChanged();
			e.StopPropagation();
			return;
		}

		if ( Navi.TryGetNetworkChange( e, out var online ) )
		{
			OnNetworkChanged( online );
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
		Files = new FileCalls( this );
		Messages = new MessageCalls( this );
		Camera = new CameraCalls( this );
		Scripts = new ScriptCalls( this );
		Hardware = new HardwareCalls( this );
		Network = new NetworkCalls( this );
		Audio = new AudioCalls( this );
		Input = new InputState( this );
		Drag = new DragCalls( this );
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
	/// The computer's files: its drive and any removable ones.
	/// </summary>
	public FileCalls Files { get; }

	/// <summary>
	/// The direct messages of the player at the computer.
	/// </summary>
	public MessageCalls Messages { get; }

	/// <summary>
	/// The camera on top of the computer's screen.
	/// </summary>
	public CameraCalls Camera { get; }

	/// <summary>
	/// Running scripts, when the game allows it.
	/// </summary>
	public ScriptCalls Scripts { get; }

	/// <summary>
	/// The hardware installed in the computer.
	/// </summary>
	public HardwareCalls Hardware { get; }

	/// <summary>
	/// Whether the computer is online, and its address.
	/// </summary>
	public NetworkCalls Network { get; }

	/// <summary>
	/// The computer's speakers.
	/// </summary>
	public AudioCalls Audio { get; }

	/// <summary>
	/// The player's keys, while the app has them. Use it instead of the engine's Input.
	/// </summary>
	public InputState Input { get; }

	/// <summary>
	/// Dragging things between windows.
	/// </summary>
	public DragCalls Drag { get; }

	/// <summary>
	/// True while the app's window has the player's controls (it is in front while the player sits at the computer).
	/// Pause when it doesn't.
	/// </summary>
	public bool HasInput => panel.HasClass( "navi-input" );

	/// <summary>
	/// True on a client that is only looking at someone else's computer in multiplayer. An app that is private to the player
	/// (not Shared) can skip drawing what costs much, e.g. a web page, and say so instead.
	/// </summary>
	public bool IsWatching => panel.HasClass( "navi-watching" );

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
		catch ( InvalidOperationException e )
			when ( e is not NoNetworkException && e.Message.StartsWith( NoNetworkPrefix ) )
		{
			throw new NoNetworkException( e );
		}
		catch ( InvalidOperationException e )
			when ( e is not FileException && FileException.TryParse( e.Message, out var error, out var path ) )
		{
			throw new FileException( error, path, e );
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
	/// Is this event the computer's hardware changing? See <see cref="TryGetAction"/> and <see cref="NaviApp.OnHardwareChanged"/>.
	/// </summary>
	public static bool TryGetHardwareChange( PanelEvent e ) => e.Name == "navi.hardware";

	/// <summary>
	/// Is this event the computer going online or offline? See <see cref="TryGetAction"/> and
	/// <see cref="NaviApp.OnNetworkChanged"/>.
	/// </summary>
	public static bool TryGetNetworkChange( PanelEvent e, out bool online )
	{
		if ( e.Name == "navi.network" && e.Value is bool value )
		{
			online = value;
			return true;
		}

		online = false;
		return false;
	}

	/// <summary>
	/// Is this event a drag over, or a drop on, one of the app's drop zones? <paramref name="phase"/> is "enter", "over",
	/// "leave" or "drop". See <see cref="TryGetAction"/> and <see cref="NaviApp.OnDrop"/>.
	/// </summary>
	public static bool TryGetDragEvent( PanelEvent e, out string phase, out DragEvent drag )
	{
		// Plain types in the event, so a Store app's own copy of this file reads what the computer's makes.
		if ( e.Name is "navi.drag-enter" or "navi.drag-over" or "navi.drag-leave" or "navi.drop"
			&& e.Value is ValueTuple<(string, string[])[], Vector2, int, Panel> value )
		{
			phase = e.Name == "navi.drop" ? "drop" : e.Name["navi.drag-".Length..];
			var offers = value.Item1.Select( x => new DragOffer( x.Item1, x.Item2 ) ).ToArray();
			drag = new DragEvent( new DragPayload( offers ), value.Item2, (DragEffects)value.Item3, value.Item4 );
			return true;
		}

		phase = null;
		drag = null;
		return false;
	}

	/// <summary>
	/// Is this event the end of a drag this app started? See <see cref="NaviApp.OnDragEnded"/>.
	/// </summary>
	public static bool TryGetDragEnded( PanelEvent e, out DragEffects effect )
	{
		if ( e.Name == "navi.drag-ended" && e.Value is int value )
		{
			effect = (DragEffects)value;
			return true;
		}

		effect = DragEffects.None;
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
	/// Is this event a keyboard shortcut for the app? See <see cref="TryGetAction"/> and <see cref="NaviApp.OnShortcut"/>.
	/// </summary>
	public static bool TryGetShortcut( PanelEvent e, out string keys )
	{
		if ( e.Name == "navi.shortcut" && e.Value is string value )
		{
			keys = value;
			return true;
		}

		keys = null;
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
		/// .napp. It is safe to call every frame. In a shared app only the host's copy should call it. In multiplayer, an
		/// app that isn't shared sets it for its own player only: everyone else sees the .napp's title.
		/// </summary>
		public Task SetTitleAsync( string title ) => navi.CallAsync( "window.title", title ?? "" );

		/// <summary>
		/// Change what the title bar says after the app's name, up to 32 characters, on this client only: only the player
		/// using the computer from here sees it, and everyone else sees the title in the .napp. It is never sent, so any copy
		/// may call it, a shared app's viewer too. For what is the player's own, such as a web page's title. Empty clears it.
		/// </summary>
		public Task SetLocalTitleAsync( string title ) => navi.CallAsync( "window.local-title", title ?? "" );


		/// <summary>
		/// Ask to be told before the player closes the window this panel is in, e.g. while it has unsaved changes:
		/// <see cref="NaviApp.OnCloseRequested"/> comes instead of the close. Turn it off once there is nothing to lose. It
		/// can't keep the window open: closing it again within a few seconds closes it, unless the player clicked in the
		/// window since (e.g. on Cancel). Only the player's closes ask; the app's own <see cref="CloseAsync()"/> doesn't. In
		/// multiplayer only a shared app is asked.
		/// </summary>
		public Task SetCloseGuardAsync( bool guard ) => navi.CallAsync( "window.close-guard", guard );


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

		/// <summary>
		/// Read, change and delete every file on the computer. Without it an app has only its own folder
		/// (<see cref="FileCalls.GetAppFolderAsync"/>).
		/// </summary>
		public const string Files = "navi.files.access";

		/// <summary>
		/// Read and send the direct messages of the player at the computer (<see cref="MessageCalls"/>).
		/// </summary>
		public const string Messages = "navi.messages.access";

		/// <summary>
		/// See through the camera on top of the computer's screen (<see cref="CameraCalls"/>).
		/// </summary>
		public const string Camera = "navi.camera.access";

		/// <summary>
		/// Run scripts on the computer (<see cref="ScriptCalls"/>). A script can reach most of the game.
		/// </summary>
		public const string Scripts = "navi.scripts.run";
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
	/// An installed app, and whether the player muted its notifications.
	/// </summary>
	public sealed record NotificationApp( string AppId, bool Muted );

	/// <summary>
	/// The camera on top of the computer's screen. The computer owns it; an app only gets the picture.
	/// </summary>
	public sealed class CameraCalls( Navi navi )
	{
		/// <summary>
		/// The picture the camera sees, as a texture of the given size in pixels. Needs <see cref="Permission.Camera"/>.
		/// The computer draws into it only while apps keep asking, so call this again about every half a second while the
		/// picture is shown (the same texture comes back while the size stays).
		/// </summary>
		public Task<Texture> GetViewAsync( int width, int height ) => navi.CallAsync<Texture>( "camera.view", width, height );

		/// <summary>
		/// Take a picture with the camera: the JPEG file of what it sees now
		/// (<see cref="FileCalls.WriteBytesAsync"/> saves it). Needs <see cref="Permission.Camera"/>, and the camera on:
		/// <see cref="GetViewAsync"/> has to have been asked for a moment ago.
		/// </summary>
		public Task<byte[]> TakePhotoAsync() => navi.CallAsync<byte[]>( "camera.photo" );
	}

	/// <summary>
	/// How a script ran: <see cref="Ok"/>, or <see cref="Message"/> with where in the source it went wrong (a range of
	/// characters, 0 and 0 when it isn't about a place).
	/// </summary>
	public sealed record ScriptResult( bool Ok, string Message, int Start, int Length );

	/// <summary>
	/// Scripts, the engine's experimental run-time scripting. The game turns them on for a computer and it is off by default,
	/// so ask <see cref="IsEnabledAsync"/> before offering to run one.
	/// </summary>
	public sealed class ScriptCalls( Navi navi )
	{
		/// <summary>
		/// Whether the game lets this computer run scripts.
		/// </summary>
		public Task<bool> IsEnabledAsync() => navi.CallAsync<bool>( "scripting.enabled" );

		/// <summary>
		/// Run the source as a script on the host's computer. Needs <see cref="Permission.Scripts"/>, and scripting on. It
		/// stops after a fixed number of instructions. Failing to run is a <see cref="ScriptResult"/>, not an exception.
		/// </summary>
		public async Task<ScriptResult> RunAsync( string source )
		{
			var (ok, message, start, length) = await navi.CallAsync<(bool, string, int, int)>( "scripting.run", source ?? "" );
			return new ScriptResult( ok, message, start, length );
		}
	}

	// Hardware.

	/// <summary>
	/// What hardware can provide. A computer is online with <see cref="Network"/> and plays sounds with <see cref="Audio"/>;
	/// a game may add its own.
	/// </summary>
	public static class Capability
	{
		public const string Network = "network";
		public const string Audio = "audio";
	}

	/// <summary>
	/// A piece of hardware installed in the computer, and the <see cref="Capability"/> values it provides.
	/// </summary>
	/// <remarks>
	/// <paramref name="Enabled"/> is false while the player has it turned off: it is installed but provides nothing.
	/// </remarks>
	public sealed record HardwareInfo( string Id, string Name, string Description, string Icon, IReadOnlyList<string> Capabilities,
		bool Enabled = true );

	/// <summary>
	/// The hardware installed in the computer. The game installs and removes it, the player can turn it off; apps hear of it
	/// in <see cref="NaviApp.OnHardwareChanged"/>.
	/// </summary>
	public sealed class HardwareCalls( Navi navi )
	{
		public async Task<IReadOnlyList<HardwareInfo>> ListAsync()
		{
			var list = await navi.CallAsync<(string, string, string, string, string[], bool)[]>( "hardware.list" );
			return list.Select( x => new HardwareInfo( x.Item1, x.Item2, x.Item3, x.Item4, x.Item5, x.Item6 ) ).ToArray();
		}
	}

	/// <summary>
	/// Whether the computer is on the network, and its IP address there (empty while it isn't).
	/// </summary>
	public sealed record NetworkStatus( bool Online, string Address );

	/// <summary>
	/// Starts the message of the error a call that needs the network gets on a computer that is offline.
	/// </summary>
	public const string NoNetworkPrefix = "No network: ";

	/// <summary>
	/// A call that goes over the network (the Store, Messages) failed because the computer has no network connection: it
	/// has no network card. Nothing was sent. Check <see cref="NetworkCalls.GetStatusAsync"/> first, or wait for
	/// <see cref="NaviApp.OnNetworkChanged"/>.
	/// </summary>
	public sealed class NoNetworkException( Exception inner = null )
		: InvalidOperationException( NoNetworkPrefix + "this computer has no network connection.", inner );

	/// <summary>
	/// The computer's network connection. A computer without a network card is offline: it can't reach the Store, Messages
	/// or other computers, and those calls throw <see cref="NoNetworkException"/>.
	/// </summary>
	public sealed class NetworkCalls( Navi navi )
	{
		public async Task<NetworkStatus> GetStatusAsync()
		{
			var (online, address) = await navi.CallAsync<(bool, string)>( "network.status" );
			return new NetworkStatus( online, address ?? "" );
		}

		/// <summary>
		/// Is the computer online?
		/// </summary>
		public async Task<bool> IsOnlineAsync() => (await GetStatusAsync()).Online;
	}

	/// <summary>
	/// The computer's speakers. Sounds play from the computer, for everyone near it. Without a sound card the computer
	/// plays no sounds, only a beep. Nothing here throws: a sound that can't play is just not played.
	/// </summary>
	public sealed class AudioCalls( Navi navi )
	{
		/// <summary>
		/// Does the computer have a sound card?
		/// </summary>
		public Task<bool> IsAvailableAsync() => Quietly( "audio.status" );

		/// <summary>
		/// Play a sound (a <c>.sound</c> resource path, e.g. "sounds/kenney/ui/ui.button.press.sound") at
		/// <paramref name="volume"/> (0 to 1). Returns false, playing nothing, without a sound card, or when the app plays
		/// too many at once.
		/// </summary>
		public Task<bool> PlayAsync( string sound, float volume = 1f ) => Quietly( "audio.play", sound ?? "", volume );

		/// <summary>
		/// Beep from the computer's own speaker, which works without a sound card. For telling the player something finished
		/// or needs them. Returns false if the computer beeped a moment ago.
		/// </summary>
		public Task<bool> BeepAsync() => Quietly( "audio.beep" );

		async Task<bool> Quietly( string method, params object[] args )
		{
			try
			{
				return await navi.CallAsync( method, args ) is true;
			}
			catch ( Exception )
			{
				return false;
			}
		}
	}

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
		/// Every installed app, core apps too, and whether each one's notifications are muted.
		/// </summary>
		public async Task<IReadOnlyList<NotificationApp>> ListAppsAsync()
		{
			var apps = await navi.CallAsync<(string, bool)[]>( "notifications.apps" );
			return apps.Select( x => new NotificationApp( x.Item1, x.Item2 ) ).ToArray();
		}

		/// <summary>
		/// Mute another app's notifications, or unmute them. Needs <see cref="Permission.ChangeSettings"/>. False if the app
		/// isn't installed, or is this one (core apps may change their own).
		/// </summary>
		public Task<bool> SetMutedAsync( string appId, bool muted ) =>
			navi.CallAsync<bool>( "notifications.set-muted", appId, muted );
	}

	/// <summary>
	/// What a file system entry is.
	/// </summary>
	public enum FileKind
	{
		Folder,
		File,

		/// <summary>
		/// Points at a path, like a Unix symlink. Paths through it are followed, so it acts as what it points at. It
		/// breaks if that moves.
		/// </summary>
		Symlink,

		/// <summary>
		/// Points at an entry itself, like a macOS alias or a Windows shortcut, so it still finds it after a move or
		/// rename. Paths don't go through it: resolve it with <see cref="FileCalls.ResolveAliasAsync"/>.
		/// </summary>
		Alias,
	}

	/// <summary>
	/// A file, folder or link. <paramref name="Size"/> is a file's size in bytes. <paramref name="Target"/> is a
	/// symlink's target path, or the path an alias was made to. <paramref name="TrashedFrom"/> is where an entry in the
	/// trash came from. <paramref name="Items"/> counts what a folder holds. <paramref name="IsDrive"/> is a drive's top
	/// folder.
	/// </summary>
	public sealed record FileInfo(
		string Name,
		string Path,
		FileKind Kind,
		long Size,
		DateTimeOffset Created,
		DateTimeOffset Modified,
		string Target,
		string TrashedFrom,
		int Items,
		bool IsDrive )
	{
		public bool IsFolder => Kind == FileKind.Folder;
	}

	/// <summary>
	/// A drive in the computer, and where it is in its files. The one inside is at "/", removable ones in "/Volumes".
	/// Sizes are in bytes.
	/// </summary>
	public sealed record DriveInfo( string Id, string Label, string Path, long Capacity, long Used, bool Removable )
	{
		public long Free => Math.Max( 0, Capacity - Used );
	}

	/// <summary>
	/// Why a file call failed.
	/// </summary>
	public enum FileError
	{
		NotFound,
		AlreadyExists,
		NotAFolder,
		IsAFolder,
		NotEmpty,
		NoSpace,
		InvalidName,
		LinkLoop,

		/// <summary>
		/// Part of how the computer is laid out (a drive, /Volumes, /Library, the trash, the app's own folder), or a
		/// folder moved into itself.
		/// </summary>
		Protected,

		/// <summary>
		/// The files are on the host, and this is a client's own copy of the app. Make the app Shared to use files in
		/// multiplayer.
		/// </summary>
		Unavailable,

		Unknown,
	}

	const string FilePrefix = "File error ";

	/// <summary>
	/// A file call failed. A missing permission throws <see cref="MissingPermissionException"/> instead.
	/// </summary>
	public sealed class FileException : InvalidOperationException
	{
		public FileException( FileError error, string path, Exception inner = null )
			: base( $"{FilePrefix}[{error}]: {path}", inner )
		{
			Error = error;
			Path = path;
		}

		public FileError Error { get; }
		public string Path { get; }

		/// <summary>
		/// Read the computer's error message: "File error [NotFound]: /path".
		/// </summary>
		internal static bool TryParse( string message, out FileError error, out string path )
		{
			error = FileError.Unknown;
			path = null;

			var end = message?.IndexOf( "]: ", StringComparison.Ordinal ) ?? -1;
			if ( end < 0 || !message.StartsWith( FilePrefix + "[", StringComparison.Ordinal ) )
				return false;

			error = Enum.TryParse<FileError>( message[(FilePrefix.Length + 1)..end], out var known ) ? known : FileError.Unknown;
			path = message[(end + 3)..];
			return true;
		}
	}

	/// <summary>
	/// The computer's files, as on macOS and Linux: one tree from "/", case-sensitive names, absolute paths
	/// ("/Documents/notes.txt"). Only files made on the computer are there. An app has its own folder
	/// (<see cref="GetAppFolderAsync"/>) and needs <see cref="Permission.Files"/> for anything else. A failed call throws
	/// <see cref="FileException"/>. Only the host has the files: in multiplayer, use them from a Shared app.
	/// </summary>
	public sealed class FileCalls( Navi navi )
	{
		/// <summary>
		/// Goes up whenever a file changes, or a drive is put in or taken out. Poll it to know when to list again.
		/// </summary>
		public Task<int> GetVersionAsync() => navi.CallAsync<int>( "files.version" );

		/// <summary>
		/// The folder this app has to itself, e.g. "/Library/Apps/fractitude.napp_notes". It needs no permission.
		/// Uninstalling the app deletes it.
		/// </summary>
		public Task<string> GetAppFolderAsync() => navi.CallAsync<string>( "files.app-folder" );

		/// <summary>
		/// Ask the player to choose a file, in a File Picker window of the computer's own (not of this app). Returns its
		/// path, or null if they cancelled. The app can then read that file, with or without <see cref="Permission.Files"/>,
		/// and gets no look at any other. <paramref name="fileTypes"/> are the extensions shown, without the dot (every
		/// file when empty). The picker opens at <paramref name="startFolder"/>, or at "/" without one. One picker is open
		/// at a time: asking while one is open throws.
		/// </summary>
		public async Task<string> PickAsync( IEnumerable<string> fileTypes = null, string startFolder = "/" ) =>
			await navi.CallAsync( "files.pick", fileTypes?.ToArray() ?? Array.Empty<string>(), startFolder ?? "/" ) as string;

		public async Task<IReadOnlyList<DriveInfo>> GetDrivesAsync()
		{
			var drives = await navi.CallAsync<(string, string, string, long, long, bool)[]>( "files.drives" );
			return drives.Select( x => new DriveInfo( x.Item1, x.Item2, x.Item3, x.Item4, x.Item5, x.Item6 ) ).ToArray();
		}

		/// <summary>
		/// What a folder holds, sorted by name. Drives in it (in "/Volumes") are folders with <see cref="FileInfo.IsDrive"/>.
		/// </summary>
		public async Task<IReadOnlyList<FileInfo>> ListAsync( string path )
		{
			var entries = await navi.CallAsync<(string, string, int, long, long, long, (string, string, int, bool))[]>(
				"files.list", path );
			return entries.Select( ToInfo ).ToArray();
		}

		/// <summary>
		/// One entry, or null if there is none. With <paramref name="followLinks"/> a symlink at the path gives what it
		/// points at; without, the link itself.
		/// </summary>
		public async Task<FileInfo> GetInfoAsync( string path, bool followLinks = true ) =>
			await navi.CallAsync( "files.info", path, followLinks )
				is ValueTuple<string, string, int, long, long, long, ValueTuple<string, string, int, bool>> entry
				? ToInfo( entry )
				: null;

		public Task<string> ReadTextAsync( string path ) => navi.CallAsync<string>( "files.read", path );

		/// <summary>
		/// Write a text file, making it or replacing what is in it. Returns its path.
		/// </summary>
		public Task<string> WriteTextAsync( string path, string text ) =>
			navi.CallAsync<string>( "files.write", path, text ?? "" );

		/// <summary>
		/// Read a file's contents as bytes: a binary file's data, or a text file's text as UTF-8.
		/// </summary>
		public Task<byte[]> ReadBytesAsync( string path ) => navi.CallAsync<byte[]>( "files.read-bytes", path );

		/// <summary>
		/// A picture made small by the computer, at most 128 pixels on its longest side, as PNG: much less to read than the
		/// picture. Null for a file that isn't a jpg or png picture, or one too big to make small.
		/// </summary>
		public Task<byte[]> GetThumbnailAsync( string path ) => navi.CallAsync<byte[]>( "files.thumbnail", path );

		/// <summary>
		/// A picture made small enough to send, at most 1600 pixels on its longest side, as PNG: enough to look at and zoom
		/// into. A shared app that shows a picture asks for this on the host's copy, and a client's copy (the seated
		/// player's, or anyone watching the screen) is sent this one picture, never the file. Null for a file that isn't a
		/// jpg or png picture, or one too big to make small.
		/// </summary>
		public Task<byte[]> GetPreviewAsync( string path ) => navi.CallAsync<byte[]>( "files.preview", path );

		/// <summary>
		/// Each file's version by its contents: the SHA-256 of what is in it, as hex, which changes whenever the contents
		/// do. Empty for a path that isn't a file, or that the app may not read. At most 1024 paths.
		/// </summary>
		public async Task<IReadOnlyList<string>> GetHashesAsync( IReadOnlyList<string> paths ) =>
			await navi.CallAsync<string[]>( "files.hashes", (object)(paths?.ToArray() ?? []) );

		/// <summary>
		/// Write a binary file (a picture, say), making it or replacing what is in it. Returns its path.
		/// </summary>
		public Task<string> WriteBytesAsync( string path, byte[] data ) =>
			navi.CallAsync<string>( "files.write-bytes", path, data ?? [] );

		public Task<string> CreateFolderAsync( string path ) => navi.CallAsync<string>( "files.create-folder", path );

		/// <summary>
		/// Make a symlink at <paramref name="path"/> to <paramref name="target"/>, absolute or relative to the link's
		/// folder. The target needn't exist.
		/// </summary>
		public Task<string> CreateSymlinkAsync( string path, string target ) =>
			navi.CallAsync<string>( "files.symlink", path, target );

		/// <summary>
		/// Make an alias at <paramref name="path"/> to the entry at <paramref name="target"/>, which must exist.
		/// </summary>
		public Task<string> CreateAliasAsync( string path, string target ) =>
			navi.CallAsync<string>( "files.alias", path, target );

		/// <summary>
		/// Where an alias points now, or null if what it pointed at is gone.
		/// </summary>
		public Task<string> ResolveAliasAsync( string path ) => navi.CallAsync<string>( "files.resolve-alias", path );

		/// <summary>
		/// Move or rename an entry. <paramref name="to"/> is its new full path and must not exist. A symlink moves as
		/// itself.
		/// </summary>
		public Task<string> MoveAsync( string from, string to ) => navi.CallAsync<string>( "files.move", from, to );

		/// <summary>
		/// Copy an entry and everything in it. Links are copied as links.
		/// </summary>
		public Task<string> CopyAsync( string from, string to ) => navi.CallAsync<string>( "files.copy", from, to );

		/// <summary>
		/// Move an entry to the trash of its drive. Returns where it is now.
		/// </summary>
		public Task<string> TrashAsync( string path ) => navi.CallAsync<string>( "files.trash", path );

		/// <summary>
		/// Put an entry in the trash back where it was. Needs <see cref="Permission.Files"/>.
		/// </summary>
		public Task<string> RestoreAsync( string path ) => navi.CallAsync<string>( "files.restore", path );

		/// <summary>
		/// Delete an entry for good, with everything in it. A symlink or alias is deleted, not what it points at.
		/// </summary>
		public Task DeleteAsync( string path ) => navi.CallAsync( "files.delete", path );

		/// <summary>
		/// Take out the removable drive whose top folder is <paramref name="path"/>. Needs <see cref="Permission.Files"/>.
		/// False if it isn't one.
		/// </summary>
		public Task<bool> EjectAsync( string path ) => navi.CallAsync<bool>( "files.eject", path );

		/// <summary>
		/// Open a file with the app for its kind, e.g. a ".txt" with Text Edit: the computer picks the app (the ones whose
		/// .napp lists the extension under FileTypes), launches it, and leaves the file for it to take with
		/// <see cref="TakeOpenedAsync"/>. False if no installed app opens that kind of file. The file must be one this app
		/// can reach.
		/// </summary>
		public Task<bool> OpenAsync( string path ) => navi.CallAsync<bool>( "files.open", path );

		/// <summary>
		/// Open a file with the app <paramref name="appId"/> instead of the computer's first choice. It must be one of
		/// <see cref="GetOpenersAsync"/>; False if it isn't.
		/// </summary>
		public Task<bool> OpenWithAsync( string path, string appId ) => navi.CallAsync<bool>( "files.open", path, appId );

		/// <summary>
		/// The installed apps that can open this file, the computer's first choice first: their ids, titles and icons.
		/// None for a folder, or a kind of file no app opens.
		/// </summary>
		public async Task<IReadOnlyList<(string Id, string Title, string Icon)>> GetOpenersAsync( string path )
		{
			var apps = await navi.CallAsync<(string, string, string)[]>( "files.openers", path );
			return apps.Select( x => (x.Item1, x.Item2, x.Item3) ).ToArray();
		}

		/// <summary>
		/// The files the player opened with this app since it last asked, oldest first, as paths with symlinks followed.
		/// Each is given once. An app that lists FileTypes in its .napp polls this a few times a second.
		/// </summary>
		public async Task<IReadOnlyList<string>> TakeOpenedAsync() => await navi.CallAsync<string[]>( "files.opened" );

		/// <summary>
		/// The trash folder of the drive <paramref name="path"/> is on.
		/// </summary>
		public Task<string> GetTrashPathAsync( string path = "/" ) => navi.CallAsync<string>( "files.trash-path", path );

		static FileInfo ToInfo( (string, string, int, long, long, long, (string, string, int, bool)) x ) => new(
			x.Item1, x.Item2, (FileKind)x.Item3, x.Item4,
			DateTimeOffset.FromUnixTimeSeconds( x.Item5 ), DateTimeOffset.FromUnixTimeSeconds( x.Item6 ),
			x.Item7.Item1, x.Item7.Item2, x.Item7.Item3, x.Item7.Item4 );
	}

	/// <summary>
	/// The longest a direct message can be, in characters. More is cut off.
	/// </summary>
	public const int MaxMessageText = 400;

	/// <summary>
	/// The player at the computer.
	/// </summary>
	public sealed record MessageUser( string Id, string Name )
	{
		/// <summary>Nobody is in the seat.</summary>
		public bool IsNobody => string.IsNullOrEmpty( Id );
	}

	/// <summary>
	/// Another player: whether they are in the game now, the newest message between them and the player at the computer
	/// (<paramref name="LastAt"/> is 0 if there is none), and how many of their messages that player hasn't read.
	/// </summary>
	public sealed record MessageContact( string Id, string Name, bool Online, string LastText, DateTimeOffset LastAt, int Unread, bool LastMine );

	/// <summary>
	/// One direct message. <paramref name="Mine"/> is whether the player at the computer sent it.
	/// </summary>
	public sealed record ChatMessage( long Id, bool Mine, string Text, DateTimeOffset At );

	/// <summary>
	/// Direct messages between players, as the player in the computer's seat. They are the game's, not the computer's: a
	/// message reaches the other player wherever they sit down, and the host keeps it. Needs
	/// <see cref="Permission.Messages"/>. With nobody in the seat the calls answer with nothing. Only the host has the
	/// messages: in multiplayer, use them from a Shared app.
	/// </summary>
	public sealed class MessageCalls( Navi navi )
	{
		/// <summary>
		/// Goes up when something about the player's messages may have changed, including who is in the seat. Poll it to know
		/// when to list again.
		/// </summary>
		public Task<int> GetVersionAsync() => navi.CallAsync<int>( "messages.version" );

		/// <summary>
		/// The player in the seat: <see cref="MessageUser.IsNobody"/> if there is none.
		/// </summary>
		public async Task<MessageUser> GetMeAsync()
		{
			var (id, name) = await navi.CallAsync<(string, string)>( "messages.me" );
			return new MessageUser( id, name );
		}

		/// <summary>
		/// The other players the host has seen, in the game now or not, each with the newest message between them and the
		/// player in the seat.
		/// </summary>
		public async Task<IReadOnlyList<MessageContact>> ListContactsAsync()
		{
			var contacts = await navi.CallAsync<(string, string, bool, string, long, int, bool)[]>( "messages.contacts" );
			return contacts.Select( x => new MessageContact( x.Item1, x.Item2, x.Item3, x.Item4,
				DateTimeOffset.FromUnixTimeSeconds( x.Item5 ), x.Item6, x.Item7 ) ).ToArray();
		}

		/// <summary>
		/// The newest <paramref name="take"/> messages (up to 100) between the player in the seat and another, oldest first.
		/// </summary>
		public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync( string playerId, int take = 30 )
		{
			var messages = await navi.CallAsync<(long, bool, string, long)[]>( "messages.history", playerId ?? "", take );
			return messages.Select( x => new ChatMessage( x.Item1, x.Item2, x.Item3, DateTimeOffset.FromUnixTimeSeconds( x.Item4 ) ) )
				.ToArray();
		}

		/// <summary>
		/// Send a message to another player, who may be away: they get it when they come back. It is made one line and cut to
		/// <see cref="MaxMessageText"/>. False if nothing was sent: the text was empty, the player is unknown or is the
		/// sender, or they are sending too fast.
		/// </summary>
		public Task<bool> SendAsync( string playerId, string text ) => navi.CallAsync<bool>( "messages.send", playerId ?? "", text ?? "" );

		/// <summary>
		/// The player in the seat has read what another player sent so far.
		/// </summary>
		public Task MarkReadAsync( string playerId ) => navi.CallAsync( "messages.read", playerId ?? "" );
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

		/// <summary>
		/// In a shared app, keep the player's keys on their own client while <paramref name="local"/> is true: neither
		/// their input actions nor their shortcuts go to the host's copy, which then sees none. For what the player types
		/// that is theirs alone, such as a web page. It is never sent, so any copy may call it, a shared app's viewer too;
		/// call it on every copy that may be the player's. Does nothing in an app that isn't shared, whose keys stay here.
		/// </summary>
		public Task KeepLocalAsync( bool local ) => navi.CallAsync( "input.keep-local", local );
	}

	/// <summary>
	/// What a drop does with what was dragged. A drag allows some of them (what its source can do), a drop zone accepts some
	/// (what its app can do), and the computer picks one of those from the keys the player holds
	/// (<see cref="DragCalls.ChooseEffect"/>): Ctrl copies, Shift moves, Alt links.
	/// </summary>
	[Flags]
	public enum DragEffects
	{
		None = 0,

		/// <summary>The target gets a copy; the source keeps what it had.</summary>
		Copy = 1,

		/// <summary>The target takes it; the source gives it up (it hears <see cref="NaviApp.OnDragEnded"/>).</summary>
		Move = 2,

		/// <summary>The target refers to it, such as opening a file where it is, without taking or copying it.</summary>
		Link = 4,

		All = Copy | Move | Link,
	}

	/// <summary>
	/// One thing a drag carries, in one form: its <see cref="Kind"/> (<see cref="DragCalls.FilesKind"/> or
	/// <see cref="DragCalls.TextKind"/>) and its items.
	/// </summary>
	public sealed record DragOffer( string Kind, IReadOnlyList<string> Items );

	/// <summary>
	/// What a drag carries: the same thing in one or more forms, like a data object. A target takes the form it understands.
	/// </summary>
	public sealed record DragPayload( IReadOnlyList<DragOffer> Offers )
	{
		/// <summary>The offer of this kind, or null.</summary>
		public DragOffer Find( string kind ) => Offers?.FirstOrDefault( x => x.Kind == kind );

		/// <summary>
		/// The paths of the computer's files carried, absolute and cleaned up by the computer. Having them gives no access: a
		/// target reads them with its own permissions (<c>Navi.Files</c>).
		/// </summary>
		public IReadOnlyList<string> Files => Find( DragCalls.FilesKind )?.Items ?? [];

		/// <summary>The text carried, or null.</summary>
		public string Text => Find( DragCalls.TextKind )?.Items.FirstOrDefault();

		public static DragPayload OfFiles( IReadOnlyList<string> paths ) => new( [new DragOffer( DragCalls.FilesKind, paths )] );
	}

	/// <summary>
	/// A drag over a drop zone, or a drop on it.
	/// </summary>
	/// <param name="Payload">What is dragged.</param>
	/// <param name="Position">Where the mouse is, in the zone's panel pixels from its top left.</param>
	/// <param name="Effect">What a drop here would do, or does.</param>
	/// <param name="Zone">The panel that was made a drop zone.</param>
	public sealed record DragEvent( DragPayload Payload, Vector2 Position, DragEffects Effect, Panel Zone );

	/// <summary>
	/// Drag and drop between windows, across the whole desktop, run by the computer like an OS does. The app that is dragged
	/// <i>from</i> starts the drag (<see cref="BeginAsync"/>) while the player holds the mouse button down over it, saying what
	/// it carries and what a drop may do. An app that is dropped <i>on</i> makes drop zones of its panels
	/// (<see cref="AcceptDrops"/>): a whole window, or just part of it. The computer finds the zone under the mouse and tells
	/// the app <see cref="NaviApp.OnDragEnter"/>, <see cref="NaviApp.OnDragOver"/>, <see cref="NaviApp.OnDragLeave"/> and
	/// <see cref="NaviApp.OnDrop"/>, and the source <see cref="NaviApp.OnDragEnded"/>. The computer draws the dragged thing and
	/// marks the zone (<c>navi-drop-over</c> class), so an app can't fake either. It all stays on the player's client.
	/// </summary>
	public sealed class DragCalls( Navi navi )
	{
		/// <summary>The <see cref="DragOffer.Kind"/> of the computer's files (paths), and of text.</summary>
		public const string FilesKind = "files", TextKind = "text";

		/// <summary>The most paths one drag carries, and the longest a path, a label or a text can be.</summary>
		public const int MaxPaths = 256, MaxPath = 1024, MaxLabel = 40, MaxText = 1024;

		/// <summary>The most offers a drag carries.</summary>
		public const int MaxOffers = 4;

		/// <summary>The attributes the computer reads on a drop zone's panel.</summary>
		public const string ZoneEffectsAttribute = "navi-drop-effects", ZoneKindsAttribute = "navi-drop-kinds";

		/// <summary>
		/// Start a drag. Only while the player holds the left mouse button down over this app's window. Files need the Files
		/// permission (the app could not name them otherwise). False if the drag didn't start. <paramref name="label"/> is what
		/// the cursor carries, <paramref name="icon"/> a Material icon name, <paramref name="allowed"/> what a drop may do with it.
		/// Every copy of an app may call it, a shared app's viewer too: it never leaves this client.
		/// </summary>
		public Task<bool> BeginAsync( DragPayload payload, string label, string icon, DragEffects allowed )
		{
			var offers = payload?.Offers ?? [];
			return navi.CallAsync<bool>( "drag.begin", label ?? "", icon ?? "", (int)allowed,
				offers.Select( x => x.Kind ?? "" ).ToArray(), offers.Select( x => (x.Items ?? []).ToArray() ).ToArray() );
		}

		/// <summary>
		/// Start dragging files of the computer. See <see cref="BeginAsync"/>.
		/// </summary>
		public Task<bool> BeginFilesAsync( string label, string icon, IReadOnlyList<string> paths, DragEffects allowed = DragEffects.All ) =>
			BeginAsync( DragPayload.OfFiles( paths ?? [] ), label, icon, allowed );

		/// <summary>
		/// Make <paramref name="zone"/>, any panel of this app (the app's own panel for the whole window), a drop zone for
		/// <paramref name="kinds"/> (an empty list means any), accepting the <paramref name="accepts"/> effects. <see cref="DragEffects.None"/>
		/// makes it no zone. Call it every frame or whenever it changes: it only does anything when it did. Where zones overlap,
		/// the innermost one under the mouse is the target. The computer gives the zone the <c>navi-drop-over</c> class while a drop
		/// on it would be taken.
		/// </summary>
		public void AcceptDrops( Panel zone, DragEffects accepts, params string[] kinds )
		{
			if ( zone is null || !zone.IsValid )
				return;

			var effects = ((int)accepts).ToString();
			var list = string.Join( ",", kinds ?? [] );

			if ( zone.GetAttribute( ZoneEffectsAttribute, null ) != effects )
				zone.SetAttribute( ZoneEffectsAttribute, effects );

			if ( zone.GetAttribute( ZoneKindsAttribute, null ) != list )
				zone.SetAttribute( ZoneKindsAttribute, list );
		}

		/// <summary>
		/// Which of <paramref name="accepts"/> a drop does, out of what the drag <paramref name="allowed"/>, by the keys the
		/// player holds: Alt (or Ctrl and Shift) links, Ctrl copies, Shift moves. Without a key, or if the one asked for isn't
		/// on both lists, move, then copy, then link. None if they have nothing in common. The computer uses this to choose;
		/// an app only needs it to tell what a drop would do without being in a zone.
		/// </summary>
		public static DragEffects ChooseEffect( DragEffects allowed, DragEffects accepts, bool ctrl, bool shift, bool alt )
		{
			var both = allowed & accepts;
			if ( both == DragEffects.None )
				return DragEffects.None;

			var asked = alt || (ctrl && shift) ? DragEffects.Link : ctrl ? DragEffects.Copy : shift ? DragEffects.Move : DragEffects.None;
			if ( (both & asked) != DragEffects.None )
				return asked;

			return (both & DragEffects.Move) != DragEffects.None ? DragEffects.Move
				: (both & DragEffects.Copy) != DragEffects.None ? DragEffects.Copy
				: DragEffects.Link;
		}
	}
}
