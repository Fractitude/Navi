using Sandbox.UI;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Panel event names and classes shared with apps. They are plain strings, so apps that don't reference Navi can use them.
/// </summary>
public static class NaviEvents
{
	/// <summary>
	/// A call to the computer. See <see cref="NaviSystemCalls"/>.
	/// </summary>
	public const string Call = NaviSystemCalls.EventName;

	// Older ways to control the window, from before NaviSystemCalls. Kept for published apps.
	public const string Close = "navi.close";
	public const string Minimize = "navi.minimize";
	public const string ToggleMaximize = "navi.maximize";

	/// <summary>
	/// Class on an app's panel while it has the player's input (see <see cref="NaviInputCapture"/>), e.g. <c>HasClass( "navi-input" )</c>.
	/// </summary>
	public const string HasInputClass = "navi-input";

	/// <summary>
	/// Class on a shared app's panel on a client whose copy only draws. See <c>Navi.IsViewer</c>.
	/// </summary>
	public const string ViewerClass = "navi-viewer";

	/// <summary>
	/// Class on an app's panel on a client that isn't the one using the computer. See <c>Navi.IsWatching</c>.
	/// </summary>
	public const string WatchingClass = "navi-watching";

	/// <summary>
	/// The app asks for the lock screen's setup dialog (<c>Navi.Settings.OpenLockSetup</c>). Only core apps are listened to.
	/// </summary>
	public const string LockSetup = "navi.lock-setup";

	/// <summary>
	/// The player did something in a shared app (<c>Navi.Act</c>). Bubbles up from the app to its window.
	/// </summary>
	public const string Act = "navi.act";

	/// <summary>
	/// An action for the host's copy of a shared app, raised on the app's panel. See <c>NaviApp.OnAction</c>.
	/// </summary>
	public const string Action = "navi.action";

	/// <summary>
	/// The player clicked the app's tray icon or one of its menu items, raised on the app's main panel. See
	/// <c>NaviApp.OnTrayClick</c>.
	/// </summary>
	public const string Tray = "navi.tray";

	/// <summary>
	/// The player pressed a keyboard shortcut while the app was in front, raised on the app's panel with the keys, e.g.
	/// "ctrl+c". See <c>NaviApp.OnShortcut</c>.
	/// </summary>
	public const string Shortcut = "navi.shortcut";

	/// <summary>
	/// The player is closing the app's window, which asked to be told first (<c>Navi.Window.SetCloseGuardAsync</c>). Raised
	/// on the app's panel. See <c>NaviApp.OnCloseRequested</c>.
	/// </summary>
	public const string CloseRequest = "navi.close-request";

	/// <summary>
	/// Hardware was installed in or removed from the computer. Raised on every panel of every app. See
	/// <c>NaviApp.OnHardwareChanged</c>.
	/// </summary>
	public const string Hardware = "navi.hardware";

	/// <summary>
	/// The computer went online or offline, raised on every panel of every app with whether it is online now. See
	/// <c>NaviApp.OnNetworkChanged</c>.
	/// </summary>
	public const string Network = "navi.network";
}

/// <summary>
/// Window body. Creates the panel of <see cref="Window"/> and keeps it as its only child, and answers the calls the app makes
/// to the computer. Razor can't create a panel whose type is only known at runtime, so this does it in code. A headless
/// app's service has one too, in the desktop's hidden layer instead of a window.
/// </summary>
public sealed class NaviAppHost : Panel
{
	public NaviComputer Computer { get; set; }
	public NaviWindow Window { get; set; }

	Panel app;

	/// <summary>
	/// <see cref="NaviWindow.Key"/> of the window the panel was made for.
	/// </summary>
	string windowKey;

	/// <summary>
	/// The app is shared and this client isn't the host, so its copy only draws (<c>Navi.IsViewer</c>).
	/// </summary>
	bool viewer;

	// What was last passed between the app and the computer, so each is only passed on when it changes.
	string lastPublished;
	string lastState;

	bool IsShared => Window?.App.Shared ?? false;

	/// <summary>The calls behind <c>Navi.Window.SetLocalTitleAsync</c> and <c>Navi.Input.KeepLocalAsync</c>, answered here and
	/// never by the host.</summary>
	const string LocalTitleCall = "window.local-title", KeepInputLocalCall = "input.keep-local";

