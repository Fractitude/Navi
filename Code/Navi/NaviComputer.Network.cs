using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Multiplayer. The host runs the computer and sends its state to every other client as a snapshot
/// (<see cref="NetworkWindows"/> and the other parts); they only draw it. What the player at the screen does goes to the
/// host as an RPC (the <c>User…</c> methods), and only the player in the seat (<see cref="UserId"/>) is listened to.
/// The computer's GameObject must be a network object: Network Mode: Object in the scene, or network-spawned by the game
/// if it is made at runtime. Scene objects default to Snapshot, which is sent to clients only once, as they join.
/// </summary>
public sealed partial class NaviComputer : Component.INetworkVisible
{
	bool warnedNotNetworked;

	/// <summary>
	/// Say so once if the host's state can't reach other clients because the computer isn't a network object. Its mode
	/// can't be changed from here: by the time a component runs, the scene has decided what it networks.
	/// </summary>
	void WarnIfNotNetworked()
	{
		if ( warnedNotNetworked || GameObject.Network.Active )
			return;

		warnedNotNetworked = true;
		Log.Warning( $"NaviComputer: '{GameObject.Name}' isn't a network object, so other players won't see its screen. "
			+ "Set its GameObject's Network Mode to Object." );
	}

	/// <summary>
	/// True where this computer's logic runs: offline, or on the host. Other clients draw what the host sends, and the
	/// methods that change the computer do nothing there.
	/// </summary>
	public bool IsAuthority => !Networking.IsActive || Networking.IsHost;

	// The host's state (NaviComputerSnapshot), as JSON, in parts that change at different rates. Only the host writes them,
	// and the network sends only the parts that changed: moving a window sends the windows, not the manifests.

	/// <summary>
	/// <see cref="NaviComputerSnapshot.Apps"/>. Changes when an app is installed or opened.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public string NetworkApps { get; set; }

	/// <summary>
	/// <see cref="NaviComputerSnapshot.State"/>. Changes when an app is installed, a permission granted or a setting changed.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public string NetworkState { get; set; }

	/// <summary>
	/// <see cref="NaviComputerSnapshot.Windows"/>. Changes whenever a window moves.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public string NetworkWindows { get; set; }

	/// <summary>
	/// <see cref="NaviSessionSnapshot"/>: the user, permission requests and downloads.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public string NetworkSession { get; set; }

	/// <summary>
	/// How often the host checks whether its state changed and sends it, in seconds.
	/// </summary>
	const float SnapshotInterval = 0.1f;

	RealTimeSince sinceSnapshot;
	(string Apps, string State, string Windows, string Session) appliedSnapshot;

	/// <summary>
	/// Connection id of the player using this computer, or empty while nobody does. Only they can work it; everyone else
	/// watches. The first player to press Use at a free computer takes the seat (<see cref="UserSit"/>), and it frees
	/// itself when they leave the game or stop using it for <see cref="SeatTimeout"/>. Offline it stays empty, and the
	/// local player sits the same way.
	/// </summary>
	public System.Guid UserId { get; private set; }

	/// <summary>
	/// The player using this computer, or null.
	/// </summary>
	public Connection User => UserId == default ? null : Connection.Find( UserId );

	/// <summary>
	/// The local player is using this computer: offline, or they have the seat.
	/// </summary>
	public bool IsUser => !Networking.IsActive || (UserId != default && UserId == Connection.Local?.Id);

	/// <summary>
	/// The local player has taken the seat: <see cref="NaviInputCapture"/> pulls the camera to the screen and gives them the
	/// mouse. Online it is the seat itself. Offline there is no one to share with, but they still sit down with Use
	/// (<see cref="UserSit"/>) before they can use the screen. <see cref="UserLeave"/> stands them up.
	/// </summary>
	public bool IsSeated => Networking.IsActive ? UserId != default && UserId == Connection.Local?.Id : seatedOffline;

	bool seatedOffline;

	/// <summary>
	/// Someone has the seat. Offline, that is the local player once they have sat down.
	/// </summary>
	public bool HasUser => Networking.IsActive ? UserId != default : seatedOffline;

	/// <summary>
	/// The local player can use the screen: they have the seat, or it is free.
	/// </summary>
	public bool CanUse => IsUser || UserId == default;

	/// <summary>
	/// The screen is locked: a lock screen covers it, and the computer does nothing the player asks until it is unlocked
	/// (<see cref="Authorize"/>). Apps keep running. Whoever locked it unlocks it, from the seat. It stays locked when they
	/// stand up, so another player can sit at the screen but not use it, and unlocks itself if the player who locked it
	/// leaves the game. Not saved: a computer starts unlocked.
	/// </summary>
	public bool IsLocked { get; private set; }

	/// <summary>
	/// Connection id of the player who locked the screen, or empty if it was locked without one (by game code), or while
	/// offline. Only they can unlock it with <see cref="UserUnlock"/>; if it is empty, whoever is in the seat can.
	/// </summary>
	public System.Guid LockOwner { get; private set; }

