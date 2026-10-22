using System;
using Sandbox.UI;

namespace NaviApps;

/// <summary>
/// Everything the Web Browser does. The panel (<c>WebBrowser.razor</c>) draws <see cref="UiState"/> and hosts the engine's
/// <c>WebPanel</c>, which shows the page. The engine's web surface can't go back or forward, so the history is kept
/// here, and the panel tells this what address the page is on (<see cref="PageChanged"/>) so a followed link joins it.
/// </summary>
/// <remarks>
/// <para>
/// The address and history never leave the player's own client, and nothing from outside may set them. The page loads
/// in the player's real browser engine, on their real connection, so an address another player could choose would let
/// them send this player to any site they like, such as an IP logger. The app is Shared only for Dino Run
/// (<see cref="WebBrowserSharedViewModel"/>, run by the host's copy). This is made by the seated player's own copy,
/// whether that is the host's or a viewer's, kept in memory only, and makes no calls but the local title. The page is
/// hidden while someone else uses the computer (<c>Navi.IsWatching</c>), and the only addresses opened are what this
/// player typed, a link they followed in the page, and the fixed <see cref="Home"/> and <see cref="SearchUrl"/>.
/// </para>
/// <para>
/// A local address (<see cref="LocalScheme"/>) is only ever typed: one a page reports is ignored, so no site can send the
/// player to one. Only <see cref="DinoPage"/> is told to the host's copy (that the player is on it, never the address),
/// because the game is shared and everyone near the screen sees it.
/// </para>
/// <para>
/// Never publish the address, the history or the page title (put them in <see cref="WebBrowserSharedState"/>, or set the
/// synced title), and never take an address from an action, a launch argument, a file or the host.
/// </para>
/// </remarks>
public sealed class WebBrowserViewModel
{
	/// <summary>The page a new browser opens, and the one the Home button goes to.</summary>
	public const string Home = "https://duckduckgo.com/";

	/// <summary>Where an address field entry that isn't an address is searched.</summary>
	const string SearchUrl = "https://duckduckgo.com/?q=";

	/// <summary>
	/// The start of a local address: a page the browser draws itself, never loaded into the web page. See
	/// <see cref="IsLocal"/>.
	/// </summary>
	public const string LocalScheme = "browser://";

	/// <summary>Dino Run, without the offline notice.</summary>
	public const string DinoPage = LocalScheme + "dino";

	/// <summary>The most a local address holds after <see cref="LocalScheme"/>.</summary>
	const int MaxLocalName = 64;

	/// <summary>The most a window title shows (see <c>Navi.Window.SetTitleAsync</c>).</summary>
	const int MaxTitle = 32;

	/// <summary>The computer, as this app. Every call is made through it.</summary>
	readonly Navi navi;

	/// <summary>The pages the player has been to, oldest first, and which one is open.</summary>
	readonly List<string> history = [Home];

	int position;

	/// <summary>The address the page last reported, to tell a change from the same thing seen again.</summary>
	string reportedUrl;

	/// <summary>
	/// A load was asked for and the page hasn't reported a different address yet. The first different one is then where
	/// the request ended up (a redirect, say), not a link the player followed.
	/// </summary>
	bool loading;

	string reportedTitle;

	/// <summary>Whether the computer is online, as the host's copy last said. Null until it has.</summary>
	bool? online;

	public WebBrowserViewModel( Navi navi )
	{
		this.navi = navi;
		UiState = new WebBrowserUiState { Url = Home, LoadId = 1, AddressText = Home };
	}

	/// <summary>
	/// The computer is online or offline, as the host's copy publishes it (<see cref="WebBrowserSharedState.Online"/>).
	/// Called every frame; it does nothing unless it changed. Back online, the open page loads again.
	/// </summary>
	public void NetworkChanged( bool online )
	{
		if ( this.online == online )
			return;

		this.online = online;

		// Offline there is no page to take the title from. Back online, the page sets it again. A local page keeps its own.
		if ( !online && !IsLocal( Current ) )
			ChangeTitle( "No internet" );

		Update( uiState => uiState with { LoadId = uiState.LoadId + 1, Notice = null } );
	}

	/// <summary>
	/// What the panel draws. Replaced whole on every change, and only ever read by the panel.
	/// </summary>
	public WebBrowserUiState UiState { get; private set; }

	public int Revision => UiState.Revision;

	string Current => history[position];

	/// <summary>
	/// The address field was submitted: open what it holds, or search for it.
	/// </summary>
	public void SubmitAddress()
	{
		if ( ToUrl( UiState.AddressText ) is not { } url )
		{
			if ( !string.IsNullOrWhiteSpace( UiState.AddressText ) )
				Update( uiState => uiState with { Notice = "Only http, https and browser addresses can be opened." } );

			return;
		}

		Open( url );
	}

	/// <summary>
	/// The address field reports an edit. Typing doesn't redraw the toolbar, so it isn't a new revision: the field already
	/// holds the text, and the panel pushes the same text back on the next rebuild.
	/// </summary>
	public void SetAddressText( string text ) => UiState = UiState with { AddressText = text ?? "" };

	public void GoBack() => Move( -1 );

	public void GoForward() => Move( 1 );

	public void GoHome() => Open( Home );

	/// <summary>
	/// Load the open page again.
	/// </summary>
	public void Reload() =>
		Update( uiState => uiState with { Url = Current, LoadId = uiState.LoadId + 1, Notice = null } );

