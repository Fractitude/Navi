using System.Threading.Tasks;
using FileEntry = System.ValueTuple<string, string, int, long, long, long, System.ValueTuple<string, string, int, bool>>;

namespace Sandbox;

/// <summary>
/// Reading the computer's files from the seated player's client. Only the host has the files, so an app's copy on a
/// client that isn't the host (a viewer's copy of a shared app, such as the Web Browser's own browsing, or a copy of an
/// app that isn't shared) asks the host for what it reads. Only the reading calls in <see cref="RemoteFileCalls"/> go,
/// only from the seated user's client, and only for a window open on this computer.
/// </summary>
/// <remarks>
/// <para>
/// The host runs the call as that window's app, through <see cref="NaviFileCalls"/>, so the app's permissions, its own
/// folder, its picked files and the double path check (as written, and where symlinks lead) all apply as they do to the
/// host's copy. The client says which window, never which permissions: a modified client can only pick another open
/// window, and so read what that app may, which the seated player can already see in the Files app. Nothing that writes,
/// moves or deletes is ever run this way.
/// </para>
/// <para>
/// The answer goes to the caller alone (<see cref="Rpc.FilterInclude(Connection)"/>), never to anyone else near the
/// screen, and only the host may send it. The files of this computer are the only ones reached: each computer has its
/// own drive (<see cref="Files"/>).
/// </para>
/// </remarks>
public sealed partial class NaviComputer
{
	/// <summary>The calls a client's copy of an app may make to read the host's files. None of them change anything.</summary>
	static readonly string[] RemoteFileCalls = ["files.info", "files.list", "files.read", "files.read-bytes"];

	/// <summary>The most a file read from a client may be, in bytes. Bigger ones are refused on the host, before sending.</summary>
	public const int MaxRemoteFileBytes = 4 * 1024 * 1024;

	/// <summary>The most entries a folder listing sent to a client holds.</summary>
	const int MaxRemoteListEntries = 2048;

	/// <summary>How many reads, and how many bytes, the user's client may ask for each second.</summary>
	const int MaxRemoteReadsPerSecond = 30, MaxRemoteBytesPerSecond = 8 * 1024 * 1024;

	/// <summary>How long a client waits for the host's answer before giving up, in seconds.</summary>
	const float RemoteReadTimeout = 15f;

	/// <summary>Is <paramref name="method"/> a call a client's copy of an app may make to read the host's files?</summary>
	internal static bool IsRemoteFileCall( string method ) => RemoteFileCalls.Contains( method );

	// On the client: the reads waiting for the host's answer.

	readonly Dictionary<int, (TaskCompletionSource<object> Reply, string Method, RealTimeSince Sent)> remoteReads = new();
	int nextRemoteRead;

	// On the host: what the user's client asked for this second.

	RealTimeSince sinceRemoteWindow;
	int remoteReadsInWindow;
	long remoteBytesInWindow;

	/// <summary>
	/// On a client: read the host's files as <paramref name="window"/>'s app, and answer <paramref name="reply"/> with what
	/// the same call gives the host's copy. Only the seated user's client asks; any other gets
	/// <see cref="NaviFileError.Unavailable"/> at once.
	/// </summary>
	internal void ReadRemoteFiles( NaviWindow window, string method, object[] args, TaskCompletionSource<object> reply )
	{
		if ( IsAuthority || !IsUser || !IsRemoteFileCall( method ) || window is null )
		{
			reply.TrySetException( new NaviFileException( NaviFileError.Unavailable, "" ) );
			return;
		}

		args ??= [];
		var path = args.Length > 0 && args[0] is string text ? text : null;
		var follow = args.Length <= 1 || args[1] is not bool value || value;
		if ( path is null || path.Length > NaviDrive.MaxPath )
		{
			reply.TrySetException( new System.ArgumentException( $"'{method}' needs a String as argument 1." ) );
			return;
		}

		var request = ++nextRemoteRead;
		remoteReads[request] = (reply, method, 0);
		UserReadFiles( window.App.Id, window.Id, request, method, path, follow );
	}

	/// <summary>
	/// On a client, every frame: give up on reads the host never answered, so an app isn't left waiting.
	/// </summary>
	void UpdateRemoteReads()
	{
		if ( remoteReads.Count == 0 )
			return;

		foreach ( var (request, waiting) in remoteReads.Where( x => x.Value.Sent > RemoteReadTimeout ).ToList() )
		{
			remoteReads.Remove( request );
			waiting.Reply.TrySetException( new NaviFileException( NaviFileError.Unavailable, "" ) );
		}
	}

	/// <summary>
	/// The user's client reads the files as one of the open windows' apps. Runs the call as that app and sends the
	/// answer back to the caller only.
	/// </summary>
	[Rpc.Host]
	void UserReadFiles( string id, string windowId, int request, string method, string path, bool follow )
	{
		var caller = Rpc.Caller;
		if ( caller is null )
			return;

		var (status, message, strings, numbers, bytes) = RunRemoteRead( id, windowId, method, path, follow );

		using ( Rpc.FilterInclude( caller ) )
			RemoteFilesRead( request, status, message ?? "", strings ?? [], numbers ?? [], bytes ?? [] );
	}

	// The answer's status: what the client makes of it.
	const int RemoteOk = 0, RemoteNull = 1, RemoteDenied = 2, RemoteFailed = 3, RemoteError = 4;

