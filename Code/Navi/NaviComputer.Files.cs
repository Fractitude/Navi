namespace Sandbox;

/// <summary>
/// The computer's files (<see cref="NaviFileSystem"/>): one drive inside, mounted at <c>/</c>, and removable drives at
/// <c>/Volumes</c>. Only what is made on the computer is there, never the player's real files. The host keeps them and
/// saves each drive to its own file; they aren't sent to clients. Apps reach them through <see cref="NaviFileCalls"/>.
/// </summary>
public sealed partial class NaviComputer
{
	/// <summary>
	/// The inside drive's name, shown in the file explorer.
	/// </summary>
	[Property, Group( "Files" )] public string DriveName { get; set; } = "Navi HD";

	/// <summary>
	/// How much the inside drive holds, in megabytes. The host keeps every computer's drive, so keep it small.
	/// </summary>
	[Property, Group( "Files" ), Range( 1, 256 )]
	public int DriveSizeMegabytes { get; set; } = (int)(NaviDrive.DefaultCapacity / (1024 * 1024));

	/// <summary>
	/// The computer's files, on the host once it has started. Null on other clients.
	/// </summary>
	public NaviFileSystem Files { get; private set; }

	long DriveCapacity => System.Math.Max( 1, DriveSizeMegabytes ) * 1024L * 1024L;

	/// <summary>
	/// Each drive's <see cref="NaviDrive.Version"/> when it was last saved.
	/// </summary>
	readonly Dictionary<NaviDrive, int> savedDrives = new();

	int filesChangedVersion;
	RealTimeSince sinceFilesChange;

	/// <summary>
	/// Set up the inside drive: from its save if <paramref name="read"/> and there is one, else empty. Removable drives
	/// that were in are taken out.
	/// </summary>
	void LoadFiles( bool read )
	{
		if ( Files is not null )
		{
			foreach ( var (_, drive) in Files.Drives.Where( x => x.Drive.Removable ).ToList() )
				EjectDrive( drive );
		}

		var save = read ? NaviSaveData.ReadDrive( SaveKey ) : null;
		var root = save is null
			? new NaviDrive( SaveKey, DriveName, DriveCapacity )
			: NaviDrive.FromSave( SaveKey, save, DriveCapacity );

		root.Label = string.IsNullOrWhiteSpace( DriveName ) ? "Navi HD" : DriveName;

		Files = new NaviFileSystem( root );
		savedDrives.Clear();
		savedDrives[root] = save is null ? -1 : root.Version;
		if ( save is not null )
			NaviSaveData.CleanBlobs( root );
		Files.EnsureLayout();
		filesChangedVersion = Files.Version;
	}

	/// <summary>
	/// Write the drives whose files changed since they were last saved.
	/// </summary>
	void SaveFiles()
	{
		if ( !IsAuthority || Files is null )
			return;

		foreach ( var (_, drive) in Files.Drives )
			SaveDrive( drive );
	}

	void SaveDrive( NaviDrive drive )
	{
		if ( savedDrives.TryGetValue( drive, out var saved ) && saved == drive.Version )
			return;

		if ( NaviSaveData.WriteDrive( drive ) )
			savedDrives[drive] = drive.Version;
	}

	/// <summary>
	/// Save the drives a moment after their files last changed. Files don't change <see cref="Version"/>, so writing one
	/// doesn't resend the computer's state to clients.
	/// </summary>
	void UpdateFilesSave()
	{
		if ( !AutoSave || Files is null )
			return;

		var version = Files.Version;
		if ( version != filesChangedVersion )
		{
			filesChangedVersion = version;
			sinceFilesChange = 0;
			return;
		}

		if ( sinceFilesChange > AutoSaveDelay )
			SaveFiles();
	}

	/// <summary>
	/// Put a removable drive in: it shows at <c>/Volumes/{Label}</c>. Returns where, or null on a client.
	/// </summary>
	public string MountDrive( NaviDrive drive )
	{
		if ( !IsAuthority || Files is null || drive is null )
			return null;

		var path = Files.Mount( drive );
		savedDrives.TryAdd( drive, drive.Version );
		return path;
	}

	/// <summary>
	/// Take a removable drive out, saving its files first. False if it wasn't in.
	/// </summary>
	public bool EjectDrive( NaviDrive drive )
	{
		if ( !IsAuthority || Files is null || drive is null || Files.MountPathOf( drive ) is null || !drive.Removable )
			return false;

		SaveDrive( drive );
		savedDrives.Remove( drive );
		return Files.Eject( drive );
	}