	/// <summary>
	/// The local player may unlock the screen, if they are in the seat: they locked it, or it has no owner.
	/// </summary>
	public bool IsLockOwner => IsLocked && (!Networking.IsActive || LockOwner == default || LockOwner == Connection.Local?.Id);

	/// <summary>
	/// The player who locked the screen, or null.
	/// </summary>
	public Connection Locker => LockOwner == default ? null : Connection.Find( LockOwner );

	/// <summary>
	/// Seconds the user can leave the computer alone before the seat is freed. It isn't freed while a permission prompt
	/// is open.
	/// </summary>
	[Property] public float SeatTimeout { get; set; } = 5f;

	RealTimeSince sinceUserActed;

	void UpdateNetwork()
	{
		if ( IsAuthority )
		{
			UpdateUserInput();
			PruneAppStates();
		}

		if ( !Networking.IsActive )
			return;

		if ( IsAuthority )
		{
			WarnIfNotNetworked();
			UpdateTransmit();
			UpdateStandIn();
			UpdateOutOfRange();
			UpdateSeat();

			if ( sinceSnapshot > SnapshotInterval )
			{
				sinceSnapshot = 0;

				SendSnapshot();
			}

			return;
		}

		var parts = (NetworkApps, NetworkState, NetworkWindows, NetworkSession);
		if ( parts != appliedSnapshot && parts is ({ }, { }, { }, { }) )
		{
			appliedSnapshot = parts;
			ApplySnapshot();
		}
	}

	/// <summary>
	/// On the host: set each part that changed. Window moves don't bump Version, so it compares what would be sent.
	/// </summary>
	void SendSnapshot()
	{
		var snapshot = CaptureSnapshot();

		var apps = Json.Serialize( snapshot.Apps );
		if ( apps != NetworkApps )
			NetworkApps = apps;

		var state = Json.Serialize( snapshot.State );
		if ( state != NetworkState )
			NetworkState = state;

		var windows = Json.Serialize( snapshot.Layout );
		if ( windows != NetworkWindows )
			NetworkWindows = windows;

		var session = Json.Serialize( new NaviSessionSnapshot
		{
			Requests = snapshot.Requests,
			Downloading = snapshot.Downloading,
			User = snapshot.User,
			Locked = IsLocked,
			LockOwner = LockOwner,
			LockSetupOpen = IsLockSetupOpen,
			StartMenuOpen = IsStartMenuOpen,
			StartMenuQuery = StartMenuQuery,
			StartMenuScroll = StartMenuScroll,
			FailedAttempts = FailedAttempts,
			RetryAt = RetryAt.Ticks,
		} );
		if ( session != NetworkSession )
			NetworkSession = session;
	}

	// Who it is sent to.

	/// <summary>
	/// How close a player must be, in world units, for the host to send them this computer's state. Players further away
	/// get nothing until they come back in range, and then get the latest state. The user always gets it.
	/// 0 sends it to everyone, wherever they are.
	/// </summary>
	[Property] public float NetworkRange { get; set; } = 1500f;

	/// <summary>
	/// On the host: send only to players in <see cref="NetworkRange"/>. The network asks <see cref="IsVisibleToConnection"/>
	/// only for objects that aren't always transmitted.
	/// </summary>
	void UpdateTransmit()
	{
		var always = NetworkRange <= 0;
		if ( GameObject.Network.AlwaysTransmit != always )
			GameObject.Network.AlwaysTransmit = always;
	}

	bool Component.INetworkVisible.IsVisibleToConnection( Connection connection, in BBox worldBounds )
	{
		if ( NetworkRange <= 0 || connection.Id == UserId || IsSpectator( connection.Id ) )
			return true;

		return connection.DistanceSquared( WorldPosition ) <= NetworkRange * NetworkRange;
	}

	/// <summary>
	/// On the host: the host's camera is out of <see cref="NetworkRange"/>, and it isn't the user. The host still runs the
	/// computer, but draws the stand-in (<see cref="NaviScreenStandIn"/>) in place of the screen, as players out of range
	/// see it. Always false on clients: the engine turns the computer off for them.
	/// </summary>
	public bool IsOutOfRange { get; private set; }

	void UpdateOutOfRange()
	{
		IsOutOfRange = standIn.IsValid()
			&& !IsUser
			&& !IsSpectating
			&& Scene.Camera is { } camera
			&& camera.WorldPosition.DistanceSquared( WorldPosition ) > NetworkRange * NetworkRange;
	}

	/// <summary>
	/// The color the screen shows to players out of <see cref="NetworkRange"/>. The engine turns the whole computer off
	/// for them, panel and all, so a plane of this color stands in for the screen (<see cref="NaviScreenStandIn"/>).
	/// </summary>
	[Property] public Color OutOfRangeColor { get; set; } = Color.Black;

	GameObject standIn;

