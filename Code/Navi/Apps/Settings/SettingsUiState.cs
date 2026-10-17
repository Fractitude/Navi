namespace NaviApps;

/// <summary>
/// A page of settings, picked in the sidebar.
/// </summary>
public enum SettingsCategory
{
	Display,
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
/// Everything the Settings panel draws. Replaced whole by <see cref="SettingsViewModel"/> on every change.
/// </summary>
public sealed record SettingsUiState
{
	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	public IReadOnlyList<CategoryItem> Categories { get; init; } = [];

	/// <summary>The page the content area shows.</summary>
	public SettingsCategory Category { get; init; }

	/// <summary>Display: the scales, in order, with the current one selected. Empty until the computer answers.</summary>
	public IReadOnlyList<ScaleOption> ScaleOptions { get; init; } = [];

	/// <summary>A message about the last change that failed, or null. Clicking it dismisses it.</summary>
	public string Notice { get; init; }
}
