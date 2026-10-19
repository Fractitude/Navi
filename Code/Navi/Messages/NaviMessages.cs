using System.Text;

namespace Sandbox;

/// <summary>
/// One direct message, as the host keeps it.
/// </summary>
public sealed class NaviChatMessage
{
	/// <summary>
	/// Goes up with each message the host takes, so a higher one is newer. Never 0.
	/// </summary>
	public long Id { get; set; }

	public string From { get; set; } = "";
	public string To { get; set; } = "";
	public string Text { get; set; } = "";

	/// <summary>
	/// When the host took it, in Unix seconds (UTC).
	/// </summary>
	public long At { get; set; }
}

/// <summary>
/// A player the host has seen, so they can be messaged while they are away too.
/// </summary>
public sealed class NaviChatContact
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";

	/// <summary>
	/// When the host last saw them, in Unix seconds (UTC).
	/// </summary>
	public long LastSeen { get; set; }
}

/// <summary>
/// What the host saves of the messages.
/// </summary>
public sealed class NaviMessagesSave
{
	public long LastId { get; set; }
	public List<NaviChatMessage> Messages { get; set; } = new();
	public List<NaviChatContact> Contacts { get; set; } = new();

	/// <summary>
	/// The newest message id each player has read, by "reader|peer".
	/// </summary>
	public Dictionary<string, long> Reads { get; set; } = new();
}

/// <summary>
/// A player in someone's message list: who they are, whether they are in the game now, the newest message between the two
/// of them and how many the asker hasn't read.
/// </summary>
public sealed record NaviChatContactView( string Id, string Name, bool Online, string LastText, long LastAt, int Unread, bool LastMine );

/// <summary>
/// Direct messages between players. One store for the whole game, kept by the host: a message belongs to the two players,
/// not to a computer, so it reaches the other one wherever they sit down. A player is their Steam id.
///
/// Every question here is asked as a player (<see cref="Identify"/>: the one in a computer's seat, as the host sees it), so
/// nobody reads another's messages or sends as another. Apps reach it only through <see cref="NaviMessageCalls"/>, which
/// needs <see cref="NaviPermissions.Messages"/>. The files are the host's alone; they are never sent to clients.
/// </summary>
public static class NaviMessages
{
	/// <summary>
	/// The most a message holds, in characters. More is cut off.
	/// </summary>
	public const int MaxText = 400;

	/// <summary>
	/// The most the host keeps: per pair of players, in all, and players it has seen. Older ones make room.
	/// </summary>
	public const int MaxPerThread = 300, MaxMessages = 4000, MaxContacts = 200;

	/// <summary>
	/// A player can send this many messages in <see cref="FloodSeconds"/>.
	/// </summary>
	const int FloodCount = 8;

	const float FloodSeconds = 4f;

	const string SavePath = "navi/messages.json";

	// Not readonly: a hot reload resets a readonly static field of only engine types to its initial value.
	static List<NaviChatMessage> messages = new();
	static Dictionary<string, NaviChatContact> contacts = new();
	static Dictionary<string, long> reads = new();
	static Dictionary<string, Queue<float>> sent = new();

	static bool loaded;
	static bool dirty;
	static RealTimeSince sinceChange;
	static RealTimeSince sinceScan = 100f;

	/// <summary>
	/// Goes up whenever a message is sent or read, or a player is seen for the first time. Apps poll it.
	/// </summary>
	public static int Version { get; private set; }

	/// <summary>
	/// The id of the newest message, even if it is gone since.
	/// </summary>
	public static long LastId { get; private set; }

	/// <summary>
	/// Once a frame, from a computer on the host: notice the players who joined or left, and save a second after the last
	/// change.
	/// </summary>
	public static void Update()
	{
		EnsureLoaded();

		if ( sinceScan > 0.5f )
		{
			sinceScan = 0;

			foreach ( var connection in Connection.All )
				Register( IdOf( connection ), connection.DisplayName );
		}

		if ( dirty && sinceChange > 1f )
			Save();
	}

	/// <summary>
	/// The player's id for a connection: their Steam id, or the connection's own id if it has none.
	/// </summary>
	public static string IdOf( Connection connection ) =>
		connection.SteamId != 0 ? connection.SteamId.ToString() : connection.Id.ToString();

