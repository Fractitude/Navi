namespace Sandbox;

/// <summary>
/// What a player wrote to a <see cref="NaviNpc"/>, handed to <see cref="NaviNpc.Received"/>. The text is whatever the player
/// typed (made one line and cut to size), so a game must not trust it.
/// </summary>
public sealed record NaviNpcMessage( NaviNpc Npc, string PlayerId, string PlayerName, long MessageId, string Text );

/// <summary>
/// A character the game plays in the Messages app. It is in every player's list like another player, but the game is the one
/// who speaks for it: nothing it says comes from the player or an app, so the game decides what is said, when, to whom, and
/// what happens to it after (<see cref="Edit"/>, <see cref="Delete"/>, <see cref="Clear"/>).
///
/// Each player has their own conversation with it: a message goes from the NPC to one player, never to all of them at once
/// (<see cref="SendToAll"/> sends one each). What the player says back arrives in <see cref="Received"/>.
///
/// Everything here runs on the host, which keeps the messages. On a client the calls do nothing (they answer 0, false or an
/// empty list), so a game can make its NPCs on every machine without asking who the host is.
/// Make NPCs with <see cref="NaviNpcs.Register"/>; they live in the game's code, so make them again on each start. The
/// messages they exchanged are saved with the others and are still there.
/// </summary>
public sealed class NaviNpc
{
	string name;
	bool online = true;
	bool hidden;

	internal NaviNpc( string key, string name )
	{
		Key = key;
		Id = NaviNpcs.Prefix + key;
		this.name = NaviMessages.CleanName( name, key );
	}

	/// <summary>
	/// The game's own name for this NPC, the one it was registered with.
	/// </summary>
	public string Key { get; }

	/// <summary>
	/// The id the Messages app knows it by. Always starts with <see cref="NaviNpcs.Prefix"/>, so it is never a player's.
	/// </summary>
	public string Id { get; }

	/// <summary>
	/// The name in the player's list and above the messages. Changes at once for everyone.
	/// </summary>
	public string Name
	{
		get => name;
		set
		{
			value = NaviMessages.CleanName( value, Key );

			if ( value == name )
				return;

			name = value;
			NaviMessages.Touch();
		}
	}

	/// <summary>
	/// The green dot. A game can make an NPC come and go, or leave it on.
	/// </summary>
	public bool Online
	{
		get => online;
		set
		{
			if ( value == online )
				return;

			online = value;
			NaviMessages.Touch();
		}
	}

	/// <summary>
	/// A hidden NPC isn't in anyone's list until it has sent them a message (<see cref="Send"/>), which is how a game makes
	/// a stranger get in touch. Unhide it to list it for everyone.
	/// </summary>
	public bool Hidden
	{
		get => hidden;
		set
		{
			if ( value == hidden )
				return;

			hidden = value;
			NaviMessages.Touch();
		}
	}

	/// <summary>
	/// A player sent this NPC a message. It comes on the host, while the player's send is being handled, so a reply made
	/// with <see cref="Send"/> is in the conversation at once; to answer after a while, wait first (<c>await GameTask.DelaySeconds</c>).
	/// Make sure to unsubscribe when what listens is destroyed.
	/// </summary>
	public event System.Action<NaviNpcMessage> Received;

	/// <summary>
	/// The NPC sends a player a message. <paramref name="at"/> is the time it shows, now if left out; it doesn't change the
	/// order, which is the order of sending. There is no limit on how many a game sends, but the text is made one line and
	/// cut to <see cref="NaviMessages.MaxText"/> characters. Returns the message's id, or 0 if nothing was sent.
	/// </summary>
	public long Send( string playerId, string text, System.DateTimeOffset? at = null ) =>
		Add( Id, playerId, text, at );

	/// <summary>
	/// The NPC sends every player in the game one message. Returns how many were sent.
	/// </summary>
	public int SendToAll( string text )
	{
		if ( !NaviNpcs.IsHost )
			return 0;

		var count = 0;
		foreach ( var player in NaviMessages.OnlinePlayerIds() )
		{
			if ( Send( player, text ) != 0 )
				count++;
		}

		return count;
	}

	/// <summary>
	/// Puts words in the player's mouth: a message in their conversation with the NPC that shows as theirs. For a scripted
	/// exchange where the player's lines are chosen by the game. It doesn't raise <see cref="Received"/>. Returns its id, or 0.
	/// </summary>
	public long SayAsPlayer( string playerId, string text, System.DateTimeOffset? at = null ) =>
		Add( playerId, Id, text, at );

	/// <summary>
	/// Changes what a message in this NPC's conversations says. Only the NPC's own conversations: a message between two
	/// players can't be reached from here. False if there is no such message or the text is empty.
	/// </summary>
	public bool Edit( long messageId, string text ) => NaviNpcs.IsHost && NaviMessages.Edit( messageId, Id, text );

