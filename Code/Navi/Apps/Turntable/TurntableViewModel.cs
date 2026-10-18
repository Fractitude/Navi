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

	/// <summary>
	/// How far one press of a rotate button turns the model, in degrees.
	/// </summary>
	const float RotateStep = 45;

	/// <summary>Show a model. Its argument is the index into <see cref="TurntableUiState.Models"/>.</summary>
	public const string ModelAction = "model";

	/// <summary>Tint the model with the next color.</summary>
	public const string TintAction = "tint";

	/// <summary>Start or stop the spin.</summary>
	public const string SpinAction = "spin";

	/// <summary>Turn the model by one step. Its argument is "left" or "right".</summary>
	public const string RotateAction = "rotate";

	/// <summary>
	/// Time since <see cref="TurntableUiState.Yaw"/> was last set, which is how far a spin has turned the model since.
	/// </summary>
	RealTimeSince sinceYawSet = 0;

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
			case RotateAction when arg is "left": Rotate( RotateStep ); break;
			case RotateAction when arg is "right": Rotate( -RotateStep ); break;
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
	/// Turn by <paramref name="degrees"/> from where the model is now. A spin carries on from there.
	/// </summary>
	public void Rotate( float degrees ) =>
		Update( uiState => uiState with { Yaw = Normalize( uiState.Yaw + degrees ) } );

	/// <summary>
	/// The only setter of <see cref="UiState"/>. Every new state starts from the yaw the model has reached, so the panel
	/// spins it on from the state's own <see cref="TurntableUiState.Yaw"/> whatever the change was.
	/// </summary>
	void Update( System.Func<TurntableUiState, TurntableUiState> change )
	{
		var yaw = UiState.Spinning ? Normalize( UiState.Yaw + sinceYawSet * SpinSpeed ) : UiState.Yaw;
		sinceYawSet = 0;

		UiState = change( UiState with { Yaw = yaw } ) with { Revision = UiState.Revision + 1 };
	}

	/// <summary>
	/// An angle in 0 to 360, so the yaw doesn't grow without end.
	/// </summary>
	static float Normalize( float degrees ) => (degrees % 360 + 360) % 360;
}
