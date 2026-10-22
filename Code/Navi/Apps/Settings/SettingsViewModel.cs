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
	/// The computer, as this app. Every call is made through it, and the computer answers as the window the app is in.
	/// </summary>
	readonly Navi navi;

	/// <summary>How often the computer's version is polled, in seconds: a change there means the settings may have changed.</summary>
	const float PollInterval = 0.25f;

	/// <summary>
	/// The pages, in sidebar order. A property, not a field, so a hot reload shows a change to it.
	/// </summary>
	static (SettingsCategory Key, string Label, string Icon)[] Pages =>
	[
		(SettingsCategory.Display, "Display", "monitor"),
		(SettingsCategory.Theming, "Theming", "palette"),
		(SettingsCategory.Windows, "Windows", "web_asset"),
		(SettingsCategory.Notifications, "Notifications", "notifications"),
		(SettingsCategory.Security, "Security", "shield"),
		(SettingsCategory.Hardware, "Hardware", "memory"),
	];

	/// <summary>The distances the Windows page offers for snapping, in panel pixels.</summary>
	static readonly int[] SnapDistances = [4, 8, 12, 20, 30];

	// What the computer said.

	float displayScale = 1f;
	IReadOnlyList<float> displayScales = [];

	Navi.Theme theme;
	Navi.ColorScheme scheme;
	Navi.Wallpaper wallpaper;
	int blurLevel;
	int transparencyLevel;

	bool snapToScreenEdges;
	bool snapToWindows;
	int snapDistance;

	IReadOnlyList<Navi.AppInfo> apps = [];
	IReadOnlyList<Navi.NotificationApp> notificationApps = [];
	IReadOnlyList<Navi.HardwareInfo> hardware = [];
	Navi.NetworkStatus network = new( false, "" );

	/// <summary>The computer's version when the settings were last fetched. They are fetched again when it changes.</summary>
	int? knownVersion;

	bool polling;
	RealTimeSince sincePoll;

	public SettingsViewModel( Navi navi )
	{
		this.navi = navi ?? throw new System.ArgumentNullException( nameof(navi) );

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

	/// <summary>Put another wallpaper behind the windows.</summary>
	public void ChooseWallpaper( Navi.Wallpaper choice ) => _ = RunAsync( () => navi.Settings.SetWallpaperAsync( choice ), "the wallpaper" );

	/// <summary>Draw the computer in another theme.</summary>
	public void ChooseTheme( Navi.Theme choice ) => _ = RunAsync( () => navi.Settings.SetThemeAsync( choice ), "the theme" );

	/// <summary>Draw the computer light or dark.</summary>
	public void ChooseScheme( Navi.ColorScheme choice ) => _ = RunAsync( () => navi.Settings.SetColorSchemeAsync( choice ), "the color scheme" );

	/// <summary>How strongly the Glass theme blurs what is behind a surface.</summary>
	public void ChooseBlur( int level ) => _ = RunAsync( () => navi.Settings.SetBlurLevelAsync( level ), "the blur" );

	/// <summary>How much shows through the Glass theme's surfaces.</summary>
	public void ChooseTransparency( int level ) => _ = RunAsync( () => navi.Settings.SetTransparencyLevelAsync( level ), "the transparency" );

	/// <summary>Snap windows to the screen's edges, or stop.</summary>
	public void ChooseSnapToScreenEdges( bool snap ) => _ = RunAsync( () => navi.Settings.SetSnapToScreenEdgesAsync( snap ), "snapping to the screen" );

	/// <summary>Snap windows to each other's edges, or stop.</summary>
	public void ChooseSnapToWindows( bool snap ) => _ = RunAsync( () => navi.Settings.SetSnapToWindowsAsync( snap ), "snapping to windows" );

	/// <summary>How close an edge has to get to snap, in panel pixels.</summary>
	public void ChooseSnapDistance( int distance ) => _ = RunAsync( () => navi.Settings.SetSnapDistanceAsync( distance ), "the snap distance" );

	/// <summary>Mute an app's notifications, or let them through again.</summary>
	public void ChooseMuted( string appId, bool muted ) =>
		_ = RunAsync( () => navi.Notifications.SetMutedAsync( appId, muted ), "the app's notifications" );

	public void DismissNotice() => Update( uiState => uiState with { Notice = null } );

	// The panel's controls, as actions (Navi.Act): they reach the host's copy from whichever client the player is on.

	public const string OpenCategoryAction = "open-category";
	public const string ChooseScaleAction = "choose-scale";
	public const string ChooseWallpaperAction = "choose-wallpaper";
	public const string ChooseThemeAction = "choose-theme";
	public const string ChooseSchemeAction = "choose-scheme";
	public const string ChooseBlurAction = "choose-blur";
	public const string ChooseTransparencyAction = "choose-transparency";
	public const string ChooseSnapScreenAction = "choose-snap-screen";
	public const string ChooseSnapWindowsAction = "choose-snap-windows";
	public const string ChooseSnapDistanceAction = "choose-snap-distance";
	public const string ChooseMutedAction = "choose-muted";
	public const string DismissNoticeAction = "dismiss-notice";

	/// <summary>
	/// Run an action from the panel. Arguments that don't parse are ignored, like a click on nothing.
	/// </summary>
	public void Act( string action, string[] args )
	{
		var arg = args is { Length: > 0 } ? args[0] : null;

		switch ( action )
		{
			case OpenCategoryAction when System.Enum.TryParse<SettingsCategory>( arg, out var category ) && System.Enum.IsDefined( category ):
				OpenCategory( category );
				break;

			case ChooseScaleAction when float.TryParse( arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale ):
				ChooseScale( scale );
				break;

			case ChooseWallpaperAction when int.TryParse( arg, out var value ) && System.Enum.IsDefined( (Navi.Wallpaper)value ):
				ChooseWallpaper( (Navi.Wallpaper)value );
				break;

			case ChooseThemeAction when int.TryParse( arg, out var value ) && System.Enum.IsDefined( (Navi.Theme)value ):
				ChooseTheme( (Navi.Theme)value );
				break;

			case ChooseSchemeAction when int.TryParse( arg, out var value ) && System.Enum.IsDefined( (Navi.ColorScheme)value ):
				ChooseScheme( (Navi.ColorScheme)value );
				break;

			case ChooseBlurAction when int.TryParse( arg, out var value ) && value is >= 0 and <= Navi.MaxThemeLevel:
				ChooseBlur( value );
				break;

			case ChooseTransparencyAction when int.TryParse( arg, out var value ) && value is >= 0 and <= Navi.MaxThemeLevel:
				ChooseTransparency( value );
				break;

			case ChooseSnapScreenAction when int.TryParse( arg, out var value ) && value is 0 or 1:
				ChooseSnapToScreenEdges( value == 1 );
				break;

			case ChooseSnapWindowsAction when int.TryParse( arg, out var value ) && value is 0 or 1:
				ChooseSnapToWindows( value == 1 );
				break;

			case ChooseSnapDistanceAction when int.TryParse( arg, out var value ) && value is >= 1 and <= Navi.MaxSnapDistance:
				ChooseSnapDistance( value );
				break;

			case ChooseMutedAction when args is [{ Length: > 0 } appId, var value] && notificationApps.Any( x => x.AppId == appId )
				&& int.TryParse( value, out var muted ) && muted is 0 or 1:
				ChooseMuted( appId, muted == 1 );
				break;

			case DismissNoticeAction:
				DismissNotice();
				break;
		}
	}

	// The work behind the events.

	async Task SetScaleAsync( float scale )
	{
		try
		{
			if ( !await navi.Settings.SetDisplayScaleAsync( scale ) )
				Update( uiState => uiState with { Notice = "The computer can't use that scale." } );
		}
		catch ( System.Exception e )
		{
			Update( uiState => uiState with { Notice = $"Couldn't change the scale: {e.Message}" } );
		}
	}

	async Task RunAsync( System.Func<Task<bool>> change, string what )
	{
		try
		{
			if ( !await change() )
				Update( uiState => uiState with { Notice = $"The computer can't use that setting for {what}." } );
		}
		catch ( System.Exception e )
		{
			Update( uiState => uiState with { Notice = $"Couldn't change {what}: {e.Message}" } );
		}
	}

	async Task PollAsync()
	{
		polling = true;
		sincePoll = 0;

		try
		{
			var version = await navi.GetVersionAsync();
			if ( version == knownVersion )
				return;

			knownVersion = version;
			(displayScale, displayScales) = await navi.Settings.GetDisplayScaleAsync();
			(theme, scheme, blurLevel, transparencyLevel) = await navi.Settings.GetThemeAsync();
			wallpaper = await navi.Settings.GetWallpaperAsync();
			(snapToScreenEdges, snapToWindows, snapDistance) = await navi.Settings.GetSnappingAsync();
			apps = await navi.Apps.ListAsync();
			notificationApps = await navi.Notifications.ListAppsAsync();
			hardware = await navi.Hardware.ListAsync();
			network = await navi.Network.GetStatusAsync();

			Update( uiState => uiState with
			{
				ScaleOptions = BuildScaleOptions(),
				WallpaperOptions = BuildWallpaperOptions(),
				ThemeOptions = BuildThemeOptions(),
				SchemeOptions = BuildSchemeOptions(),
				BlurOptions = BuildLevelOptions( blurLevel ),
				TransparencyOptions = BuildLevelOptions( transparencyLevel ),
				IsGlass = theme == Navi.Theme.Glass,
				SnapScreenOptions = BuildOnOffOptions( snapToScreenEdges ),
				SnapWindowsOptions = BuildOnOffOptions( snapToWindows ),
				SnapDistanceOptions = BuildSnapDistanceOptions(),
				SnapsToAnything = snapToScreenEdges || snapToWindows,
				NotificationApps = BuildNotificationApps(),
				NetworkText = network.Online ? $"Online. This computer's IP address is {network.Address}." : $"Offline. {network.OfflineHint}",
				HardwareRows = hardware.OrderBy( x => x.Name ).Select( x => new HardwareRow( x.Enabled ? x.Name : $"{x.Name} (off)", x.Description, x.Icon ) ).ToArray(),
			} );
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

	IReadOnlyList<ThemeOption> BuildWallpaperOptions() =>
		[new( (int)Navi.Wallpaper.Paper, "Paper", wallpaper == Navi.Wallpaper.Paper ), new( (int)Navi.Wallpaper.Glass, "Glass", wallpaper == Navi.Wallpaper.Glass )];

	IReadOnlyList<ThemeOption> BuildThemeOptions() =>
		[new( (int)Navi.Theme.Paper, "Paper", theme == Navi.Theme.Paper ), new( (int)Navi.Theme.Glass, "Glass", theme == Navi.Theme.Glass )];

	IReadOnlyList<ThemeOption> BuildSchemeOptions() =>
		[new( (int)Navi.ColorScheme.Light, "Light", scheme == Navi.ColorScheme.Light ), new( (int)Navi.ColorScheme.Dark, "Dark", scheme == Navi.ColorScheme.Dark )];

	/// <summary>The labels of the blur and transparency levels, 0 to <see cref="Navi.MaxThemeLevel"/>.</summary>
	static readonly string[] LevelLabels = ["None", "Low", "Medium", "High", "Max"];

	static IReadOnlyList<ThemeOption> BuildLevelOptions( int selected ) =>
		LevelLabels.Select( ( x, i ) => new ThemeOption( i, x, i == selected ) ).ToArray();

	/// <summary>On and Off, sent back as 1 and 0.</summary>
	static IReadOnlyList<ThemeOption> BuildOnOffOptions( bool on ) => [new( 1, "On", on ), new( 0, "Off", !on )];

	/// <summary>
	/// The snap distances, plus the current one if it isn't among them (a saved or edited value), so one is always selected.
	/// </summary>
	IReadOnlyList<ThemeOption> BuildSnapDistanceOptions() =>
		SnapDistances.Append( snapDistance ).Distinct().Order().Select( x => new ThemeOption( x, $"{x} px", x == snapDistance ) ).ToArray();

	/// <summary>
	/// Every installed app, core apps too, by name. On is sent back as 0 (not muted), Muted as 1.
	/// </summary>
	IReadOnlyList<NotificationAppRow> BuildNotificationApps() => notificationApps
		.Select( x => (Setting: x, App: apps.FirstOrDefault( y => y.Id == x.AppId )) )
		.Where( x => x.App is not null )
		.OrderBy( x => x.App.Name, System.StringComparer.OrdinalIgnoreCase )
		.Select( x => new NotificationAppRow( x.App.Id, x.App.Name, x.App.Icon, x.App.Color,
			[new( 0, "On", !x.Setting.Muted ), new( 1, "Muted", x.Setting.Muted )] ) )
		.ToArray();

	void Update( System.Func<SettingsUiState, SettingsUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };
}
