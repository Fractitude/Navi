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

	static string GetName( string id ) =>
		NaviNpcs.Find( id )?.Name ?? (contacts.TryGetValue( id, out var contact ) ? contact.Name : id);

	/// <summary>
	/// A real player the host has seen, or an NPC the game has made: someone who can be messaged.
	/// </summary>
	static bool IsKnown( string id ) => contacts.ContainsKey( id ) || NaviNpcs.Find( id ) is not null;

	/// <summary>
	/// The ids of the players in the game now. Offline, the local player.
	/// </summary>
	public static IEnumerable<string> OnlinePlayerIds() =>
		Networking.IsActive
			? Connection.All.Select( IdOf ).ToList()
			: [Sandbox.Utility.Steam.SteamId.ToString()];

	/// <summary>
	/// Something the apps show changed without a message: an NPC's name, presence or listing.
	/// </summary>
	internal static void Touch() => Changed();

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

	internal static string CleanName( string name, string fallback )
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
		if ( NaviNpcs.Find( id ) is { } npc )
			return npc.Online;

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
			list.Add( ViewOf( me, contact.Id, contact.Name, Thread( me, contact.Id ) ) );

		// A game's characters. A hidden one is listed only once it has said something to this player.
		foreach ( var npc in NaviNpcs.All )
		{
			var thread = Thread( me, npc.Id );

			if ( !npc.Hidden || thread.Count > 0 )
				list.Add( ViewOf( me, npc.Id, npc.Name, thread ) );
		}

		return list;
	}

	static NaviChatContactView ViewOf( string me, string id, string name, List<NaviChatMessage> thread )
	{
		var last = thread.LastOrDefault();
		var readUpTo = reads.GetValueOrDefault( ReadKey( me, id ) );
		var unread = thread.Count( x => x.To == me && x.Id > readUpTo );

		return new NaviChatContactView( id, name, IsOnline( id ), last?.Text ?? "", last?.At ?? 0, unread, last?.From == me );
	}

	/// <summary>
	/// How many messages <paramref name="peer"/> sent <paramref name="me"/> that they haven't read.
	/// </summary>
	public static int UnreadCount( string me, string peer )
	{
		EnsureLoaded();

		var readUpTo = reads.GetValueOrDefault( ReadKey( me, peer ) );
		return Thread( me, peer ).Count( x => x.To == me && x.Id > readUpTo );
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

		if ( me == peer || !IsKnown( peer ) || !contacts.ContainsKey( me ) )
			return false;

		// A hidden character can't be reached until it has reached out.
		if ( NaviNpcs.Find( peer ) is { Hidden: true } && Thread( me, peer ).Count == 0 )
			return false;

		text = Clean( text );
		if ( text.Length == 0 || IsFlooding( me ) )
			return false;

		var message = Add( me, peer, text, 0 );

		// A game's character hears it, and may answer.
		NaviNpcs.Find( peer )?.Deliver( message );
		return true;
	}

	/// <summary>
	/// Takes a message that is already clean, with no limit on how fast. <paramref name="at"/> is Unix seconds, or 0 for now.
	/// Who may send it is for the caller to decide.
	/// </summary>
	internal static NaviChatMessage Add( string from, string to, string text, long at )
	{
		EnsureLoaded();

		var message = new NaviChatMessage
		{
			Id = ++LastId,
			From = from,
			To = to,
			Text = text,
			At = at > 0 ? at : System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
		};

		messages.Add( message );
		Trim( from, to );
		Changed();
		return message;
	}

	/// <summary>
	/// Rewrites a message, if <paramref name="party"/> is the sender or the receiver.
	/// </summary>
	internal static bool Edit( long id, string party, string text )
	{
		EnsureLoaded();

		text = Clean( text );
		var message = messages.FirstOrDefault( x => x.Id == id && (x.From == party || x.To == party) );
		if ( message is null || text.Length == 0 )
			return false;

		message.Text = text;
		Changed();
		return true;
	}

	/// <summary>
	/// Removes a message, if <paramref name="party"/> is the sender or the receiver.
	/// </summary>
	internal static bool Delete( long id, string party )
	{
		EnsureLoaded();

		var message = messages.FirstOrDefault( x => x.Id == id && (x.From == party || x.To == party) );
		if ( message is null )
			return false;

		messages.Remove( message );
		Changed();
		return true;
	}

	/// <summary>
	/// Removes every message between two players. Returns how many.
	/// </summary>
	internal static int ClearThread( string a, string b )
	{
		EnsureLoaded();

		var removed = messages.RemoveAll( x => (x.From == a && x.To == b) || (x.From == b && x.To == a) );
		if ( removed > 0 )
			Changed();

		return removed;
	}

	/// <summary>
	/// <paramref name="me"/> has read everything <paramref name="peer"/> sent so far.
	/// </summary>
	public static void MarkRead( string me, string peer )
	{
		EnsureLoaded();

		var key = ReadKey( me, peer );
		if ( reads.GetValueOrDefault( key ) >= LastId || !IsKnown( peer ) )
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
