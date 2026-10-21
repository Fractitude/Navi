namespace NaviApps;

/// <summary>
/// A model the turntable can show: the button's label and the model's path.
/// </summary>
public sealed record TurntableModel( string Label, string Path );

/// <summary>
/// A color the model can be tinted with: the swatch's label and the tint.
/// </summary>
public sealed record TurntableTint( string Label, string Hex );

/// <summary>
/// Everything the Turntable panel draws. Replaced whole by <see cref="TurntableViewModel"/> on every change.
/// </summary>
public sealed record TurntableUiState
{
	/// <summary>What a viewer draws until the host's state arrives.</summary>
	public static TurntableUiState Empty { get; } = new();

	/// <summary>
	/// The models to pick from. They never change, so they aren't part of the state that is sent.
	/// </summary>
	public static IReadOnlyList<TurntableModel> Models =>
	[
		// The first one is the default. This is a property and not a field, which is only set when the class loads, so
		// editing the list shows up after a hot reload.
		new( "Citizen", "models/citizen/citizen.vmdl" ),
		new( "Man", "models/citizen_human/citizen_human_male.vmdl" ),
		new( "Woman", "models/citizen_human/citizen_human_female.vmdl" ),
	];

	/// <summary>
	/// The tints to pick from, in the order the Color button cycles through them.
	/// </summary>
	public static IReadOnlyList<TurntableTint> Tints { get; } =
	[
		new( "White", "#ffffff" ),
		new( "Amber", "#e0b060" ),
		new( "Green", "#b8f0c0" ),
		new( "Violet", "#a890e0" ),
	];

	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>Index into <see cref="Models"/>.</summary>
	public int ModelIndex { get; init; }

	/// <summary>Index into <see cref="Tints"/>.</summary>
	public int TintIndex { get; init; }

	/// <summary>
	/// The model's yaw in degrees. While <see cref="Spinning"/>, where it was when this state was made: the panel spins it on from there.
	/// </summary>
	public float Yaw { get; init; } = 180;

	public bool Spinning { get; init; }

	/// <summary>
	/// A player has the model grabbed to turn it by hand, which pauses the spin until they let go.
	/// </summary>
	public bool Holding { get; init; }

	/// <summary>
	/// Is the model turning by itself now: spinning, and not held.
	/// </summary>
	public bool IsTurning => Spinning && !Holding;

	public TurntableModel Model => Models[ModelIndex];
	public TurntableTint Tint => Tints[TintIndex];
	public string SpinLabel => Spinning ? "Stop" : "Spin";
}
