namespace Sandbox;

/// <summary>
/// The <c>files.*</c> calls apps make (<c>Navi.Files</c>), for <see cref="NaviSystemCalls"/>. They run as the calling app:
/// one with <see cref="NaviPermissions.Files"/> (or root) reaches every file; any other only its own folder
/// (<see cref="NaviComputer.AppFolderOf"/>), and gets <see cref="NaviPermissions.Missing"/> for anything outside it.
/// </summary>
/// <remarks>
/// A path is checked twice: as the app wrote it, before anything is looked up, so an app can't learn what exists
/// outside its folder; and where it leads once symlinks are followed, so a link in the app's folder can't reach out of it.
/// Every call runs on the host, all at once, so nothing changes between the check and the use.
/// </remarks>
public static class NaviFileCalls
{
	public static object Run( NaviComputer computer, NaviProcess process, string method, object[] args )
	{
		// Only the host has the files. A client's own copy of an app that isn't shared gets nothing.
		var files = computer.Files ?? throw new NaviFileException( NaviFileError.Unavailable, "" );
		var self = process.App.Id;
		var full = process.HasPermission( NaviPermissions.Files );

		if ( !full )
			EnsureAppFolder( files, self );

		switch ( method )
		{
			case "files.version":
				return files.Version;

			case "files.app-folder":
				return NaviComputer.AppFolderOf( self );

			case "files.drives":
				return files.Drives.Select( x => (x.Drive.Id, x.Drive.Label, x.Path, x.Drive.Capacity, x.Drive.Used, x.Drive.Removable) ).ToArray();

			case "files.list":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: true );
				return files.List( path ).Select( x => Describe( files, x ) ).ToArray();
			}

			case "files.info":
			{
				var path = Arg( args, 0, method );
				var follow = Opt( args, 1, true );
				Check( files, process, path, follow, read: true );
				return files.TryResolve( path, follow ) is { } at ? Describe( files, at ) : null;
			}