	public override void Tick()
	{
		base.Tick();

		var wantViewer = IsShared && Computer is { IsAuthority: false };

		// The host left and this client took over (or the other way round): the copy starts over in its new role.
		if ( app is null || !app.IsValid || windowKey != Window?.Key || viewer != wantViewer )
		{
			app?.Delete();
			Computer?.UnregisterAppHost( windowKey, this );
			app = null;
			windowKey = null;
			lastPublished = null;
			lastState = null;
			knownScroll = "";
			lastRemoteScroll = null;

			if ( Window is null )
				return;

			app = Window.Info.Create();
			windowKey = Window.Key;
			viewer = wantViewer;

			// Before the app's first tick, so it never runs as the host's copy here.
			app.SetClass( NaviEvents.ViewerClass, viewer );
			AddChild( app );
		}

		// A shared app's copy on the host runs for whoever is using the computer.
		var capture = Computer?.InputCapture == Window;
		app.SetClass( NaviEvents.HasInputClass, capture && (Computer.IsSeated || IsShared) );
		app.SetClass( NaviEvents.WatchingClass, Computer is not null && !Computer.IsUser );

		// Where tray clicks and a shared app's actions are delivered to. A viewer's copy gets neither.
		if ( Computer is not null && !viewer )
			Computer.RegisterAppHost( windowKey, this );

		if ( IsShared && Computer is not null )
		{
			TickShared( capture );
			TickScroll();
		}

		// A window with a set size decides the app's size. Otherwise the window fits the app.
		var fill = Window.Size is not null || Window.IsMaximized;
		app.Style.Width = fill ? Length.Percent( 100 ) : null;
		app.Style.Height = fill ? Length.Percent( 100 ) : null;
	}

	/// <summary>
	/// Pass a shared app's state on. The host's copy publishes and gets the user's input; a viewer's copy gets what the
	/// host published.
	/// </summary>
	void TickShared( bool capture )
	{
		if ( viewer )
		{
			var state = Computer.GetAppState( windowKey ) ?? "";
			if ( state != lastState )
			{
				lastState = state;
				app.SetAttribute( "navi-state", state );
			}

			return;
		}

		var published = app.GetAttribute( "navi-published", null );
		if ( published is not null && published != lastPublished )
		{
			lastPublished = published;
			Computer.SetAppState( windowKey, published );
		}

		var (down, pressed) = Computer.UserInput;
		app.SetAttribute( "navi-input-down", capture ? string.Join( ",", down ) : "" );
		app.SetAttribute( "navi-input-pressed", capture ? string.Join( ",", pressed ) : "" );
	}

	// Scrolling. Each copy scrolls its own panels, so the user's client sends where its lists are, and every other client
	// scrolls its copy to match. A list is named by the path of child indexes from the app's panel, e.g. "0/3".

	/// <summary>How often the user's scrolling is sent, in seconds.</summary>
	const float ScrollSendInterval = 0.1f;

	RealTimeSince sinceScrollSent;

	/// <summary>How often a watcher checks its lists are where the user's are, in seconds.</summary>
	const float ScrollRecheckInterval = 0.5f;

	RealTimeSince sinceScrollChecked;

	/// <summary>Where this copy's lists were, as last sent or applied. A change from it is the local player scrolling.</summary>
	string knownScroll = "";

	/// <summary>The scrolling the host passed on last.</summary>
	string lastRemoteScroll;

	void TickScroll()
	{
		var remote = Computer.GetAppScroll( windowKey );
		if ( remote != lastRemoteScroll )
		{
			lastRemoteScroll = remote;

			// The user's own client is where it came from, and may have scrolled on since.
			if ( !Computer.IsUser && remote is not null )
			{
				ApplyScroll( remote );
				knownScroll = EncodeScroll();
			}
		}

		// A watcher's list may fill in after the scrolling arrived, or clamp it while it was shorter: catch up now and
		// then. Only while someone else has the seat, so it never fights the local player's own scrolling.
		if ( !Computer.CanUse && remote is not null && sinceScrollChecked > ScrollRecheckInterval )
		{
			sinceScrollChecked = 0;

			if ( EncodeScroll() != remote )
			{
				ApplyScroll( remote );
				knownScroll = EncodeScroll();
			}
		}

		if ( !Computer.CanUse || sinceScrollSent < ScrollSendInterval )
			return;

		sinceScrollSent = 0;

		var scroll = EncodeScroll();
		if ( scroll == knownScroll )
			return;

		knownScroll = scroll;
		Computer.UserScroll( Window.App.Id, Window.Id, scroll );
	}

