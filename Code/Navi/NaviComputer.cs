using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// An app that is running. It has the permissions it was granted, any windows it has open (<see cref="Windows"/>), and
/// maybe an icon in the system tray (<see cref="Tray"/>). A normal app has its main window; a headless one
/// (<see cref="NaviAppInfo.Headless"/>) starts with none.
/// </summary>
public sealed class NaviProcess
{
	public NaviAppInfo App { get; init; }

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

	readonly List<NaviWindow> windows = new();

	/// <summary>
	/// The app's windows, in the order they were opened. For a headless app that includes its service, which isn't drawn.
	/// </summary>
	public IReadOnlyList<NaviWindow> Windows => windows;

	internal List<NaviWindow> WindowList => windows;

	/// <summary>
	/// The windows the player can see: all but the service of a headless app.
	/// </summary>
	public IEnumerable<NaviWindow> DrawnWindows => windows.Where( x => !x.IsService );

	/// <summary>
	/// The window with this id (empty for the main one), or null.
	/// </summary>
	public NaviWindow GetWindow( string id ) => windows.FirstOrDefault( x => x.Id == (id ?? "") );

	/// <summary>
	/// Every window the player can see is minimized.
	/// </summary>
	public bool IsMinimized => DrawnWindows.Any() && DrawnWindows.All( x => x.IsMinimized );

	/// <summary>
	/// The app's icon in the system tray, or null. A headless app always has one, so the player can reach it.
	/// </summary>
	public NaviTray Tray { get; set; }
}

/// <summary>
/// One window of a running app. Position and size are in panel pixels.
/// </summary>
public sealed class NaviWindow
{
	/// <summary>
	/// Separates the app's id from the window's in <see cref="Key"/>. No app id contains it (<see cref="NaviAppRegistry.IdOf"/>).
	/// </summary>
	public const char KeySeparator = '#';

	/// <summary>
	/// The longest a window's title can be, in characters.
	/// </summary>
	public const int MaxTitle = 32;

	/// <summary>
	/// Names a window across the computer: the app's id for its main window, otherwise the app's id, '#', and the window's id.
	/// </summary>
	public static string KeyOf( string appId, string windowId ) =>
		string.IsNullOrEmpty( windowId ) ? appId : appId + KeySeparator + windowId;

	public NaviProcess Process { get; init; }
	public NaviWindowInfo Info { get; init; }

	/// <summary>
	/// What the title bar says after the app's name: the title the app set while running (<c>Navi.Window.SetTitleAsync</c>), or else the
	/// one its manifest gave.
	/// </summary>
	public string Title
	{
		get => title ?? Info.Title;
		set => title = string.IsNullOrWhiteSpace( value ) ? null : value;
	}

	string title;

	public NaviAppInfo App => Process.App;

	/// <summary>
	/// Empty for the app's main window.
	/// </summary>
	public string Id => Info.Id;

	public string Key => KeyOf( Process.App.Id, Id );

	/// <summary>
	/// The panel of a headless app that runs it. It has no window: it is never drawn, minimized or focused.
	/// </summary>
	public bool IsService => Info.Id == "" && Process.App.Headless;

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
	/// The app wants to be asked before the player closes this window, e.g. because it has unsaved changes
	/// (<c>Navi.Window.SetCloseGuardAsync</c>). It can't stop the close: see <see cref="NaviComputer.AskBeforeClosing"/>.
	/// On the host.
	/// </summary>
	public bool GuardsClose { get; internal set; }

	/// <summary>
	/// Since the app was last asked about the player closing it, or null if it wasn't. On the host.
	/// </summary>
	internal RealTimeSince? CloseAskedAt { get; set; }
}

/// <summary>
/// An app's icon in the system tray, which the app sets (<c>Navi.Tray.SetAsync</c>). The desktop draws it and its menu: the
/// app's items, then Quit, which the app can't change.
/// </summary>
public sealed class NaviTray
{
	public const int MaxIcon = 32, MaxTooltip = 64, MaxLabel = 48, MaxId = 32, MaxItems = 12;

	/// <summary>
	/// Material icon name.
	/// </summary>
	public string Icon { get; set; } = "";

	/// <summary>
	/// Shown after the app's name when the pointer rests on the icon.
	/// </summary>
	public string Tooltip { get; set; } = "";

	public IReadOnlyList<NaviTrayItem> Items { get; set; } = [];

	internal bool SameAs( NaviTray other ) =>
		Icon == other.Icon && Tooltip == other.Tooltip && Items.Count == other.Items.Count
		&& Items.Zip( other.Items ).All( x => x.First.Id == x.Second.Id && x.First.Label == x.Second.Label && x.First.Icon == x.Second.Icon );

	internal NaviTraySnapshot ToSnapshot() => new() { Icon = Icon, Tooltip = Tooltip, Items = Items.ToList() };

	internal static NaviTray FromSnapshot( NaviTraySnapshot tray ) =>
		tray is null ? null : Make( tray.Icon, tray.Tooltip, tray.Items?.Select( x => (x?.Id, x?.Label, x?.Icon) ) );

	/// <summary>
	/// A tray from what an app asked for, cut to size. Items without an id or label, or with an id that repeats, are dropped.
	/// </summary>
	internal static NaviTray Make( string icon, string tooltip, IEnumerable<(string Id, string Label, string Icon)> items )
	{
		var made = new List<NaviTrayItem>();

		foreach ( var (id, label, itemIcon) in items ?? [] )
		{
			if ( string.IsNullOrWhiteSpace( id ) || string.IsNullOrWhiteSpace( label ) || id.Length > MaxId
				|| made.Count >= MaxItems || made.Any( x => x.Id == id ) )
				continue;

			made.Add( new NaviTrayItem { Id = id, Label = NaviAppInfo.Cap( label, MaxLabel ), Icon = NaviAppInfo.Cap( itemIcon, MaxIcon ) } );
		}

		return new NaviTray { Icon = NaviAppInfo.Cap( icon, MaxIcon ), Tooltip = NaviAppInfo.Cap( tooltip, MaxTooltip ), Items = made };
	}
}

/// <summary>
/// An entry in a tray icon's menu. Choosing it gives the app's main panel its <see cref="Id"/> (<c>NaviApp.OnTrayClick</c>).
/// </summary>
public sealed class NaviTrayItem
{
	public string Id { get; set; }
	public string Label { get; set; }

	/// <summary>
	/// Material icon name, or empty.
	/// </summary>
	public string Icon { get; set; } = "";
}