	/// <summary>
	/// On the host: spawn the stand-in for the screen. It is its own network object, sent to everyone, because a player
	/// who joins out of range doesn't get the computer at all.
	/// </summary>
	void UpdateStandIn()
	{
		if ( standIn.IsValid() || NetworkRange <= 0 || !GameObject.Network.Active || Components.Get<WorldPanel>() is not { } panel )
			return;

		// The plane model lies flat, facing up (+Z); pitched up it faces the panel's forward, its x along Down.
		var (center, size) = ScreenRect( panel );
		var local = new Transform( center, Rotation.FromPitch( 90 ), new Vector3( size.y / 100f, size.x / 100f, 1f ) );

		standIn = new GameObject( false, $"{GameObject.Name} (out of range)" );
		standIn.Flags |= GameObjectFlags.NotSaved;
		standIn.WorldTransform = GameObject.WorldTransform.ToWorld( local );

		var renderer = standIn.Components.Create<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/plane.vmdl" );
		renderer.Tint = OutOfRangeColor;
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		standIn.Components.Create<NaviScreenStandIn>().Computer = GameObject.Id;

		standIn.Enabled = true;
		standIn.NetworkSpawn( new NetworkSpawnOptions { AlwaysTransmit = true } );
	}

	void DestroyStandIn()
	{
		if ( standIn.IsValid() )
			standIn.Destroy();

		standIn = null;
	}

	NaviComputerSnapshot CaptureSnapshot() => new()
	{
		// Only the host opens windows, so clients don't need where they were. It would change with every drag.
		State = CaptureSave( placements: false, secrets: false ),
		Apps = Installed
			.Concat( running.Select( x => x.App ) )
			.Concat( permissionRequests.Select( x => x.App ) )
			.Where( x => NaviStore.NormalizeIdent( x.Id ) is null )
			.DistinctBy( x => x.Id )
			.Select( x => x.ToSnapshot() )
			.ToList(),
		Layout = new NaviLayoutSnapshot
		{
			Processes = running.Select( x => new NaviProcessSnapshot
			{
				AppId = x.App.Id,
				IsRoot = x.IsRoot,
				Permissions = x.Permissions.ToList(),
				Tray = x.Tray?.ToSnapshot(),
			} ).ToList(),
			Windows = Windows.Select( x => new NaviWindowSnapshot
			{
				AppId = x.App.Id,
				WindowId = x.Id,
				Position = x.Position,
				Size = x.Size,
				IsMinimized = x.IsMinimized,
				IsMaximized = x.IsMaximized,
				ZIndex = x.ZIndex,
				Title = x.Title,
			} ).ToList(),
		},
		Requests = permissionRequests.Select( x => new NaviRequestSnapshot
		{
			Id = x.Id,
			AppId = x.App.Id,
			Permissions = x.Permissions.ToList(),
			Ticked = x.Ticked.ToList(),
			IsReview = x.IsReview,
			IsRoot = x.IsRoot,
			Launch = x.Launch,
		} ).ToList(),
		Downloading = downloading.ToList(),
		User = UserId,
		Locked = IsLocked,
		LockOwner = LockOwner,
		LockSetupOpen = IsLockSetupOpen,
		StartMenuOpen = IsStartMenuOpen,
		StartMenuQuery = StartMenuQuery,
		StartMenuScroll = StartMenuScroll,
		FailedAttempts = FailedAttempts,
		RetryAt = RetryAt.Ticks,
	};

	/// <summary>
	/// Take on the host's state. Windows and requests are kept as the same objects where they still exist, so the
	/// desktop and the secure layer don't start over. Apps this client doesn't have yet are downloaded, and left out until
	/// they are. Parts can arrive in different updates, e.g. a window before its app's manifest: the window is left out
	/// until the manifest comes, and then everything is applied again.
	/// </summary>
	void ApplySnapshot()
	{
		NaviComputerSnapshot snapshot;

		try
		{
			var session = Json.Deserialize<NaviSessionSnapshot>( NetworkSession ) ?? new();
			snapshot = new NaviComputerSnapshot
			{
				Apps = Json.Deserialize<List<NaviAppSnapshot>>( NetworkApps ),
				State = Json.Deserialize<NaviComputerSave>( NetworkState ),
				Layout = Json.Deserialize<NaviLayoutSnapshot>( NetworkWindows ) ?? new(),
				Requests = session.Requests,
				Downloading = session.Downloading,
				User = session.User,
				Locked = session.Locked,
				LockOwner = session.LockOwner,
				LockSetupOpen = session.LockSetupOpen,
				StartMenuOpen = session.StartMenuOpen,
				StartMenuQuery = session.StartMenuQuery,
				StartMenuScroll = session.StartMenuScroll,
				FailedAttempts = session.FailedAttempts,
				RetryAt = session.RetryAt,
			};
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"NaviComputer: couldn't read the host's state: {e.Message}" );
			return;
		}