	/// <summary>
	/// On the host: run a client's read, and put the answer in a form an RPC carries. A file's entry is four strings
	/// (name, path, link target, where it was trashed from) and six numbers (kind, size, created, modified, entries in it,
	/// whether it is a drive), as <see cref="NaviFileCalls"/> describes it.
	/// </summary>
	(int Status, string Message, string[] Strings, long[] Numbers, byte[] Bytes) RunRemoteRead( string id, string windowId,
		string method, string path, bool follow )
	{
		var unavailable = new NaviFileException( NaviFileError.Unavailable, "" ).Message;

		// Only the user, never while the screen is locked, only a window open here, and only a call that reads.
		if ( !Authorize() || !IsRemoteFileCall( method ) || path is null || path.Length > NaviDrive.MaxPath
			|| GetWindow( id, windowId ) is not { } window )
			return (RemoteFailed, unavailable, null, null, null);

		if ( sinceRemoteWindow > 1f )
		{
			sinceRemoteWindow = 0;
			remoteReadsInWindow = 0;
			remoteBytesInWindow = 0;
		}

		if ( ++remoteReadsInWindow > MaxRemoteReadsPerSecond || remoteBytesInWindow > MaxRemoteBytesPerSecond )
			return (RemoteFailed, unavailable, null, null, null);

		object result;
		try
		{
			result = NaviFileCalls.Run( this, window.Process, method, [path, follow] );
		}
		catch ( System.UnauthorizedAccessException e )
		{
			return (RemoteDenied, e.Message, null, null, null);
		}
		catch ( System.InvalidOperationException e )
		{
			return (RemoteFailed, e.Message, null, null, null);
		}
		catch ( System.Exception e )
		{
			return (RemoteError, e.Message, null, null, null);
		}

		switch ( result )
		{
			case null:
				return (RemoteNull, null, null, null, null);

			case string text:
			{
				var bytes = System.Text.Encoding.UTF8.GetBytes( text );
				return Sent( bytes.Length ) ? (RemoteOk, null, null, null, bytes) : TooBig();
			}

			case byte[] bytes:
				return Sent( bytes.Length ) ? (RemoteOk, null, null, null, bytes) : TooBig();

			case FileEntry entry:
			{
				var (strings, numbers) = (new List<string>(), new List<long>());
				AddEntry( entry, strings, numbers );
				return (RemoteOk, null, strings.ToArray(), numbers.ToArray(), null);
			}

			case FileEntry[] entries:
			{
				var (strings, numbers) = (new List<string>(), new List<long>());
				foreach ( var entry in entries.Take( MaxRemoteListEntries ) )
					AddEntry( entry, strings, numbers );

				return Sent( strings.Sum( x => x.Length ) )
					? (RemoteOk, null, strings.ToArray(), numbers.ToArray(), null)
					: TooBig();
			}
		}

		return (RemoteError, $"Navi has no call '{method}'.", null, null, null);

		bool Sent( long size )
		{
			if ( size > MaxRemoteFileBytes )
				return false;

			remoteBytesInWindow += size;
			return true;
		}

		(int, string, string[], long[], byte[]) TooBig() =>
			(RemoteFailed, $"{path} is too big to send to a client.", null, null, null);
	}

	static void AddEntry( FileEntry entry, List<string> strings,
		List<long> numbers )
	{
		var (name, path, kind, size, created, modified, (target, trashedFrom, count, drive)) = entry;
		strings.AddRange( [name, path, target, trashedFrom] );
		numbers.AddRange( [kind, size, created, modified, count, drive ? 1 : 0] );
	}

	static FileEntry ReadEntry( string[] strings, long[] numbers,
		int index ) =>
	(
		strings[index * 4], strings[index * 4 + 1], (int)numbers[index * 6], numbers[index * 6 + 1], numbers[index * 6 + 2],
		numbers[index * 6 + 3], (strings[index * 4 + 2], strings[index * 4 + 3], (int)numbers[index * 6 + 4], numbers[index * 6 + 5] != 0)
	);

	/// <summary>
	/// On the client that asked: the host's answer to a read. Only the host sends it, and only to that client; an answer
	/// to nothing this client asked is ignored.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void RemoteFilesRead( int request, int status, string message, string[] strings, long[] numbers, byte[] bytes )
	{
		if ( IsAuthority || !remoteReads.Remove( request, out var waiting ) )
			return;

		var reply = waiting.Reply;
		strings ??= [];
		numbers ??= [];

		switch ( status )
		{
			case RemoteOk when waiting.Method == "files.read":
				reply.TrySetResult( System.Text.Encoding.UTF8.GetString( bytes ?? [] ) );
				return;

			case RemoteOk when waiting.Method == "files.read-bytes":
				reply.TrySetResult( bytes ?? [] );
				return;

			case RemoteOk when strings.Length % 4 == 0 && numbers.Length == strings.Length / 4 * 6:
			{
				var entries = Enumerable.Range( 0, strings.Length / 4 ).Select( x => ReadEntry( strings, numbers, x ) ).ToArray();
				if ( waiting.Method == "files.list" )
					reply.TrySetResult( entries );
				else if ( entries.Length == 1 )
					reply.TrySetResult( entries[0] );
				else
					reply.TrySetResult( null );

				return;
			}

			case RemoteNull:
				reply.TrySetResult( null );
				return;

			case RemoteDenied:
				reply.TrySetException( new System.UnauthorizedAccessException( message ) );
				return;

			case RemoteFailed:
				reply.TrySetException( new System.InvalidOperationException( message ) );
				return;
		}

		reply.TrySetException( new System.Exception( string.IsNullOrEmpty( message ) ? "The host couldn't read the files." : message ) );
	}
}
