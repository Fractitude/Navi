using System.Globalization;
using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// Everything the Messages app does: which player's messages are open, what the sidebar lists, how messages are grouped and
/// worded, and which call a click becomes. The panel (<c>Messages.razor</c>) draws <see cref="UiState"/> and calls the
/// methods below.
/// </summary>
public sealed class MessagesViewModel
{
	readonly Navi navi;

	/// <summary>How often the messages' version is polled, in seconds.</summary>
	const float PollInterval = 0.25f;

	/// <summary>The most messages of a conversation the state carries. The state goes to every player at the screen.</summary>
	const int ShownMessages = 30;

	/// <summary>The most players the sidebar lists, and emoji the Recent tab keeps.</summary>
	const int ShownContacts = 40, KeptRecent = 16;

	/// <summary>Messages by the same sender this close together (seconds) share a group.</summary>
	const long GroupGap = 5 * 60;

	/// <summary>Avatar colors, picked by a player's id so they have the same one on every screen.</summary>
	static readonly string[] AvatarColors =
		["#5865f2", "#3ba55d", "#eb459e", "#faa81a", "#ed4245", "#17a2b8", "#9b59b6", "#e67e22"];

	public MessagesViewModel( Navi navi )
	{
		this.navi = navi;
		UiState = MessagesUiState.Empty;
	}

	public MessagesUiState UiState { get; private set; }
	public int Revision => UiState.Revision;

	// What the computer said.

	Navi.MessageUser me = new( "", "" );
	IReadOnlyList<Navi.MessageContact> contacts = [];
	IReadOnlyList<Navi.ChatMessage> history = [];
	int knownVersion = int.MinValue;
	long lastShownMessage;

	RealTimeSince sincePoll;
	bool polling;

	void Update( System.Func<MessagesUiState, MessagesUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };

	/// <summary>Polled by the panel each frame.</summary>
	public void Tick()
	{
		if ( polling || sincePoll < PollInterval )
			return;

		_ = PollAsync();
	}

	// The panel's controls, as actions (Navi.Act).

	public const string SelectAction = "select";
	public const string FilterAction = "filter";
	public const string DraftAction = "draft";
	public const string SendAction = "send";
	public const string TogglePickerAction = "toggle-picker";
	public const string ClosePickerAction = "close-picker";
	public const string PickerCategoryAction = "picker-category";
	public const string PickerSearchAction = "picker-search";
	public const string EmojiAction = "emoji";
	public const string AllowAction = "allow";
	public const string DismissNoticeAction = "dismiss-notice";

	/// <summary>
	/// Run an action from the panel. One that is missing its argument is ignored, like a click on nothing. Everything it
	/// does still goes through the computer's checks.
	/// </summary>
	public void Act( string action, string[] args )
	{
		var arg = args is { Length: > 0 } ? args[0] : null;

		switch ( action )
		{
			case SelectAction when !string.IsNullOrEmpty( arg ): Select( arg ); break;
			case FilterAction: SetFilter( arg ); break;
			case DraftAction: SetDraft( arg ); break;
			case SendAction: Send(); break;
			case TogglePickerAction: TogglePicker(); break;
			case ClosePickerAction: ClosePicker(); break;
			case PickerCategoryAction when MessagesEmoji.Categories.Any( x => x.Key == arg ): ChooseCategory( arg ); break;
			case PickerSearchAction: SetPickerSearch( arg ); break;
			case EmojiAction when !string.IsNullOrEmpty( arg ): InsertEmoji( arg ); break;
			case AllowAction: Allow(); break;
			case DismissNoticeAction: DismissNotice(); break;
		}
	}

	/// <summary>Open the conversation with a player.</summary>
	public void Select( string id )
	{
		if ( contacts.All( x => x.Id != id ) )
			return;

		history = [];
		lastShownMessage = 0;
		Update( uiState => RebuildState( uiState with { ActiveId = id, PickerOpen = false, ScrollKey = uiState.ScrollKey + 1 } ) );

		_ = LoadHistoryAsync( id, markRead: true );
	}