		NaviAppRegistry.AddHostApps( snapshot.Apps );
		ApplySave( snapshot.State ?? new() );

		foreach ( var id in (snapshot.State?.InstalledApps ?? []).Concat( snapshot.State?.InstalledPackages ?? [] ) )
			FindOrFetch( id );

		downloading.Clear();
		downloading.UnionWith( snapshot.Downloading ?? [] );
		UserId = snapshot.User;
		IsLocked = snapshot.Locked;
		LockOwner = snapshot.LockOwner;
		IsLockSetupOpen = snapshot.LockSetupOpen;
		IsStartMenuOpen = snapshot.StartMenuOpen;
		StartMenuQuery = snapshot.StartMenuQuery ?? "";
		StartMenuScroll = snapshot.StartMenuScroll;
		seenFailed = snapshot.FailedAttempts;
		seenRetryAt = snapshot.RetryAt > 0 ? new System.DateTime( snapshot.RetryAt, System.DateTimeKind.Utc ) : default;

		var processes = new List<NaviProcess>();
		foreach ( var entry in snapshot.Layout?.Processes ?? [] )
		{
			if ( FindOrFetch( entry.AppId ) is not { } app || processes.Any( x => x.App.Id == app.Id ) )
				continue;

			var process = running.FirstOrDefault( x => x.App.Id == app.Id && x.IsRoot == entry.IsRoot )
				?? new NaviProcess { App = app, IsRoot = entry.IsRoot };

			process.Permissions.Clear();
			process.Permissions.UnionWith( entry.Permissions ?? [] );
			process.Tray = NaviTray.FromSnapshot( entry.Tray );
			processes.Add( process );
		}

		// Windows are kept as the same objects where they still exist, so their panels don't start over.
		var windows = processes.ToDictionary( x => x, x => new List<NaviWindow>() );
		foreach ( var entry in snapshot.Layout?.Windows ?? [] )
		{
			var process = processes.FirstOrDefault( x => x.App.Id == entry.AppId );
			if ( process is null || process.App.FindWindow( entry.WindowId ) is not { } info
				|| windows[process].Any( x => x.Id == info.Id ) )
				continue;

			var window = process.GetWindow( info.Id ) ?? new NaviWindow { Process = process, Info = info };
			window.Position = entry.Position;
			window.Size = entry.Size;
			window.IsMinimized = entry.IsMinimized;
			window.IsMaximized = entry.IsMaximized;
			window.ZIndex = entry.ZIndex;
			window.Title = NaviAppInfo.Cap( entry.Title, NaviWindow.MaxTitle );
			windows[process].Add( window );
		}

		foreach ( var process in processes )
		{
			process.WindowList.Clear();
			process.WindowList.AddRange( windows[process] );
		}

		running.Clear();
		running.AddRange( processes );
		topZIndex = Windows.Any() ? Windows.Max( x => x.ZIndex ) : 0;

		var requests = new List<NaviPermissionRequest>();
		foreach ( var request in snapshot.Requests ?? [] )
		{
			if ( FindOrFetch( request.AppId ) is not { } app )
				continue;

			requests.Add( permissionRequests.FirstOrDefault( x => x.Id == request.Id ) ?? new NaviPermissionRequest
			{
				Id = request.Id,
				App = app,
				Permissions = request.Permissions?.ToArray() ?? [],
				Ticked = request.Ticked?.ToArray() ?? [],
				IsReview = request.IsReview,
				IsRoot = request.IsRoot,
				Launch = request.Launch,
			} );
		}

