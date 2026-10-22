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

	/// <summary>
	/// The notification list, oldest first. See <see cref="NaviComputer.Notifications"/>.
	/// </summary>
	public List<NaviNotification> Notifications { get; set; } = new();
	public List<string> MutedNotificationApps { get; set; } = new();
	public int LastNotificationId { get; set; }
	public int NotificationsReadUpTo { get; set; }
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

	/// <summary>
	/// Ids of the installed hardware (<see cref="NaviComputer.Hardware"/>). Null in a save from before Navi had hardware,
	/// which then keeps what the scene set up.
	/// </summary>
	public List<string> Hardware { get; set; }

	/// <summary>
	/// The computer's IP address, given to it the first time it started (<see cref="NaviComputer.IpAddress"/>).
	/// </summary>
	public string IpAddress { get; set; } = "";
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

	// A drive is a folder: navi/saves/{slot}/drives/{drive}/index.{n}.json is the tree (names, folders, links, sizes and the
	// revision of each file's blob, no contents), and blobs/{entry}-{revision} are the files' raw bytes. Nothing is
	// overwritten: a changed file gets a new blob revision, and a save writes the next index.{n}.json once the blobs it
	// names are written, then removes the older ones, so a crash leaves the previous index and the blobs it names.

	static string DriveFolderFor( string id ) => $"navi/saves/{Slot}/drives/{id}";
	static string IndexPathFor( string id, long generation ) => $"{DriveFolderFor( id )}/index.{generation}.json";

	/// <summary>
	/// The generations of a drive's saved index, newest first. Each save writes a new file and removes the older ones, so
	/// there is one, or two if a save stopped before it cleaned up (and a half-written newest one is skipped on read).
	/// </summary>
	static List<long> IndexGenerations( BaseFileSystem data, string id )
	{
		if ( !data.DirectoryExists( DriveFolderFor( id ) ) )
			return [];

		return data.FindFile( DriveFolderFor( id ), "index.*.json", false )
			.Select( x => x.Split( '.' ) )
			.Where( x => x.Length == 3 && long.TryParse( x[1], out _ ) )
			.Select( x => long.Parse( x[1] ) )
			.OrderByDescending( x => x )
			.ToList();
	}
	static string BlobPathFor( string id, long entry, long rev ) => $"{DriveFolderFor( id )}/blobs/{entry}-{rev}";

	/// <summary>
	/// A drive's tree in the current slot, or null if it has none. Each drive has its own folder, apart from its
	/// computer's, so a removable drive keeps its files on any computer.
	/// </summary>
	public static NaviDriveSave ReadDrive( string id )
	{
		if ( !IsPlainName( id ) || FileSystem.Data is not { } data )
			return null;

		// The newest index that reads; one that doesn't is a save that was cut off.
		foreach ( var path in IndexGenerations( data, id ).Select( x => IndexPathFor( id, x ) ) )
		{
			try
			{
				if ( data.ReadJson<NaviDriveSave>( path ) is { } save )
					return save;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Navi: couldn't read drive '{id}' from {path}: {e.Message}" );
			}
		}

		return null;
	}

	/// <summary>
	/// A file's contents, or null if there is no such blob.
	/// </summary>
	public static byte[] ReadBlob( string drive, long entry, long rev )
	{
		if ( !IsPlainName( drive ) || FileSystem.Data is not { } data || !data.FileExists( BlobPathFor( drive, entry, rev ) ) )
			return null;

		try
		{
			return data.ReadAllBytes( BlobPathFor( drive, entry, rev ) ).ToArray();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't read file {entry} of drive '{drive}': {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Save a drive: the contents written since the last save as new blobs, then the index, then remove the blobs the
	/// index no longer names. False if it couldn't be written, in which case the drive still counts as unsaved.
	/// </summary>
	public static bool WriteDrive( NaviDrive drive )
	{
		var id = drive.Id;
		if ( !IsPlainName( id ) || FileSystem.Data is not { } data )
			return false;

		try
		{
			data.CreateDirectory( $"{DriveFolderFor( id )}/blobs" );

			var written = drive.PendingContents();
			foreach ( var (entry, rev, bytes) in written )
				data.WriteAllBytes( BlobPathFor( id, entry, rev ), bytes );

			var generations = IndexGenerations( data, id );
			var next = generations.DefaultIfEmpty( 0 ).Max() + 1;
			data.WriteAllText( IndexPathFor( id, next ), Json.Serialize( drive.CaptureSave() ) );

			foreach ( var older in generations )
				data.DeleteFile( IndexPathFor( id, older ) );

			foreach ( var (entry, rev) in drive.Committed( written ) )
				DeleteBlob( data, id, entry, rev );

			return true;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't save drive '{id}': {e.Message}" );
			return false;
		}
	}

	static void DeleteBlob( BaseFileSystem data, string drive, long entry, long rev )
	{
		try
		{
			if ( data.FileExists( BlobPathFor( drive, entry, rev ) ) )
				data.DeleteFile( BlobPathFor( drive, entry, rev ) );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't remove a file of drive '{drive}': {e.Message}" );
		}
	}

	/// <summary>
	/// Remove blobs the drive's index doesn't name: left by a save that stopped before its index was written, or by
	/// entries that didn't load.
	/// </summary>
	public static void CleanBlobs( NaviDrive drive )
	{
		var folder = $"{DriveFolderFor( drive.Id )}/blobs";
		if ( !IsPlainName( drive.Id ) || FileSystem.Data is not { } data || !data.DirectoryExists( folder ) )
			return;

		try
		{
			var named = drive.CommittedBlobs.Select( x => $"{x.Key}-{x.Value}" ).ToHashSet();

			foreach ( var name in data.FindFile( folder, "*", false ).ToList() )
			{
				if ( !named.Contains( name ) )
					data.DeleteFile( $"{folder}/{name}" );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't clean up drive '{drive.Id}': {e.Message}" );
		}
	}

	static string AddressBookPath => $"navi/saves/{Slot}/addresses.json";

	/// <summary>
	/// The IP addresses given out in the current slot, to the save key (<see cref="NaviComputer.SaveKey"/>) of the computer
	/// that has each. It spans scenes, so a computer in one scene never gets the address of one in another.
	/// </summary>
	public static Dictionary<string, string> ReadAddressBook()
	{
		if ( FileSystem.Data is not { } data || !data.FileExists( AddressBookPath ) )
			return new();

		try
		{
			return data.ReadJson<Dictionary<string, string>>( AddressBookPath ) ?? new();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't read the address book: {e.Message}" );
			return new();
		}
	}

	/// <summary>
	/// Record that the computer with <paramref name="saveKey"/> has <paramref name="address"/>, and no other address.
	/// </summary>
	public static void WriteAddress( string saveKey, string address )
	{
		if ( !IsPlainName( saveKey ) || !NaviNetwork.IsAddress( address ) || FileSystem.Data is not { } data )
			return;

		var book = ReadAddressBook();
		if ( book.TryGetValue( address, out var owner ) && owner == saveKey && book.Count( x => x.Value == saveKey ) == 1 )
			return;

		foreach ( var old in book.Where( x => x.Value == saveKey ).Select( x => x.Key ).ToList() )
			book.Remove( old );

		book[address] = saveKey;

		try
		{
			data.CreateDirectory( $"navi/saves/{Slot}" );
			data.WriteJson( AddressBookPath, book );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't write the address book: {e.Message}" );
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