	/// <summary>Filter the sidebar. Changes the list, so it moves the revision; the search field keeps its own text.</summary>
	public void SetFilter( string text )
	{
		text = Cap( text, 40 );
		Update( uiState => RebuildState( uiState with { Filter = text } ) );
	}

	/// <summary>The message being written. Typing doesn't redraw the lists.</summary>
	public void SetDraft( string text ) => UiState = UiState with { Draft = Cap( text, Navi.MaxMessageText ) };

	/// <summary>Send the draft to the open conversation.</summary>
	public void Send()
	{
		var id = UiState.ActiveId;
		var text = UiState.Draft.Trim();

		if ( id.Length == 0 || text.Length == 0 )
			return;

		Update( uiState => uiState with { Draft = "", PickerOpen = false } );
		_ = SendAsync( id, MessagesEmoji.ReplaceShortcodes( text ) );
	}

	/// <summary>Open or close the emoji picker. It opens with an empty search: its field starts empty too.</summary>
	public void TogglePicker() => Update( uiState => uiState with { PickerOpen = !uiState.PickerOpen, PickerSearch = "" } );

	public void ClosePicker()
	{
		if ( UiState.PickerOpen )
			Update( uiState => uiState with { PickerOpen = false } );
	}

	public void ChooseCategory( string key ) =>
		Update( uiState => uiState with { PickerCategory = key, PickerSearch = "" } );

	/// <summary>Search the picker. The emoji list follows, so the revision moves; the field keeps its own text.</summary>
	public void SetPickerSearch( string text ) =>
		Update( uiState => uiState with { PickerSearch = Cap( text, 24 ) } );

	/// <summary>Put an emoji at the end of the draft, and first in Recent.</summary>
	public void InsertEmoji( string emoji )
	{
		// Only emoji the picker offers: an action is what a player could have clicked, not text to trust.
		if ( MessagesEmoji.All.All( x => x.Char != emoji ) )
			return;

		Update( uiState => uiState with
		{
			Draft = Cap( uiState.Draft + emoji, Navi.MaxMessageText ),
			Recent = uiState.Recent.Where( x => x != emoji ).Prepend( emoji ).Take( KeptRecent ).ToList(),
		} );
	}

	/// <summary>Ask the player for the permission to read messages.</summary>
	public void Allow() => _ = AllowAsync();

	public void DismissNotice()
	{
		if ( UiState.Notice is not null )
			Update( uiState => uiState with { Notice = null } );
	}

	// The work behind the events.