/// <summary>
/// A permission dialog on the desktop. Either a review of all the permissions an app declares, which sets exactly what it
/// has (before its first launch, or from its taskbar menu), or a running app asking for more (<c>Navi.Permissions.RequestAsync</c>).
/// </summary>
public sealed class NaviPermissionRequest
{
	/// <summary>
	/// Names the request across the network, so another client's answer can say which one it is for.
	/// </summary>
	public int Id { get; init; }

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
/// In multiplayer the host runs it (<see cref="IsAuthority"/>), and other clients draw what it sends: see
/// <c>NaviComputer.Network.cs</c>. Every method that changes the computer does nothing on them.
/// </summary>
public sealed partial class NaviComputer : Component
{
	/// <summary>
	/// Ids of the installed apps. System apps are always installed and don't need to be listed.
	/// </summary>
	[Property]
	public List<string> InstalledApps { get; set; } = new()
	{
		NaviAppRegistry.BuiltInFolder + "calculator.napp", NaviAppRegistry.BuiltInFolder + "textedit.napp",
	};

	/// <summary>
	/// Makes this computer a kiosk. When it lists any app ids, those are the only apps the computer has: system apps are
	/// not installed unless listed, and nothing else can be installed. Set in the scene, never saved, and no app or
	/// <c>User...</c> call can change it. Leave it empty for a normal computer.
	/// </summary>
	[Property] public List<string> OnlyApps { get; set; } = new();

	/// <summary>
	/// Names this computer for the game, e.g. "home". An app whose manifest lists <see cref="NaviAppResource.ComputerTags"/>
	/// can only be installed on a computer with one of them. Compared without case. Set in the scene, never saved, and no
	/// app or <c>User...</c> call can change it.
	/// </summary>
	[Property] public List<string> Tags { get; set; } = new();

	/// <summary>
	/// Can this computer install the app? Yes if the app lists no <see cref="NaviAppInfo.ComputerTags"/>, or this computer
	/// has one of them. An app that is already installed stays, whatever its tags.
	/// </summary>
	public bool CanInstall( NaviAppInfo app )
	{
		if ( app?.ComputerTags is not { Count: > 0 } allowed )
			return true;

		var own = CleanTags( Tags );
		return allowed.Any( own.Contains );
	}

	internal static string[] CleanTags( IEnumerable<string> tags ) =>
		tags?.Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( x => x.Trim().ToLowerInvariant() ).Distinct().ToArray() ?? [];

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
	/// Ids of the apps pinned to the taskbar, in the order they show there. Set it in the editor to preconfigure a computer
	/// (a kiosk, say) with its main apps laid out; the player's own pins and moves change it from there. Nothing is pinned
	/// by default, not even core apps. See <see cref="TaskbarApps"/>.
	/// </summary>
	[Property] public List<string> PinnedApps { get; set; } = new();

	/// <summary>
	/// Ids of the apps the player pinned to the top of the start menu, in the order they put them in. See
	/// <see cref="StartFavoriteApps"/>.
	/// </summary>
	[Property] public List<string> StartFavorites { get; set; } = new() { NaviAppRegistry.PackagesApp, NaviAppRegistry.SettingsApp };

	/// <summary>
	/// When each app was installed, in Unix seconds (UTC), by app id. The start menu lists the ones from the last
	/// <see cref="RecentWindowSeconds"/> so they're easy to find. See <see cref="RecentApps"/>.
	/// </summary>
	public Dictionary<string, long> InstalledAt { get; set; } = new();

	/// <summary>
	/// How many recently installed apps the start menu lists.
	/// </summary>
	public const int MaxRecentApps = 2;

	/// <summary>
	/// How long after being installed an app counts as recently installed, in seconds.
	/// </summary>
	public const long RecentWindowSeconds = 60 * 60;

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
		if ( !IsAuthority || !IsDisplayScale( scale ) )
			return false;

		DisplayScale = scale;
		Version++;
		return true;
	}

	static bool IsDisplayScale( float scale ) => DisplayScales.Any( x => System.MathF.Abs( x - scale ) < 0.001f );

	/// <summary>
	/// The highest blur and transparency level. Both go from 0 (none) to this, in steps the style sheets define.
	/// </summary>
	public const int MaxThemeLevel = Navi.MaxThemeLevel;

	public const int DefaultBlurLevel = 2;
	public const int DefaultTransparencyLevel = 2;

	/// <summary>
	/// The theme the desktop, its windows and the apps are drawn in.
	/// </summary>
	[Property] public Navi.Theme Theme { get; set; } = Navi.Theme.Paper;

	/// <summary>
	/// Light or dark. Every theme has both.
	/// </summary>
	[Property] public Navi.ColorScheme ColorScheme { get; set; } = Navi.ColorScheme.Light;

	/// <summary>
	/// The picture behind the windows. Not tied to the theme: any wallpaper goes with any theme.
	/// </summary>
	[Property] public Navi.Wallpaper Wallpaper { get; set; } = Navi.Wallpaper.Paper;

	/// <summary>
	/// How strongly the Glass theme blurs what is behind a surface, 0 (not at all) to <see cref="MaxThemeLevel"/>.
	/// </summary>
	[Property, Range( 0, MaxThemeLevel )] public int BlurLevel { get; set; } = DefaultBlurLevel;

	/// <summary>
	/// How much of what is behind a surface shows through it in the Glass theme, 0 (nothing: solid) to
	/// <see cref="MaxThemeLevel"/>.
	/// </summary>
	[Property, Range( 0, MaxThemeLevel )] public int TransparencyLevel { get; set; } = DefaultTransparencyLevel;

	/// <summary>
	/// The classes the desktop and the secure layer put on their root panels for the look above, e.g.
	/// <c>theme-glass scheme-dark wallpaper-paper blur-2 clear-2</c>. See <see cref="NaviThemeClasses"/>.
	/// </summary>
	public string ThemeClasses =>
		$"{NaviThemeClasses.Of( Theme )} {NaviThemeClasses.Of( ColorScheme )} {NaviThemeClasses.Of( Wallpaper )} blur-{BlurLevel} clear-{TransparencyLevel}";

	/// <summary>
	/// Set <see cref="Wallpaper"/>. Returns false if it isn't a wallpaper.
	/// </summary>
	public bool SetWallpaper( Navi.Wallpaper wallpaper )
	{
		if ( !IsAuthority || !System.Enum.IsDefined( wallpaper ) )
			return false;

		Wallpaper = wallpaper;
		Version++;
		return true;
	}

	/// <summary>
	/// Set <see cref="Theme"/>. Returns false if it isn't a theme.
	/// </summary>
	public bool SetTheme( Navi.Theme theme )
	{
		if ( !IsAuthority || !System.Enum.IsDefined( theme ) )
			return false;

		Theme = theme;
		Version++;
		return true;
	}

	/// <summary>
	/// Set <see cref="ColorScheme"/>. Returns false if it isn't a scheme.
	/// </summary>
	public bool SetColorScheme( Navi.ColorScheme scheme )
	{
		if ( !IsAuthority || !System.Enum.IsDefined( scheme ) )
			return false;

		ColorScheme = scheme;
		Version++;
		return true;
	}

