namespace NaviApps;

/// <summary>
/// A player in the sidebar, as the row shows them: already named, colored, previewed and counted.
/// </summary>
public sealed record ContactRow
{
	public string Id { get; init; } = "";
	public string Name { get; init; } = "";

	/// <summary>The letter on the avatar until the picture loads.</summary>
	public string Initial { get; init; } = "";

	/// <summary>The avatar's background, as a CSS color.</summary>
	public string Color { get; init; } = "";

	public bool Online { get; init; }

	/// <summary>The newest message, cut short and prefixed "You: " if the player sent it. Empty if there is none.</summary>
	public string Preview { get; init; } = "";

	public int Unread { get; init; }
	public bool Active { get; init; }
}

/// <summary>One message of a group.</summary>
public sealed record MessageLine( string Text, bool Jumbo );

/// <summary>
/// Messages by one sender in a row, like Discord's: one avatar, name and time, then their lines.
/// </summary>
public sealed record MessageGroup
{
	public string SenderId { get; init; } = "";
	public string SenderName { get; init; } = "";
	public string Initial { get; init; } = "";
	public string Color { get; init; } = "";

	/// <summary>When the group's first message was sent, already formatted.</summary>
	public string Time { get; init; } = "";

	public IReadOnlyList<MessageLine> Lines { get; init; } = [];
}

/// <summary>
/// Everything the Messages panel draws. The draft and the search texts are here too, so everyone at the screen sees what is
/// being typed, but they change without moving <see cref="Revision"/>, so typing doesn't redraw the lists.
/// </summary>
public sealed record MessagesUiState
{
	public static readonly MessagesUiState Empty = new();

	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>The app lacks the permission to read messages: the panel offers to ask.</summary>
	public bool NeedsPermission { get; init; }

	/// <summary>Somebody is in the seat. Without them there are no messages to show.</summary>
	public bool SignedIn { get; init; }

	public string SelfName { get; init; } = "";
	public string SelfInitial { get; init; } = "";
	public string SelfColor { get; init; } = "";
	public string SelfId { get; init; } = "";

	public IReadOnlyList<ContactRow> Contacts { get; init; } = [];

	/// <summary>Why the sidebar is empty: nobody else yet, or nobody matches the search.</summary>
	public string ContactsEmptyText { get; init; } = "";

	/// <summary>The conversation that is open, or empty.</summary>
	public string ActiveId { get; init; } = "";

	public string ActiveName { get; init; } = "";
	public string ActiveInitial { get; init; } = "";
	public string ActiveColor { get; init; } = "";
	public bool ActiveOnline { get; init; }

	public IReadOnlyList<MessageGroup> Groups { get; init; } = [];

	/// <summary>Goes up when the conversation changes or a message arrives, so the panel scrolls to the newest.</summary>
	public int ScrollKey { get; init; }

	public bool HasActive => ActiveId.Length > 0;

	/// <summary>The message being written. Changes without a new <see cref="Revision"/>.</summary>
	public string Draft { get; init; } = "";

	/// <summary>The text the sidebar is filtered by.</summary>
	public string Filter { get; init; } = "";

	public bool PickerOpen { get; init; }
	public string PickerCategory { get; init; } = "smileys";
	public string PickerSearch { get; init; } = "";

	/// <summary>The emoji the player picked last, newest first.</summary>
	public IReadOnlyList<string> Recent { get; init; } = [];

	/// <summary>A message about the last action, e.g. a send that didn't go. Click to dismiss.</summary>
	public string Notice { get; init; }
}
