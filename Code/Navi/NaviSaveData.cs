namespace Sandbox;

/// <summary>
/// What a <see cref="NaviComputer"/> remembers between game launches: everything the player changed on it. Open windows
/// aren't in it; a loaded computer starts at an empty desktop. Where each window was is
/// (<see cref="WindowPlacements"/>), so it opens there again.
/// </summary>
public sealed class NaviComputerSave
{
	public List<string> InstalledApps { get; set; } = new();

	/// <summary>
	/// The apps from the scene's <see cref="NaviComputer.InstalledApps"/> that this computer has already taken on. An app
	/// in the scene's list that isn't here is new since the last launch, and gets installed.
	/// </summary>
	public List<string> ConfiguredApps { get; set; } = new();
	public List<string> InstalledPackages { get; set; } = new();
	public List<string> GrantedPermissions { get; set; } = new();
	public List<string> ReviewedApps { get; set; } = new();
	public List<string> RootApps { get; set; } = new();
	public List<string> TaskbarOrder { get; set; } = new();
	public List<string> StartFavorites { get; set; } = new() { NaviAppRegistry.PackagesApp, NaviAppRegistry.SettingsApp };
	public Dictionary<string, long> InstalledAt { get; set; } = new();
	public NaviLockKind LockKind { get; set; }
	public string LockSalt { get; set; }

	/// <summary>
	/// The host's hash of the lock's proof. In what is saved to disk, never in what the host sends to clients.
	/// </summary>
	public string LockVerifier { get; set; }
	public float DisplayScale { get; set; } = 1f;
	public Navi.Theme Theme { get; set; } = Navi.Theme.Paper;
	public Navi.ColorScheme ColorScheme { get; set; } = Navi.ColorScheme.Light;
	public Navi.Wallpaper Wallpaper { get; set; } = Navi.Wallpaper.Paper;
	public int BlurLevel { get; set; } = NaviComputer.DefaultBlurLevel;
	public int TransparencyLevel { get; set; } = NaviComputer.DefaultTransparencyLevel;
	public bool SnapToScreenEdges { get; set; } = true;
	public bool SnapToWindows { get; set; } = true;
	public int SnapDistance { get; set; } = NaviComputer.DefaultSnapDistance;
	public List<NaviWindowPlacement> WindowPlacements { get; set; } = new();
}

/// <summary>
/// Where a window was when it last closed, or when the computer was last saved, so it opens there again. The window
/// manager keeps one per window (<see cref="NaviWindow.Key"/>) from what the player did; apps can't set it.
/// </summary>
public sealed class NaviWindowPlacement
{
	/// <summary>
	/// The window's <see cref="NaviWindow.Key"/>.
	/// </summary>
	public string Window { get; set; }

	/// <summary>
	/// Position when not maximized, in panel pixels.
	/// </summary>
	public Vector2 Position { get; set; }

	/// <summary>
	/// Size when not maximized, in panel pixels, or null to fit the app's content.
	/// </summary>
	public Vector2? Size { get; set; }

	public bool IsMaximized { get; set; }

	internal static NaviWindowPlacement Of( NaviWindow window ) => new()
	{
		Window = window.Key,
		Position = window.Position,
		Size = window.Size,
		IsMaximized = window.IsMaximized,
	};
}

/// <summary>
/// Saves Navi computers to the game's local data (<see cref="FileSystem.Data"/>), one file per computer in a folder per
/// save slot: <c>navi/saves/{slot}/{computer}.json</c>. A computer loads its save when it starts and saves itself when
/// it changes (<see cref="NaviComputer.AutoSave"/>). A game with save slots sets <see cref="Slot"/> and calls
/// <see cref="LoadAll"/> when the player loads a save.
/// </summary>
/// <remarks>
/// The files are plain JSON on the player's disk, and every package's code in the game can write them, the way it can
/// change a <see cref="NaviComputer"/> directly. They keep the player's choices; they don't add security. Loading still
/// goes through the computer's rules: root needs a system app, permissions are only ones the app declares.
/// </remarks>
public static class NaviSaveData
{
	/// <summary>
	/// The save slot computers load from and save to. A plain name; anything else falls back to "default".
	/// </summary>
	public static string Slot
	{
		get;
		set => field = IsPlainName( value ) ? value : "default";
	} = "default";

	static string PathFor( string id ) => $"navi/saves/{Slot}/{id}.json";

	/// <summary>
	/// The computer's save in the current slot, or null if it has none.
	/// </summary>
	public static NaviComputerSave Read( string id )
	{
		if ( !IsPlainName( id ) || FileSystem.Data is not { } data || !data.FileExists( PathFor( id ) ) )
			return null;

		try
		{
			return data.ReadJson<NaviComputerSave>( PathFor( id ) );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't read the save of computer '{id}': {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Write the computer's save to the current slot.
	/// </summary>
	public static void Write( string id, NaviComputerSave save )
	{
		if ( !IsPlainName( id ) || FileSystem.Data is not { } data )
			return;

		try
		{
			data.CreateDirectory( $"navi/saves/{Slot}" );
			data.WriteJson( PathFor( id ), save );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't save computer '{id}': {e.Message}" );
		}
	}

	/// <summary>
	/// Load every computer in the scene from the current slot. Computers without a save there keep what they have.
	/// </summary>
	public static void LoadAll( Scene scene )
	{
		foreach ( var computer in scene?.GetAllComponents<NaviComputer>() ?? [] )
			computer.Load();
	}

	/// <summary>
	/// Save every computer in the scene to the current slot.
	/// </summary>
	public static void SaveAll( Scene scene )
	{
		foreach ( var computer in scene?.GetAllComponents<NaviComputer>() ?? [] )
			computer.Save();
	}

	/// <summary>
	/// Letters, digits, '-' and '_' only, so a name can't reach outside the saves folder.
	/// </summary>
	static bool IsPlainName( string name ) =>
		!string.IsNullOrEmpty( name ) && name.Length <= 64 && name.All( x => char.IsAsciiLetterOrDigit( x ) || x is '-' or '_' );
}
