namespace Sandbox;

/// <summary>
/// Why a file system call failed. The <c>Navi</c> client reads it from the error's message (<see cref="NaviFileException"/>),
/// so keep the names stable.
/// </summary>
public enum NaviFileError
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
	/// The entry is part of how the computer is laid out (a drive's root, <c>/Volumes</c>, <c>/Library</c>), or a move
	/// would put a folder inside itself.
	/// </summary>
	Protected,

	/// <summary>
	/// The files are on the host, and this copy of the app runs on a client.
	/// </summary>
	Unavailable,
}

/// <summary>
/// A file system call failed. The message starts with <see cref="Prefix"/> and the error's name in brackets, e.g.
/// <c>File error [NotFound]: /Documents/notes.txt</c>, so the <c>Navi</c> client, which has no Navi types, can read it.
/// </summary>
public sealed class NaviFileException : System.InvalidOperationException
{
	public const string Prefix = "File error ";

	public NaviFileException( NaviFileError error, string path ) : base( $"{Prefix}[{error}]: {path}" )
	{
		Error = error;
		Path = path;
	}

	public NaviFileError Error { get; }
	public string Path { get; }
}

/// <summary>
/// Where a path leads: the drive, the entry on it, and the path with every symlink followed.
/// </summary>
public readonly record struct NaviFileLocation( NaviDrive Drive, NaviFileNode Node, string Path );

/// <summary>
/// The paths of one computer, the way macOS and Linux have them: one tree from <c>/</c>, the inside drive. Removable
/// drives appear in it at <c>/Volumes/{Label}</c>. Names are case-sensitive. Host only. It does no permission checks:
/// <see cref="NaviComputer"/> does those for apps.
/// </summary>
public sealed class NaviFileSystem
{
	public const string VolumesFolder = "/Volumes", LibraryFolder = "/Library", AppsFolder = "/Library/Apps";
	public const string TrashName = ".Trash";

	/// <summary>
	/// The folders a new inside drive starts with.
	/// </summary>
	public static IReadOnlyList<string> StartFolders => ["/Documents", "/Downloads", "/Pictures", VolumesFolder, AppsFolder];

	/// <summary>
	/// How many symlinks one path may go through before it is taken for a loop.
	/// </summary>
	public const int MaxLinks = 16;

	public NaviDrive Root { get; }

	readonly Dictionary<string, NaviDrive> mounts = new( System.StringComparer.Ordinal );

	/// <summary>
	/// Goes up when a drive is mounted or ejected. Each drive has its own version for its files.
	/// </summary>
	public int MountVersion { get; private set; }

	public NaviFileSystem( NaviDrive root )
	{
		Root = root;
		mounts["/"] = root;
	}

	/// <summary>
	/// Every mounted drive with where it is, the inside drive first.
	/// </summary>
	public IEnumerable<(string Path, NaviDrive Drive)> Drives =>
		mounts.OrderBy( x => x.Key == "/" ? 0 : 1 ).ThenBy( x => x.Key, System.StringComparer.Ordinal ).Select( x => (x.Key, x.Value) );

	/// <summary>
	/// The version of every drive and the mounts together, for knowing when anything changed.
	/// </summary>
	public int Version => mounts.Values.Aggregate( MountVersion, ( sum, x ) => sum + x.Version );

	public NaviDrive FindDrive( string id ) => mounts.Values.FirstOrDefault( x => x.Id == id );

	public string MountPathOf( NaviDrive drive ) => mounts.FirstOrDefault( x => x.Value == drive ).Key;

	/// <summary>
	/// Make the folders a computer's drive should have, where they are missing.
	/// </summary>
	public void EnsureLayout()
	{
		foreach ( var folder in StartFolders )
			CreateFolders( folder );
	}

