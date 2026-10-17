using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Multiplayer. The host runs the computer and sends its state to every other client as
/// <see cref="NetworkSnapshot"/>; they only draw it. What the player at the screen does goes to the host as an RPC
/// (the <c>User…</c> methods), and only the player in the seat (<see cref="UserId"/>) is listened to.
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

	/// <summary>
	/// The host's state as JSON (<see cref="NaviComputerSnapshot"/>). Only the host writes it.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public string NetworkSnapshot { get; set; }

	/// <summary>
	/// How often the host checks whether its state changed and sends it, in seconds.
	/// </summary>
	const float SnapshotInterval = 0.1f;

	RealTimeSince sinceSnapshot;
	string appliedSnapshot;

	/// <summary>
	/// Connection id of the player using this computer, or empty while nobody does. Only they can work it; everyone else
	/// watches. The first player to click a free computer takes the seat, and it frees itself when they leave the game or
	/// stop using it for <see cref="SeatTimeout"/>. Offline it stays empty and the local player can always use it.
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
	/// The local player can use the screen: they have the seat, or it is free.
	/// </summary>
	public bool CanUse => IsUser || UserId == default;

	/// <summary>
	/// Seconds the user can leave the computer alone before the seat is freed. It isn't freed while a permission prompt
	/// is open or a window has their controls.
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
			UpdateSeat();

			if ( sinceSnapshot > SnapshotInterval )
			{
				sinceSnapshot = 0;

				// Window moves don't bump Version, so compare what would be sent instead.
				var json = Json.Serialize( CaptureSnapshot() );
				if ( json != NetworkSnapshot )
					NetworkSnapshot = json;
			}

			return;
		}

		if ( NetworkSnapshot is { } snapshot && snapshot != appliedSnapshot )
		{
			appliedSnapshot = snapshot;
			ApplySnapshot( snapshot );
		}
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
		if ( NetworkRange <= 0 || connection.Id == UserId )
			return true;

		return connection.DistanceSquared( WorldPosition ) <= NetworkRange * NetworkRange;
	}

	NaviComputerSnapshot CaptureSnapshot() => new()
	{
		State = CaptureSave(),
		Apps = Installed
			.Concat( running.Select( x => x.App ) )
			.Concat( permissionRequests.Select( x => x.App ) )
			.Where( x => NaviStore.NormalizeIdent( x.Id ) is null )
			.DistinctBy( x => x.Id )
			.Select( x => x.ToSnapshot() )
			.ToList(),
		Windows = running.Select( x => new NaviWindowSnapshot
		{
			AppId = x.App.Id,
			Position = x.Position,
			Size = x.Size,
			IsMinimized = x.IsMinimized,
			IsMaximized = x.IsMaximized,
			ZIndex = x.ZIndex,
			IsRoot = x.IsRoot,
			Permissions = x.Permissions.ToList(),
		} ).ToList(),
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
	};

	/// <summary>
	/// Take on the host's state. Windows and requests are kept as the same objects where they still exist, so the
	/// desktop and the secure layer don't start over. Apps this client doesn't have yet are downloaded, and left out until
	/// they are.
	/// </summary>
	void ApplySnapshot( string json )
	{
		NaviComputerSnapshot snapshot;

		try
		{
			snapshot = Json.Deserialize<NaviComputerSnapshot>( json );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"NaviComputer: couldn't read the host's state: {e.Message}" );
			return;
		}

		if ( snapshot is null )
			return;

		NaviAppRegistry.AddHostApps( snapshot.Apps );
		ApplySave( snapshot.State ?? new() );

		foreach ( var id in (snapshot.State?.InstalledApps ?? []).Concat( snapshot.State?.InstalledPackages ?? [] ) )
			FindOrFetch( id );

		downloading.Clear();
		downloading.UnionWith( snapshot.Downloading ?? [] );
		UserId = snapshot.User;

		var processes = new List<NaviProcess>();
		foreach ( var window in snapshot.Windows ?? [] )
		{
			if ( FindOrFetch( window.AppId ) is not { } app )
				continue;

			var process = running.FirstOrDefault( x => x.App.Id == app.Id && x.IsRoot == window.IsRoot )
				?? new NaviProcess { App = app, IsRoot = window.IsRoot };

			process.Position = window.Position;
			process.Size = window.Size;
			process.IsMinimized = window.IsMinimized;
			process.IsMaximized = window.IsMaximized;
			process.ZIndex = window.ZIndex;
			process.Permissions.Clear();
			process.Permissions.UnionWith( window.Permissions ?? [] );
			processes.Add( process );
		}

		running.Clear();
		running.AddRange( processes );
		topZIndex = running.Count > 0 ? running.Max( x => x.ZIndex ) : 0;

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
		appliedSnapshot = null;
	}

	// The seat.

	/// <summary>
	/// Free the seat when the user has left the game, or hasn't used the computer for <see cref="SeatTimeout"/>.
	/// </summary>
	void UpdateSeat()
	{
		if ( UserId == default )
			return;

		var idle = sinceUserActed > SeatTimeout && permissionRequests.Count == 0 && InputCapture is null;
		if ( User is null || idle )
			SetUser( default );
	}

	/// <summary>
	/// Give the seat to someone else. Open permission prompts are dismissed: whoever sits down didn't ask for them, and
	/// mustn't answer them for the player who did.
	/// </summary>
	void SetUser( System.Guid id )
	{
		if ( UserId == id )
			return;

		UserId = id;
		sinceUserActed = 0;
		userInput.Clear();

		foreach ( var request in permissionRequests.ToList() )
			DismissPermissionRequest( request );

		Version++;
	}

	/// <summary>
	/// On the host, in an RPC: may the caller work the computer? The user may, and so may anyone while the seat is free,
	/// which gives it to them when <paramref name="take"/> is set. Never trusts what the caller says about itself: the
	/// connection is the network's.
	/// </summary>
	bool Authorize( bool take = true )
	{
		if ( !IsAuthority )
			return false;

		if ( !Networking.IsActive )
			return true;

		var caller = Rpc.Caller ?? Connection.Local;
		if ( caller is null )
			return false;

		if ( caller.Id == UserId )
		{
			sinceUserActed = 0;
			return true;
		}

		if ( !take || UserId != default )
			return false;

		SetUser( caller.Id );
		return true;
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

	[Rpc.Host]
	public void UserClose( string id )
	{
		if ( Authorize() )
			Close( id );
	}

	[Rpc.Host]
	public void UserFocus( string id )
	{
		if ( Authorize() )
			Focus( id );
	}

	[Rpc.Host]
	public void UserMinimize( string id )
	{
		if ( Authorize() )
			Minimize( id );
	}

	[Rpc.Host]
	public void UserToggleMaximized( string id )
	{
		if ( Authorize() )
			ToggleMaximized( id );
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

		var process = GetProcess( id );
		if ( process is not null && process == Focused && Minimize( id ) )
			return;

		Launch( id );
	}

	/// <summary>
	/// The user moved or resized a window. <paramref name="size"/> is ignored unless <paramref name="sized"/>, for a
	/// window that fits its app. The desktop keeps it inside the screen.
	/// </summary>
	[Rpc.Host]
	public void UserMoveWindow( string id, Vector2 position, Vector2 size, bool sized )
	{
		if ( !Authorize() || GetProcess( id ) is not { } process || process.IsMaximized )
			return;

		if ( !IsPlausible( position ) || (sized && (!process.App.CanResize || !IsPlausible( size ))) )
			return;

		process.Position = position;
		if ( sized )
			process.Size = size;
	}

	/// <summary>
	/// A real number of panel pixels, not NaN or something absurd.
	/// </summary>
	static bool IsPlausible( Vector2 value ) =>
		float.IsFinite( value.x ) && float.IsFinite( value.y ) && System.MathF.Abs( value.x ) < 100000f && System.MathF.Abs( value.y ) < 100000f;

	/// <summary>
	/// The user is still at the computer, so the seat stays theirs.
	/// </summary>
	[Rpc.Host( NetFlags.Unreliable )]
	public void UserKeepSeat()
	{
		Authorize( take: false );
	}

	// Shared apps (NaviAppInfo.Shared). The host's copy of each runs the app and publishes its state; the others draw it.

	/// <summary>
	/// The state each shared app's host copy published, as JSON, by app id. Only the host writes it.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, string> AppStates { get; set; } = new();

	/// <summary>
	/// The most a shared app's published state can be, in characters. More is dropped, with a warning.
	/// </summary>
	public const int MaxAppState = 32 * 1024;

	/// <summary>
	/// On the host: what the host copy of a running shared app published. Ignored for an app that isn't running or isn't
	/// shared.
	/// </summary>
	internal void SetAppState( string id, string json )
	{
		if ( !IsAuthority || GetProcess( id ) is not { App.Shared: true } )
			return;

		if ( json is not null && json.Length > MaxAppState )
		{
			Log.Warning( $"NaviComputer: {id} published {json.Length} characters of state, more than {MaxAppState}; not sent" );
			return;
		}

		if ( AppStates.TryGetValue( id, out var current ) && current == json )
			return;

		AppStates[id] = json;
	}

	/// <summary>
	/// What the host copy of a shared app published last, or null.
	/// </summary>
	public string GetAppState( string id ) => AppStates.TryGetValue( id, out var json ) ? json : null;

	/// <summary>
	/// Forget the state of apps that closed, so a new window starts from nothing.
	/// </summary>
	void PruneAppStates()
	{
		foreach ( var id in AppStates.Keys.Where( x => !IsRunning( x ) ).ToArray() )
			AppStates.Remove( id );

		foreach ( var id in AppScrolls.Keys.Where( x => !IsRunning( x ) ).ToArray() )
			AppScrolls.Remove( id );
	}

	/// <summary>
	/// Where the user scrolled each shared app's lists, by app id, as <see cref="NaviAppHost"/> encodes it. Only the host
	/// writes it. Scrolling is the panel's own, not the app's state, so Navi passes it on for every shared app.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, string> AppScrolls { get; set; } = new();

	/// <summary>
	/// The most an app's scroll positions can be, in characters.
	/// </summary>
	const int MaxAppScroll = 2048;

	public string GetAppScroll( string id ) => AppScrolls.TryGetValue( id, out var scroll ) ? scroll : null;

	/// <summary>
	/// The user scrolled in a shared app.
	/// </summary>
	[Rpc.Host]
	public void UserScroll( string id, string scroll )
	{
		if ( !Authorize() || GetProcess( id ) is not { App.Shared: true } || scroll is null || scroll.Length > MaxAppScroll )
			return;

		AppScrolls[id] = scroll;
	}

	/// <summary>
	/// The host's copy of each open app's window body, which actions are delivered to.
	/// </summary>
	readonly Dictionary<string, NaviAppHost> appHosts = new();

	internal void RegisterAppHost( string id, NaviAppHost host ) => appHosts[id] = host;

	internal void UnregisterAppHost( string id, NaviAppHost host )
	{
		if ( id is not null && appHosts.TryGetValue( id, out var current ) && current == host )
			appHosts.Remove( id );
	}

	/// <summary>
	/// The user did something in a shared app (<c>Navi.Act</c>). Delivered to the host's copy of that app only, as if
	/// they had clicked it there.
	/// </summary>
	[Rpc.Host]
	public void UserAct( string id, string action, string[] args )
	{
		if ( !Authorize() || GetProcess( id ) is not { App.Shared: true } || !appHosts.TryGetValue( id, out var host ) )
			return;

		args ??= [];
		if ( string.IsNullOrEmpty( action ) || action.Length > Navi.MaxActionName || args.Sum( x => x?.Length ?? 0 ) > Navi.MaxActionArgs )
			return;

		host.DeliverAction( action, args );
	}

	/// <summary>
	/// On the host: the input actions the user holds while a window has their controls (<see cref="NaviInputCapture"/>).
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
		if ( !Authorize( take: false ) || InputCapture is null )
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
		if ( Authorize( take: false ) && permissionRequests.FirstOrDefault( x => x.Id == requestId ) is { } request )
			AnswerPermissionRequest( request, ticked );
	}

	[Rpc.Host]
	public void UserDismissPermissionRequest( int requestId )
	{
		if ( Authorize( take: false ) && permissionRequests.FirstOrDefault( x => x.Id == requestId ) is { } request )
			DismissPermissionRequest( request );
	}
}