		permissionRequests.Clear();
		permissionRequests.AddRange( requests );
		hasNotifications = true;
		Version++;
	}

	/// <summary>
	/// Store packages this client is downloading because the host has them.
	/// </summary>
	readonly HashSet<string> fetching = new();

	/// <summary>
	/// The app, or null if this client doesn't have it. A Store app's id is its package ident, so a missing one is
	/// downloaded, and the host's state is applied again once it is here.
	/// </summary>
	NaviAppInfo FindOrFetch( string id )
	{
		if ( NaviAppRegistry.Find( id ) is { } app )
			return app;

		if ( NaviStore.NormalizeIdent( id ) is { } ident && fetching.Add( ident ) )
			_ = FetchAsync( ident );

		return null;
	}

	async Task FetchAsync( string ident )
	{
		await NaviStore.DownloadAsync( ident );
		appliedSnapshot = default;
	}

	// The seat.

	/// <summary>
	/// Free the seat when the user has left the game, or hasn't used the computer for <see cref="SeatTimeout"/>.
	/// </summary>
	void UpdateSeat()
	{
		// The player who locked the screen left the game: nobody could unlock it. One with a password stays locked, for
		// whoever knows it.
		if ( IsLocked && !HasLockCredential && LockOwner != default && Locker is null )
			Unlock();

		PruneAttempts();
		PruneSpectators();

		if ( UserId == default )
			return;

		var idle = sinceUserActed > SeatTimeout && permissionRequests.Count == 0;
		if ( User is null || idle )
			SetUser( default );
	}

	/// <summary>
	/// Give the seat to someone else. Open permission prompts are dismissed: whoever sits down didn't ask for them, and
	/// mustn't answer them for the player who did.
	/// </summary>
	/// <param name="keepStartMenu">Leave the start menu as it is, as when the player stands up with Escape.</param>
	void SetUser( System.Guid id, bool keepStartMenu = false )
	{
		if ( UserId == id )
			return;

		// The player who stood up with the menu open finds it as they left it when they sit down again.
		var returning = id != default && id == menuOwner;
		menuOwner = keepStartMenu ? UserId : default;

		UserId = id;
		ResetSession( keepStartMenu || returning );
	}

	/// <summary>
	/// The player who stood up with Escape, whose open start menu was kept for when they sit down again.
	/// </summary>
	System.Guid menuOwner;

	void ResetSession( bool keepStartMenu = false )
	{
		sinceUserActed = 0;
		userInput.Clear();
		IsLockSetupOpen = false;

		if ( !keepStartMenu )
			ResetStartMenu();

		foreach ( var request in permissionRequests.ToList() )
			DismissPermissionRequest( request );

		Version++;
	}

	/// <summary>
	/// On the host, in an RPC: may the caller work the computer? Only the user may. With <paramref name="sit"/>, anyone may
	/// while the seat is free, which gives it to them. Never trusts what the caller says about itself: the connection is
	/// the network's.
	/// </summary>
	bool Authorize( bool sit = false, bool whileLocked = false )
	{
		if ( !IsAuthority || (IsLocked && !whileLocked) )
			return false;

		if ( !Networking.IsActive )
		{
			// A spectator is not the user: they stand up from their seat first.
			if ( spectatingOffline != default )
				return false;

			seatedOffline |= sit;
			return seatedOffline;
		}

		var caller = Rpc.Caller ?? Connection.Local;
		if ( caller is null )
			return false;

		if ( caller.Id == UserId )
		{
			sinceUserActed = 0;
			return true;
		}

		if ( !sit || UserId != default || IsSpectator( caller.Id ) )
			return false;

		SetUser( caller.Id );
		return true;
	}

	/// <summary>
	/// The start menu is open. The host keeps it and sends it to everyone, so players watching the screen see the menu too.
	/// Cleared when the seat changes or the screen locks.
	/// </summary>
	public bool IsStartMenuOpen { get; private set; }

	/// <summary>
	/// What is typed in the start menu's search field, sent to everyone with <see cref="IsStartMenuOpen"/>.
	/// </summary>
	public string StartMenuQuery { get; private set; } = "";

	/// <summary>
	/// The most the start menu's search can be, in characters.
	/// </summary>
	public const int MaxStartMenuQuery = 64;

	/// <summary>
	/// The user opened or closed the start menu, or typed in its search.
	/// </summary>
	[Rpc.Host]
	public void UserSetStartMenu( bool open, string query )
	{
		query = open ? NaviAppInfo.Cap( query ?? "", MaxStartMenuQuery ) : "";

		if ( !Authorize() || (IsStartMenuOpen == open && StartMenuQuery == query) )
			return;

		if ( !open )
			StartMenuScroll = 0f;

		IsStartMenuOpen = open;
		StartMenuQuery = query;
		Version++;
	}

	/// <summary>
	/// How far the start menu's list is scrolled, in panel pixels, sent to everyone with <see cref="IsStartMenuOpen"/>.
	/// </summary>
	public float StartMenuScroll { get; private set; }

	/// <summary>
	/// The user scrolled the start menu's list. Only passed on: it doesn't rebuild anything.
	/// </summary>
	[Rpc.Host]
	public void UserScrollStartMenu( float scroll )
	{
		if ( !Authorize() || !IsStartMenuOpen || !float.IsFinite( scroll ) )
			return;

		StartMenuScroll = System.Math.Clamp( scroll, 0f, 100000f );
	}

	void ResetStartMenu()
	{
		IsStartMenuOpen = false;
		StartMenuQuery = "";
		StartMenuScroll = 0f;
	}

	// What the player at the screen does. Each runs on the host, for the user only. The desktop and the secure layer call
	// these; game code calls the methods they wrap.

	/// <summary>
	/// Open an app, or bring it to the front. See <see cref="Launch"/>.
	/// </summary>
	[Rpc.Host]
	public void UserLaunch( string id )
	{
		if ( Authorize() )
			Launch( id );
	}

	/// <summary>
	/// Close an app and all its windows.
	/// </summary>
	[Rpc.Host]
	public void UserClose( string id )
	{
		if ( Authorize() && !(GetProcess( id ) is { } process && AskBeforeClosing( process.Windows )) )
			Close( id );
	}

	/// <summary>
	/// Open one of an app's windows, or bring it to the front. See <see cref="OpenWindow"/>. The app has to list the window in
	/// its manifest, whatever the caller sends.
	/// </summary>
	[Rpc.Host]
	public void UserOpenWindow( string id, string windowId )
	{
		if ( Authorize() )
			OpenWindow( id, windowId );
	}

	/// <summary>
	/// Close a window. See <see cref="CloseWindow"/>. The app is asked first if it guards its close
	/// (<see cref="AskBeforeClosing"/>): closing the main window closes all of them.
	/// </summary>
	[Rpc.Host]
	public void UserCloseWindow( string id, string windowId )
	{
		if ( !Authorize() || GetWindow( id, windowId ) is not { } window )
			return;

		if ( !AskBeforeClosing( window.Id == "" ? window.Process.Windows : [window] ) )
			CloseWindow( id, windowId );
	}

	[Rpc.Host]
	public void UserFocus( string id, string windowId )
	{
		if ( Authorize() )
			Focus( id, windowId );
	}

	[Rpc.Host]
	public void UserMinimize( string id, string windowId )
	{
		if ( Authorize() )
			Minimize( id, windowId );
	}

	[Rpc.Host]
	public void UserToggleMaximized( string id, string windowId )
	{
		if ( Authorize() )
			ToggleMaximized( id, windowId );
	}

	/// <summary>
	/// The user clicked a shared app's tray icon (<paramref name="item"/> empty) or one of its menu items. Quit closes the
	/// app. The click only reaches the host's copy of the app. An app that isn't shared hears it on the user's own client,
	/// from the desktop. An item the app's tray doesn't have gets nothing.
	/// </summary>
	[Rpc.Host]
	public void UserTrayClick( string id, string item )
	{
		if ( !Authorize() || GetProcess( id ) is not { } process )
			return;

		if ( item == QuitItem )
		{
			if ( !AskBeforeClosing( process.Windows ) )
				Close( id );
		}
		else if ( string.IsNullOrEmpty( item ) || (process.App.Shared && process.Tray?.Items.Any( x => x.Id == item ) == true) )
			DeliverTrayClick( id, item );
	}

	[Rpc.Host]
	public void UserReviewPermissions( string id )
	{
		if ( Authorize() )
			ReviewPermissions( id );
	}

	/// <summary>
	/// A click on an app's taskbar icon: minimizes the window in front, otherwise opens, restores or focuses the app.
	/// </summary>
	[Rpc.Host]
	public void UserTaskbarClick( string id )
	{
		if ( !Authorize() )
			return;

		if ( GetProcess( id ) is { } process && FocusedWindow?.Process == process && MinimizeApp( id ) )
			return;

		Launch( id );
	}

	/// <summary>
	/// The user dragged an app's taskbar icon to a new place. See <see cref="MoveTaskbarApp"/>.
	/// </summary>
	[Rpc.Host]
	public void UserMoveTaskbarApp( string id, int index, bool pin )
	{
		if ( Authorize() )
			MoveTaskbarApp( id, index, pin );
	}

	/// <summary>
	/// The user pinned an app to the taskbar, or unpinned it. See <see cref="ToggleTaskbarPin"/>.
	/// </summary>
	[Rpc.Host]
	public void UserToggleTaskbarPin( string id )
	{
		if ( Authorize() )
			ToggleTaskbarPin( id );
	}

	/// <summary>
	/// The user pinned an app to the start menu, or unpinned it. See <see cref="ToggleStartFavorite"/>.
	/// </summary>
	[Rpc.Host]
	public void UserToggleStartFavorite( string id )
	{
		if ( Authorize() )
			ToggleStartFavorite( id );
	}

	/// <summary>
	/// The user dragged a pinned app to a new place in the start menu. See <see cref="MoveStartFavorite"/>.
	/// </summary>
	[Rpc.Host]
	public void UserMoveStartFavorite( string id, int index )
	{
		if ( Authorize() )
			MoveStartFavorite( id, index );
	}

	/// <summary>
	/// The user moved or resized a window. <paramref name="size"/> is ignored unless <paramref name="sized"/>, for a
	/// window that fits its app. The desktop keeps it inside the screen.
	/// </summary>
	[Rpc.Host]
	public void UserMoveWindow( string id, string windowId, Vector2 position, Vector2 size, bool sized )
	{
		if ( !Authorize() || GetWindow( id, windowId ) is not { IsService: false } window || window.IsMaximized )
			return;

		if ( !IsPlausible( position ) || (sized && (!window.Info.CanResize || !IsPlausible( size ))) )
			return;

		window.Position = position;
		if ( sized )
			window.Size = size;
	}

	/// <summary>
	/// A real number of panel pixels, not NaN or something absurd.
	/// </summary>
	static bool IsPlausible( Vector2 value ) =>
		float.IsFinite( value.x ) && float.IsFinite( value.y ) && System.MathF.Abs( value.x ) < 100000f && System.MathF.Abs( value.y ) < 100000f;

	/// <summary>
	/// The player sits down at the computer, if the seat is free. Nothing else takes it: until they sit, their clicks on the
	/// screen do nothing.
	/// </summary>
	[Rpc.Host]
	public void UserSit()
	{
		Authorize( sit: true, whileLocked: true );
	}

	/// <summary>
	/// The user is still at the computer, so the seat stays theirs.
	/// </summary>
	[Rpc.Host( NetFlags.Unreliable )]
	public void UserKeepSeat()
	{
		Authorize( whileLocked: true );
	}

	/// <summary>
	/// Lock the screen. See <see cref="IsLocked"/>. The player in the seat owns the lock, if there is one. Returns false if it
	/// is locked already.
	/// </summary>
	public bool Lock()
	{
		if ( !IsAuthority || IsLocked )
			return false;

		IsLocked = true;
		IsLockSetupOpen = false;
		ResetStartMenu();
		LockOwner = Networking.IsActive ? UserId : default;
		Version++;
		return true;
	}

	/// <summary>
	/// Unlock the screen, whoever locked it. Returns false if it isn't locked.
	/// </summary>
	public bool Unlock()
	{
		if ( !IsAuthority || !IsLocked )
			return false;

		IsLocked = false;
		LockOwner = default;
		attempts.Clear();
		Version++;
		return true;
	}

	/// <summary>
	/// The user locks the screen, from the start menu.
	/// </summary>
	[Rpc.Host]
	public void UserLock()
	{
		if ( Authorize() )
			Lock();
	}

	/// <summary>
	/// The user unlocks a screen that has no password or pattern. Only the player who locked it may (see
	/// <see cref="LockOwner"/>), and only from the seat. One with a password is unlocked with <see cref="UserUnlockWith"/>.
	/// </summary>
	[Rpc.Host]
	public void UserUnlock()
	{
		if ( !Authorize( whileLocked: true ) || !IsLocked || HasLockCredential )
			return;

		var caller = Networking.IsActive ? Rpc.Caller ?? Connection.Local : null;
		if ( !Networking.IsActive || LockOwner == default || caller?.Id == LockOwner )
			Unlock();
	}

	/// <summary>
	/// The user stands up and gives the seat back. A spectator stands up from their seat.
	/// </summary>
	[Rpc.Host]
	public void UserLeave()
	{
		if ( LeaveSpectating() || !Authorize( whileLocked: true ) )
			return;

		if ( Networking.IsActive )
		{
			SetUser( default, keepStartMenu: true );
		}
		else if ( seatedOffline )
		{
			seatedOffline = false;
			ResetSession( keepStartMenu: true );
		}
	}

	// Shared apps (NaviAppInfo.Shared). The host's copy of each runs the app and publishes its state; the others draw it.

	/// <summary>
	/// The state each shared app's host copy published, as JSON, by window key (<see cref="NaviWindow.Key"/>). Only the host
	/// writes it.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, string> AppStates { get; set; } = new();

	/// <summary>
	/// The most a shared app's published state can be, in characters. More is dropped, with a warning.
	/// </summary>
	public const int MaxAppState = 32 * 1024;

	/// <summary>
	/// On the host: what the host copy of a window of a running shared app published. Ignored for a window that isn't open
	/// or isn't shared.
	/// </summary>
	internal void SetAppState( string key, string json )
	{
		if ( !IsAuthority || FindWindow( key ) is not { App.Shared: true } )
			return;

		if ( json is not null && json.Length > MaxAppState )
		{
			Log.Warning( $"NaviComputer: {key} published {json.Length} characters of state, more than {MaxAppState}; not sent" );
			return;
		}

		if ( AppStates.TryGetValue( key, out var current ) && current == json )
			return;

		AppStates[key] = json;
	}

	/// <summary>
	/// What the host copy of a shared app's window published last, or null.
	/// </summary>
	public string GetAppState( string key ) => AppStates.TryGetValue( key, out var json ) ? json : null;

	/// <summary>
	/// Forget the state of windows that closed, so a new window starts from nothing.
	/// </summary>
	void PruneAppStates()
	{
		var open = Windows.Select( x => x.Key ).ToHashSet();

		foreach ( var key in AppStates.Keys.Where( x => !open.Contains( x ) ).ToArray() )
			AppStates.Remove( key );

		foreach ( var key in AppScrolls.Keys.Where( x => !open.Contains( x ) ).ToArray() )
			AppScrolls.Remove( key );
	}

	/// <summary>
	/// Where the user scrolled each shared app's lists, by window key, as <see cref="NaviAppHost"/> encodes it. Only the host
	/// writes it. Scrolling is the panel's own, not the app's state, so Navi passes it on for every shared app.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, string> AppScrolls { get; set; } = new();

	/// <summary>
	/// The most an app's scroll positions can be, in characters.
	/// </summary>
	const int MaxAppScroll = 2048;

	public string GetAppScroll( string key ) => AppScrolls.TryGetValue( key, out var scroll ) ? scroll : null;

	/// <summary>
	/// The user scrolled in a shared app.
	/// </summary>
	[Rpc.Host]
	public void UserScroll( string id, string windowId, string scroll )
	{
		if ( !Authorize() || GetWindow( id, windowId ) is not { App.Shared: true } window || scroll is null || scroll.Length > MaxAppScroll )
			return;

		AppScrolls[window.Key] = scroll;
	}

	/// <summary>
	/// The host's copy of each open window's body, by window key, which actions and tray clicks are delivered to.
	/// </summary>
	readonly Dictionary<string, NaviAppHost> appHosts = new();

	internal void RegisterAppHost( string key, NaviAppHost host ) => appHosts[key] = host;

	internal void UnregisterAppHost( string key, NaviAppHost host )
	{
		if ( key is not null && appHosts.TryGetValue( key, out var current ) && current == host )
			appHosts.Remove( key );
	}

	/// <summary>
	/// The user did something in a shared app (<c>Navi.Act</c>). Delivered to the host's copy of that app only, as if
	/// they had clicked it there.
	/// </summary>
	[Rpc.Host]
	public void UserAct( string id, string windowId, string action, string[] args )
	{
		if ( !Authorize() || GetWindow( id, windowId ) is not { App.Shared: true } window || !appHosts.TryGetValue( window.Key, out var host ) )
			return;

		args ??= [];
		if ( string.IsNullOrEmpty( action ) || action.Length > Navi.MaxActionName || args.Sum( x => x?.Length ?? 0 ) > Navi.MaxActionArgs )
			return;

		host.DeliverAction( action, args );
	}

	/// <summary>
	/// The user pressed a keyboard shortcut (<see cref="NaviInputCapture.ShortcutKeys"/>) while <paramref name="window"/> was
	/// in front. A shared app gets it on the host, as an RPC; any other app on this client, where its clicks go too.
	/// </summary>
	internal void SendShortcut( NaviWindow window, string keys )
	{
		if ( window.App.Shared )
			UserShortcut( window.App.Id, window.Id, keys );
		else if ( appHosts.TryGetValue( window.Key, out var host ) )
			host.DeliverShortcut( keys );
	}

	/// <summary>
	/// The user pressed a keyboard shortcut in a shared app. Delivered to the host's copy only, and only if that window is
	/// the one in front, so a shortcut goes where the player's keys go.
	/// </summary>
	[Rpc.Host]
	public void UserShortcut( string id, string windowId, string keys )
	{
		if ( !Authorize() || GetWindow( id, windowId ) is not { App.Shared: true } window || InputCapture != window
			|| !NaviInputCapture.IsShortcut( keys ) || !appHosts.TryGetValue( window.Key, out var host ) )
			return;

		host.DeliverShortcut( keys );
	}

	/// <summary>
	/// On the host: the input actions the user holds while they sit at the computer (<see cref="NaviInputCapture"/>).
	/// </summary>
	readonly HashSet<string> userInput = new();

	/// <summary>
	/// The input actions the user holds, and those they pressed since the last frame. Empty unless a shared app's window
	/// has the user's controls. On the host only.
	/// </summary>
	internal (IReadOnlySet<string> Down, IReadOnlySet<string> Pressed) UserInput => (userInput, userPressed);

	readonly HashSet<string> userPressed = new();
	readonly HashSet<string> userInputLastFrame = new();

	/// <summary>
	/// Work out which actions went down since the last frame. Once a frame, on the host.
	/// </summary>
	void UpdateUserInput()
	{
		if ( InputCapture is null )
			userInput.Clear();

		userPressed.Clear();
		userPressed.UnionWith( userInput.Except( userInputLastFrame ) );
		userInputLastFrame.Clear();
		userInputLastFrame.UnionWith( userInput );
	}

	/// <summary>
	/// The user's client says which input actions they hold, whenever that changes. Only names
	/// <see cref="NaviInputCapture.ForwardedActions"/> lists count.
	/// </summary>
	[Rpc.Host]
	public void UserHoldInput( string[] actions )
	{
		if ( !Authorize() || InputCapture is null )
			return;

		userInput.Clear();
		userInput.UnionWith( (actions ?? []).Where( NaviInputCapture.ForwardedActions.Contains ) );
	}

	/// <summary>
	/// The user answered a permission prompt in the secure layer. Never takes the seat: only the player who sat there
	/// while the prompt was up can answer it.
	/// </summary>
	[Rpc.Host]
	public void UserAnswerPermissionRequest( int requestId, string[] ticked )
	{
		if ( Authorize() && permissionRequests.FirstOrDefault( x => x.Id == requestId ) is { } request )
			AnswerPermissionRequest( request, ticked );
	}

	[Rpc.Host]
	public void UserDismissPermissionRequest( int requestId )
	{
		if ( Authorize() && permissionRequests.FirstOrDefault( x => x.Id == requestId ) is { } request )
			DismissPermissionRequest( request );
	}
}