	/// <summary>
	/// The folder an app has to itself, which it may use without <see cref="NaviPermissions.Files"/>:
	/// <c>/Library/Apps/{app id}</c>, with the id's '/' written as ':' (app ids have no ':').
	/// </summary>
	public static string AppFolderOf( string appId ) =>
		NaviFileSystem.Join( NaviFileSystem.AppsFolder, appId.Replace( '/', ':' ) );

	/// <summary>
	/// Delete an uninstalled app's own folder.
	/// </summary>
	void ForgetAppFolder( string appId )
	{
		if ( Files?.TryResolve( AppFolderOf( appId ), followLast: false ) is { } folder )
			folder.Drive.Delete( folder.Node );
	}

	// Opening a file with the app for its kind (NaviAppResource.FileTypes).

	/// <summary>
	/// The most files waiting for one app to take them. More are dropped, oldest first.
	/// </summary>
	const int MaxFilesToOpen = 8;

	/// <summary>
	/// By app id, the files the player opened with that app that it hasn't taken yet (<c>files.opened</c>). On the host.
	/// </summary>
	readonly Dictionary<string, List<string>> filesToOpen = new();

	/// <summary>
	/// The installed app that opens files with this extension, or null. One that ships with the game comes first; a Store
	/// app only counts if it declares <see cref="NaviPermissions.Files"/>, or the file would be out of its reach.
	/// </summary>
	public NaviAppInfo FindFileHandler( string extension ) => FindFileHandlers( extension ).FirstOrDefault();

	/// <summary>
	/// Every installed app that opens files with this extension, the one <see cref="FindFileHandler"/> picks first.
	/// </summary>
	public IReadOnlyList<NaviAppInfo> FindFileHandlers( string extension )
	{
		if ( string.IsNullOrEmpty( extension ) )
			return [];

		extension = extension.ToLowerInvariant();
		return NaviAppRegistry.All
			.Where( x => x.FileTypes.Contains( extension ) && IsInstalled( x ) )
			.Where( x => x.IsPrepackaged || x.IsSystem || x.Permissions.Contains( NaviPermissions.Files ) )
			.OrderBy( x => x.IsCore ? 0 : x.IsPrepackaged ? 1 : 2 )
			.ToList();
	}

	/// <summary>
	/// Launch <paramref name="app"/> and leave <paramref name="path"/> for it to take. The caller checks that whoever asked
	/// may reach the path; the app reads it with its own permissions, so this gives it nothing it didn't have.
	/// </summary>
	internal bool OpenFileWith( NaviAppInfo app, string path )
	{
		if ( !IsAuthority || app is null )
			return false;

		if ( !filesToOpen.TryGetValue( app.Id, out var waiting ) )
			filesToOpen[app.Id] = waiting = new();

		waiting.Remove( path );
		waiting.Add( path );
		if ( waiting.Count > MaxFilesToOpen )
			waiting.RemoveRange( 0, waiting.Count - MaxFilesToOpen );

		if ( Launch( app.Id ) )
			return true;

		filesToOpen.Remove( app.Id );
		return false;
	}

	/// <summary>
	/// The files waiting for an app, which it now has: each is given once.
	/// </summary>
	internal string[] TakeFilesToOpen( string appId )
	{
		if ( !filesToOpen.Remove( appId, out var waiting ) )
			return [];

		return waiting.ToArray();
	}

	// Console commands, to look at and change a computer's files while there is no app for it. Host only. They work on
	// the computer named by its save key, else the one the local player sits at, else the first one in the scene.

	static NaviComputer CommandComputer( string key )
	{
		if ( Networking.IsActive && !Networking.IsHost )
		{
			Log.Warning( "Navi: only the host has the computers' files" );
			return null;
		}

		var all = Game.ActiveScene?.GetAllComponents<NaviComputer>().ToList() ?? [];
		var computer = string.IsNullOrEmpty( key )
			? all.FirstOrDefault( x => x.IsSeated ) ?? all.FirstOrDefault()
			: all.FirstOrDefault( x => x.SaveKey == key );

		if ( computer?.Files is null )
			Log.Warning( string.IsNullOrEmpty( key ) ? "Navi: no computer has started" : $"Navi: no computer '{key}'" );

		return computer?.Files is null ? null : computer;
	}

