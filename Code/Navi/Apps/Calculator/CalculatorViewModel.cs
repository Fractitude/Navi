namespace NaviApps;

/// <summary>
/// Everything the Calculator does. The panel (<c>Calculator.razor</c>) draws <see cref="UiState"/>, and its keys are
/// actions (<c>Navi.Act</c>) that end up in <see cref="Act"/>. It makes no calls to the computer.
/// </summary>
public sealed class CalculatorViewModel
{
	const int MaxDigits = 12;

	/// <summary>
	/// The panel's one action: a key was pressed. Its argument is the key's label.
	/// </summary>
	public const string PressAction = "press";

	/// <summary>
	/// The number being typed, or the last result.
	/// </summary>
	string entry = "0";

	/// <summary>
	/// Left-hand side of the pending operation.
	/// </summary>
	double? accumulator;

	string pendingOperator;

	/// <summary>
	/// True when the next digit starts a new number instead of extending <see cref="entry"/>.
	/// </summary>
	bool startNewEntry = true;

	bool hasError;

	/// <summary>
	/// What the panel draws. Replaced whole on every change, and only ever read by the panel.
	/// </summary>
	public CalculatorUiState UiState { get; private set; } = new();

	/// <summary>
	/// Run an action from the panel. A key that isn't on the keypad is ignored.
	/// </summary>
	public void Act( string action, string[] args )
	{
		if ( action == PressAction && args is { Length: > 0 } && CalculatorUiState.Keys.Any( row => row.Any( x => x.Label == args[0] ) ) )
			Press( args[0] );
	}

	/// <summary>
	/// A key on the keypad was pressed.
	/// </summary>
	public void Press( string key )
	{
		if ( hasError && key != "←" )
			Clear();

		switch ( key )
		{
			case "C": Clear(); break;
			case "←": Backspace(); break;
			case ".": Decimal(); break;
			case "=": Calculate(); break;
			case "+" or "−" or "×" or "÷": Operator( key ); break;
			default: Digit( key ); break;
		}

		UiState = UiState with
		{
			Revision = UiState.Revision + 1,
			Entry = entry,
			Expression = pendingOperator is null ? "" : $"{Format( accumulator ?? 0 )} {pendingOperator}",
		};
	}

	void Digit( string digit )
	{
		if ( startNewEntry )
		{
			entry = digit;
			startNewEntry = false;
		}
		else if ( entry.Count( char.IsDigit ) < MaxDigits )
		{
			entry = entry == "0" ? digit : entry + digit;
		}
	}

	void Decimal()
	{
		if ( startNewEntry )
		{
			entry = "0.";
			startNewEntry = false;
		}
		else if ( !entry.Contains( '.' ) )
		{
			entry += ".";
		}
	}

	void Backspace()
	{
		if ( startNewEntry || hasError )
			return;

		entry = entry[..^1];
		if ( entry is "" or "-" )
			entry = "0";
	}

	void Operator( string op )
	{
		// Chained operations evaluate as they go: 2 + 3 × shows 5.
		// Pressing an operator right after another one just swaps it.
		if ( pendingOperator is null )
		{
			accumulator = Parse( entry );
		}
		else if ( !startNewEntry && !Evaluate() )
		{
			return;
		}

		pendingOperator = op;
		startNewEntry = true;
	}

	void Calculate()
	{
		if ( pendingOperator is null )
			return;

		if ( !Evaluate() )
			return;

		pendingOperator = null;
		startNewEntry = true;
	}

	/// <summary>
	/// Apply the pending operator to the accumulator and the entry. Shows the result.
	/// </summary>
	bool Evaluate()
	{
		var lhs = accumulator ?? 0;
		var rhs = Parse( entry );

		double result = pendingOperator switch
		{
			"+" => lhs + rhs,
			"−" => lhs - rhs,
			"×" => lhs * rhs,
			"÷" => lhs / rhs,
			_ => rhs,
		};

		if ( double.IsNaN( result ) || double.IsInfinity( result ) )
		{
			Clear();
			entry = "ERROR";
			hasError = true;
			return false;
		}

		accumulator = result;
		entry = Format( result );
		return true;
	}

	void Clear()
	{
		entry = "0";
		accumulator = null;
		pendingOperator = null;
		startNewEntry = true;
		hasError = false;
	}

	static double Parse( string text ) => double.Parse( text, System.Globalization.CultureInfo.InvariantCulture );

	static string Format( double value ) =>
		value.ToString( $"G{MaxDigits}", System.Globalization.CultureInfo.InvariantCulture );
}
