namespace Sandbox;

/// <summary>
/// Thumbnails of the computer's pictures, made by the host. Whoever lists a folder (<c>files.list</c>), the host makes
/// the thumbnail of every picture in it at once, unless one was made from the same contents (by
/// <see cref="NaviFileNode.Hash"/>, so a changed file gets a new one), and whoever asks for one (<c>files.thumbnail</c>)
/// is answered at once from them. A client is only ever sent the thumbnail, never the picture.
/// </summary>
/// <remarks>
/// <para>
/// The seated player's client asks as the app, like every remote read (<see cref="ReadRemoteFiles"/>), with the app's
/// permissions. A client watching the screen may ask only for the pictures a shared app's host copy listed last
/// (<see cref="UserWatchThumbnail"/>), which that app draws for everyone near the screen: a watcher can't reach anything
/// the screen doesn't show, and is never sent anything but a thumbnail.
/// </para>
/// </remarks>
public sealed partial class NaviComputer
{
	/// <summary>The longest side of a thumbnail, in pixels.</summary>
	public const int ThumbnailSize = 128;

	/// <summary>The biggest picture made into a thumbnail, in bytes. Bigger ones get none.</summary>
	public const int MaxThumbnailSource = 8 * 1024 * 1024;

	/// <summary>The files thumbnails are made of.</summary>
	static readonly string[] ThumbnailExtensions = [".jpg", ".jpeg", ".png"];

	/// <summary>The most bytes of thumbnails the host keeps for a computer. The ones used longest ago go first.</summary>
	const long MaxThumbnailCache = 16 * 1024 * 1024;

	/// <summary>How many folders a shared app listed last are kept, whose pictures watchers may ask for.</summary>
	const int MaxShownFolders = 8;

	/// <summary>How many thumbnail asks, and bytes, each watching client may have answered a second.</summary>
	const int MaxWatchAsksPerSecond = 30, MaxWatchBytesPerSecond = 2 * 1024 * 1024;

	/// <summary>
	/// On the host, by the contents' <see cref="NaviFileNode.Hash"/>: the thumbnail made from them, null for contents
	/// that aren't a picture that can be made small, and when it was last used.
	/// </summary>
	readonly Dictionary<string, (byte[] Png, long Used)> thumbnailCache = new();

	long thumbnailUse;
	long thumbnailCacheBytes;

	/// <summary>
	/// On the host, by shared app id: the pictures in the folders it listed last, as it listed them, oldest folder first.
	/// </summary>
	readonly Dictionary<string, List<HashSet<string>>> shownPictures = new();

	/// <summary>On the host: what each watching client asked for this second, by connection.</summary>
	readonly Dictionary<System.Guid, (RealTimeSince Since, int Asks, long Bytes)> watchAsks = new();

	static bool IsThumbnailPicture( string name ) =>
		ThumbnailExtensions.Any( x => name.EndsWith( x, System.StringComparison.OrdinalIgnoreCase ) );

	/// <summary>
	/// On the host: the thumbnail of the file at <paramref name="at"/>, made now if there is none for its contents. Null for
	/// a file that isn't a picture, or is too big.
	/// </summary>
	internal byte[] ThumbnailOf( NaviFileLocation at )
	{
		if ( at.Node.Kind != NaviFileKind.File )
			throw new NaviFileException( at.Node.Kind == NaviFileKind.Folder ? NaviFileError.IsAFolder : NaviFileError.NotFound, at.Path );

		if ( !IsThumbnailPicture( at.Node.Name ) || NaviDrive.Size( at.Node ) is <= 0 or > MaxThumbnailSource )
			return null;

		var key = at.Drive.HashOf( at.Node );
		if ( thumbnailCache.TryGetValue( key, out var cached ) )
		{
			thumbnailCache[key] = cached with { Used = ++thumbnailUse };
			return cached.Png;
		}

		var png = MakeThumbnail( at );
		thumbnailCacheBytes += png?.Length ?? 0;
		thumbnailCache[key] = (png, ++thumbnailUse);

		while ( thumbnailCacheBytes > MaxThumbnailCache && thumbnailCache.Count > 1 )
		{
			var oldest = thumbnailCache.MinBy( x => x.Value.Used );
			thumbnailCacheBytes -= oldest.Value.Png?.Length ?? 0;
			thumbnailCache.Remove( oldest.Key );
		}

		return png;
	}