	/// <summary>
	/// Takes a message out of a conversation of this NPC.
	/// </summary>
	public bool Delete( long messageId ) => NaviNpcs.IsHost && NaviMessages.Delete( messageId, Id );

	/// <summary>
	/// Takes every message out of the NPC's conversation with a player, to start it over. Returns how many were removed.
	/// </summary>
	public int Clear( string playerId ) => NaviNpcs.IsHost ? NaviMessages.ClearThread( Id, playerId ) : 0;

	/// <summary>
	/// The newest <paramref name="take"/> messages between the NPC and a player, oldest first.
	/// </summary>
	public IReadOnlyList<NaviChatMessage> GetHistory( string playerId, int take = 50 ) =>
		NaviNpcs.IsHost ? NaviMessages.GetHistory( Id, playerId, take ) : [];

	/// <summary>
	/// How many of the NPC's messages the player hasn't read yet. A game can ask it to see if they have read an answer.
	/// </summary>
	public int GetUnread( string playerId ) => NaviNpcs.IsHost ? NaviMessages.UnreadCount( playerId, Id ) : 0;

	/// <summary>
	/// Takes the NPC out of everyone's list. Its messages stay saved, and show again if it is registered again under the same
	/// key.
	/// </summary>
	public void Remove() => NaviNpcs.Remove( Key );

	long Add( string from, string to, string text, System.DateTimeOffset? at )
	{
		if ( !NaviNpcs.IsHost || string.IsNullOrEmpty( from ) || string.IsNullOrEmpty( to ) || from == to )
			return 0;

		text = NaviMessages.Clean( text );
		if ( text.Length == 0 )
			return 0;

		return NaviMessages.Add( from, to, text, at?.ToUnixTimeSeconds() ?? 0 ).Id;
	}

	/// <summary>
	/// A player's message to this NPC was taken. One handler that throws doesn't stop the others, or the player's send.
	/// </summary>
	internal void Deliver( NaviChatMessage message )
	{
		if ( Received is not { } handlers )
			return;

		var arrived = new NaviNpcMessage( this, message.From, NaviMessages.NameOf( message.From ), message.Id, message.Text );

		foreach ( var handler in handlers.GetInvocationList() )
		{
			try
			{
				((System.Action<NaviNpcMessage>)handler)( arrived );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"NaviNpc '{Key}': a Received handler threw: {e.Message}" );
			}
		}
	}
}

/// <summary>
/// The game's characters in the Messages app. See <see cref="NaviNpc"/>.
/// </summary>
public static class NaviNpcs
{
	/// <summary>
	/// What every NPC's id starts with. A player's id is a Steam id or a connection's own id, so it never does.
	/// </summary>
	public const string Prefix = "npc:";

	// Not readonly: a hot reload resets a readonly static field of only engine types to its initial value.
	static Dictionary<string, NaviNpc> npcs = new();

	/// <summary>
	/// Only the host has the messages. Offline, the player is their own host.
	/// </summary>
	internal static bool IsHost => !Networking.IsActive || Networking.IsHost;

	/// <summary>
	/// Every NPC the game has made.
	/// </summary>
	public static IReadOnlyCollection<NaviNpc> All => npcs.Values;

	/// <summary>
	/// Makes an NPC, or gives the one made earlier under the same key (its name is updated, and what listens to it stays).
	/// </summary>
	public static NaviNpc Register( string key, string name )
	{
		if ( string.IsNullOrWhiteSpace( key ) )
			throw new System.ArgumentException( "An NPC needs a key.", nameof(key) );

		key = key.Trim();

		if ( npcs.TryGetValue( key, out var npc ) )
		{
			npc.Name = name;
			return npc;
		}

		npcs[key] = npc = new NaviNpc( key, name );
		NaviMessages.Touch();
		return npc;
	}

	public static NaviNpc Get( string key ) => key is not null && npcs.TryGetValue( key.Trim(), out var npc ) ? npc : null;

	public static bool Remove( string key )
	{
		if ( key is null || !npcs.Remove( key.Trim() ) )
			return false;

		NaviMessages.Touch();
		return true;
	}

	/// <summary>
	/// Is this id an NPC's, whether it is made now or not?
	/// </summary>
	public static bool IsNpc( string id ) => id is not null && id.StartsWith( Prefix, System.StringComparison.Ordinal );

	/// <summary>
	/// The NPC an id (<see cref="NaviNpc.Id"/>) belongs to, if it is made now.
	/// </summary>
	internal static NaviNpc Find( string id ) => IsNpc( id ) ? Get( id[Prefix.Length..] ) : null;
}