	/// <summary>
	/// Who is at the computer, as the host sees it: the connection in the seat, or the local player offline once they sit.
	/// Null while nobody is. Never what an app says about itself.
	/// </summary>
	public static (string Id, string Name)? Identify( NaviComputer computer )
	{
		if ( !computer.IsAuthority )
			return null;

		EnsureLoaded();

		if ( Networking.IsActive )
		{
			if ( computer.User is not { } user )
				return null;

			var id = IdOf( user );
			Register( id, user.DisplayName );
			return (id, GetName( id ));
		}

		if ( !computer.IsSeated )
			return null;

		var local = Sandbox.Utility.Steam.SteamId.ToString();
		Register( local, Sandbox.Utility.Steam.PersonaName );
		return (local, GetName( local ));
	}

	static string GetName( string id ) => contacts.TryGetValue( id, out var contact ) ? contact.Name : id;

	static void Register( string id, string name )
	{
		if ( string.IsNullOrEmpty( id ) )
			return;

		name = CleanName( name, id );
		var now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

		if ( !contacts.TryGetValue( id, out var contact ) )
		{
			contacts[id] = new NaviChatContact { Id = id, Name = name, LastSeen = now };
			TrimContacts();
			Changed();
			return;
		}

		// Seen again: the time only matters to the order, so it isn't worth a save by itself.
		contact.LastSeen = now;

		if ( contact.Name != name )
		{
			contact.Name = name;
			Changed();
		}
	}

	static string CleanName( string name, string fallback )
	{
		name = name?.ReplaceLineEndings( " " ).Trim();
		if ( string.IsNullOrEmpty( name ) )
			name = fallback;

		return name.Length > 32 ? name[..32] : name;
	}

	static void TrimContacts()
	{
		while ( contacts.Count > MaxContacts )
		{
			var oldest = contacts.Values.OrderBy( x => x.LastSeen ).First();
			contacts.Remove( oldest.Id );
		}
	}

	/// <summary>
	/// Is this player in the game now? Offline, only the local player is.
	/// </summary>
	static bool IsOnline( string id )
	{
		if ( !Networking.IsActive )
			return id == Sandbox.Utility.Steam.SteamId.ToString();

		return Connection.All.Any( x => IdOf( x ) == id );
	}

	/// <summary>
	/// Every other player the host has seen, with the newest message between them and <paramref name="me"/> and how many of
	/// them <paramref name="me"/> hasn't read.
	/// </summary>
	public static IReadOnlyList<NaviChatContactView> GetContacts( string me )
	{
		EnsureLoaded();

		var list = new List<NaviChatContactView>();

		foreach ( var contact in contacts.Values.Where( x => x.Id != me ) )
		{
			var thread = Thread( me, contact.Id );
			var last = thread.LastOrDefault();
			var readUpTo = reads.GetValueOrDefault( ReadKey( me, contact.Id ) );
			var unread = thread.Count( x => x.To == me && x.Id > readUpTo );

			list.Add( new NaviChatContactView(
				contact.Id, contact.Name, IsOnline( contact.Id ),
				last?.Text ?? "", last?.At ?? 0, unread, last?.From == me ) );
		}

		return list;
	}

	/// <summary>
	/// The newest <paramref name="take"/> messages between the two players, oldest first.
	/// </summary>
	public static IReadOnlyList<NaviChatMessage> GetHistory( string me, string peer, int take )
	{
		EnsureLoaded();

		var thread = Thread( me, peer );
		return thread.Skip( System.Math.Max( 0, thread.Count - take ) ).ToList();
	}

	/// <summary>
	/// Messages newer than <paramref name="id"/>, to anyone, oldest first. The host's computers use it to tell the player a
	/// message came.
	/// </summary>
	public static IReadOnlyList<NaviChatMessage> Since( long id ) => messages.Where( x => x.Id > id ).ToList();

	/// <summary>
	/// The player's name, as the host knows it.
	/// </summary>
	public static string NameOf( string id ) => GetName( id );

	static List<NaviChatMessage> Thread( string a, string b ) =>
		messages.Where( x => (x.From == a && x.To == b) || (x.From == b && x.To == a) ).ToList();

	static string ReadKey( string reader, string peer ) => $"{reader}|{peer}";

	/// <summary>
	/// <paramref name="me"/> sends a message to a player the host has seen. The text is made one line, cut to size and
	/// trimmed; nothing is sent if nothing is left, or the player is sending too fast. Returns false then.
	/// </summary>
	public static bool Send( string me, string peer, string text )
	{
		EnsureLoaded();

		if ( me == peer || !contacts.ContainsKey( peer ) || !contacts.ContainsKey( me ) )
			return false;

		text = Clean( text );
		if ( text.Length == 0 || IsFlooding( me ) )
			return false;

		messages.Add( new NaviChatMessage
		{
			Id = ++LastId,
			From = me,
			To = peer,
			Text = text,
			At = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
		} );

		Trim( me, peer );
		Changed();
		return true;
	}