	/// <summary>
	/// The app's scrollable panels and where they are scrolled to, in panel pixels so screen scaling doesn't matter:
	/// "0/3:0,120;1:0,40".
	/// </summary>
	string EncodeScroll()
	{
		var parts = new List<string>();
		var culture = System.Globalization.CultureInfo.InvariantCulture;

		foreach ( var (path, panel) in Scrollables( app, "" ) )
		{
			var offset = panel.ScrollOffset * panel.ScaleFromScreen;
			parts.Add( $"{path}:{System.MathF.Round( offset.x ).ToString( culture )},{System.MathF.Round( offset.y ).ToString( culture )}" );
		}

		return string.Join( ";", parts );
	}

	void ApplyScroll( string scroll )
	{
		var panels = Scrollables( app, "" ).ToDictionary( x => x.Path, x => x.Panel );
		var culture = System.Globalization.CultureInfo.InvariantCulture;

		foreach ( var part in scroll.Split( ';', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( part.Split( ':' ) is not [var path, var value] || !panels.TryGetValue( path, out var panel )
				|| value.Split( ',' ) is not [var xs, var ys] )
				continue;

			if ( !float.TryParse( xs, System.Globalization.NumberStyles.Float, culture, out var x ) || !float.IsFinite( x )
				|| !float.TryParse( ys, System.Globalization.NumberStyles.Float, culture, out var y ) || !float.IsFinite( y ) )
				continue;

			panel.ScrollVelocity = Vector2.Zero;
			panel.ScrollOffset = new Vector2( x, y ) * panel.ScaleToScreen;
		}
	}

	/// <summary>
	/// The panels under <paramref name="panel"/> that scroll, with their paths.
	/// </summary>
	static IEnumerable<(string Path, Panel Panel)> Scrollables( Panel panel, string path )
	{
		if ( panel is null || !panel.IsValid )
			yield break;

		if ( panel.HasScrollX || panel.HasScrollY )
			yield return (path, panel);

		var index = 0;
		foreach ( var child in panel.Children )
		{
			foreach ( var found in Scrollables( child, path == "" ? $"{index}" : $"{path}/{index}" ) )
				yield return found;

			index++;
		}
	}

	/// <summary>
	/// On the host: give a shared app's copy an action the user took (<c>Navi.Act</c>).
	/// </summary>
	internal void DeliverAction( string action, string[] args )
	{
		if ( app is not null && app.IsValid && !viewer )
			app.CreateEvent( new PanelEvent( NaviEvents.Action, app ) { Value = (action, args) } );
	}

	/// <summary>
	/// Give the app's copy here a keyboard shortcut: on the host for a shared app, on the user's client for one that isn't.
	/// </summary>
	internal void DeliverShortcut( string keys )
	{
		if ( app is not null && app.IsValid && !viewer )
			app.CreateEvent( new PanelEvent( NaviEvents.Shortcut, app ) { Value = keys } );
	}

	/// <summary>
	/// On the host: tell the app's copy the player is closing its window. False if there is no copy here to tell.
	/// </summary>
	internal bool DeliverCloseRequest()
	{
		if ( app is null || !app.IsValid || viewer )
			return false;

		app.CreateEvent( new PanelEvent( NaviEvents.CloseRequest, app ) );
		return true;
	}

	/// <summary>
	/// On this client: tell the app's copy the computer's hardware changed. A viewer's copy only draws, so it isn't told.
	/// </summary>
	internal void DeliverHardwareChanged()
	{
		if ( app is not null && app.IsValid && !viewer )
			app.CreateEvent( new PanelEvent( NaviEvents.Hardware, app ) );
	}

	/// <summary>
	/// On this client: tell the app's copy the computer went online or offline.
	/// </summary>
	internal void DeliverNetworkChanged( bool online )
	{
		if ( app is not null && app.IsValid && !viewer )
			app.CreateEvent( new PanelEvent( NaviEvents.Network, app ) { Value = online } );
	}

	/// <summary>
	/// On this client: give the app's panel the click on its tray icon, or on a menu item.
	/// </summary>
	internal void DeliverTray( string item )
	{
		if ( app is not null && app.IsValid && !viewer )
			app.CreateEvent( new PanelEvent( NaviEvents.Tray, app ) { Value = item ?? "" } );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();
		Computer?.UnregisterAppHost( windowKey, this );
	}

	/// <summary>
	/// Handle the <see cref="NaviEvents"/> that bubble up from the app.
	/// </summary>
	protected override void OnEvent( PanelEvent e )
	{
		var id = Window?.App.Id;
		var windowId = Window?.Id;

		if ( Computer is not null && id is not null )
		{
			switch ( e.Name )
			{
				// This client's own title bar, which is never sent or saved, so any copy may set it here, a viewer's too. See
				// NaviComputer.SetLocalTitle.
				case NaviEvents.Call when e.Value is System.ValueTuple<string, object[], TaskCompletionSource<object>> call
					&& call.Item1 == LocalTitleCall:
					call.Item3.TrySetResult( Computer.SetLocalTitle( Window, call.Item2 is [string title, ..] ? title : "" ) );
					e.StopPropagation();
					return;

				// Only ever sends less to the host, so it is safe from any copy too. See NaviInputCapture.
				case NaviEvents.Call when e.Value is System.ValueTuple<string, object[], TaskCompletionSource<object>> call
					&& call.Item1 == KeepInputLocalCall:
					Window.KeepsInputLocal = call.Item2 is [bool local, ..] && local;
					call.Item3.TrySetResult( null );
					e.StopPropagation();
					return;

				// Reading the computer's files from a client's copy, the seated player's only: the host has them, and runs the
				// read as this window's app. Nothing that changes a file crosses the network. See NaviComputer.ReadRemoteFiles.
				case NaviEvents.Call when e.Value is System.ValueTuple<string, object[], TaskCompletionSource<object>> call
					&& !Computer.IsAuthority && NaviComputer.IsRemoteFileCall( call.Item1 ):
					Computer.ReadRemoteFiles( Window, call.Item1, call.Item2, call.Item3 );
					e.StopPropagation();
					return;

				// A viewer's copy of a shared app only draws. Apart from reading files, calls never cross the network, so no
				// client can make one as an app on the host.
				case NaviEvents.Call when e.Value is System.ValueTuple<string, object[], TaskCompletionSource<object>> call && viewer:
					call.Item3.TrySetException( new System.NotSupportedException( "This copy of the app only draws. Calls run on the host's copy." ) );
					e.StopPropagation();
					return;

				case NaviEvents.Call when e.Value is System.ValueTuple<string, object[], TaskCompletionSource<object>> call:
					_ = NaviSystemCalls.RunAsync( Computer, Window, call.Item1, call.Item2, call.Item3 );
					e.StopPropagation();
					return;

				// A shared app's action goes to the host's copy. An app that isn't shared runs here alone, so its copy here
				// gets it.
				case NaviEvents.Act when e.Value is System.ValueTuple<string, string[]> act:
					if ( IsShared )
						Computer.UserAct( id, windowId, act.Item1, act.Item2 );
					else
						DeliverAction( act.Item1, act.Item2 );

					e.StopPropagation();
					return;

				// Settings asks the secure layer for the lock's setup dialog, like the start menu did. Only core apps may, and only
				// the seated player's client opens it: the password is typed in the secure layer, never in the app.
				case NaviEvents.LockSetup:
					if ( Window.App.IsCore && Computer.IsSeated )
						Computer.LockSetupRequested = true;

					e.StopPropagation();
					return;

				// The app's copy on this client asks, so it counts as the local player's request.
				case NaviEvents.Close:
					Computer.UserCloseWindow( id, windowId );
					e.StopPropagation();
					return;

				case NaviEvents.Minimize:
					Computer.UserMinimize( id, windowId );
					e.StopPropagation();
					return;

				case NaviEvents.ToggleMaximize:
					Computer.UserToggleMaximized( id, windowId );
					e.StopPropagation();
					return;

				// A real press in the app's content answers its close question, so the next close asks again. Only a real
				// one: an app that could fake it could keep its window from ever closing. It goes on bubbling to the app
				// and the window.
				case "onmousedown":
					if ( e is MousePanelEvent { MouseButton: MouseButtons.Left } && Input.Keyboard.Down( "MOUSE1" ) )
						Computer.UserPressWindow( id, windowId );

					break;
			}
		}

		base.OnEvent( e );
	}
}
