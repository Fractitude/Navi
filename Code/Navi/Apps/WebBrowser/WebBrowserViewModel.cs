using System;
using Sandbox.UI;

namespace NaviApps;

/// <summary>
/// Everything one tab of the Web Browser does: each tab is one of these, with a page, a history and an address of its
/// own (<see cref="WebBrowserTabs"/>). The panel (<c>WebBrowser.razor</c>) draws <see cref="UiState"/> and hosts the engine's
/// <c>WebPanel</c>, which shows the page. The engine's web surface can't go back or forward, so the history is kept
/// here, and the panel tells this what address the page is on (<see cref="PageChanged"/>) so a followed link joins it.
/// </summary>
/// <remarks>
/// <para>
/// The address and history never leave the player's own client, and nothing from outside may set them. The page loads
/// in the player's real browser engine, on their real connection, so an address another player could choose would let
/// them send this player to any site they like, such as an IP logger. The app is Shared only for Dino Run
/// (<see cref="WebBrowserSharedViewModel"/>, run by the host's copy). This is made by the seated player's own copy,
/// whether that is the host's or a viewer's, kept in memory only, and makes no calls but, for a file address, reading the
/// computer's files (its title is shown by <see cref="WebBrowserTabs"/>, as the local title). Those are on the host only, so a viewer's copy (a client in the seat) asks the
/// host for them, which reads them as this app and answers that client alone (<c>NaviComputer.ReadRemoteFiles</c>). The
/// page is hidden while someone else uses the computer (<c>Navi.IsWatching</c>), and the only addresses opened are what this
/// player typed, a link they followed in the page, and the fixed <see cref="Home"/> and <see cref="SearchUrl"/>.
/// </para>
/// <para>
/// A local address (<see cref="LocalScheme"/>) or a file address (<see cref="FileScheme"/>) is only ever typed, or clicked
/// in the browser's own pages: when a page goes to one, the browser goes back to the page, so no site can send the
/// player to one, nor show the files of the player's real machine. Only <see cref="DinoPage"/> is told to the host's copy (that the player is on it, never the address),
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

	/// <summary>
	/// The start of an address in the computer's own files, as in a real browser: <c>file:///Pictures/angel.jpg</c> is
	/// <c>/Pictures/angel.jpg</c>. Drawn by the browser (<see cref="WebBrowserFilePage"/>), never loaded into the web page.
	/// </summary>
	public const string FileScheme = "file://";

	/// <summary>The biggest text file shown, in bytes.</summary>
	const int MaxTextBytes = 256 * 1024;

	/// <summary>The biggest picture shown, in bytes. Bigger ones are refused before they are decoded.</summary>
	const int MaxImageBytes = 32 * 1024 * 1024;

	/// <summary>
	/// The biggest picture shown on a client that isn't the host, which the host sends it: the most the host sends for one
	/// read (<c>NaviComputer.MaxRemoteFileBytes</c>).
	/// </summary>
	const int MaxRemoteImageBytes = 4 * 1024 * 1024;

	/// <summary>The files shown as pictures. Any other file is shown as text, if it is text.</summary>
	static readonly string[] ImageExtensions = ["jpg", "jpeg", "png"];

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

	/// <summary>Whether the computer is online, as the host's copy last said. Null until it has.</summary>
	bool? online;

	/// <summary>
	/// Goes up on every file address opened, so one that finishes reading after the player moved on is dropped.
	/// </summary>
	int fileLoad;

	/// <summary>
	/// The picture a <c>file://</c> page shows. Not part of the state: it is a texture, replaced whole with the page.
	/// </summary>
	public Texture Picture { get; private set; }

	public WebBrowserViewModel( Navi navi, int id )
	{
		this.navi = navi;
		Id = id;
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

		// Offline there is no page to take the title from. Back online, the page sets it again. A local or file page keeps
		// its own.
		if ( !online && !IsLocal( Current ) && !IsFile( Current ) )
			ChangeTitle( "No internet" );

		Update( uiState => uiState with { LoadId = uiState.LoadId + 1, Notice = null } );
	}

	/// <summary>
	/// What the panel draws. Replaced whole on every change, and only ever read by the panel.
	/// </summary>
	public WebBrowserUiState UiState { get; private set; }

	public int Revision => UiState.Revision;

	/// <summary>Which tab this is, unique among the browser's tabs. The panel keys the tab's page on it.</summary>
	public int Id { get; }

	string Current => history[position];

	/// <summary>
	/// The address field was submitted: open what it holds, or search for it.
	/// </summary>
	public void SubmitAddress()
	{
		if ( ToUrl( UiState.AddressText ) is not { } url )
		{
			if ( !string.IsNullOrWhiteSpace( UiState.AddressText ) )
				Update( uiState => uiState with { Notice = "Only http, https, file and browser addresses can be opened." } );

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
		if ( string.IsNullOrEmpty( url ) || url == "about:blank" || url == reportedUrl )
			return;

		reportedUrl = url;

		// A page can't send the player to a local or file address, or show the files of their real machine: back to the
		// page it came from.
		if ( IsLocal( url ) || url.StartsWith( "file:", StringComparison.OrdinalIgnoreCase ) )
		{
			loading = true;
			Update( uiState => uiState with { Url = Current, LoadId = uiState.LoadId + 1 } );
			return;
		}

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
		var file = IsFile( Current );
		loading = !local && !file && reportedUrl != Current;

		// A local or file page has no web page to take the title from.
		if ( local )
			ChangeTitle( Current == DinoPage ? "Dino Run" : Current );
		else if ( file )
			ChangeTitle( NameOf( PathOf( Current ) ) );

		Picture = null;
		fileLoad++;

		Update( uiState => uiState with
		{
			Url = Current,
			LocalPage = local ? Current : null,
			File = file ? new WebBrowserFilePage { Path = PathOf( Current ), Kind = WebBrowserFileKind.Loading } : null,
			LoadId = uiState.LoadId + 1,
			AddressText = Current,
			CanGoBack = position > 0,
			CanGoForward = position < history.Count - 1,
			Notice = null,
		} );

		// After the state above, which a read that finishes at once would otherwise be overwritten by.
		if ( file )
			_ = LoadFileAsync( Current, fileLoad );
	}

	/// <summary>
	/// An entry was clicked in a folder's <c>file://</c> page.
	/// </summary>
	public void OpenFile( string url )
	{
		if ( IsFile( url ) )
			Open( url );
	}

	/// <summary>
	/// The player dropped a file of the computer on the browser (a drag from Files): open it as a <c>file://</c> address. The
	/// path comes from the computer's drag and drop, which only the player's own drag makes. Like a typed file address it is
	/// read with this app's Files permission, so the drop grants nothing.
	/// </summary>
	public void OpenDropped( string path )
	{
		if ( path is { Length: > 0 and <= 1024 } && path[0] == '/' && !path.Any( char.IsControl ) )
			Open( FileScheme + path );
	}

	/// <summary>
	/// Read the file or folder at a file address and show it. Asks the player for Files access the first time, on the
	/// host's copy: a viewer's copy can't ask, so it says where to allow it.
	/// </summary>
	async System.Threading.Tasks.Task LoadFileAsync( string url, int load )
	{
		var path = PathOf( url );
		Texture picture = null;
		WebBrowserFilePage page;

		try
		{
			page = await ReadFileAsync( path, x => picture = x );
		}
		catch ( Navi.MissingPermissionException )
		{
			page = Error( "The browser isn't allowed to open the computer's files. Allow it Files access in the taskbar's "
				+ "\"Permissions...\"." );
		}
		catch ( Navi.FileException e )
		{
			page = Error( e.Error switch
			{
				Navi.FileError.NotFound => $"{e.Path} isn't there.",
				Navi.FileError.LinkLoop => "Those links point at each other in a loop.",
				Navi.FileError.Unavailable => "The host didn't send the computer's files. Try again from the seat.",
				_ => e.Message,
			} );
		}
		catch ( Exception e )
		{
			page = Error( e.Message );
		}

		if ( load != fileLoad )
			return;

		Picture = picture;
		Update( uiState => uiState with { File = page with { Path = path } } );
	}

	async System.Threading.Tasks.Task<WebBrowserFilePage> ReadFileAsync( string path, Action<Texture> setPicture )
	{
		var info = navi.IsViewer
			? await navi.Files.GetInfoAsync( path )
			: await navi.WithPermissionAsync( () => navi.Files.GetInfoAsync( path ) );
		if ( info is null )
			return Error( $"{path} isn't there." );

		if ( info.IsFolder )
		{
			var entries = await navi.Files.ListAsync( info.Path );
			var listed = entries
				.OrderBy( x => !x.IsFolder )
				.Select( x => new WebBrowserFileEntry( x.Name, FileScheme + x.Path, x.IsFolder, x.IsFolder ? "" : SizeText( x.Size ) ) );

			WebBrowserFileEntry[] parent = info.Path == "/" ? [] : [new( "..", FileScheme + ParentOf( info.Path ), true, "" )];
			return new WebBrowserFilePage { Kind = WebBrowserFileKind.Folder, Entries = [.. parent, .. listed] };
		}

		var dot = info.Name.LastIndexOf( '.' );
		var image = dot >= 0 && ImageExtensions.Contains( info.Name[(dot + 1)..].ToLowerInvariant() );

		var maxImage = navi.IsViewer ? MaxRemoteImageBytes : MaxImageBytes;
		if ( info.Size > (image ? maxImage : MaxTextBytes) )
			return Error( $"{info.Name} is too big to show here." );

		var bytes = await navi.Files.ReadBytesAsync( info.Path ) ?? [];

		if ( image )
		{
			using var bitmap = bytes.Length > 0 ? Bitmap.CreateFromBytes( bytes ) : null;
			if ( bitmap is not { Width: > 0, Height: > 0 } )
				return Error( $"{info.Name} isn't a picture that can be shown." );

			setPicture( bitmap.ToTexture() );
			return new WebBrowserFilePage { Kind = WebBrowserFileKind.Image, Message = $"{bitmap.Width} x {bitmap.Height} px" };
		}

		// Anything else is shown if it is text: a NUL byte near the start means it isn't.
		if ( bytes.Take( 8000 ).Contains( (byte)0 ) )
			return Error( $"{info.Name} isn't text or a picture." );

		return new WebBrowserFilePage { Kind = WebBrowserFileKind.Text, Text = System.Text.Encoding.UTF8.GetString( bytes ) };
	}

	static WebBrowserFilePage Error( string message ) => new() { Kind = WebBrowserFileKind.Error, Message = message };

	static string SizeText( long bytes ) => bytes switch
	{
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024f:0.#} KB",
		_ => $"{bytes / (1024f * 1024f):0.#} MB",
	};

	/// <summary>
	/// The page title is the tab's, and the active tab's is the window's title bar text (<see cref="WebBrowserTabs"/>).
	/// </summary>
	void ChangeTitle( string title )
	{
		title = (title ?? "").Trim();
		if ( title != UiState.Title )
			Update( uiState => uiState with { Title = title } );
	}

	/// <summary>
	/// The address to open for what the player typed: a web address as it is, a bare host name with https in front, and
	/// anything else searched for. A local address (<see cref="LocalScheme"/>) and a file address (<see cref="FileScheme"/>)
	/// stay one. Null for an address that is none of
	/// those, such as <c>javascript:</c> or <c>file:</c>.
	/// </summary>
	public static string ToUrl( string text )
	{
		text = (text ?? "").Trim();
		if ( text.Length == 0 )
			return null;

		// A file address, made one form: file:// and an absolute path, with "." and ".." worked out.
		if ( text.StartsWith( "file:", StringComparison.OrdinalIgnoreCase ) )
			return FileUrl( text["file:".Length..] );

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
	/// The file address for what follows <c>file:</c>, e.g. "///Pictures/my%20cat.png" or "/Documents/../Pictures". Escapes
	/// such as %20 are undone, back slashes count as slashes, and ".." never goes above "/".
	/// </summary>
	static string FileUrl( string rest )
	{
		var parts = new List<string>();
		foreach ( var part in Uri.UnescapeDataString( rest ).Split( '/', '\\' ) )
		{
			if ( part is "" or "." )
				continue;

			if ( part == ".." )
			{
				if ( parts.Count > 0 )
					parts.RemoveAt( parts.Count - 1 );

				continue;
			}

			parts.Add( part );
		}

		return FileScheme + "/" + string.Join( "/", parts );
	}

	/// <summary>
	/// Is <paramref name="url"/> a file address (<see cref="FileScheme"/>), which the browser draws itself from the
	/// computer's files?
	/// </summary>
	public static bool IsFile( string url ) =>
		url is not null && url.StartsWith( FileScheme, StringComparison.OrdinalIgnoreCase );

	/// <summary>The path in the computer's files a file address is for.</summary>
	static string PathOf( string url ) => url[FileScheme.Length..] is { Length: > 0 } path ? path : "/";

	static string ParentOf( string path )
	{
		var slash = path.TrimEnd( '/' ).LastIndexOf( '/' );
		return slash <= 0 ? "/" : path[..slash];
	}

	static string NameOf( string path )
	{
		var trimmed = path.TrimEnd( '/' );
		return trimmed.Length == 0 ? "/" : trimmed[(trimmed.LastIndexOf( '/' ) + 1)..];
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