	/// <summary>
	/// Set <see cref="BlurLevel"/>. Returns false if it is out of range.
	/// </summary>
	public bool SetBlurLevel( int level )
	{
		if ( !IsAuthority || level is < 0 or > MaxThemeLevel )
			return false;

		BlurLevel = level;
		Version++;
		return true;
	}

	/// <summary>
	/// Set <see cref="TransparencyLevel"/>. Returns false if it is out of range.
	/// </summary>
	public bool SetTransparencyLevel( int level )
	{
		if ( !IsAuthority || level is < 0 or > MaxThemeLevel )
			return false;

		TransparencyLevel = level;
		Version++;
		return true;
	}

	public const int DefaultSnapDistance = 12;

	/// <summary>
	/// The most <see cref="SnapDistance"/> can be, in panel pixels.
	/// </summary>
	public const int MaxSnapDistance = 40;

	/// <summary>
	/// Whether a window being moved or resized snaps to the edges of the screen.
	/// </summary>
	[Property] public bool SnapToScreenEdges { get; set; } = true;

	/// <summary>
	/// Whether a window being moved or resized snaps to the edges of the other windows.
	/// </summary>
	[Property] public bool SnapToWindows { get; set; } = true;

	/// <summary>
	/// How close, in panel pixels, a window's edge has to get to snap to an edge.
	/// </summary>
	[Property, Range( 1, MaxSnapDistance )] public int SnapDistance { get; set; } = DefaultSnapDistance;

	/// <summary>
	/// Set <see cref="SnapToScreenEdges"/>.
	/// </summary>
	public bool SetSnapToScreenEdges( bool snap )
	{
		if ( !IsAuthority )
			return false;

		SnapToScreenEdges = snap;
		Version++;
		return true;
	}

	/// <summary>
	/// Set <see cref="SnapToWindows"/>.
	/// </summary>
	public bool SetSnapToWindows( bool snap )
	{
		if ( !IsAuthority )
			return false;

		SnapToWindows = snap;
		Version++;
		return true;
	}

	/// <summary>
	/// Set <see cref="SnapDistance"/>. Returns false if it is out of range.
	/// </summary>
	public bool SetSnapDistance( int distance )
	{
		if ( !IsAuthority || distance is < 1 or > MaxSnapDistance )
			return false;

		SnapDistance = distance;
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
	int lastRequestId;

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
		// Other clients get all of it from the host.
		if ( !IsAuthority )
			return;

		sceneApps = InstalledApps?.ToList() ?? new();

		// A save replaces what the scene set up, and downloads its own packages.
		if ( !AutoSave || !Load() )
		{
			InstallSceneApps();
			_ = DownloadInstalledPackagesAsync();
		}

		if ( Files is null )
			LoadFiles( read: AutoSave );

		hasNotifications = true;
	}

	/// <summary>
	/// <see cref="InstalledApps"/> as the scene had it when the game started, before a save replaced it.
	/// </summary>
	List<string> sceneApps;

	/// <summary>
	/// The apps in <see cref="sceneApps"/> that have been installed from it, once. These go in the save, so a save knows
	/// which of the scene's apps it has seen.
	/// </summary>
	List<string> configuredApps = new();

	/// <summary>
	/// Install the scene's apps that this computer hasn't taken on yet. A save holds the installed apps as they were, so
	/// without this an app added to the scene's list after the save was made would never be installed. An app the player
	/// uninstalled stays uninstalled: it is already in <see cref="configuredApps"/>. An app that doesn't exist yet isn't
	/// marked, and is tried again at the next start.
	/// </summary>
	void InstallSceneApps()
	{
		foreach ( var id in sceneApps ?? [] )
		{
			if ( configuredApps.Contains( id ) || NaviAppRegistry.Find( id ) is null )
				continue;

			configuredApps.Add( id );
			Install( id );
		}
	}

	/// <summary>
	/// Players can't walk through the screen. Adds a thin collider the size of the <see cref="WorldPanel"/> on this
	/// GameObject.
	/// </summary>
	[Property( Title = "Solid object" )] public bool SolidObject { get; set; }

	/// <summary>
	/// How thick the screen's collider is, in the GameObject's local units.
	/// </summary>
	const float SolidThickness = 1f;

	BoxCollider solidCollider;

	/// <summary>
	/// Add, resize or remove the screen's collider to match <see cref="SolidObject"/> and the panel. It isn't saved with the
	/// scene: the flag is.
	/// </summary>
	void UpdateSolid()
	{
		var panel = SolidObject ? Components.Get<WorldPanel>() : null;

		if ( !panel.IsValid() )
		{
			if ( solidCollider.IsValid() )
				solidCollider.Destroy();

			solidCollider = null;
			return;
		}

		if ( !solidCollider.IsValid() )
		{
			solidCollider = Components.Create<BoxCollider>();
			solidCollider.Flags |= ComponentFlags.NotSaved | ComponentFlags.Hidden;
			solidCollider.Static = true;
		}

		var (center, size) = ScreenRect( panel );
		var scale = new Vector3( SolidThickness, size.x, size.y );

		if ( solidCollider.Center != center ) solidCollider.Center = center;
		if ( solidCollider.Scale != scale ) solidCollider.Scale = scale;
	}

	/// <summary>
	/// Where the panel's screen is on this GameObject, in local units: its center, and its width and height.
	/// </summary>
	static (Vector3 Center, Vector2 Size) ScreenRect( WorldPanel panel )
	{
		// Same maths as NaviInputCapture.ScreenView, in local units: panel x along Right (-Y), y along Down (-Z).
		var size = panel.PanelSize * Sandbox.UI.WorldPanel.ScreenToWorldScale;

		var offset = Vector2.Zero;
		if ( panel.HorizontalAlign == WorldPanel.HAlignment.Left ) offset.x = size.x * 0.5f;
		if ( panel.HorizontalAlign == WorldPanel.HAlignment.Right ) offset.x = -size.x * 0.5f;
		if ( panel.VerticalAlign == WorldPanel.VAlignment.Top ) offset.y = size.y * 0.5f;
		if ( panel.VerticalAlign == WorldPanel.VAlignment.Bottom ) offset.y = -size.y * 0.5f;

		return (new Vector3( 0, -offset.x, -offset.y ), size);
	}

	/// <summary>
	/// How far behind the screen its black back plane sits, in the GameObject's local units. Enough that it doesn't z-fight
	/// with the panel.
	/// </summary>
	const float BackPlaneOffset = 0.1f;

	GameObject backPlane;