	/// <summary>
	/// Put a removable drive at <c>/Volumes/{Label}</c>, or <c>{Label} 2</c> and so on if that is taken. Returns where.
	/// </summary>
	public string Mount( NaviDrive drive )
	{
		if ( MountPathOf( drive ) is { } already )
			return already;

		if ( FindDrive( drive.Id ) is not null )
			throw new NaviFileException( NaviFileError.AlreadyExists, drive.Id );

		var label = IsValidName( drive.Label ) ? drive.Label : "Untitled";
		var path = $"{VolumesFolder}/{label}";
		for ( var i = 2; mounts.ContainsKey( path ) || TryResolve( path, false ) is not null; i++ )
			path = $"{VolumesFolder}/{label} {i}";

		mounts[path] = drive;
		MountVersion++;
		return path;
	}

	/// <summary>
	/// Take a removable drive out. False if it isn't mounted, or is the inside drive.
	/// </summary>
	public bool Eject( NaviDrive drive )
	{
		if ( drive == Root || MountPathOf( drive ) is not { } path )
			return false;

		mounts.Remove( path );
		MountVersion++;
		return true;
	}

	// Paths.

	public static bool IsValidName( string name ) =>
		!string.IsNullOrEmpty( name ) && name.Length <= NaviDrive.MaxName && name is not "." and not ".."
		&& !name.Any( x => x is '/' or '\0' || char.IsControl( x ) );

	/// <summary>
	/// An absolute path with '.' and '..' worked out and no empty parts, e.g. <c>/Documents/notes.txt</c>. Throws
	/// <see cref="NaviFileError.InvalidName"/> for a relative path or a bad name.
	/// </summary>
	public static string Normalize( string path )
	{
		if ( string.IsNullOrEmpty( path ) || path[0] != '/' || path.Length > NaviDrive.MaxPath )
			throw new NaviFileException( NaviFileError.InvalidName, path ?? "" );

		var parts = new List<string>();
		foreach ( var part in path.Split( '/' ) )
		{
			if ( part is "" or "." )
				continue;

			if ( part == ".." )
			{
				if ( parts.Count > 0 )
					parts.RemoveAt( parts.Count - 1 );

				continue;
			}

			if ( !IsValidName( part ) )
				throw new NaviFileException( NaviFileError.InvalidName, path );

			parts.Add( part );
		}

		return "/" + string.Join( "/", parts );
	}

	public static string Join( string folder, string name ) => folder == "/" ? "/" + name : folder + "/" + name;

	public static string NameOf( string path ) => path == "/" ? "" : path[(path.LastIndexOf( '/' ) + 1)..];

	public static string FolderOf( string path )
	{
		var slash = path.LastIndexOf( '/' );
		return slash <= 0 ? "/" : path[..slash];
	}

	/// <summary>
	/// Is <paramref name="path"/> <paramref name="folder"/> or inside it? Both normalized.
	/// </summary>
	public static bool IsWithin( string path, string folder ) =>
		path == folder || folder == "/" || path.StartsWith( folder + "/", System.StringComparison.Ordinal );

	/// <summary>
	/// Find what a path leads to, following symlinks on the way. With <paramref name="followLast"/>, a symlink at the end
	/// is followed too; without it, the result is the link itself.
	/// </summary>
	public NaviFileLocation Resolve( string path, bool followLast = true ) => Resolve( Normalize( path ), followLast, 0 );

	public NaviFileLocation? TryResolve( string path, bool followLast = true )
	{
		try
		{
			return Resolve( path, followLast );
		}
		catch ( NaviFileException )
		{
			return null;
		}
	}

