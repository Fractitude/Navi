namespace NaviApps;

/// <summary>
/// The Web Browser's tabs. Each is a browser of its own (<see cref="WebBrowserViewModel"/>), with a page, a history and an
/// address of its own, and its own <c>WebPanel</c> in the panel, which keeps loading while another tab is shown.
/// </summary>
/// <remarks>
/// The tabs are browsing, so they are the seated player's own, like everything in <see cref="WebBrowserViewModel"/>: made
/// on their client, kept in memory only, and never published. Other players don't see the tab bar, how many tabs there
/// are or what is in them. Only the active tab's title leaves this class, as the local title, which only this client sees.
/// </remarks>
public sealed class WebBrowserTabs
{
	/// <summary>The most tabs open at once. Each is a web page of the player's real browser engine.</summary>
	public const int MaxTabs = 8;

	/// <summary>The most a window title shows (see <c>Navi.Window.SetTitleAsync</c>).</summary>
	const int MaxTitle = 32;

	/// <summary>The computer, as this app. Every tab makes its calls through it.</summary>
	readonly Navi navi;

	readonly List<WebBrowserViewModel> tabs = [];

	/// <summary>The last <see cref="WebBrowserViewModel.Id"/> given out.</summary>
	int lastId;

	/// <summary>Goes up when a tab is opened, closed or picked. The tabs' own changes are their revisions.</summary>
	int revision;

	/// <summary>The title last put in the title bar.</summary>
	string shownTitle;

	public WebBrowserTabs( Navi navi )
	{
		this.navi = navi;
		Add();
	}

	/// <summary>The open tabs, in the order the tab bar shows them. Never empty.</summary>
	public IReadOnlyList<WebBrowserViewModel> All => tabs;

	/// <summary>The tab shown, whose toolbar and page the panel draws.</summary>
	public WebBrowserViewModel Active { get; private set; }

	public bool CanAdd => tabs.Count < MaxTabs;

	/// <summary>The only tab can't be closed: the browser always has one.</summary>
	public bool CanClose => tabs.Count > 1;

	/// <summary>
	/// Changes whenever anything a tab draws changes, the tab bar included. The panel's BuildHash is this.
	/// </summary>
	public int Revision
	{
		get
		{
			var hash = new System.HashCode();
			hash.Add( revision );

			foreach ( var tab in tabs )
				hash.Add( tab.Revision );

			return hash.ToHashCode();
		}
	}

	/// <summary>
	/// Open a tab on the home page, after the others, and show it.
	/// </summary>
	public void NewTab()
	{
		if ( CanAdd )
			Add();
	}

	void Add()
	{
		var tab = new WebBrowserViewModel( navi, ++lastId );
		tabs.Add( tab );
		Active = tab;
		revision++;
	}

	/// <summary>
	/// Close a tab and its page. Closing the one shown shows the tab that took its place, or the last one.
	/// </summary>
	public void Close( WebBrowserViewModel tab )
	{
		var index = tabs.IndexOf( tab );
		if ( index < 0 || !CanClose )
			return;

		tabs.RemoveAt( index );
		if ( Active == tab )
			Active = tabs[System.Math.Min( index, tabs.Count - 1 )];

		revision++;
	}

	/// <summary>
	/// Put a tab at another place in the tab bar, the tabs between moving up one. The player dragged it there.
	/// </summary>
	public void Move( WebBrowserViewModel tab, int to )
	{
		var from = tabs.IndexOf( tab );
		if ( from < 0 || to < 0 || to >= tabs.Count || to == from )
			return;

		tabs.RemoveAt( from );
		tabs.Insert( to, tab );
		revision++;
	}

	public void Select( WebBrowserViewModel tab )
	{
		if ( tab == Active || !tabs.Contains( tab ) )
			return;

		Active = tab;
		revision++;
	}

	/// <summary>
	/// The computer is online or offline, as the host's copy publishes it. Every tab hears it; see
	/// <see cref="WebBrowserViewModel.NetworkChanged"/>.
	/// </summary>
	public void NetworkChanged( bool online )
	{
		foreach ( var tab in tabs )
			tab.NetworkChanged( online );
	}

	/// <summary>
	/// The active tab's title is the window's title bar text, and the app's name only while it has none (the desktop lets
	/// a pre-packaged app's title stand alone). It stays on this client, like the address: other players see "Web
	/// Browser" (<c>Navi.Window.SetLocalTitleAsync</c>). Called every frame; it does nothing unless the title changed.
	/// </summary>
	public void ShowTitle()
	{
		var title = Active.UiState.Title;
		if ( title == shownTitle )
			return;

		shownTitle = title;
		_ = navi.Window.SetLocalTitleAsync( title.Length > MaxTitle ? title[..MaxTitle] : title );
	}
}
