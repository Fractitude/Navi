using Sandbox.UI;
using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// Everything the Settings app does: which page is open, what the computer's settings are, and which call a click
/// becomes. The panel (<c>Settings.razor</c>) draws <see cref="UiState"/> and calls the methods below.
/// </summary>
public sealed class SettingsViewModel
{
	/// <summary>
	/// The app's panel. Every call to the computer is made with it, and it is the window the computer answers as.
	/// </summary>
	readonly Panel app;

	/// <summary>How often the computer's version is polled, in seconds: a change there means the settings may have changed.</summary>
	const float PollInterval = 0.25f;

	/// <summary>
	/// The pages, in sidebar order. A property, not a field, so a hot reload shows a change to it.
	/// </summary>
	static (SettingsCategory Key, string Label, string Icon)[] Pages =>
	[
		(SettingsCategory.Display, "Display", "monitor"),
	];

	// What the computer said.

	float displayScale = 1f;
	float[] displayScales = [];

	/// <summary>The computer's version when the settings were last fetched. They are fetched again when it changes.</summary>
	int? knownVersion;

	bool polling;
	RealTimeSince sincePoll;

	public SettingsViewModel( Panel app )
	{
		this.app = app ?? throw new System.ArgumentNullException( nameof(app) );

		UiState = new SettingsUiState
		{
			Category = SettingsCategory.Display,
			Categories = BuildCategories( SettingsCategory.Display ),
		};
	}

	/// <summary>
	/// What the panel draws. Replaced whole on every change, and only ever read by the panel.
	/// </summary>
	public SettingsUiState UiState { get; private set; }

	/// <summary>The panel's <c>BuildHash</c>.</summary>
	public int Revision => UiState.Revision;

	/// <summary>
	/// Called every frame by the panel. The settings are refetched when the computer's version changes, so a change
	/// made anywhere else shows up here.
	/// </summary>
	public void Tick()
	{
		if ( !polling && sincePoll > PollInterval )
			_ = PollAsync();
	}

	/// <summary>Show a page of settings.</summary>
	public void OpenCategory( SettingsCategory category ) =>
		Update( uiState => uiState with { Category = category, Categories = BuildCategories( category ) } );

	/// <summary>Draw the desktop at another scale.</summary>
	public void ChooseScale( float scale )
	{
		if ( scale != displayScale )
			_ = SetScaleAsync( scale );
	}

	public void DismissNotice() => Update( uiState => uiState with { Notice = null } );

	// The work behind the events.

	async Task SetScaleAsync( float scale )
	{
		try
		{
			if ( !await Navi.SetDisplayScale( app, scale ) )
				Update( uiState => uiState with { Notice = "The computer can't use that scale." } );
		}
		catch ( System.Exception e )
		{
			Update( uiState => uiState with { Notice = $"Couldn't change the scale: {e.Message}" } );
		}
	}

	async Task PollAsync()
	{
		polling = true;
		sincePoll = 0;

		try
		{
			var version = await Navi.GetVersion( app );
			if ( version == knownVersion )
				return;

			knownVersion = version;
			(displayScale, displayScales) = await Navi.GetDisplayScale( app );

			Update( uiState => uiState with { ScaleOptions = BuildScaleOptions() } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Settings: couldn't read the settings: {e.Message}" );
		}
		finally
		{
			polling = false;
		}
	}

	static IReadOnlyList<CategoryItem> BuildCategories( SettingsCategory selected ) =>
		Pages.Select( x => new CategoryItem( x.Key, x.Label, x.Icon, x.Key == selected ) ).ToArray();

	IReadOnlyList<ScaleOption> BuildScaleOptions() =>
		displayScales.Select( x => new ScaleOption( x, $"{System.MathF.Round( x * 100 )}%", x == displayScale ) ).ToArray();

	void Update( System.Func<SettingsUiState, SettingsUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };
}