	NaviFileLocation Resolve( string path, bool followLast, int links )
	{
		var parts = path.Split( '/', System.StringSplitOptions.RemoveEmptyEntries );
		var here = new NaviFileLocation( Root, Root.Root, "/" );

		for ( var i = 0; i < parts.Length; i++ )
		{
			if ( here.Node.Kind != NaviFileKind.Folder )
				throw new NaviFileException( NaviFileError.NotAFolder, here.Path );

			var next = Join( here.Path, parts[i] );
			if ( mounts.TryGetValue( next, out var mounted ) )
			{
				here = new NaviFileLocation( mounted, mounted.Root, next );
				continue;
			}

			var child = here.Drive.Child( here.Node, parts[i] )
				?? throw new NaviFileException( NaviFileError.NotFound, next );

			var last = i == parts.Length - 1;
			if ( child.Kind == NaviFileKind.Symlink && (!last || followLast) )
			{
				if ( links >= MaxLinks )
					throw new NaviFileException( NaviFileError.LinkLoop, path );

				var target = child.Target ?? "";
				var rest = string.Join( "/", parts.Skip( i + 1 ) );
				var full = (target.StartsWith( '/' ) ? target : Join( here.Path, target )) + (rest == "" ? "" : "/" + rest);
				return Resolve( Normalize( full ), followLast, links + 1 );
			}

			here = new NaviFileLocation( here.Drive, child, next );
		}

		return here;
	}

	/// <summary>
	/// The folder a new entry at <paramref name="path"/> goes in, and its name.
	/// </summary>
	public (NaviFileLocation Folder, string Name) ResolveNew( string path )
	{
		path = Normalize( path );
		if ( path == "/" )
			throw new NaviFileException( NaviFileError.AlreadyExists, path );

		var folder = Resolve( FolderOf( path ) );
		if ( folder.Node.Kind != NaviFileKind.Folder )
			throw new NaviFileException( NaviFileError.NotAFolder, folder.Path );

		return (folder, NameOf( path ));
	}

	/// <summary>
	/// Where an entry is now, from its drive, or null if that drive isn't mounted.
	/// </summary>
	public string PathOf( NaviDrive drive, NaviFileNode node ) =>
		MountPathOf( drive ) is { } mount ? (mount == "/" ? drive.PathOf( node ) : mount + (node.Id == NaviDrive.RootId ? "" : drive.PathOf( node ))) : null;

	/// <summary>
	/// Is the entry part of how the computer is laid out: a drive's root, a mount point, the trash folder, or one of the
	/// inside drive's <c>/Volumes</c>, <c>/Library</c> and <c>/Library/Apps</c>? It can't be moved, renamed or deleted.
	/// </summary>
	public bool IsProtected( NaviFileLocation at ) =>
		at.Node.Id == NaviDrive.RootId || at.Path is VolumesFolder or LibraryFolder or AppsFolder
		|| (at.Node.Parent == NaviDrive.RootId && at.Node.Name == TrashName);

	void EnsureNotProtected( NaviFileLocation at )
	{
		if ( IsProtected( at ) )
			throw new NaviFileException( NaviFileError.Protected, at.Path );
	}

	// Reading.

	public IEnumerable<NaviFileLocation> List( string path )
	{
		var folder = Resolve( path );
		if ( folder.Node.Kind != NaviFileKind.Folder )
			throw new NaviFileException( NaviFileError.NotAFolder, folder.Path );

		var entries = folder.Drive.Children( folder.Node ).Select( x => new NaviFileLocation( folder.Drive, x, Join( folder.Path, x.Name ) ) );

		// Drives mounted in this folder show as folders in it, over any entry of the same name.
		var mounted = mounts.Where( x => x.Key != "/" && FolderOf( x.Key ) == folder.Path )
			.Select( x => new NaviFileLocation( x.Value, x.Value.Root, x.Key ) )
			.ToList();

		return entries.Where( x => mounted.All( m => m.Path != x.Path ) ).Concat( mounted )
			.OrderBy( x => x.Path, System.StringComparer.Ordinal );
	}

	/// <summary>
	/// The name an entry shows with: its own, or for a drive's root the name it is mounted at.
	/// </summary>
	public static string DisplayName( NaviFileLocation at ) => at.Path == "/" ? at.Drive.Label : NameOf( at.Path );