	/// <summary>
	/// <paramref name="me"/> has read everything <paramref name="peer"/> sent so far.
	/// </summary>
	public static void MarkRead( string me, string peer )
	{
		EnsureLoaded();

		var key = ReadKey( me, peer );
		if ( reads.GetValueOrDefault( key ) >= LastId || !contacts.ContainsKey( peer ) )
			return;

		reads[key] = LastId;
		Changed();
	}

	/// <summary>
	/// One line, no control characters, at most <see cref="MaxText"/> characters without cutting an emoji in half.
	/// </summary>
	public static string Clean( string text )
	{
		if ( string.IsNullOrEmpty( text ) )
			return "";

		var builder = new StringBuilder( text.Length );
		foreach ( var c in text )
			builder.Append( char.IsControl( c ) ? ' ' : c );

		var clean = builder.ToString().Trim();
		if ( clean.Length <= MaxText )
			return clean;

		var end = MaxText;
		if ( char.IsHighSurrogate( clean[end - 1] ) )
			end--;

		return clean[..end].TrimEnd();
	}

	static bool IsFlooding( string me )
	{
		if ( !sent.TryGetValue( me, out var times ) )
			sent[me] = times = new Queue<float>();

		var now = Time.Now;
		while ( times.Count > 0 && now - times.Peek() > FloodSeconds )
			times.Dequeue();

		if ( times.Count >= FloodCount )
			return true;

		times.Enqueue( now );
		return false;
	}

	/// <summary>
	/// Drop the oldest messages of the pair past <see cref="MaxPerThread"/>, and the oldest of all past
	/// <see cref="MaxMessages"/>.
	/// </summary>
	static void Trim( string a, string b )
	{
		var thread = Thread( a, b );
		foreach ( var old in thread.Take( System.Math.Max( 0, thread.Count - MaxPerThread ) ) )
			messages.Remove( old );

		if ( messages.Count > MaxMessages )
			messages.RemoveRange( 0, messages.Count - MaxMessages );
	}

	static void Changed()
	{
		Version++;
		dirty = true;
		sinceChange = 0;
	}

	// Saved by the host, in its local data, a second after the last change.

	static void EnsureLoaded()
	{
		if ( loaded )
			return;

		loaded = true;

		try
		{
			if ( FileSystem.Data is not { } data || !data.FileExists( SavePath ) )
				return;

			var save = Json.Deserialize<NaviMessagesSave>( data.ReadAllText( SavePath ) );
			if ( save is null )
				return;

			messages.Clear();
			messages.AddRange( (save.Messages ?? []).Where( x => x is not null && x.Id > 0 && !string.IsNullOrEmpty( x.From )
				&& !string.IsNullOrEmpty( x.To ) ).OrderBy( x => x.Id ) );

			contacts.Clear();
			foreach ( var contact in save.Contacts ?? [] )
			{
				if ( contact is not null && !string.IsNullOrEmpty( contact.Id ) )
					contacts[contact.Id] = contact;
			}

			reads.Clear();
			foreach ( var (key, value) in save.Reads ?? [] )
				reads[key] = value;

			LastId = System.Math.Max( save.LastId, messages.Count > 0 ? messages[^1].Id : 0 );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"NaviMessages: couldn't read the saved messages: {e.Message}" );
		}
	}

	/// <summary>
	/// Write the messages now if they changed since they were last written. The host's computers do it when they turn off.
	/// </summary>
	public static void Flush()
	{
		if ( dirty )
			Save();
	}

	/// <summary>
	/// Write the messages now. Done on its own a second after a change.
	/// </summary>
	public static void Save()
	{
		dirty = false;

		try
		{
			if ( FileSystem.Data is not { } data )
				return;

			data.CreateDirectory( "navi" );
			data.WriteAllText( SavePath, Json.Serialize( new NaviMessagesSave
			{
				LastId = LastId,
				Messages = messages.ToList(),
				Contacts = contacts.Values.ToList(),
				Reads = new Dictionary<string, long>( reads ),
			} ) );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"NaviMessages: couldn't save the messages: {e.Message}" );
		}
	}
}
