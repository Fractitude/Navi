using System.Text;

namespace Sandbox;

/// <summary>
/// What a file system entry is. Numbered as the <c>Navi</c> client's <c>FileKind</c>; keep them the same.
/// </summary>
public enum NaviFileKind
{
	Folder,
	File,

	/// <summary>
	/// Points at a path, like a Unix symlink. Paths through it are followed. It breaks if the target moves.
	/// </summary>
	Symlink,

	/// <summary>
	/// Points at an entry itself, like a macOS alias, so it still finds it after a move or rename. Paths aren't followed
	/// through it; an app resolves it.
	/// </summary>
	Alias,
}

/// <summary>
/// An entry on a <see cref="NaviDrive"/>: a folder, a file, or a link. Plain data, as it is saved.
/// </summary>
public sealed class NaviFileNode
{
	/// <summary>
	/// Unique on its drive, never reused. The root is 1.
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// The folder it is in, or 0 for the drive's root.
	/// </summary>
	public long Parent { get; set; }

	public string Name { get; set; } = "";
	public NaviFileKind Kind { get; set; }

	/// <summary>
	/// A file's size in bytes. Its contents are not here: they are in the drive's blob for this entry
	/// (<see cref="NaviDrive.ReadContent"/>).
	/// </summary>
	public long Size { get; set; }

	/// <summary>
	/// Is the file binary (a picture, say) rather than text? Reading it as text gives nothing.
	/// </summary>
	public bool Binary { get; set; }

	/// <summary>
	/// Goes up with every write of the contents. A blob is saved as <c>{Id}-{Rev}</c> and never overwritten, so the
	/// saved index always points at a blob that was written completely.
	/// </summary>
	public long Rev { get; set; }

	/// <summary>
	/// The SHA-256 of the contents, as hex: the file's version by what is in it. Set on every write; empty for a file
	/// saved before there were hashes, until <see cref="NaviDrive.HashOf"/> works it out.
	/// </summary>
	public string Hash { get; set; } = "";

	/// <summary>
	/// A symlink's target path, absolute or relative to its folder. An alias's target path when it was made, used if
	/// the target's drive isn't mounted or the entry is gone.
	/// </summary>
	public string Target { get; set; }

	/// <summary>
	/// An alias's target: the drive's <see cref="NaviDrive.Id"/> and the entry's <see cref="Id"/> on it.
	/// </summary>
	public string TargetDrive { get; set; }

	public long TargetNode { get; set; }

	/// <summary>
	/// Where an entry in the trash was before, so it can be put back.
	/// </summary>
	public string TrashedFrom { get; set; }

	/// <summary>
	/// Unix seconds (UTC).
	/// </summary>
	public long Created { get; set; }

	public long Modified { get; set; }
}

/// <summary>
/// A drive's contents as they are saved. See <see cref="NaviSaveData.WriteDrive"/>.
/// </summary>
public sealed class NaviDriveSave
{
	public string Label { get; set; } = "";
	public bool Removable { get; set; }
	public long Capacity { get; set; }
	public long NextId { get; set; }
	public List<NaviFileNode> Nodes { get; set; } = new();
}

/// <summary>
/// A drive: a tree of folders, files and links, with a capacity. Every computer has one inside, mounted at <c>/</c>.
/// Removable drives (flash drives, external disks) are mounted at <c>/Volumes/{Label}</c> and keep their files when
/// moved to another computer. A drive knows nothing about paths outside itself or about apps: <see cref="NaviFileSystem"/>
/// does the paths and <see cref="NaviComputer"/> the permissions. Host only.
/// </summary>
public sealed class NaviDrive
{
	/// <summary>
	/// The default capacity, in bytes. Small, since the host keeps every drive.
	/// </summary>
	public const long DefaultCapacity = 32L * 1024 * 1024;

	/// <summary>
	/// The most entries a drive holds, so many empty files can't make a huge save.
	/// </summary>
	public const int MaxNodes = 20000;

	/// <summary>
	/// What an entry costs on top of its name and contents, in bytes, so empty entries aren't free.
	/// </summary>
	public const int NodeOverhead = 64;