	public string ReadText( string path )
	{
		var file = Resolve( path );
		if ( file.Node.Kind == NaviFileKind.Folder )
			throw new NaviFileException( NaviFileError.IsAFolder, file.Path );

		if ( file.Node.Kind != NaviFileKind.File )
			throw new NaviFileException( NaviFileError.NotFound, file.Path );

		return file.Node.Binary ? "" : System.Text.Encoding.UTF8.GetString( file.Drive.ReadContent( file.Node ) );
	}

	/// <summary>
	/// A file's contents as bytes: a binary file's data, or a text file's text as UTF-8.
	/// </summary>
	public byte[] ReadBytes( string path )
	{
		var file = Resolve( path );
		if ( file.Node.Kind == NaviFileKind.Folder )
			throw new NaviFileException( NaviFileError.IsAFolder, file.Path );

		if ( file.Node.Kind != NaviFileKind.File )
			throw new NaviFileException( NaviFileError.NotFound, file.Path );

		return file.Drive.ReadContent( file.Node );
	}

	/// <summary>
	/// Where an alias points now: its target entry's path, or the path it was made with if that entry's drive isn't
	/// mounted or it is gone. Null when neither leads anywhere.
	/// </summary>
	public string ResolveAlias( string path )
	{
		var alias = Resolve( path, followLast: false );
		if ( alias.Node.Kind != NaviFileKind.Alias )
			throw new NaviFileException( NaviFileError.NotFound, alias.Path );

		if ( FindDrive( alias.Node.TargetDrive ) is { } drive && drive.Get( alias.Node.TargetNode ) is { } node
			&& PathOf( drive, node ) is { } found )
			return found;

		return alias.Node.Target is { } fallback && TryResolve( fallback ) is { } there ? there.Path : null;
	}

	// Writing.

	/// <summary>
	/// Write a text file, making it or replacing what is in it. A symlink at the path writes its target.
	/// </summary>
	public NaviFileLocation WriteText( string path, string text ) =>
		Write( path, System.Text.Encoding.UTF8.GetBytes( text ?? "" ), binary: false );

	/// <summary>
	/// Write a binary file, making it or replacing what is in it. A symlink at the path writes its target.
	/// </summary>
	public NaviFileLocation WriteBytes( string path, byte[] data ) => Write( path, data ?? [], binary: true );

	NaviFileLocation Write( string path, byte[] data, bool binary )
	{
		var size = data.LongLength;
		var (folder, name) = ResolveNew( path );

		if ( folder.Drive.Child( folder.Node, name ) is { } existing )
		{
			var file = existing.Kind == NaviFileKind.Symlink ? Resolve( Join( folder.Path, name ) ) : new NaviFileLocation( folder.Drive, existing, Join( folder.Path, name ) );
			if ( file.Node.Kind == NaviFileKind.Folder )
				throw new NaviFileException( NaviFileError.IsAFolder, file.Path );

			if ( file.Node.Kind != NaviFileKind.File )
				throw new NaviFileException( NaviFileError.AlreadyExists, file.Path );

			file.Drive.EnsureRoom( size - NaviDrive.Size( file.Node ), 0, file.Path );
			file.Drive.SetContent( file.Node, data, binary );
			return file;
		}

		var created = Create( folder, name, NaviFileKind.File, size );
		created.Drive.SetContent( created.Node, data, binary );
		return created;
	}

	public NaviFileLocation CreateFolder( string path )
	{
		var (folder, name) = ResolveNew( path );
		return Create( folder, name, NaviFileKind.Folder, 0 );
	}

	/// <summary>
	/// Make a folder and the folders above it that are missing. Nothing happens to the ones that exist.
	/// </summary>
	public NaviFileLocation CreateFolders( string path )
	{
		path = Normalize( path );
		if ( TryResolve( path ) is { } existing )
			return existing.Node.Kind == NaviFileKind.Folder ? existing : throw new NaviFileException( NaviFileError.NotAFolder, path );

		CreateFolders( FolderOf( path ) );
		return CreateFolder( path );
	}

