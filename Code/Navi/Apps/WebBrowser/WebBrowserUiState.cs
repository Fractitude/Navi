namespace NaviApps;

/// <summary>
/// What the Web Browser draws for the player browsing, on their own client only. Replaced whole by
/// <see cref="WebBrowserViewModel"/> on every change. Never published: see the remarks there.
/// </summary>
public sealed record WebBrowserUiState
{
	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>
	/// The address the web page should be showing. The panel loads it into its <c>WebPanel</c> whenever
	/// <see cref="LoadId"/> changes, and not otherwise, so a link the player follows inside the page doesn't fight it. A
	/// followed link changes it too, without a new load, so a page made again (the player sat back down) opens there.
	/// </summary>
	public string Url { get; init; } = "";

	/// <summary>
	/// Goes up on every request to load <see cref="Url"/>, so reloading a page that is already open is a change too.
	/// </summary>
	public int LoadId { get; init; }

	/// <summary>
	/// The local address (<c>browser://</c>) the browser draws instead of the web page, or null on a web page.
	/// </summary>
	public string LocalPage { get; init; }

	/// <summary>
	/// The computer's file or folder at a <c>file://</c> address, drawn instead of the web page, or null on any other page.
	/// </summary>
	public WebBrowserFilePage File { get; init; }

	/// <summary>
	/// The page title, or what the browser calls its own page ("Dino Run", a file's name). Empty while the page has none.
	/// The tab shows it, and the active tab's is the window's title.
	/// </summary>
	public string Title { get; init; } = "";

	/// <summary>What the address field holds.</summary>
	public string AddressText { get; init; } = "";

	public bool CanGoBack { get; init; }
	public bool CanGoForward { get; init; }

	/// <summary>A message about the last thing the player tried, e.g. an address that can't be opened. Null for none.</summary>
	public string Notice { get; init; }
}

/// <summary>
/// What every player near the screen sees of the Web Browser: whether the computer is online, and the offline game. The
/// host's copy publishes it (<see cref="WebBrowserSharedViewModel"/>). Nothing about browsing is here.
/// </summary>
public sealed record WebBrowserSharedState
{
	public static WebBrowserSharedState Empty { get; } = new();

	/// <summary>
	/// Whether the computer is online, once it has said. Until then, and while it is offline, no page is drawn: a computer
	/// without a network card can't reach the web.
	/// </summary>
	public bool? Online { get; init; }

	/// <summary>
	/// While offline, why and what to do about it (<see cref="Navi.NetworkStatus.OfflineHint"/>), or null if not known yet.
	/// </summary>
	public string OfflineHint { get; init; }

	/// <summary>
	/// The seated player opened <c>browser://dino</c>, so everyone sees the game without the offline notice. Set only by
	/// their action (<see cref="WebBrowserSharedViewModel.ShowAction"/>); it never moves their own page.
	/// </summary>
	public bool DinoShown { get; init; }

	/// <summary>Dino Run, played while the computer is offline or the seated player opened it.</summary>
	public DinoRunFrame Dino { get; init; } = DinoRunFrame.Empty;
}

public enum WebBrowserFileKind
{
	Loading,
	Text,
	Image,
	Folder,

	/// <summary>It couldn't be shown: <see cref="WebBrowserFilePage.Message"/> says why.</summary>
	Error,
}

/// <summary>
/// One entry of a folder's listing. <see cref="Url"/> is its <c>file://</c> address.
/// </summary>
public sealed record WebBrowserFileEntry( string Name, string Url, bool IsFolder, string SizeText );

/// <summary>
/// A <c>file://</c> page: a text file's text, a picture (the texture is <c>WebBrowserViewModel.Picture</c>), a folder's
/// listing, or why it can't be shown.
/// </summary>
public sealed record WebBrowserFilePage
{
	/// <summary>The path in the computer's files, e.g. "/Pictures/angel.jpg".</summary>
	public string Path { get; init; } = "/";

	public WebBrowserFileKind Kind { get; init; }

	/// <summary>A text file's text.</summary>
	public string Text { get; init; } = "";

	/// <summary>A folder's entries, its parent ("..") first.</summary>
	public IReadOnlyList<WebBrowserFileEntry> Entries { get; init; } = [];

	/// <summary>Why it can't be shown, or a picture's size.</summary>
	public string Message { get; init; } = "";
}