	public const int MaxName = 255, MaxPath = 1024;

	/// <summary>
	/// Names the save file, and aliases find their target's drive by it. Letters, digits, '-' and '_'.
	/// </summary>
	public string Id { get; }

	public string Label { get; set; }
	public bool Removable { get; }
	public long Capacity { get; set; }

	/// <summary>
	/// Bytes in use: every entry's name, contents and link target, plus <see cref="NodeOverhead"/> each.
	/// </summary>
	public long Used { get; private set; }

	public long Free => System.Math.Max( 0, Capacity - Used );

	/// <summary>
	/// Goes up on every change, so the drive is saved and file lists refresh.
	/// </summary>
	public int Version { get; private set; }

	public NaviFileNode Root => nodes[RootId];

	public const long RootId = 1;

	readonly Dictionary<long, NaviFileNode> nodes = new();
	readonly Dictionary<long, Dictionary<string, NaviFileNode>> children = new();
	long nextId = RootId + 1;

	public NaviDrive( string id, string label, long capacity, bool removable = false )
	{
		Id = id;
		Label = label;
		Capacity = capacity;
		Removable = removable;

		var now = Now;
		Add( new NaviFileNode { Id = RootId, Kind = NaviFileKind.Folder, Created = now, Modified = now } );
	}

	public static long Now => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	public NaviFileNode Get( long id ) => nodes.GetValueOrDefault( id );

	public NaviFileNode Child( NaviFileNode folder, string name ) =>
		children.TryGetValue( folder.Id, out var list ) ? list.GetValueOrDefault( name ) : null;

	public IEnumerable<NaviFileNode> Children( NaviFileNode folder ) =>
		children.TryGetValue( folder.Id, out var list ) ? list.Values : [];

	public int ChildCount( NaviFileNode folder ) => children.TryGetValue( folder.Id, out var list ) ? list.Count : 0;

	public NaviFileNode ParentOf( NaviFileNode node ) => node.Parent == 0 ? null : Get( node.Parent );

	/// <summary>
	/// Is <paramref name="node"/> <paramref name="folder"/> or inside it?
	/// </summary>
	public bool IsWithin( NaviFileNode node, NaviFileNode folder )
	{
		for ( var x = node; x is not null; x = ParentOf( x ) )
		{
			if ( x == folder )
				return true;
		}

		return false;
	}

	/// <summary>
	/// The entry's path from the drive's root, starting with '/'.
	/// </summary>
	public string PathOf( NaviFileNode node )
	{
		var names = new List<string>();
		for ( var x = node; x is not null && x.Id != RootId; x = ParentOf( x ) )
			names.Add( x.Name );

		names.Reverse();
		return "/" + string.Join( "/", names );
	}

	public static long Cost( NaviFileNode node ) =>
		NodeOverhead + Bytes( node.Name ) + Size( node ) + Bytes( node.Target ) + Bytes( node.TargetDrive )
		+ Bytes( node.TrashedFrom );

	/// <summary>
	/// A file's size in bytes.
	/// </summary>
	public static long Size( NaviFileNode node ) => node.Size;

	// Contents. They are not in the nodes: a file's bytes are a blob on disk (NaviSaveData), read when asked for. A write
	// is kept here, in memory, until the drive is saved.

	/// <summary>
	/// Contents written since the drive was saved, by entry id.
	/// </summary>
	readonly Dictionary<long, byte[]> pending = new();

	/// <summary>
	/// The blobs on disk that the saved index points at, by entry id: the <see cref="NaviFileNode.Rev"/> of each.
	/// </summary>
	readonly Dictionary<long, long> committed = new();

	/// <summary>
	/// Blobs whose entries were deleted, to remove from disk once the index no longer names them.
	/// </summary>
	readonly List<(long Id, long Rev)> abandoned = new();

	/// <summary>
	/// A file's contents. Empty if it has none or its blob is gone.
	/// </summary>
	public byte[] ReadContent( NaviFileNode node )
	{
		if ( pending.TryGetValue( node.Id, out var bytes ) )
			return bytes;

		return node.Rev > 0 ? NaviSaveData.ReadBlob( Id, node.Id, node.Rev ) ?? [] : [];
	}

