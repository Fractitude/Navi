namespace NaviApps;

/// <summary>
/// What the Ticker demo's service and its window share on the host. Panels are separate instances, so they meet in the
/// app's own code, not through Navi.
/// </summary>
public static class TickerState
{
	public static int Seconds { get; set; }

	/// <summary>
	/// The host's copies of the Ticker windows, oldest first. They number themselves from their place in it.
	/// </summary>
	public static List<TickerWindow> Windows { get; private set; } = new();

	/// <summary>
	/// The ids of the windows the .napp lists, in the order they are opened.
	/// </summary>
	public static readonly string[] WindowIds = ["log", "log2"];
}

/// <summary>
/// What the window draws. The host's copy publishes it, so every player sees the same count.
/// </summary>
public sealed record TickerUiState
{
	public static readonly TickerUiState Empty = new();

	public int Seconds { get; init; }

	/// <summary>
	/// Every window the app has is open, so another can't be.
	/// </summary>
	public bool AllOpen { get; init; }
}
