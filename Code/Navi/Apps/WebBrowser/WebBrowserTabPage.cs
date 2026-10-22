using Sandbox.UI;

namespace NaviApps;

/// <summary>
/// One tab's web page: the engine's <c>WebPanel</c>, a browser instance of its own. The Web Browser keeps one for every
/// tab on a web page, the ones not shown too, so a tab in the background keeps its page. Loading into it and reading which
/// address it is on are done here, between it and the tab's view model. Neither is app state.
/// </summary>
public sealed class WebBrowserTabPage : WebPanel
{
	/// <summary>The tab this is the page of. Set by the markup, keyed on the tab, so a page never changes tabs.</summary>
	public WebBrowserViewModel Tab { get; set; }

	/// <summary>The <see cref="WebBrowserUiState.LoadId"/> the page was last told to load.</summary>
	int loadedId = -1;

	public override void Tick()
	{
		base.Tick();

		if ( Tab is null )
			return;

		if ( loadedId != Tab.UiState.LoadId )
		{
			loadedId = Tab.UiState.LoadId;
			Load( Tab.UiState.Url );
		}

		Tab.PageChanged( Surface?.Url, Surface?.PageTitle );
	}

	/// <summary>
	/// The engine refuses addresses it doesn't allow, and says so by throwing.
	/// </summary>
	void Load( string url )
	{
		try
		{
			Url = url;
		}
		catch ( System.Exception e )
		{
			Tab.LoadFailed( e.Message );
		}
	}
}