	/// <summary>
	/// Replace a file's contents. The caller checked there is room.
	/// </summary>
	public void SetContent( NaviFileNode node, byte[] bytes, bool binary )
	{
		Edit( node, x =>
		{
			x.Size = bytes.LongLength;
			x.Binary = binary;
			x.Rev++;
			x.Hash = HashOf( bytes );
		} );

		pending[node.Id] = bytes;
	}

	/// <summary>
	/// The file's <see cref="NaviFileNode.Hash"/>, worked out from its contents if it has none yet.
	/// </summary>
	public string HashOf( NaviFileNode node )
	{
		if ( string.IsNullOrEmpty( node.Hash ) && node.Kind == NaviFileKind.File )
			node.Hash = HashOf( ReadContent( node ) );

		return node.Hash ?? "";
	}

	static string HashOf( byte[] bytes ) =>
		System.Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( bytes ?? [] ) ).ToLowerInvariant();

	/// <summary>
	/// What a save has to write: the contents written since the last one.
	/// </summary>
	public List<(long Id, long Rev, byte[] Bytes)> PendingContents() =>
		pending.Where( x => nodes.ContainsKey( x.Key ) ).Select( x => (x.Key, nodes[x.Key].Rev, x.Value) ).ToList();

	/// <summary>
	/// The blobs on disk that the saved index points at. A save names these and no others.
	/// </summary>
	public IReadOnlyDictionary<long, long> CommittedBlobs => committed;

	/// <summary>
	/// A save wrote <paramref name="written"/> and the index. Returns the blobs that are no longer named by it, to delete.
	/// </summary>
	public List<(long Id, long Rev)> Committed( IEnumerable<(long Id, long Rev, byte[] Bytes)> written )
	{
		var old = new List<(long Id, long Rev)>( abandoned );
		abandoned.Clear();

		foreach ( var (id, rev, bytes) in written )
		{
			if ( committed.TryGetValue( id, out var before ) && before != rev )
				old.Add( (id, before) );

			committed[id] = rev;

			if ( pending.TryGetValue( id, out var current ) && ReferenceEquals( current, bytes ) )
				pending.Remove( id );
		}

		return old;
	}

	public static long Bytes( string text ) => string.IsNullOrEmpty( text ) ? 0 : Encoding.UTF8.GetByteCount( text );

	/// <summary>
	/// Throws <see cref="NaviFileError.NoSpace"/> unless <paramref name="bytes"/> more and <paramref name="count"/> more
	/// entries fit.
	/// </summary>
	public void EnsureRoom( long bytes, int count, string path )
	{
		if ( Used + bytes > Capacity || nodes.Count + count > MaxNodes )
			throw new NaviFileException( NaviFileError.NoSpace, path );
	}

	/// <summary>
	/// Make a new entry in <paramref name="folder"/>. The caller checked the name is free and there is room.
	/// </summary>
	public NaviFileNode Create( NaviFileNode folder, string name, NaviFileKind kind )
	{
		var now = Now;
		var node = new NaviFileNode { Id = nextId++, Parent = folder.Id, Name = name, Kind = kind, Created = now, Modified = now };
		Add( node );
		Touch( folder );
		return node;
	}

	void Add( NaviFileNode node )
	{
		nodes[node.Id] = node;
		if ( node.Parent != 0 )
		{
			if ( !children.TryGetValue( node.Parent, out var list ) )
				children[node.Parent] = list = new( System.StringComparer.Ordinal );

			list[node.Name] = node;
		}

		Used += Cost( node );
		Version++;
	}

	/// <summary>
	/// Change an entry's data (not its name or folder): <paramref name="change"/> runs on it, and the drive keeps count.
	/// </summary>
	public void Edit( NaviFileNode node, System.Action<NaviFileNode> change )
	{
		Used -= Cost( node );
		change( node );
		Used += Cost( node );
		node.Modified = Now;
		Version++;
	}

	/// <summary>
	/// Move an entry to another folder of this drive, under a new name. The caller checked the name is free and that a
	/// folder isn't moved into itself.
	/// </summary>
	public void Move( NaviFileNode node, NaviFileNode folder, string name )
	{
		Used -= Bytes( node.Name );
		children[node.Parent].Remove( node.Name );
		Touch( Get( node.Parent ) );

		node.Parent = folder.Id;
		node.Name = name;
		if ( !children.TryGetValue( folder.Id, out var list ) )
			children[folder.Id] = list = new( System.StringComparer.Ordinal );

		list[name] = node;
		Used += Bytes( name );
		Touch( folder );
	}

	/// <summary>
	/// Delete an entry and everything in it.
	/// </summary>
	public void Delete( NaviFileNode node )
	{
		if ( node.Id == RootId )
			return;

		foreach ( var child in Children( node ).ToList() )
			Delete( child );

		pending.Remove( node.Id );
		if ( committed.Remove( node.Id, out var rev ) )
			abandoned.Add( (node.Id, rev) );

		children.Remove( node.Id );
		children[node.Parent].Remove( node.Name );
		nodes.Remove( node.Id );
		Used -= Cost( node );
		Touch( Get( node.Parent ) );
	}

	/// <summary>
	/// What an entry and everything in it costs, and how many entries that is.
	/// </summary>
	public (long Bytes, int Count) Measure( NaviFileNode node )
	{
		var (bytes, count) = (Cost( node ), 1);
		foreach ( var child in Children( node ) )
		{
			var (b, c) = Measure( child );
			bytes += b;
			count += c;
		}

		return (bytes, count);
	}

	void Touch( NaviFileNode folder )
	{
		if ( folder is not null )
			folder.Modified = Now;

		Version++;
	}

	public NaviDriveSave CaptureSave() => new()
	{
		Label = Label,
		Removable = Removable,
		Capacity = Capacity,
		NextId = nextId,
		Nodes = nodes.Values.OrderBy( x => x.Id ).ToList(),
	};

	/// <summary>
	/// A drive from its save. Entries that don't fit the tree (no parent, a bad name, a name used twice, a loop) are
	/// dropped, with what is in them. <paramref name="capacity"/> overrides the saved one when given; a drive that is
	/// over it keeps its files but can't grow.
	/// </summary>
	public static NaviDrive FromSave( string id, NaviDriveSave save, long? capacity = null )
	{
		var drive = new NaviDrive( id, save.Label ?? "", capacity ?? save.Capacity, save.Removable );
		var byParent = (save.Nodes ?? []).Where( x => x is not null && x.Id > RootId && System.Enum.IsDefined( x.Kind ) )
			.GroupBy( x => x.Parent )
			.ToDictionary( x => x.Key, x => x.ToList() );

		if ( save.Nodes?.FirstOrDefault( x => x?.Id == RootId ) is { } root )
		{
			drive.Root.Created = root.Created;
			drive.Root.Modified = root.Modified;
		}

		// From the root down, so only entries reachable from it come back, and each once.
		var queue = new Queue<long>();
		queue.Enqueue( RootId );
		var seen = new HashSet<long> { RootId };

		while ( queue.Count > 0 )
		{
			var parent = queue.Dequeue();
			foreach ( var node in byParent.GetValueOrDefault( parent ) ?? [] )
			{
				if ( !seen.Add( node.Id ) || !NaviFileSystem.IsValidName( node.Name )
					|| drive.Child( drive.Get( parent ), node.Name ) is not null )
					continue;

				var created = node.Created;
				var modified = node.Modified;

				// A file's blob on disk is the one its saved revision names.
				if ( node.Kind == NaviFileKind.File && node.Rev > 0 )
					drive.committed[node.Id] = node.Rev;

				drive.Add( node );
				node.Created = created;
				node.Modified = modified;

				if ( node.Kind == NaviFileKind.Folder )
					queue.Enqueue( node.Id );
			}
		}

		drive.nextId = System.Math.Max( save.NextId, (drive.nodes.Keys.DefaultIfEmpty( RootId ).Max()) + 1 );
		drive.Version = 0;
		return drive;
	}
}
