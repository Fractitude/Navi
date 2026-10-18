namespace NaviApps;

/// <summary>
/// Everything the Web Browser panel draws. Replaced whole by <see cref="WebBrowserViewModel"/> on every change.
/// </summary>
public sealed record WebBrowserUiState
{
	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>
	/// The address the web page should be showing. The panel loads it into its <c>WebPanel</c> whenever
	/// <see cref="LoadId"/> changes, and not otherwise, so a link the player follows inside the page doesn't fight it.
	/// </summary>
	public string Url { get; init; } = "";

	/// <summary>
	/// Goes up on every request to load <see cref="Url"/>, so reloading a page that is already open is a change too.
	/// </summary>
	public int LoadId { get; init; }

	/// <summary>What the address field holds.</summary>
	public string AddressText { get; init; } = "";

	public bool CanGoBack { get; init; }
	public bool CanGoForward { get; init; }

	/// <summary>A message about the last thing the player tried, e.g. an address that can't be opened. Null for none.</summary>
	public string Notice { get; init; }
}
