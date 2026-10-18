namespace NaviApps;

/// <summary>
/// What the Ticker demo's service and its window share on the host. Panels are separate instances, so they meet in the
/// app's own code, not through Navi.
/// </summary>
public static class TickerState
{
	public static int Seconds { get; set; }
}

/// <summary>
/// What the window draws. The host's copy publishes it, so every player sees the same count.
/// </summary>
public sealed record TickerUiState
{
	public static readonly TickerUiState Empty = new();

	public int Seconds { get; init; }
}
