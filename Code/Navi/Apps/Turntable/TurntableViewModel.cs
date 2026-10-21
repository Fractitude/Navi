namespace NaviApps;

/// <summary>
/// Everything the Turntable does. The panel (<c>Turntable.razor</c>) draws <see cref="UiState"/> as a 3D scene, and its
/// buttons are actions (<c>Navi.Act</c>) that end up in <see cref="Act"/>. It makes no calls to the computer.
/// </summary>
public sealed class TurntableViewModel
{
	/// <summary>
	/// How fast the model spins, in degrees per second.
	/// </summary>
	public const float SpinSpeed = 45;

	/// <summary>Show a model. Its argument is the index into <see cref="TurntableUiState.Models"/>.</summary>
	public const string ModelAction = "model";

	/// <summary>Tint the model with the next color.</summary>
	public const string TintAction = "tint";

	/// <summary>Start or stop the spin.</summary>
	public const string SpinAction = "spin";

	/// <summary>Turn the model to the yaw in its argument, in degrees, as a player drags it.</summary>
	public const string DragAction = "drag";

	/// <summary>Grab ("1") or let go of ("0") the model, which pauses the spin while it is held.</summary>
	public const string HoldAction = "hold";

	/// <summary>
	/// A hold lasts this many seconds without a message from the panel that has it, then lets go. The panel repeats the
	/// message while the mouse is down, so this only matters when it never comes back (the player left).
	/// </summary>
	public const float HoldTimeout = 3;

	/// <summary>
	/// Time since <see cref="TurntableUiState.Yaw"/> was last set, which is how far a spin has turned the model since.
	/// </summary>
	RealTimeSince sinceYawSet = 0;

	/// <summary>
	/// Time since a panel last grabbed or dragged the model.
	/// </summary>
	RealTimeSince sinceHeld = 0;

	/// <summary>
	/// What the panel draws. Replaced whole on every change, and only ever read by the panel.
	/// </summary>
	public TurntableUiState UiState { get; private set; } = new();

	/// <summary>
	/// Run an action from the panel. One that doesn't parse is ignored, as if nothing had been clicked.
	/// </summary>
	public void Act( string action, string[] args )
	{
		var arg = args is { Length: > 0 } ? args[0] : null;

		switch ( action )
		{
			case ModelAction when int.TryParse( arg, out var index ): ShowModel( index ); break;
			case TintAction: NextTint(); break;
			case SpinAction: ToggleSpin(); break;
			case DragAction when float.TryParse( arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var degrees ) && float.IsFinite( degrees ):
				Drag( degrees );
				break;
			case HoldAction when arg is "1" or "0": Hold( arg == "1" ); break;
		}
	}

	public void ShowModel( int index )
	{
		if ( index < 0 || index >= TurntableUiState.Models.Count )
			return;

		Update( uiState => uiState with { ModelIndex = index } );
	}

	public void NextTint() =>
		Update( uiState => uiState with { TintIndex = (uiState.TintIndex + 1) % TurntableUiState.Tints.Count } );

	public void ToggleSpin() => Update( uiState => uiState with { Spinning = !uiState.Spinning } );

	/// <summary>
	/// Turn the model to <paramref name="yaw"/> for a player dragging it. Counts as holding it. The dragging panel
	/// already shows the model there, so the host sets the angle instead of adding to it: both end up in the same place
	/// however late the messages are.
	/// </summary>
	public void Drag( float yaw )
	{
		sinceHeld = 0;
		Update( uiState => uiState with { Holding = true, Yaw = Normalize( yaw ) } );
	}

	/// <summary>
	/// Grab the model, or let go of it. While it is held it does not spin.
	/// </summary>
	public void Hold( bool held )
	{
		sinceHeld = 0;

		if ( UiState.Holding != held )
			Update( uiState => uiState with { Holding = held } );
	}

	/// <summary>
	/// Called every frame by the host copy of the panel: lets go of a model whose holder went away.
	/// </summary>
	public void Tick()
	{
		if ( UiState.Holding && sinceHeld > HoldTimeout )
			Hold( false );
	}

	/// <summary>
	/// The only setter of <see cref="UiState"/>. Every new state starts from the yaw the model has reached, so the panel
	/// spins it on from the state's own <see cref="TurntableUiState.Yaw"/> whatever the change was.
	/// </summary>
	void Update( System.Func<TurntableUiState, TurntableUiState> change )
	{
		var yaw = UiState.IsTurning ? Normalize( UiState.Yaw + sinceYawSet * SpinSpeed ) : UiState.Yaw;
		sinceYawSet = 0;

		UiState = change( UiState with { Yaw = yaw } ) with { Revision = UiState.Revision + 1 };
	}

	/// <summary>
	/// An angle in 0 to 360, so the yaw doesn't grow without end.
	/// </summary>
	static float Normalize( float degrees ) => (degrees % 360 + 360) % 360;
}