	/// <summary>
	/// Make a symlink to <paramref name="target"/>, absolute or relative to the link's folder. The target needn't exist.
	/// </summary>
	public NaviFileLocation CreateSymlink( string path, string target )
	{
		if ( string.IsNullOrEmpty( target ) || target.Length > NaviDrive.MaxPath || target.Contains( '\0' ) )
			throw new NaviFileException( NaviFileError.InvalidName, target ?? "" );

		var (folder, name) = ResolveNew( path );
		return Create( folder, name, NaviFileKind.Symlink, NaviDrive.Bytes( target ), x => x.Target = target );
	}

	/// <summary>
	/// Make an alias to the entry at <paramref name="target"/>, which must exist.
	/// </summary>
	public NaviFileLocation CreateAlias( string path, string target )
	{
		var to = Resolve( target, followLast: false );
		var (folder, name) = ResolveNew( path );
		return Create( folder, name, NaviFileKind.Alias, NaviDrive.Bytes( to.Path ) + NaviDrive.Bytes( to.Drive.Id ), x =>
		{
			x.Target = to.Path;
			x.TargetDrive = to.Drive.Id;
			x.TargetNode = to.Node.Id;
		} );
	}

	/// <summary>
	/// Make a new entry, with <paramref name="bytes"/> of contents that <paramref name="fill"/> sets.
	/// </summary>
	NaviFileLocation Create( NaviFileLocation folder, string name, NaviFileKind kind, long bytes,
		System.Action<NaviFileNode> fill = null )
	{
		var path = Join( folder.Path, name );
		if ( folder.Drive.Child( folder.Node, name ) is not null || mounts.ContainsKey( path ) )
			throw new NaviFileException( NaviFileError.AlreadyExists, path );

		folder.Drive.EnsureRoom( NaviDrive.NodeOverhead + NaviDrive.Bytes( name ) + bytes, 1, path );

		var node = folder.Drive.Create( folder.Node, name, kind );
		if ( fill is not null )
			folder.Drive.Edit( node, fill );

		return new NaviFileLocation( folder.Drive, node, path );
	}

	/// <summary>
	/// Move or rename an entry to <paramref name="to"/>, which must not exist. A symlink moves as a link. Between drives
	/// it is copied and then deleted, so it needs room on the other drive.
	/// </summary>
	public NaviFileLocation Move( string from, string to )
	{
		var source = Resolve( from, followLast: false );
		EnsureNotProtected( source );

		var (folder, name) = ResolveNew( to );
		var path = Join( folder.Path, name );
		if ( folder.Drive.Child( folder.Node, name ) is not null || mounts.ContainsKey( path ) )
			throw new NaviFileException( NaviFileError.AlreadyExists, path );

		if ( folder.Drive != source.Drive )
		{
			var copy = CopyInto( source, folder, name );
			source.Drive.Delete( source.Node );
			return copy;
		}

		if ( source.Drive.IsWithin( folder.Node, source.Node ) )
			throw new NaviFileException( NaviFileError.Protected, path );

		folder.Drive.EnsureRoom( NaviDrive.Bytes( name ) - NaviDrive.Bytes( source.Node.Name ), 0, path );
		source.Drive.Move( source.Node, folder.Node, name );
		return new NaviFileLocation( folder.Drive, source.Node, path );
	}

	/// <summary>
	/// Copy an entry, and everything in it, to <paramref name="to"/>, which must not exist. Links are copied as links.
	/// </summary>
	public NaviFileLocation Copy( string from, string to )
	{
		var source = Resolve( from, followLast: false );
		var (folder, name) = ResolveNew( to );

		if ( source.Drive == folder.Drive && source.Drive.IsWithin( folder.Node, source.Node ) )
			throw new NaviFileException( NaviFileError.Protected, Join( folder.Path, name ) );

		return CopyInto( source, folder, name );
	}

