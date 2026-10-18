namespace NaviApps;

/// <summary>
/// A page of settings, picked in the sidebar.
/// </summary>
public enum SettingsCategory
{
	Display,
	Theming,
	Windows,
	Notifications,
	Security,
}

/// <summary>
/// A sidebar entry.
/// </summary>
public sealed record CategoryItem( SettingsCategory Key, string Label, string Icon, bool Selected );

/// <summary>
/// One display scale the player can pick, e.g. "125%".
/// </summary>
public sealed record ScaleOption( float Scale, string Label, bool Selected );

/// <summary>
/// One choice in a row of the Theming page: a theme, a color scheme, or a blur or transparency level. <see cref="Value"/>
/// is what the action sends back, as an integer.
/// </summary>
public sealed record ThemeOption( int Value, string Label, bool Selected );

/// <summary>
/// An app on the Notifications page, with its two choices: On, and Muted.
/// </summary>
public sealed record NotificationAppRow( string AppId, string Name, string Icon, string Color, IReadOnlyList<ThemeOption> Options );

/// <summary>
/// Everything the Settings panel draws. Replaced whole by <see cref="SettingsViewModel"/> on every change.
/// </summary>
public sealed record SettingsUiState
{
	/// <summary>What a viewer draws until the host's state arrives.</summary>
	public static SettingsUiState Empty { get; } = new();

	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	public IReadOnlyList<CategoryItem> Categories { get; init; } = [];

	/// <summary>The page the content area shows.</summary>
	public SettingsCategory Category { get; init; }

	/// <summary>Display: the scales, in order, with the current one selected. Empty until the computer answers.</summary>
	public IReadOnlyList<ScaleOption> ScaleOptions { get; init; } = [];

	/// <summary>Theming: the themes, the color schemes, and the blur and transparency levels, each with the current one selected.</summary>
	public IReadOnlyList<ThemeOption> ThemeOptions { get; init; } = [];

	/// <summary>Theming: the wallpapers, with the current one selected.</summary>
	public IReadOnlyList<ThemeOption> WallpaperOptions { get; init; } = [];

	public IReadOnlyList<ThemeOption> SchemeOptions { get; init; } = [];

	public IReadOnlyList<ThemeOption> BlurOptions { get; init; } = [];

	public IReadOnlyList<ThemeOption> TransparencyOptions { get; init; } = [];

	/// <summary>Theming: whether the theme in use blurs and is see-through, so the blur and transparency rows apply.</summary>
	public bool IsGlass { get; init; }

	/// <summary>Windows: whether windows snap to the screen's edges and to each other's, and how close an edge has to get.</summary>
	public IReadOnlyList<ThemeOption> SnapScreenOptions { get; init; } = [];

	public IReadOnlyList<ThemeOption> SnapWindowsOptions { get; init; } = [];

	public IReadOnlyList<ThemeOption> SnapDistanceOptions { get; init; } = [];

	/// <summary>Windows: whether either kind of snapping is on, so the distance row applies.</summary>
	public bool SnapsToAnything { get; init; }

	/// <summary>Notifications: every installed app, by name, each with whether it is muted.</summary>
	public IReadOnlyList<NotificationAppRow> NotificationApps { get; init; } = [];

	/// <summary>A message about the last change that failed, or null. Clicking it dismisses it.</summary>
	public string Notice { get; init; }
}