	async Task PollAsync()
	{
		polling = true;
		sincePoll = 0;

		try
		{
			// Messages travel over the network. Offline there is nothing to fetch; back online everything is fetched again.
			var online = await navi.Network.IsOnlineAsync();
			if ( online == UiState.Offline )
			{
				knownVersion = int.MinValue;
				Update( uiState => uiState with { Offline = !online, Notice = null } );
			}

			if ( !online )
				return;

			var version = await navi.Messages.GetVersionAsync();

			// What is on screen is what the player has read: while the window is in front, an open conversation is read.
			if ( version != knownVersion || (navi.HasInput && UiState.HasActive && UnreadOf( UiState.ActiveId ) > 0) )
			{
				knownVersion = version;
				await RefreshAsync();
			}

			if ( UiState.NeedsPermission )
				Update( uiState => uiState with { NeedsPermission = false } );
		}
		catch ( Navi.MissingPermissionException )
		{
			if ( !UiState.NeedsPermission )
				Update( uiState => uiState with { NeedsPermission = true } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Messages: couldn't read the messages: {e.Message}" );
		}
		finally
		{
			polling = false;
		}
	}

	int UnreadOf( string id ) => contacts.FirstOrDefault( x => x.Id == id )?.Unread ?? 0;

	async Task RefreshAsync()
	{
		me = await navi.Messages.GetMeAsync();
		contacts = me.IsNobody ? [] : await navi.Messages.ListContactsAsync();

		// A different player in the seat starts from nothing; so does a conversation with someone who isn't listed.
		if ( UiState.SelfId != me.Id || (UiState.HasActive && contacts.All( x => x.Id != UiState.ActiveId )) )
		{
			history = [];
			lastShownMessage = 0;
			Update( uiState => RebuildState( uiState with { ActiveId = "", Draft = "", PickerOpen = false } ) );
			return;
		}

		if ( UiState.HasActive )
			await LoadHistoryAsync( UiState.ActiveId, markRead: navi.HasInput );
		else
			Update( uiState => RebuildState( uiState ) );
	}

	async Task LoadHistoryAsync( string id, bool markRead )
	{
		try
		{
			var messages = await navi.Messages.GetHistoryAsync( id, ShownMessages );

			// The player moved on to someone else while the answer was on its way.
			if ( id != UiState.ActiveId )
				return;

			history = messages;

			if ( markRead && UnreadOf( id ) > 0 )
			{
				await navi.Messages.MarkReadAsync( id );
				knownVersion = int.MinValue;
			}

			var newest = messages.Count > 0 ? messages[^1].Id : 0;
			var scroll = newest != lastShownMessage;
			lastShownMessage = newest;

			Update( uiState => RebuildState( scroll ? uiState with { ScrollKey = uiState.ScrollKey + 1 } : uiState ) );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Messages: couldn't read the conversation: {e.Message}" );
		}
	}

	async Task SendAsync( string id, string text )
	{
		try
		{
			if ( await navi.Messages.SendAsync( id, text ) )
			{
				knownVersion = int.MinValue;
				return;
			}

			Update( uiState => uiState with { Notice = "That message wasn't sent. You may be sending too fast." } );
		}
		catch ( Navi.MissingPermissionException )
		{
			Update( uiState => uiState with { NeedsPermission = true } );
		}
		catch ( Navi.NoNetworkException )
		{
			Update( uiState => uiState with { Offline = true } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Messages: couldn't send: {e.Message}" );
			Update( uiState => uiState with { Notice = "That message wasn't sent." } );
		}
	}

	async Task AllowAsync()
	{
		try
		{
			var request = await navi.Permissions.RequestAsync( Navi.Permission.Messages );
			if ( request.Granted.Contains( Navi.Permission.Messages ) )
				knownVersion = int.MinValue;
			else
				Update( uiState => uiState with { Notice = "Messages can't work without that permission." } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Messages: couldn't ask for the permission: {e.Message}" );
		}
	}

	// Rows from data.

	/// <summary>
	/// The state with everything drawn from the data rebuilt: the sidebar, the open conversation and who is signed in.
	/// </summary>
	MessagesUiState RebuildState( MessagesUiState uiState )
	{
		var active = contacts.FirstOrDefault( x => x.Id == uiState.ActiveId );

		return uiState with
		{
			SignedIn = !me.IsNobody,
			SelfId = me.Id,
			SelfName = me.Name,
			SelfInitial = InitialOf( me.Name ),
			SelfColor = ColorOf( me.Id ),
			Contacts = BuildContacts( uiState.ActiveId, uiState.Filter ),
			ContactsEmptyText = EmptyContactsText( uiState.Filter ),
			ActiveId = active?.Id ?? "",
			ActiveName = active?.Name ?? "",
			ActiveInitial = InitialOf( active?.Name ),
			ActiveColor = ColorOf( active?.Id ),
			ActiveOnline = active?.Online ?? false,
			Groups = active is null ? [] : BuildGroups( active ),
		};
	}

	string EmptyContactsText( string filter )
	{
		if ( me.IsNobody )
			return "";

		return contacts.Count == 0
			? "No other players yet. Anyone who joins this game shows up here."
			: filter.Length > 0 ? "No one matches that." : "";
	}

	/// <summary>
	/// The sidebar: players with messages, newest first, then the ones in the game, then the rest by name.
	/// </summary>
	IReadOnlyList<ContactRow> BuildContacts( string activeId, string filter )
	{
		filter = filter?.Trim() ?? "";

		return contacts
			.Where( x => filter.Length == 0 || x.Name.Contains( filter, System.StringComparison.OrdinalIgnoreCase ) )
			.OrderByDescending( x => x.LastAt.ToUnixTimeSeconds() )
			.ThenByDescending( x => x.Online )
			.ThenBy( x => x.Name, System.StringComparer.OrdinalIgnoreCase )
			.Take( ShownContacts )
			.Select( x => new ContactRow
			{
				Id = x.Id,
				Name = x.Name,
				Initial = InitialOf( x.Name ),
				Color = ColorOf( x.Id ),
				Online = x.Online,
				Preview = x.LastAt.ToUnixTimeSeconds() > 0 ? (x.LastMine ? "You: " : "") + Cap( x.LastText, 40 ) : "",
				Unread = x.Unread,
				Active = x.Id == activeId,
			} )
			.ToList();
	}

	/// <summary>
	/// The open conversation as groups: a new one when the sender changes or the pause is long.
	/// </summary>
	IReadOnlyList<MessageGroup> BuildGroups( Navi.MessageContact peer )
	{
		var groups = new List<MessageGroup>();
		var lines = new List<MessageLine>();
		Navi.ChatMessage first = null, previous = null;

		void Close()
		{
			if ( first is null )
				return;

			var mine = first.Mine;
			groups.Add( new MessageGroup
			{
				SenderId = mine ? me.Id : peer.Id,
				SenderName = mine ? me.Name : peer.Name,
				Initial = InitialOf( mine ? me.Name : peer.Name ),
				Color = ColorOf( mine ? me.Id : peer.Id ),
				Time = TimeLabel( first.At ),
				Lines = lines.ToList(),
			} );

			lines.Clear();
		}

		foreach ( var message in history )
		{
			if ( previous is null || previous.Mine != message.Mine
				|| message.At.ToUnixTimeSeconds() - previous.At.ToUnixTimeSeconds() > GroupGap )
			{
				Close();
				first = message;
			}

			lines.Add( new MessageLine( message.Text, MessagesEmoji.IsJumbo( message.Text ) ) );
			previous = message;
		}

		Close();
		return groups;
	}

	/// <summary>"Today at 14:05", "Yesterday at 09:30", or the date.</summary>
	static string TimeLabel( System.DateTimeOffset at )
	{
		var local = at.ToLocalTime();
		var today = System.DateTimeOffset.Now.Date;
		var time = local.ToString( "HH:mm", CultureInfo.InvariantCulture );

		if ( local.Date == today )
			return $"Today at {time}";

		if ( local.Date == today.AddDays( -1 ) )
			return $"Yesterday at {time}";

		return $"{local.ToString( "yyyy-MM-dd", CultureInfo.InvariantCulture )} {time}";
	}

	static string InitialOf( string name )
	{
		name = name?.Trim();
		return string.IsNullOrEmpty( name ) ? "?" : char.ToUpperInvariant( name[0] ).ToString();
	}

	static string ColorOf( string id )
	{
		if ( string.IsNullOrEmpty( id ) )
			return AvatarColors[0];

		// Not string.GetHashCode: that differs from one run to the next.
		var sum = 0;
		foreach ( var c in id )
			sum = unchecked(sum * 31 + c);

		return AvatarColors[(sum & 0x7fffffff) % AvatarColors.Length];
	}

	static string Cap( string text, int max )
	{
		text ??= "";
		if ( text.Length <= max )
			return text;

		var end = char.IsHighSurrogate( text[max - 1] ) ? max - 1 : max;
		return text[..end];
	}
}