	NaviFileLocation CopyInto( NaviFileLocation source, NaviFileLocation folder, string name )
	{
		var path = Join( folder.Path, name );
		if ( folder.Drive.Child( folder.Node, name ) is not null || mounts.ContainsKey( path ) )
			throw new NaviFileException( NaviFileError.AlreadyExists, path );

		var (bytes, count) = source.Drive.Measure( source.Node );
		folder.Drive.EnsureRoom( bytes - NaviDrive.Bytes( source.Node.Name ) + NaviDrive.Bytes( name ), count, path );

		return CopyNode( source.Drive, source.Node, folder, name );
	}

	NaviFileLocation CopyNode( NaviDrive drive, NaviFileNode node, NaviFileLocation folder, string name )
	{
		var copy = folder.Drive.Create( folder.Node, name, node.Kind );
		folder.Drive.Edit( copy, x =>
		{
			x.Target = node.Target;
			x.TargetDrive = node.TargetDrive;
			x.TargetNode = node.TargetNode;
		} );

		if ( node.Kind == NaviFileKind.File )
			folder.Drive.SetContent( copy, drive.ReadContent( node ), node.Binary );

		var at = new NaviFileLocation( folder.Drive, copy, Join( folder.Path, name ) );
		foreach ( var child in drive.Children( node ).ToList() )
			CopyNode( drive, child, at, child.Name );

		return at;
	}

	/// <summary>
	/// Delete an entry for good, with everything in it. A symlink is deleted, not its target.
	/// </summary>
	public void Delete( string path )
	{
		var at = Resolve( path, followLast: false );
		EnsureNotProtected( at );
		at.Drive.Delete( at.Node );
	}

	// The trash. Each drive has its own, at its root, like on macOS.

	public string TrashPathOf( NaviDrive drive ) => PathOf( drive, drive.Root ) is { } root ? Join( root, TrashName ) : null;

	/// <summary>
	/// Move an entry to its drive's trash, remembering where it was. Returns where it is now.
	/// </summary>
	public NaviFileLocation Trash( string path )
	{
		var at = Resolve( path, followLast: false );
		EnsureNotProtected( at );

		var trash = TrashPathOf( at.Drive );
		if ( IsWithin( at.Path, trash ) )
			throw new NaviFileException( NaviFileError.Protected, at.Path );

		var folder = CreateFolders( trash );
		var name = FreeName( folder, at.Node.Name );

		at.Drive.EnsureRoom( NaviDrive.Bytes( at.Path ), 0, at.Path );
		at.Drive.Move( at.Node, folder.Node, name );
		at.Drive.Edit( at.Node, x => x.TrashedFrom = at.Path );
		return new NaviFileLocation( at.Drive, at.Node, Join( folder.Path, name ) );
	}

	/// <summary>
	/// Put an entry in the trash back where it was. Throws <see cref="NaviFileError.AlreadyExists"/> if something took
	/// its place.
	/// </summary>
	public NaviFileLocation Restore( string path )
	{
		var at = Resolve( path, followLast: false );
		if ( at.Node.TrashedFrom is not { } from )
			throw new NaviFileException( NaviFileError.NotFound, at.Path );

		var moved = Move( at.Path, from );
		moved.Drive.Edit( moved.Node, x => x.TrashedFrom = null );
		return moved;
	}

	/// <summary>
	/// <paramref name="name"/>, or "name 2.txt" and so on if the folder has one.
	/// </summary>
	public string FreeName( NaviFileLocation folder, string name )
	{
		var dot = name.LastIndexOf( '.' );
		var (stem, extension) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");

		var candidate = name;
		for ( var i = 2; folder.Drive.Child( folder.Node, candidate ) is not null || mounts.ContainsKey( Join( folder.Path, candidate ) ); i++ )
			candidate = $"{stem} {i}{extension}";

		return candidate;
	}
}