			case "files.read":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: true, read: true );
				return files.ReadText( path );
			}

			case "files.read-bytes":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: true, read: true );
				return files.ReadBytes( path );
			}

			case "files.thumbnail":
			{
				// A picture made small here, so a client is sent the thumbnail and never the whole picture. Null for a file
				// that isn't a picture, or is too big to decode.
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: true, read: true );
				return Thumbnail( files, path );
			}

			case "files.write-bytes":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: true );
				return files.WriteBytes( path, Opt( args, 1, System.Array.Empty<byte>() ) ).Path;
			}

			case "files.write":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: true );
				return files.WriteText( path, Opt( args, 1, "" ) ).Path;
			}

			case "files.create-folder":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: false );
				return files.CreateFolder( path ).Path;
			}

			case "files.symlink":
			{
				// The target is only text until the link is used, and then it is checked like any path.
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: false );
				return files.CreateSymlink( path, Arg( args, 1, method ) ).Path;
			}

			case "files.alias":
			{
				var (path, target) = (Arg( args, 0, method ), Arg( args, 1, method ));
				Check( files, process, path, followLast: false );
				Check( files, process, target, followLast: false );
				return files.CreateAlias( path, target ).Path;
			}

			case "files.resolve-alias":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: false );
				var target = files.ResolveAlias( path );
				if ( target is not null && !full && !Allowed( process, target ) )
					throw NaviPermissions.Missing( NaviPermissions.Files );

				return target;
			}

			case "files.move":
			{
				var (from, to) = (Arg( args, 0, method ), Arg( args, 1, method ));
				Check( files, process, from, followLast: false );
				Check( files, process, to, followLast: false );
				return files.Move( from, to ).Path;
			}

			case "files.copy":
			{
				var (from, to) = (Arg( args, 0, method ), Arg( args, 1, method ));
				Check( files, process, from, followLast: false );
				Check( files, process, to, followLast: false );
				return files.Copy( from, to ).Path;
			}

			case "files.delete":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: false );
				if ( !full && NaviFileSystem.Normalize( path ) == NaviComputer.AppFolderOf( self ) )
					throw new NaviFileException( NaviFileError.Protected, path );

				files.Delete( path );
				return null;
			}

			case "files.trash":
			{
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: false );
				if ( !full && NaviFileSystem.Normalize( path ) == NaviComputer.AppFolderOf( self ) )
					throw new NaviFileException( NaviFileError.Protected, path );

				return files.Trash( path ).Path;
			}

			case "files.restore":
				// The trash and where things go back to are outside the app's folder.
				if ( !full )
					throw NaviPermissions.Missing( NaviPermissions.Files );

				return files.Restore( Arg( args, 0, method ) ).Path;

			case "files.eject":
			{
				// Taking a drive out takes it from every app, so it needs the whole file system.
				if ( !full )
					throw NaviPermissions.Missing( NaviPermissions.Files );

				var at = files.Resolve( Arg( args, 0, method ) );
				return at.Node.Id == NaviDrive.RootId && computer.EjectDrive( at.Drive );
			}

			case "files.open":
			{
				// The app that opens it is the computer's choice, never the caller's, and the caller must reach the file
				// itself: as written and where it leads. So no app can use another to read what it couldn't.
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: true );
				var at = files.Resolve( path );

				// A folder opens in the Files app, which takes it as it takes a file waiting for it.
				if ( at.Node.Kind == NaviFileKind.Folder )
					return NaviAppRegistry.Find( "navi/apps/files.napp" ) is { } explorer && computer.IsInstalled( explorer )
						&& computer.OpenFileWith( explorer, at.Path );

				if ( at.Node.Kind != NaviFileKind.File )
					throw new NaviFileException( NaviFileError.NotFound, at.Path );

				// The player may choose among the apps that open this kind (files.openers); only one of those counts.
				var handlers = HandlersOf( computer, at.Path );
				var wanted = Opt( args, 1, "" );
				var handler = wanted == "" ? handlers.FirstOrDefault() : handlers.FirstOrDefault( x => x.Id == wanted );
				return handler is not null && computer.OpenFileWith( handler, at.Path );
			}

			case "files.openers":
			{
				// The apps files.open can open this file with, best first; none for a folder or a kind nothing opens.
				var path = Arg( args, 0, method );
				Check( files, process, path, followLast: true );
				var at = files.Resolve( path );
				return at.Node.Kind == NaviFileKind.File
					? HandlersOf( computer, at.Path ).Select( x => (x.Id, x.Name, x.Icon ?? "") ).ToArray()
					: System.Array.Empty<(string, string, string)>();
			}

			case "files.opened":
				// Only the app's own: what the player opened with it.
				return computer.TakeFilesToOpen( self );

			case "files.trash-path":
			{
				var path = Opt( args, 0, "/" );
				Check( files, process, path, followLast: true );
				return files.TrashPathOf( files.Resolve( path ).Drive );
			}
		}

		throw new System.NotSupportedException( $"Navi has no call '{method}'." );
	}

	/// <summary>The longest side of a thumbnail, in pixels.</summary>
	public const int ThumbnailSize = 128;

	/// <summary>The biggest picture made into a thumbnail, in bytes. Bigger ones get none.</summary>
	public const int MaxThumbnailSource = 8 * 1024 * 1024;

	/// <summary>
	/// The picture at <paramref name="path"/>, no bigger than <see cref="ThumbnailSize"/> on its longest side, as PNG.
	/// </summary>
	static byte[] Thumbnail( NaviFileSystem files, string path )
	{
		var at = files.Resolve( path );
		if ( at.Node.Kind != NaviFileKind.File )
			throw new NaviFileException( at.Node.Kind == NaviFileKind.Folder ? NaviFileError.IsAFolder : NaviFileError.NotFound, at.Path );

		if ( NaviDrive.Size( at.Node ) is <= 0 or > MaxThumbnailSource )
			return null;

		using var bitmap = Bitmap.CreateFromBytes( files.ReadBytes( at.Path ) );
		if ( bitmap is not { Width: > 0, Height: > 0 } )
			return null;

		var scale = System.Math.Min( 1f, ThumbnailSize / (float)System.Math.Max( bitmap.Width, bitmap.Height ) );
		using var small = bitmap.Resize( System.Math.Max( 1, (int)(bitmap.Width * scale) ),
			System.Math.Max( 1, (int)(bitmap.Height * scale) ), true );
		return small.ToPng();
	}

	/// <summary>
	/// The app's own folder, made if it is missing. A drive too full for it leaves it missing.
	/// </summary>
	static void EnsureAppFolder( NaviFileSystem files, string appId )
	{
		try
		{
			files.CreateFolders( NaviComputer.AppFolderOf( appId ) );
		}
		catch ( NaviFileException )
		{
		}
	}

	/// <summary>
	/// The app's own folder, and for reading only, the files the player chose for it in the file picker.
	/// </summary>
	static bool Allowed( NaviProcess process, string path, bool read = false ) =>
		NaviFileSystem.IsWithin( path, NaviComputer.AppFolderOf( process.App.Id ) ) || (read && process.PickedFiles.Contains( path ));

	/// <summary>
	/// Throw <see cref="NaviPermissions.Missing"/> unless the app may use <paramref name="path"/>: as written, and where it
	/// leads. For a path that doesn't exist yet, where its folder leads.
	/// </summary>
	static void Check( NaviFileSystem files, NaviProcess process, string path, bool followLast, bool read = false )
	{
		if ( process.HasPermission( NaviPermissions.Files ) )
			return;

		var normalized = NaviFileSystem.Normalize( path );
		if ( !Allowed( process, normalized, read ) )
			throw NaviPermissions.Missing( NaviPermissions.Files );

		string real;
		if ( files.TryResolve( normalized, followLast ) is { } at )
			real = at.Path;
		else if ( files.TryResolve( NaviFileSystem.FolderOf( normalized ) ) is { } folder )
			real = NaviFileSystem.Join( folder.Path, NaviFileSystem.NameOf( normalized ) );
		else
			return; // Nothing there to reach: the call fails on its own.

		if ( !Allowed( process, real, read ) )
			throw NaviPermissions.Missing( NaviPermissions.Files );
	}

	/// <summary>
	/// An entry as the client reads it: name, path, kind, size in bytes, created, modified, and then the link's target (a
	/// symlink's path, an alias's path when made), where a trashed entry came from, how many entries a folder holds, and
	/// whether it is a drive.
	/// </summary>
	static (string, string, int, long, long, long, (string, string, int, bool)) Describe( NaviFileSystem files, NaviFileLocation at ) =>
	(
		NaviFileSystem.DisplayName( at ),
		at.Path,
		(int)at.Node.Kind,
		at.Node.Kind == NaviFileKind.File ? NaviDrive.Size( at.Node ) : 0,
		at.Node.Created,
		at.Node.Modified,
		(at.Node.Target ?? "", at.Node.TrashedFrom ?? "", at.Drive.ChildCount( at.Node ), at.Node.Id == NaviDrive.RootId)
	);

	static IReadOnlyList<NaviAppInfo> HandlersOf( NaviComputer computer, string path )
	{
		var name = NaviFileSystem.NameOf( path );
		var dot = name.LastIndexOf( '.' );
		return dot > 0 ? computer.FindFileHandlers( name[(dot + 1)..] ) : [];
	}

	static T Opt<T>( object[] args, int index, T fallback ) =>
		index < args.Length && args[index] is T value ? value : fallback;

	static string Arg( object[] args, int index, string method ) =>
		index < args.Length && args[index] is string value
			? value
			: throw new System.ArgumentException( $"'{method}' needs a String as argument {index + 1}." );
}
