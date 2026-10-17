namespace NaviApps;

/// <summary>
/// A key on the calculator's keypad, and the class it is drawn with.
/// </summary>
public sealed record CalculatorKey( string Label, string Class );

/// <summary>
/// Everything the Calculator panel draws. Replaced whole by <see cref="CalculatorViewModel"/> on every change.
/// </summary>
public sealed record CalculatorUiState
{
	/// <summary>What a viewer draws until the host's state arrives.</summary>
	public static CalculatorUiState Empty { get; } = new();

	/// <summary>
	/// The keypad, row by row. It never changes, so it isn't part of the state that is sent.
	/// </summary>
	public static IReadOnlyList<IReadOnlyList<CalculatorKey>> Keys { get; } =
	[
		[new( "C", "function" ), new( "←", "function" )],
		[new( "7", "" ), new( "8", "" ), new( "9", "" ), new( "÷", "operator" )],
		[new( "4", "" ), new( "5", "" ), new( "6", "" ), new( "×", "operator" )],
		[new( "1", "" ), new( "2", "" ), new( "3", "" ), new( "−", "operator" )],
		[new( "0", "" ), new( ".", "" ), new( "=", "equals" ), new( "+", "operator" )],
	];

	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>The pending operation, e.g. "12 +", or empty.</summary>
	public string Expression { get; init; } = "";

	/// <summary>The number being typed, the last result, or "ERROR".</summary>
	public string Entry { get; init; } = "0";
}