	public void DismissNotice() => Update( uiState => uiState with { Notice = null } );

	/// <summary>
	/// The page couldn't be loaded, e.g. the engine refused the address. The panel reports it.
	/// </summary>
	public void LoadFailed( string reason ) =>
		Update( uiState => uiState with { Notice = $"Couldn't open this page: {reason}" } );

	/// <summary>
	/// The panel's web page is on <paramref name="url"/> and titled <paramref name="title"/>. Called every frame; it does
	/// nothing unless one of them changed. An address that isn't the one asked for is a link the player followed, which
	/// joins the history.
	/// </summary>
	public void PageChanged( string url, string title )
	{
		ChangeTitle( title );

		// The page has none until it starts loading, and about:blank is the engine's own page.
		// A page can't send the player to a local address: only one they type opens.
		if ( string.IsNullOrEmpty( url ) || url == "about:blank" || url == reportedUrl || IsLocal( url ) )
			return;

		reportedUrl = url;

		if ( url == Current )
		{
			loading = false;
			return;
		}

		if ( loading )
		{
			// Where our own request ended up, such as a redirect to https: the page the player is on, not a new one.
			history[position] = url;
			loading = false;
		}
		else
		{
			history.RemoveRange( position + 1, history.Count - position - 1 );
			history.Add( url );
			position++;
		}

		// The page isn't loaded again for this (the load id stays), but a page made again later, when the player sits back
		// down, opens where they were.
		Update( uiState => uiState with
		{
			Url = url,
			AddressText = url,
			CanGoBack = position > 0,
			CanGoForward = false,
			Notice = null,
		} );
	}

	/// <summary>
	/// Open a new page after the open one, forgetting the pages that were forward of it.
	/// </summary>
	void Open( string url )
	{
		history.RemoveRange( position + 1, history.Count - position - 1 );
		history.Add( url );
		position++;

		Load();
	}

	void Move( int step )
	{
		var target = position + step;
		if ( target < 0 || target >= history.Count )
			return;

		position = target;
		Load();
	}

	/// <summary>
	/// Show the open page: ask the panel to load it and put its address in the field.
	/// </summary>
	void Load()
	{
		var local = IsLocal( Current );
		loading = !local && reportedUrl != Current;

		// A local page has no web page to take the title from.
		if ( local )
			ChangeTitle( Current == DinoPage ? "Dino Run" : Current );

		Update( uiState => uiState with
		{
			Url = Current,
			LocalPage = local ? Current : null,
			LoadId = uiState.LoadId + 1,
			AddressText = Current,
			CanGoBack = position > 0,
			CanGoForward = position < history.Count - 1,
			Notice = null,
		} );
	}

	/// <summary>
	/// The page title is the window's title bar text, and the app's name only while the page has none (the desktop lets a
	/// pre-packaged app's title stand alone). It isn't drawn by the panel, so it isn't state. It stays on this client, like
	/// the address: other players see "Web Browser" (<c>Navi.Window.SetLocalTitleAsync</c>).
	/// </summary>
	void ChangeTitle( string title )
	{
		title = (title ?? "").Trim();
		if ( title == reportedTitle )
			return;

		reportedTitle = title;
		_ = navi.Window.SetLocalTitleAsync( title.Length > MaxTitle ? title[..MaxTitle] : title );
	}

	/// <summary>
	/// The address to open for what the player typed: a web address as it is, a bare host name with https in front, and
	/// anything else searched for. A local address (<see cref="LocalScheme"/>) stays one. Null for an address that is none of
	/// those, such as <c>javascript:</c> or <c>file:</c>.
	/// </summary>
	public static string ToUrl( string text )
	{
		text = (text ?? "").Trim();
		if ( text.Length == 0 )
			return null;

		// A local address, made one form: lowercase, without a trailing slash.
		if ( text.StartsWith( LocalScheme, StringComparison.OrdinalIgnoreCase ) )
		{
			var name = text[LocalScheme.Length..].Trim( '/' ).ToLowerInvariant();
			return name.Length is > 0 and <= MaxLocalName && !name.Any( char.IsWhiteSpace ) ? LocalScheme + name : null;
		}

		if ( text.Contains( "://" ) )
		{
			return Uri.TryCreate( text, UriKind.Absolute, out var uri ) && uri.Scheme is "http" or "https"
				? uri.AbsoluteUri
				: null;
		}

		// A bare host name, such as "sbox.game/news": one word with a dot (or localhost) that parses as a host.
		var looksLikeHost = !text.Any( char.IsWhiteSpace ) && (text.Contains( '.' ) || text.StartsWith( "localhost" ));
		var scheme = text.StartsWith( "localhost" ) ? "http://" : "https://";
		if ( looksLikeHost && Uri.TryCreate( scheme + text, UriKind.Absolute, out var host ) )
			return host.AbsoluteUri;

		return SearchUrl + Uri.EscapeDataString( text );
	}

	/// <summary>
	/// Is <paramref name="url"/> a local address (<see cref="LocalScheme"/>), which the browser draws itself?
	/// </summary>
	public static bool IsLocal( string url ) =>
		url is not null && url.StartsWith( LocalScheme, StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The only setter of <see cref="UiState"/>, and the only place <see cref="Revision"/> moves.
	/// </summary>
	void Update( System.Func<WebBrowserUiState, WebBrowserUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };
}
