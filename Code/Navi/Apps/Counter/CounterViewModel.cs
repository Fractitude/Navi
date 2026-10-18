namespace NaviApps;

/// <summary>
/// What the Counter panel draws. Replaced whole by <see cref="CounterViewModel"/> on every change.
/// </summary>
public sealed record CounterUiState
{
	/// <summary>What a viewer draws until the host's state arrives.</summary>
	public static CounterUiState Empty { get; } = new();

	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	public int Count { get; init; }
}

/// <summary>
/// Everything the Counter does. The panel's buttons are actions (Navi.Act) that end up in <see cref="Act"/>.
/// </summary>
public sealed class CounterViewModel
{
	public const string IncrementAction = "increment";
	public const string DecrementAction = "decrement";

	public CounterUiState UiState { get; private set; } = new();

	public void Act( string action, string[] args )
	{
		switch ( action )
		{
			case IncrementAction: Add( 1 ); break;
			case DecrementAction: Add( -1 ); break;
		}
	}

	void Add( int amount ) => UiState = UiState with { Revision = UiState.Revision + 1, Count = UiState.Count + amount };
}