	static void RunCommand( string key, System.Action<NaviFileSystem> run )
	{
		if ( CommandComputer( key ) is not { } computer )
			return;

		try
		{
			run( computer.Files );
		}
		catch ( NaviFileException e )
		{
			Log.Warning( e.Message );
		}
	}

	static string Describe( NaviFileLocation at ) => at.Node.Kind switch
	{
		NaviFileKind.Folder => $"{NaviFileSystem.DisplayName( at )}/",
		NaviFileKind.File => $"{NaviFileSystem.DisplayName( at )}  ({NaviDrive.Size( at.Node )} B)",
		NaviFileKind.Symlink => $"{NaviFileSystem.DisplayName( at )} -> {at.Node.Target}",
		_ => $"{NaviFileSystem.DisplayName( at )} => alias of {at.Node.Target}",
	};

	[ConCmd( "navi_fs_ls" )]
	public static void ListCommand( string path = "/", string computer = "" ) => RunCommand( computer, files =>
	{
		var entries = files.List( path ).ToList();
		Log.Info( $"{files.Resolve( path ).Path} ({entries.Count})" );
		foreach ( var entry in entries )
			Log.Info( "  " + Describe( entry ) );
	} );

	[ConCmd( "navi_fs_cat" )]
	public static void CatCommand( string path, string computer = "" ) =>
		RunCommand( computer, files => Log.Info( files.ReadText( path ) ) );

	[ConCmd( "navi_fs_write" )]
	public static void WriteCommand( string path, string text, string computer = "" ) =>
		RunCommand( computer, files => Log.Info( $"Wrote {files.WriteText( path, text ).Path}" ) );

	[ConCmd( "navi_fs_mkdir" )]
	public static void MakeFolderCommand( string path, string computer = "" ) =>
		RunCommand( computer, files => Log.Info( $"Made {files.CreateFolders( path ).Path}" ) );

	[ConCmd( "navi_fs_ln" )]
	public static void SymlinkCommand( string target, string path, string computer = "" ) =>
		RunCommand( computer, files => Log.Info( $"Linked {files.CreateSymlink( path, target ).Path} -> {target}" ) );

	[ConCmd( "navi_fs_alias" )]
	public static void AliasCommand( string target, string path, string computer = "" ) =>
		RunCommand( computer, files =>
		{
			files.CreateAlias( path, target );
			Log.Info( $"Alias {path} => {files.ResolveAlias( path ) ?? "(broken)"}" );
		} );

	[ConCmd( "navi_fs_mv" )]
	public static void MoveCommand( string from, string to, string computer = "" ) =>
		RunCommand( computer, files => Log.Info( $"Moved to {files.Move( from, to ).Path}" ) );

	[ConCmd( "navi_fs_rm" )]
	public static void DeleteCommand( string path, string computer = "" ) =>
		RunCommand( computer, files =>
		{
			files.Delete( path );
			Log.Info( $"Deleted {path}" );
		} );

	[ConCmd( "navi_fs_drives" )]
	public static void DrivesCommand( string computer = "" ) => RunCommand( computer, files =>
	{
		foreach ( var (path, drive) in files.Drives )
			Log.Info( $"{drive.Label} at {path}: {drive.Used} of {drive.Capacity} bytes used{(drive.Removable ? ", removable" : "")} (id {drive.Id})" );
	} );

	/// <summary>
	/// Put in a new, empty removable drive, to try out <c>/Volumes</c> until there are real ones.
	/// </summary>
	[ConCmd( "navi_fs_usb" )]
	public static void UsbCommand( string label = "USB", string computer = "" )
	{
		if ( CommandComputer( computer ) is not { } target )
			return;

		var drive = new NaviDrive( System.Guid.NewGuid().ToString( "N" ), label, 8L * 1024 * 1024, removable: true );
		Log.Info( $"Mounted {label} at {target.MountDrive( drive )}" );
	}

	[ConCmd( "navi_fs_eject" )]
	public static void EjectCommand( string path, string computer = "" ) => RunCommand( computer, files =>
	{
		var at = files.Resolve( path );
		var target = CommandComputer( computer );
		Log.Info( at.Node.Id == NaviDrive.RootId && target.EjectDrive( at.Drive ) ? $"Ejected {at.Drive.Label}" : $"{path} isn't a removable drive" );
	} );
}