	byte[] MakeThumbnail( NaviFileLocation at )
	{
		try
		{
			using var bitmap = Bitmap.CreateFromBytes( Files.ReadBytes( at.Path ) );
			if ( bitmap is not { Width: > 0, Height: > 0 } )
				return null;

			var scale = System.Math.Min( 1f, ThumbnailSize / (float)System.Math.Max( bitmap.Width, bitmap.Height ) );
			using var small = bitmap.Resize( System.Math.Max( 1, (int)(bitmap.Width * scale) ),
				System.Math.Max( 1, (int)(bitmap.Height * scale) ), true );
			return small.ToPng();
		}
		catch ( System.Exception )
		{
			return null;
		}
	}

	/// <summary>
	/// On the host: <paramref name="process"/> listed a folder, these entries. The thumbnails of the pictures in it are
	/// made now, or made again for the ones whose file changed. For a shared app, they are what watchers may ask for.
	/// </summary>
	internal void FolderListed( NaviProcess process, IEnumerable<NaviFileLocation> entries )
	{
		if ( !IsAuthority || Files is null )
			return;

		var pictures = new HashSet<string>();
		foreach ( var entry in entries )
		{
			if ( !IsThumbnailPicture( entry.Path ) || Files.TryResolve( entry.Path ) is not { Node.Kind: NaviFileKind.File } at )
				continue;

			ThumbnailOf( at );
			pictures.Add( entry.Path );
		}

		if ( !process.App.Shared )
			return;

		if ( !shownPictures.TryGetValue( process.App.Id, out var folders ) )
			shownPictures[process.App.Id] = folders = new();

		folders.RemoveAll( x => x.SetEquals( pictures ) );
		folders.Add( pictures );
		if ( folders.Count > MaxShownFolders )
			folders.RemoveRange( 0, folders.Count - MaxShownFolders );
	}

	/// <summary>
	/// On the host: let go of what an app listed when it closes, or of every thumbnail when the files are loaded again.
	/// </summary>
	void ForgetThumbnails( string appId = null )
	{
		if ( appId is not null )
		{
			shownPictures.Remove( appId );
			return;
		}

		shownPictures.Clear();
		thumbnailCache.Clear();
		thumbnailCacheBytes = 0;
	}

	/// <summary>
	/// A client that can see the screen asks for the thumbnail of a picture a shared app shows on it: one in a folder
	/// that app's host copy listed last. Made now if it is missing or old, and sent to that client alone. Never while the
	/// screen is locked.
	/// </summary>
	[Rpc.Host]
	void UserWatchThumbnail( string id, string windowId, int request, string path )
	{
		if ( Rpc.Caller is not { } caller )
			return;

		var (status, bytes) = WatchThumbnail( caller, id, windowId, path );
		var message = status == RemoteFailed ? new NaviFileException( NaviFileError.Unavailable, "" ).Message : "";

		using ( Rpc.FilterInclude( caller ) )
			RemoteFilesRead( request, status, message, [], [], bytes ?? [] );
	}

	(int Status, byte[] Bytes) WatchThumbnail( Connection caller, string id, string windowId, string path )
	{
		if ( !IsAuthority || IsLocked || Files is null || path is null || path.Length > NaviDrive.MaxPath
			|| !((Component.INetworkVisible)this).IsVisibleToConnection( caller, default )
			|| GetWindow( id, windowId ) is not { App.Shared: true } window
			|| !shownPictures.TryGetValue( window.App.Id, out var folders ) || !folders.Any( x => x.Contains( path ) ) )
			return (RemoteFailed, null);

		var asks = watchAsks.TryGetValue( caller.Id, out var counted ) && counted.Since <= 1f
			? counted
			: (Since: new RealTimeSince(), Asks: 0, Bytes: 0L);

		if ( asks.Asks >= MaxWatchAsksPerSecond || asks.Bytes > MaxWatchBytesPerSecond )
			return (RemoteFailed, null);

		if ( Files.TryResolve( path ) is not { Node.Kind: NaviFileKind.File } at )
			return (RemoteNull, null);

		var png = ThumbnailOf( at );
		watchAsks[caller.Id] = (asks.Since, asks.Asks + 1, asks.Bytes + (png?.Length ?? 0));
		return png is null ? (RemoteNull, null) : (RemoteOk, png);
	}
}