	/// <summary>
	/// Keep a black plane just behind the screen, facing away from it, so the back of the screen is black rather than the
	/// panel seen from behind. Every client makes its own: it isn't saved or sent over the network.
	/// </summary>
	void UpdateBackPlane()
	{
		if ( Components.Get<WorldPanel>() is not { } panel )
		{
			if ( backPlane.IsValid() )
				backPlane.Destroy();

			backPlane = null;
			return;
		}

		if ( !backPlane.IsValid() )
		{
			backPlane = new GameObject( GameObject, true, "Navi Back Plane" );
			backPlane.Flags |= GameObjectFlags.NotSaved;
			backPlane.NetworkMode = NetworkMode.Never;

			var renderer = backPlane.Components.Create<ModelRenderer>();
			renderer.Model = Model.Load( "models/dev/plane.vmdl" );
			renderer.Tint = Color.Black;
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}

		// The plane model lies flat, facing up (+Z); pitched down it faces backward, its x along Up.
		var (center, size) = ScreenRect( panel );
		var local = new Transform( center - Vector3.Forward * BackPlaneOffset, Rotation.FromPitch( -90 ), new Vector3( size.y / 100f, size.x / 100f, 1f ) );

		if ( backPlane.LocalTransform != local )
			backPlane.LocalTransform = local;
	}

	protected override void OnUpdate()
	{
		UpdateBackPlane();
		UpdateSolid();
		UpdateNetwork();
		UpdateNotificationArrivals();

		if ( !IsAuthority )
			return;

		UpdateFilesSave();
		UpdateMessages();

		// The delay counts from the latest change.
		if ( changedVersion != Version )
		{
			changedVersion = Version;
			sinceChange = 0;
		}

		if ( AutoSave && savedVersion != Version && sinceChange > AutoSaveDelay )
			Save();
	}

	protected override void OnDisabled()
	{
		if ( solidCollider.IsValid() )
			solidCollider.Destroy();

		solidCollider = null;

		// On clients this runs when the computer goes out of range too, and the stand-in must stay for that. The host's
		// computer is never out of range, so here it was really turned off.
		if ( IsAuthority )
			DestroyStandIn();

		if ( IsAuthority )
			NaviMessages.Flush();

		// Moving a window doesn't change Version; Save skips the write if nothing changed.
		if ( AutoSave && IsAuthority )
			Save();
	}

	/// <summary>
	/// Names this computer's save file (<see cref="NaviSaveData"/>). Letters, digits, '-' and '_'. Empty uses the
	/// GameObject's id, which stays the same across launches for an object saved in a scene. Set it on computers that are
	/// spawned at runtime, or the save won't find them again.
	/// </summary>
	[Property] public string SaveId { get; set; } = "";

	/// <summary>
	/// Load this computer's save when it starts, and save it a moment after every change. Turn it off to save and load
	/// only from game code (<see cref="Save"/>, <see cref="Load"/>, <see cref="NaviSaveData"/>).
	/// </summary>
	[Property] public bool AutoSave { get; set; } = true;

	/// <summary>
	/// How long the computer waits after a change before it saves, in seconds, so a burst of changes is one write.
	/// </summary>
	const float AutoSaveDelay = 1f;

	/// <summary>The file name this computer saves to.</summary>
	public string SaveKey => string.IsNullOrWhiteSpace( SaveId ) ? GameObject.Id.ToString( "N" ) : SaveId;

	int savedVersion;
	int changedVersion;
	RealTimeSince sinceChange;
	string savedJson;

	/// <summary>
	/// Write what the player changed on this computer to the current save slot. Skipped if nothing changed since the last
	/// save.
	/// </summary>
	public void Save()
	{
		if ( !IsAuthority )
			return;

		savedVersion = Version;
		SaveFiles();

		var save = CaptureSave();
		var json = Json.Serialize( save );
		if ( json == savedJson )
			return;

		NaviSaveData.Write( SaveKey, save );
		savedJson = json;
	}

	/// <summary>
	/// Replace this computer's state with its save in the current slot. Every window closes and open prompts are
	/// dismissed, since what they were opened with may no longer hold. Returns false, changing nothing, if there is no save.
	/// </summary>
	public bool Load()
	{
		if ( !IsAuthority || NaviSaveData.Read( SaveKey ) is not { } save )
			return false;

		foreach ( var process in running.ToList() )
			Close( process.App.Id );

		foreach ( var request in permissionRequests.ToList() )
			DismissPermissionRequest( request );

		RestoreSave( save );
		LoadFiles( read: true );

		// What the save brings didn't arrive just now: no banners, no sound.
		arrivedUpTo = -1;
		savedJson = Json.Serialize( CaptureSave() );
		savedVersion = Version;

		// After the saved state is recorded, so what this installs counts as a change and is saved.
		InstallSceneApps();

		_ = DownloadInstalledPackagesAsync();
		return true;
	}

	/// <summary>
	/// What the player changed on this computer, as it would be saved.
	/// </summary>
	public NaviComputerSave CaptureSave() => CaptureSave( placements: true, secrets: true );

	/// <summary>
	/// What the player changed on this computer. Without <paramref name="placements"/>, it leaves out where windows
	/// were, which only the host needs. Without <paramref name="secrets"/>, it leaves out the lock's verifier, which only the
	/// host keeps and is never sent.
	/// </summary>
	NaviComputerSave CaptureSave( bool placements, bool secrets ) => new()
	{
		InstalledApps = InstalledApps?.ToList() ?? new(),
		ConfiguredApps = configuredApps.ToList(),
		InstalledPackages = InstalledPackages?.ToList() ?? new(),
		GrantedPermissions = GrantedPermissions?.ToList() ?? new(),
		ReviewedApps = ReviewedApps?.ToList() ?? new(),
		RootApps = RootApps?.ToList() ?? new(),
		TaskbarOrder = PinnedApps?.ToList() ?? new(),
		StartFavorites = StartFavorites?.ToList() ?? new(),
		InstalledAt = InstalledAt is null ? new() : new( InstalledAt ),
		Notifications = CaptureNotifications(),
		MutedNotificationApps = MutedNotificationApps?.ToList() ?? new(),
		LastNotificationId = LastNotificationId,
		NotificationsReadUpTo = NotificationsReadUpTo,
		LockKind = LockKind,
		LockSalt = LockSalt,
		LockVerifier = secrets ? lockVerifier : null,
		DisplayScale = DisplayScale,
		Theme = Theme,
		ColorScheme = ColorScheme,
		Wallpaper = Wallpaper,
		BlurLevel = BlurLevel,
		TransparencyLevel = TransparencyLevel,
		SnapToScreenEdges = SnapToScreenEdges,
		SnapToWindows = SnapToWindows,
		SnapDistance = SnapDistance,
		WindowPlacements = placements ? CapturePlacements() : new(),
	};

	/// <summary>
	/// Where each window was when it last closed, and where the open ones are now. Sorted, so the same layout is the
	/// same JSON.
	/// </summary>
	List<NaviWindowPlacement> CapturePlacements()
	{
		var all = new Dictionary<string, NaviWindowPlacement>( windowPlacements );
		foreach ( var window in Windows.Where( x => !x.IsService ) )
			all[window.Key] = NaviWindowPlacement.Of( window );

		return all.Values.OrderBy( x => x.Window, System.StringComparer.Ordinal ).ToList();
	}

	/// <summary>
	/// Take on a save's state. Unknown permissions and display scales are dropped; the rest is checked where it is used,
	/// as always (root needs a system app, a grant only counts for a permission the app declares).
	/// </summary>
	public void RestoreSave( NaviComputerSave save )
	{
		if ( IsAuthority )
			ApplySave( save );
	}

	void ApplySave( NaviComputerSave save )
	{
		InstalledApps = save.InstalledApps?.Distinct().ToList() ?? new();
		configuredApps = save.ConfiguredApps?.Distinct().ToList() ?? new();
		InstalledPackages = save.InstalledPackages?.Distinct().ToList() ?? new();
		GrantedPermissions = save.GrantedPermissions?.Where( x => NaviPermissions.IsKnown( x.Split( ' ' ).Last() ) ).Distinct().ToList() ?? new();
		ReviewedApps = save.ReviewedApps?.Distinct().ToList() ?? new();
		RootApps = save.RootApps?.Distinct().ToList() ?? new();
		PinnedApps = save.TaskbarOrder?.Distinct().ToList() ?? new();
		StartFavorites = save.StartFavorites?.Distinct().ToList() ?? new();
		InstalledAt = save.InstalledAt is null ? new() : new( save.InstalledAt );
		ApplyNotifications( save );

		// A lock needs its salt, and on the host its verifier: clients are never sent that one.
		var hasLock = System.Enum.IsDefined( save.LockKind ) && save.LockKind != NaviLockKind.None
			&& NaviLockCrypto.IsHex( save.LockSalt, NaviLockCrypto.SaltLength )
			&& (!IsAuthority || NaviLockCrypto.IsHex( save.LockVerifier, NaviLockCrypto.ProofLength ));
		LockKind = hasLock ? save.LockKind : NaviLockKind.None;
		LockSalt = hasLock ? save.LockSalt.ToUpperInvariant() : null;
		lockVerifier = hasLock && IsAuthority ? save.LockVerifier.ToUpperInvariant() : null;

		DisplayScale = IsDisplayScale( save.DisplayScale ) ? save.DisplayScale : 1f;

		Theme = System.Enum.IsDefined( save.Theme ) ? save.Theme : Navi.Theme.Paper;
		ColorScheme = System.Enum.IsDefined( save.ColorScheme ) ? save.ColorScheme : Navi.ColorScheme.Light;
		Wallpaper = System.Enum.IsDefined( save.Wallpaper ) ? save.Wallpaper : Navi.Wallpaper.Paper;
		BlurLevel = int.Clamp( save.BlurLevel, 0, MaxThemeLevel );
		TransparencyLevel = int.Clamp( save.TransparencyLevel, 0, MaxThemeLevel );

		SnapToScreenEdges = save.SnapToScreenEdges;
		SnapToWindows = save.SnapToWindows;
		SnapDistance = int.Clamp( save.SnapDistance, 1, MaxSnapDistance );

		// Whether a window may be maximized or sized is checked when it opens, against its manifest at that time.
		windowPlacements.Clear();
		foreach ( var placement in save.WindowPlacements ?? [] )
		{
			if ( placement is null || string.IsNullOrEmpty( placement.Window ) || !IsPlausible( placement.Position )
				|| (placement.Size is { } size && (!IsPlausible( size ) || size.x <= 0f || size.y <= 0f)) )
				continue;

			windowPlacements[placement.Window] = placement;
		}

		Version++;
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
	/// Every window of every running app.
	/// </summary>
	public IEnumerable<NaviWindow> Windows => running.SelectMany( x => x.Windows );

	/// <summary>
	/// The visible window in front, or null.
	/// </summary>
	public NaviWindow FocusedWindow => Windows.Where( x => !x.IsService && !x.IsMinimized ).OrderByDescending( x => x.ZIndex ).FirstOrDefault();

	/// <summary>
	/// The window that has the player's input: the window in front, while someone sits at the computer. Otherwise null.
	/// See <see cref="NaviInputCapture"/>.
	/// </summary>
	public NaviWindow InputCapture => HasUser && !IsLocked && !SecureEntryActive ? FocusedWindow : null;

	/// <summary>
	/// Installed apps, sorted by name.
	/// </summary>
	public IEnumerable<NaviAppInfo> Installed => NaviAppRegistry.All.Where( IsInstalled );

	/// <summary>
	/// The apps on the taskbar: the pinned ones in the player's order, then the running ones that have a window and aren't
	/// pinned, in the order they were opened.
	/// </summary>
	public IEnumerable<NaviAppInfo> TaskbarApps
	{
		get
		{
			var pinned = (PinnedApps ?? []).Select( NaviAppRegistry.Find ).Where( x => x is not null && IsInstalled( x ) );
			var open = Running.Where( x => x.Windows.Any( y => !y.IsService ) ).Select( x => x.App );

			return pinned.Concat( open ).DistinctBy( x => x.Id );
		}
	}

	/// <summary>
	/// Whether an app is pinned to the taskbar.
	/// </summary>
	public bool IsTaskbarPinned( string id ) => PinnedApps?.Contains( id ) == true;

	/// <summary>
	/// Pin an installed app to the taskbar, after the others, or unpin it. Returns false if the app isn't installed.
	/// </summary>
	public bool ToggleTaskbarPin( string id )
	{
		if ( !IsAuthority || !IsInstalled( id ) )
			return false;

		PinnedApps ??= new();
		if ( !PinnedApps.Remove( id ) )
			PinnedApps.Add( id );

		Version++;
		return true;
	}

	/// <summary>
	/// Move an app's taskbar icon to <paramref name="index"/> in <see cref="TaskbarApps"/>. With <paramref name="pin"/> an app
	/// that isn't pinned becomes pinned, at <paramref name="index"/> among the pinned ones (after them all if that is their
	/// count); without it, moving it among the apps that are only open reorders those, and pins nothing. A pinned app stays
	/// among the pinned ones. Returns false if the app isn't on the taskbar, the index is out of range, or the icon is
	/// already there and isn't being pinned.
	/// </summary>
	public bool MoveTaskbarApp( string id, int index, bool pin = false )
	{
		if ( !IsAuthority )
			return false;

		var apps = TaskbarApps.Select( x => x.Id ).ToList();
		var from = apps.IndexOf( id );
		if ( from < 0 || index < 0 || index >= apps.Count || (index == from && !(pin && !IsTaskbarPinned( id ))) )
			return false;

		var wasPinned = IsTaskbarPinned( id );

		apps.RemoveAt( from );
		apps.Insert( index, id );

		if ( wasPinned || pin )
		{
			var pinned = PinnedApps?.ToHashSet() ?? [];
			pinned.Add( id );
			PinnedApps = apps.Where( pinned.Contains ).ToList();
		}
		else
		{
			// Open apps are in the order they were opened, so the new order goes into the slots they have in it.
			var order = apps.Where( x => !IsTaskbarPinned( x ) ).ToList();
			var slots = Enumerable.Range( 0, running.Count ).Where( i => order.Contains( running[i].App.Id ) ).ToList();
			var moved = slots.Select( i => running[i] ).OrderBy( x => order.IndexOf( x.App.Id ) ).ToList();

			for ( var i = 0; i < slots.Count; i++ )
				running[slots[i]] = moved[i];
		}

		Version++;
		return true;
	}

	/// <summary>
	/// The apps pinned to the start menu, in the player's order. Ones that aren't installed are left out.
	/// </summary>
	public IEnumerable<NaviAppInfo> StartFavoriteApps =>
		(StartFavorites ?? []).Select( NaviAppRegistry.Find ).Where( x => x is not null && IsInstalled( x ) );

	/// <summary>
	/// The last <see cref="MaxRecentApps"/> apps installed within <see cref="RecentWindowSeconds"/>, newest first.
	/// </summary>
	public IEnumerable<NaviAppInfo> RecentApps
	{
		get
		{
			var since = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() - RecentWindowSeconds;

			return (InstalledAt ?? [])
				.Where( x => x.Value >= since )
				.OrderByDescending( x => x.Value )
				.Select( x => NaviAppRegistry.Find( x.Key ) )
				.Where( x => x is not null && IsInstalled( x ) )
				.Take( MaxRecentApps );
		}
	}

	public bool IsStartFavorite( string id ) => StartFavorites?.Contains( id ) ?? false;

	/// <summary>
	/// Pin an installed app to the start menu, after the others, or unpin it. Returns false if the app isn't installed.
	/// </summary>
	public bool ToggleStartFavorite( string id )
	{
		if ( !IsAuthority || !IsInstalled( id ) )
			return false;

		StartFavorites ??= new();
		if ( !StartFavorites.Remove( id ) )
			StartFavorites.Add( id );

		Version++;
		return true;
	}

	/// <summary>
	/// Move a pinned app to <paramref name="index"/> among the pinned apps. Returns false if it isn't pinned, the index is
	/// out of range, or it is already there.
	/// </summary>
	public bool MoveStartFavorite( string id, int index )
	{
		if ( !IsAuthority || StartFavorites is null )
			return false;

		var from = StartFavorites.IndexOf( id );
		if ( from < 0 || index < 0 || index >= StartFavorites.Count || index == from )
			return false;

		StartFavorites.RemoveAt( from );
		StartFavorites.Insert( index, id );
		Version++;
		return true;
	}

	public bool IsInstalled( string id ) =>NaviAppRegistry.Find( id ) is { } app && IsInstalled( app );

	public bool IsInstalled( NaviAppInfo app ) =>
		OnlyApps is { Count: > 0 }
			? OnlyApps.Contains( app.Id )
			: app.IsCore || (InstalledApps?.Contains( app.Id ) ?? false);

	/// <summary>
	/// Has full system access: a system app that is part of the OS, or that the player accepted.
	/// </summary>
	public bool HasRoot( NaviAppInfo app ) => app.IsSystem && (app.IsPrepackaged || (RootApps?.Contains( app.Id ) ?? false));

	public bool IsRunning( string id ) => running.Any( x => x.App.Id == id );

	/// <summary>
	/// The running app, or null.
	/// </summary>
	public NaviProcess GetProcess( string id ) => running.FirstOrDefault( x => x.App.Id == id );

	/// <summary>
	/// A window of a running app (the main one when <paramref name="windowId"/> is empty), or null.
	/// </summary>
	public NaviWindow GetWindow( string id, string windowId = "" ) => GetProcess( id )?.GetWindow( windowId );

	/// <summary>
	/// The window with this key (<see cref="NaviWindow.Key"/>), or null.
	/// </summary>
	public NaviWindow FindWindow( string key ) => key is null ? null : Windows.FirstOrDefault( x => x.Key == key );

	public bool IsPackageInstalled( string packageIdent ) => InstalledPackages?.Contains( packageIdent ) ?? false;

	public bool IsDownloading( string packageIdent ) => downloading.Contains( packageIdent );

	/// <summary>
	/// Store packages being downloaded right now.
	/// </summary>
	public IEnumerable<string> DownloadingPackages => downloading;

	/// <summary>
	/// Download an app from the Store and install it. Returns false if the download failed, or the game doesn't allow
	/// the package (<see cref="NaviStore.IsAllowed"/>).
	/// </summary>
	public async Task<bool> InstallPackageAsync( string packageIdent )
	{
		if ( !IsAuthority || !NaviStore.IsAllowed( packageIdent ) || IsPackageInstalled( packageIdent ) || IsDownloading( packageIdent ) )
			return false;

		var app = await DownloadPackageAsync( packageIdent );
		if ( app is null )
			return false;

		// The manifest can only be read once it is downloaded, so the tags are checked here, before root is asked for.
		if ( !CanInstall( app ) )
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

	/// <summary>
	/// Forget a Store package, and uninstall its app if it is installed. For a package whose app didn't load (the
	/// download failed), which has no app to uninstall. Returns false if the package wasn't installed.
	/// </summary>
	public bool UninstallPackage( string packageIdent )
	{
		if ( !IsAuthority || InstalledPackages is null )
			return false;

		var id = appPackages.FirstOrDefault( x => x.Value == packageIdent ).Key;
		if ( id is not null && Uninstall( id ) )
			return true;

		if ( !InstalledPackages.Remove( packageIdent ) )
			return false;

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
	/// Install an app. Returns false if it doesn't exist, is already installed, or is a Store app the game doesn't allow
	/// (<see cref="NaviStore.IsAllowed"/>), even one whose code is loaded.
	/// </summary>
	public bool Install( string id )
	{
		if ( !IsAuthority )
			return false;

		// A Store app's id is its package ident; other apps' ids are asset paths, which aren't.
		if ( NaviStore.NormalizeIdent( id ) is not null && !NaviStore.IsAllowed( id ) )
		{
			Log.Warning( $"NaviComputer: this game doesn't allow '{id}'" );
			return false;
		}

		var app = NaviAppRegistry.Find( id );
		if ( app is null )
		{
			Log.Warning( $"NaviComputer: no app with id '{id}'" );
			return false;
		}

		if ( IsInstalled( app ) || OnlyApps is { Count: > 0 } )
			return false;

		if ( !CanInstall( app ) )
		{
			Log.Warning( $"NaviComputer: '{id}' can't be installed on this computer (tags)" );
			return false;
		}

		InstalledApps ??= new();
		InstalledApps.Add( id );

		InstalledAt ??= new();
		InstalledAt[id] = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		Version++;
		return true;
	}

	/// <summary>
	/// Uninstall an app, closing it if it is open. Returns false if it isn't installed or is a system app.
	/// A Store app's code stays loaded until the game restarts, but it won't be downloaded again.
	/// </summary>
	public bool Uninstall( string id )
	{
		if ( !IsAuthority )
			return false;

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
		PinnedApps?.Remove( id );
		StartFavorites?.Remove( id );
		InstalledAt?.Remove( id );
		ForgetNotifications( id );
		ForgetAppFolder( id );
		filesToOpen.Remove( id );

		var windowPrefix = id + NaviWindow.KeySeparator;
		foreach ( var key in windowPlacements.Keys.Where( x => x == id || x.StartsWith( windowPrefix ) ).ToList() )
			windowPlacements.Remove( key );

		Version++;
		return true;
	}

	/// <summary>
	/// Open an installed app, or restore it and bring it to the front if it is already open. An app that declares
	/// permissions the player hasn't reviewed yet shows the review first, and opens once they answer it.
	/// </summary>
	public bool Launch( string id )
	{
		if ( !IsAuthority )
			return false;

		if ( GetProcess( id ) is not null )
		{
			// An app with no window to show gets the click its tray icon would.
			if ( !FocusApp( id ) )
				DeliverTrayClick( id, "" );

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
			ReviewPermissions( id, launch: true );
			return true;
		}

		Open( app );
		return true;
	}

	void Open( NaviAppInfo app )
	{
		var process = new NaviProcess
		{
			App = app,
			IsRoot = HasRoot( app ),
		};

		process.Permissions.UnionWith( GetPermissions( app ) );

		// The main panel is the app's window, or its service if the app is headless. A headless app also gets a tray icon, so
		// the player can find it and quit it.
		process.WindowList.Add( NewWindow( process, app.MainWindow ) );
		if ( app.Headless )
			process.Tray = new NaviTray { Icon = app.Icon };

		running.Add( process );
		Version++;
	}

	NaviWindow NewWindow( NaviProcess process, NaviWindowInfo info )
	{
		var window = new NaviWindow
		{
			Process = process,
			Info = info,
			ZIndex = ++topZIndex,
		};

		// A window opens where it was last time, as far as its app allows now. The desktop keeps it inside the screen.
		if ( !window.IsService && windowPlacements.TryGetValue( window.Key, out var placement ) )
		{
			window.Position = placement.Position;
			window.Size = info.CanResize ? placement.Size : null;
			window.IsMaximized = info.CanMaximize && placement.IsMaximized;
			return window;
		}

		// Cascade new windows so they don't cover each other exactly.
		var offset = (Windows.Count() % 6) * 16f;
		window.Position = new Vector2( 16f + offset, 8f + offset );
		return window;
	}

	/// <summary>
	/// Window key (<see cref="NaviWindow.Key"/>) to where the window was when it last closed. The window manager keeps
	/// them; apps can't set them. Saved with the computer, on the host only.
	/// </summary>
	readonly Dictionary<string, NaviWindowPlacement> windowPlacements = new();

	void RememberPlacement( NaviWindow window )
	{
		if ( !window.IsService )
			windowPlacements[window.Key] = NaviWindowPlacement.Of( window );
	}

	/// <summary>
	/// Close an app, and with it every window it has open.
	/// </summary>
	public void Close( string id )
	{
		if ( !IsAuthority || GetProcess( id ) is not { } process )
			return;

		foreach ( var window in process.Windows )
			RememberPlacement( window );

		running.RemoveAll( x => x.App.Id == id );

		// Nobody is left to answer these.
		foreach ( var request in permissionRequests.Where( x => x.App.Id == id && !x.IsReview ).ToList() )
			DismissPermissionRequest( request );

		Version++;
	}

	/// <summary>
	/// Open one of the windows the app's manifest lists, or bring it to the front if it is already open. An app has one of
	/// each. Returns false if the app isn't running, has no such window, or <paramref name="windowId"/> is empty: the main
	/// window opens with the app.
	/// </summary>
	public bool OpenWindow( string id, string windowId )
	{
		if ( !IsAuthority || string.IsNullOrEmpty( windowId ) || GetProcess( id ) is not { } process
			|| process.App.FindWindow( windowId ) is not { } info )
			return false;

		if ( process.GetWindow( windowId ) is not null )
		{
			Focus( id, windowId );
			return true;
		}

		process.WindowList.Add( NewWindow( process, info ) );
		Version++;
		return true;
	}

	/// <summary>
	/// Seconds after an app is asked about the player closing it (<see cref="AskBeforeClosing"/>) in which closing it again
	/// closes it without asking.
	/// </summary>
	public const float ForceCloseSeconds = 5f;

	/// <summary>
	/// Ask an app before the player closes it, or these windows of it, if one of them guards its close
	/// (<see cref="NaviWindow.GuardsClose"/>). The app gets <c>NaviApp.OnCloseRequested</c> and answers in its own window,
	/// e.g. with Save / Don't save / Cancel, then closes itself or doesn't. Returns true if it was asked, and the windows
	/// should stay open for now.
	/// </summary>
	/// <remarks>
	/// The app can only ask to be asked; it can never keep a window open. Closing it again within
	/// <see cref="ForceCloseSeconds"/> of the question closes it, whatever the app does or doesn't answer. Only the
	/// player's own closes ask: the app closing itself, another app with <see cref="NaviPermissions.ManageApps"/>,
	/// uninstalling, loading a save and game code close at once. In multiplayer only a shared app is asked, since the
	/// host's copy of any other app isn't the one the player sees.
	/// </remarks>
	internal bool AskBeforeClosing( IEnumerable<NaviWindow> windows )
	{
		var window = windows.FirstOrDefault( x => x.GuardsClose && !x.IsService );
		if ( window is null || (Networking.IsActive && !window.App.Shared) )
			return false;

		if ( window.CloseAskedAt is { } asked && asked < ForceCloseSeconds )
			return false;

		if ( !appHosts.TryGetValue( window.Key, out var host ) || !host.DeliverCloseRequest() )
			return false;

		window.CloseAskedAt = 0;
		Focus( window.App.Id, window.Id );
		return true;
	}

	/// <summary>
	/// Have the player asked before <paramref name="windowId"/> of the app is closed, or stop. False if the window isn't
	/// open. See <see cref="AskBeforeClosing"/>.
	/// </summary>
	public bool SetCloseGuard( string id, string windowId, bool guard )
	{
		if ( !IsAuthority || GetWindow( id, windowId ) is not { IsService: false } window )
			return false;

		window.GuardsClose = guard;
		if ( !guard )
			window.CloseAskedAt = null;

		return true;
	}

	/// <summary>
	/// Close a window. Closing an app's main window, or its service, closes the app (<see cref="Close"/>). Returns false if
	/// the window isn't open.
	/// </summary>
	public bool CloseWindow( string id, string windowId = "" )
	{
		if ( !IsAuthority || GetWindow( id, windowId ) is not { } window )
			return false;

		if ( window.Id == "" )
		{
			Close( id );
			return true;
		}

		RememberPlacement( window );
		window.Process.WindowList.Remove( window );
		Version++;
		return true;
	}

	/// <summary>
	/// Change what a window's title bar says after the app's name, cut to <see cref="NaviWindow.MaxTitle"/>. Empty goes back to
	/// the manifest's title. Returns false if the window isn't open.
	/// </summary>
	public bool SetTitle( string id, string windowId, string title )
	{
		if ( !IsAuthority || GetWindow( id, windowId ) is not { IsService: false } window )
			return false;

		title = NaviAppInfo.Cap( title?.Trim(), NaviWindow.MaxTitle );

		// An app may set it every frame; only a change redraws the desktop.
		if ( window.Title == (string.IsNullOrEmpty( title ) ? window.Info.Title : title) )
			return true;

		window.Title = title;
		Version++;
		return true;
	}

	// The system tray.

	/// <summary>
	/// The id the desktop gives the Quit item it adds to every tray menu. The app can't remove it or use the id for its own
	/// items.
	/// </summary>
	public const string QuitItem = "navi.quit";

	/// <summary>
	/// Set the icon and menu of an app's tray icon. Everything is cut to size (<see cref="NaviTray"/>). Returns false if the
	/// app isn't running.
	/// </summary>
	public bool SetTray( string id, string icon, string tooltip, IEnumerable<(string Id, string Label, string Icon)> items )
	{
		if ( !IsAuthority || GetProcess( id ) is not { } process )
			return false;

		var tray = NaviTray.Make( icon, tooltip, items?.Where( x => x.Id != QuitItem ) );
		if ( string.IsNullOrWhiteSpace( tray.Icon ) )
			tray.Icon = process.App.Icon;

		// An app may set it every frame; only a change redraws the desktop.
		if ( process.Tray is { } current && current.SameAs( tray ) )
			return true;

		process.Tray = tray;
		Version++;
		return true;
	}

	/// <summary>
	/// Give an app's main panel the click on its tray icon (<paramref name="item"/> empty), or on one of its menu items. It
	/// goes to this client's copy of the panel, so on a client that isn't the host an app that isn't shared hears the
	/// click on its own copy.
	/// </summary>
	internal void DeliverTrayClick( string id, string item )
	{
		if ( appHosts.TryGetValue( NaviWindow.KeyOf( id, "" ), out var host ) )
			host.DeliverTray( item );
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
		if ( !IsAuthority )
			return;

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

		if ( IsAuthority && missing.Length > 0 && running.Contains( process ) )
		{
			var request = new NaviPermissionRequest { Id = ++lastRequestId, App = process.App, Permissions = missing, Ticked = missing };
			permissionRequests.Add( request );
			Version++;

			await request.Answer.Task;
		}

		return asked.Where( process.HasPermission ).ToArray();
	}

	/// <summary>
	/// Show the player every permission an app declares, ticked as it has them (all ticked the first time), so they can
	/// change them. The taskbar menu's "Permissions..." does this, for a running app or not, and opens nothing. With
	/// <paramref name="launch"/> the app opens afterwards: that is the review before an app's first launch.
	/// </summary>
	public void ReviewPermissions( string id, bool launch = false )
	{
		if ( !IsAuthority )
			return;

		var app = NaviAppRegistry.Find( id );
		if ( app is null || !IsInstalled( app ) || app.IsSystem || app.Permissions.Count == 0 )
			return;

		if ( permissionRequests.Any( x => x.App.Id == id && x.IsReview ) )
			return;

		permissionRequests.Add( new NaviPermissionRequest
		{
			Id = ++lastRequestId,
			App = app,
			Permissions = app.Permissions,
			Ticked = NeedsReview( app ) ? app.Permissions : GetPermissions( app ).ToArray(),
			IsReview = true,
			Launch = launch && !IsRunning( id ),
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

		if ( !IsAuthority )
			return false;

		var request = permissionRequests.FirstOrDefault( x => x.App.Id == app.Id && x.IsRoot );
		if ( request is null )
		{
			request = new NaviPermissionRequest { Id = ++lastRequestId, App = app, Permissions = [], Ticked = [], IsRoot = true, Launch = launch };
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
		if ( !IsAuthority || !permissionRequests.Remove( request ) )
			return;

		request.Answer.TrySetResult( [] );
		Version++;
	}

	/// <summary>
	/// The player answered a permission dialog. <paramref name="ticked"/> are the permissions they ticked.
	/// </summary>
	public void AnswerPermissionRequest( NaviPermissionRequest request, IEnumerable<string> ticked )
	{
		if ( !IsAuthority || !permissionRequests.Remove( request ) )
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
	/// Bring a window to the front, restoring it if it is minimized.
	/// </summary>
	public void Focus( string id, string windowId = "" )
	{
		var window = GetWindow( id, windowId );
		if ( !IsAuthority || window is null || window.IsService || window == FocusedWindow )
			return;

		window.IsMinimized = false;
		window.ZIndex = ++topZIndex;
		Version++;
	}

	/// <summary>
	/// Bring all of an app's windows to the front, restoring the minimized ones, in the order they were stacked. Returns
	/// false if the app has no window to show.
	/// </summary>
	public bool FocusApp( string id )
	{
		if ( !IsAuthority || GetProcess( id ) is not { } process )
			return false;

		var windows = process.DrawnWindows.OrderBy( x => x.ZIndex ).ToList();
		if ( windows.Count == 0 )
			return false;

		foreach ( var window in windows )
		{
			window.IsMinimized = false;
			window.ZIndex = ++topZIndex;
		}

		Version++;
		return true;
	}

	/// <summary>
	/// Hide a window. Returns false if the app doesn't allow it.
	/// </summary>
	public bool Minimize( string id, string windowId = "" )
	{
		var window = GetWindow( id, windowId );
		if ( !IsAuthority || window is null || window.IsService || window.IsMinimized || !window.Info.CanMinimize )
			return false;

		window.IsMinimized = true;
		Version++;
		return true;
	}

	/// <summary>
	/// Hide all of an app's windows that allow it. Returns false if none did.
	/// </summary>
	public bool MinimizeApp( string id )
	{
		if ( !IsAuthority || GetProcess( id ) is not { } process )
			return false;

		var any = false;
		foreach ( var window in process.DrawnWindows.ToList() )
			any |= Minimize( id, window.Id );

		return any;
	}

	/// <summary>
	/// Maximize or restore a window. Returns false if the app doesn't allow it.
	/// </summary>
	public bool SetMaximized( string id, bool maximized, string windowId = "" )
	{
		var window = GetWindow( id, windowId );
		if ( !IsAuthority || window is null || window.IsService || !window.Info.CanMaximize )
			return false;

		Focus( id, windowId );

		if ( window.IsMaximized != maximized )
		{
			window.IsMaximized = maximized;
			Version++;
		}

		return true;
	}

	public bool ToggleMaximized( string id, string windowId = "" ) =>
		GetWindow( id, windowId ) is { } window && SetMaximized( id, !window.IsMaximized, windowId );
}
