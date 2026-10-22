using Sandbox.UI;
using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// Everything the Packages app does: what the computer knows, what each kind of app means, and which call a click
/// becomes. The panel (<c>Packages.razor</c>) holds an instance of this and nothing else — it draws
/// <see cref="UiState"/> and calls the methods below, and those replace <see cref="UiState"/> and touch nothing in the
/// panel tree.
/// </summary>
/// <remarks>
/// The division is the two halves of a UI: this class is the app, the panel is one way of drawing it. Nothing here
/// reads a panel, so every rule can be exercised without one — a console probe can select a filter, search the
/// Store or install an app and print the state that came out.
/// </remarks>
public sealed class PackagesViewModel
{
	/// <summary>
	/// The computer, as this app. Every call is made through it, and the computer answers as the window the app is in.
	/// </summary>
	readonly Navi navi;

	/// <summary>How often the computer's version is polled, in seconds: a change there is what makes the lists stale.</summary>
	const float PollInterval = 0.25f;

	/// <summary>
	/// How long a refresh shows as running at least. A Store search usually answers within a frame or two, and a
	/// refresh that finishes before it is drawn looks like a click that did nothing, so the status and the disabled
	/// button stay up this long even when the search is instant.
	/// </summary>
	const float RefreshVisibleTime = 0.5f;

	/// <summary>
	/// Store apps fetched per request. The backend pages them, so this is how many a refresh or a "More" adds.
	/// </summary>
	const int StorePageSize = 20;

	/// <summary>
	/// The kinds of app an app tab can show, in menu order; the first is the one it starts on. A method, not a field:
	/// a hot reload patches methods, so a change to these lists shows up in a running game, while a field's
	/// initializer only runs once per load.
	/// </summary>
	static (string Key, string Name)[] KindsOf( PackagesTab tab ) => tab switch
	{
		PackagesTab.Prepackaged =>
		[
			("prepackaged", "Regular"),
			("core", "Core"),
		],
		_ =>
		[
			("store", "From Store"),
			("system", "Root access"),
		],
	};

	/// <summary>The kind each app tab is showing, kept while another tab is up.</summary>
	readonly Dictionary<PackagesTab, string> filters = new();

	// What the computer said, kept apart from what is drawn from it: drawing formats and filters, this is the data.

	/// <summary>Every app on the computer, installed or not. See <see cref="Navi.AppCalls.ListAsync"/>.</summary>
	IReadOnlyList<Navi.AppInfo> apps = [];

	/// <summary>Ids of the apps that came from the Store. The app list doesn't say where an app came from, so it's a second call.</summary>
	HashSet<string> storeAppIds = new();

	HashSet<string> installedPackages = new();
	HashSet<string> installingPackages = new();

	/// <summary>Idents of the packages the game blocks. See <see cref="Navi.GetStoreBlacklist"/>.</summary>
	HashSet<string> blacklistedPackages = new( System.StringComparer.OrdinalIgnoreCase );

	/// <summary>The Store entries fetched so far. The backend pages them, so the list grows with "More".</summary>
	IReadOnlyList<Navi.StoreListing> storePackages = [];

	/// <summary>The computer's version when the lists were last fetched. They are fetched again when it changes.</summary>
	int? knownVersion;

	bool polling;
	RealTimeSince sincePoll;

	/// <summary>The guard for <see cref="FetchStoreAsync"/>; <see cref="PackagesUiState.Refreshing"/> is its reflection.</summary>
	bool refreshing;

	bool storeLoaded;

	/// <summary>Counts the searches that started the Store list over. See <see cref="PackagesUiState.StoreSearch"/>.</summary>
	int search;

	/// <summary>Idents of the Store entries showing their details.</summary>
	readonly HashSet<string> expanded = new();

	/// <summary>Ids of the Apps entries showing their details.</summary>
	readonly HashSet<string> expandedApps = new();

	/// <summary>
	/// The version of each Store package this computer has installed, by ident, as the engine remembers it. Null when it
	/// doesn't know. Read when an entry is opened, to set against the latest one.
	/// </summary>
	readonly Dictionary<string, string> installedVersions = new();

	/// <summary>Full package info by ident. Search results leave some of it out, like the revision, so it's fetched when an entry is expanded.</summary>
	readonly Dictionary<string, Package> details = new();

	public PackagesViewModel( Navi navi )
	{
		this.navi = navi ?? throw new System.ArgumentNullException( nameof(navi) );

		UiState = WithFilter( new PackagesUiState { Tab = PackagesTab.Installed }, FilterOf( PackagesTab.Installed ) );
	}

	/// <summary>
	/// What the panel draws. Replaced whole on every change, and only ever read by the panel.
	/// </summary>
	public PackagesUiState UiState { get; private set; }

	/// <summary>The panel's <c>BuildHash</c>: the state's revision is the panel's reason to redraw.</summary>
	public int Revision => UiState.Revision;

	/// <summary>
	/// What the Store search field holds, as the field reports it. Typing is no reason to redraw the list, so this
	/// replaces the state without going through <see cref="Update"/>; Enter (<see cref="SubmitSearch"/>) is what
	/// searches.
	/// </summary>
	public void SetSearchText( string text ) => UiState = UiState with { SearchText = text is { Length: > 80 } ? text[..80] : text ?? "" };

	/// <summary>
	/// Called every frame by the panel. Nothing here is a click: the lists are refetched when the computer's version
	/// changes, so an installation made anywhere else shows up without the player doing anything.
	/// </summary>
	public void Tick()
	{
		if ( !polling && sincePoll > PollInterval )
			_ = PollAsync();
	}

	// Tabs.

	/// <summary>Show the apps the player installed.</summary>
	public void OpenInstalled() => OpenAppTab( PackagesTab.Installed );

	/// <summary>Show the apps that ship with the game or a library.</summary>
	public void OpenPrepackaged() => OpenAppTab( PackagesTab.Prepackaged );

	/// <summary>Show the Store, fetching its first page the first time.</summary>
	public void OpenStore()
	{
		// The filter belongs to the app tabs: the button is gone now, and a menu with no button to anchor it is a ghost.
		Update( uiState => uiState with { Tab = PackagesTab.Store, FilterOpen = false } );

		if ( !storeLoaded )
			_ = FetchStoreAsync( false );
	}

	/// <summary>
	/// Show one of the app lists, on the kind it was showing when it was left. Its menu lists other kinds, so an open
	/// one closes.
	/// </summary>
	void OpenAppTab( PackagesTab tab ) =>
		Update( uiState => WithFilter( uiState with { Tab = tab, FilterOpen = false }, FilterOf( tab ) ) );

	// The kinds of app the app tabs show.

	public void ToggleFilterMenu() => Update( uiState => uiState with { FilterOpen = !uiState.FilterOpen } );

	public void CloseFilterMenu() => Update( uiState => uiState with { FilterOpen = false } );

	/// <summary>Show the apps of one of the current tab's kinds (<see cref="KindsOf"/>).</summary>
	public void ChooseFilter( string filter )
	{
		filters[UiState.Tab] = filter;
		Update( uiState => WithFilter( uiState with { FilterOpen = false }, filter ) );
	}

	/// <summary>The kind <paramref name="tab"/> shows: the one last chosen there, or its first.</summary>
	string FilterOf( PackagesTab tab ) => filters.GetValueOrDefault( tab ) ?? KindsOf( tab )[0].Key;

	/// <summary>The state with its app list showing the kind <paramref name="filter"/> of its tab.</summary>
	PackagesUiState WithFilter( PackagesUiState uiState, string filter ) => uiState with
	{
		Filter = filter,
		Filters = BuildFilters( uiState.Tab, filter ),
		FilterName = NameOf( uiState.Tab, filter ),
		EmptyText = EmptyTextFor( filter ),
		AppRows = BuildAppRows( filter ),
	};

	/// <summary>Dismiss the notice above the list.</summary>
	public void DismissNotice()
	{
		if ( UiState.Notice is not null )
			Update( uiState => uiState with { Notice = null } );
	}

	// The Store.

	/// <summary>Search again from the first entry, with whatever the field holds.</summary>
	public void SubmitSearch() => _ = FetchStoreAsync( false );

	const string NoNetworkText = "No network connection. This computer needs a network card to reach the Store.";

	/// <summary>
	/// The computer went online or offline. A Store list that was shown is fetched again: offline it empties and says why,
	/// online it fills.
	/// </summary>
	public void NetworkChanged( bool online )
	{
		if ( storeLoaded )
			_ = FetchStoreAsync( false );
	}

	/// <summary>Fetch the page of the Store after the entries listed.</summary>
	public void LoadMore() => _ = FetchStoreAsync( true );

	/// <summary>Show or hide a Store entry's details, fetching them the first time it is opened.</summary>
	public void ToggleDetails( string ident )
	{
		if ( !expanded.Add( ident ) )
		{
			expanded.Remove( ident );
			Update( uiState => uiState with { StoreRows = BuildStoreRows() } );
			return;
		}

		Update( uiState => uiState with { StoreRows = BuildStoreRows() } );

		if ( !details.ContainsKey( ident ) )
			_ = FetchDetailsAsync( ident );
	}

	/// <summary>Show or hide an Apps entry's details. A Store app's are fetched the first time it is opened.</summary>
	public void ToggleAppDetails( string id )
	{
		var opened = expandedApps.Add( id );
		if ( !opened )
			expandedApps.Remove( id );

		Update( uiState => uiState with { AppRows = BuildAppRows() } );

		if ( opened && storeAppIds.Contains( id ) )
			_ = FetchDetailsAsync( id );
	}

	/// <summary>Download and install a Store app.</summary>
	public void InstallPackage( string ident ) => Run( "install this app", () => navi.Store.InstallAsync( ident ) );

	/// <summary>Install an app that ships with the game or a library.</summary>
	public void InstallApp( string id ) => Run( "install apps", () => navi.Apps.InstallAsync( id ) );

	/// <summary>Remove an installed Store package, including one whose app didn't load.</summary>
	public void UninstallPackage( string ident ) => Run( "uninstall apps", () => navi.Store.UninstallAsync( ident ) );

	/// <summary>Uninstall an app.</summary>
	public void UninstallApp( string id ) => Run( "uninstall apps", () => navi.Apps.UninstallAsync( id ) );

	// The panel's controls, as actions (Navi.Act): they reach the host's copy from whichever client the player is on.

	public const string SetSearchTextAction = "set-search-text";
	public const string OpenInstalledAction = "open-installed";
	public const string OpenPrepackagedAction = "open-prepackaged";
	public const string OpenStoreAction = "open-store";
	public const string ToggleFilterMenuAction = "toggle-filter-menu";
	public const string CloseFilterMenuAction = "close-filter-menu";
	public const string ChooseFilterAction = "choose-filter";
	public const string DismissNoticeAction = "dismiss-notice";
	public const string SubmitSearchAction = "submit-search";
	public const string LoadMoreAction = "load-more";
	public const string ToggleDetailsAction = "toggle-details";
	public const string ToggleAppDetailsAction = "toggle-app-details";
	public const string InstallPackageAction = "install-package";
	public const string InstallAppAction = "install-app";
	public const string UninstallAppAction = "uninstall-app";
	public const string UninstallPackageAction = "uninstall-package";

	/// <summary>
	/// Run an action from the panel. One that is missing its argument is ignored, like a click on nothing. Installing
	/// and uninstalling still go through the computer's permission checks, as a click here always did.
	/// </summary>
	public void Act( string action, string[] args )
	{
		var arg = args is { Length: > 0 } ? args[0] : null;

		switch ( action )
		{
			case SetSearchTextAction: SetSearchText( arg ); break;
			case OpenInstalledAction: OpenInstalled(); break;
			case OpenPrepackagedAction: OpenPrepackaged(); break;
			case OpenStoreAction: OpenStore(); break;
			case ToggleFilterMenuAction: ToggleFilterMenu(); break;
			case CloseFilterMenuAction: CloseFilterMenu(); break;
			case ChooseFilterAction when KindsOf( UiState.Tab ).Any( x => x.Key == arg ): ChooseFilter( arg ); break;
			case DismissNoticeAction: DismissNotice(); break;
			case SubmitSearchAction: SubmitSearch(); break;
			case LoadMoreAction: LoadMore(); break;
			case ToggleDetailsAction when !string.IsNullOrEmpty( arg ): ToggleDetails( arg ); break;
			case ToggleAppDetailsAction when !string.IsNullOrEmpty( arg ): ToggleAppDetails( arg ); break;
			case InstallPackageAction when !string.IsNullOrEmpty( arg ): InstallPackage( arg ); break;
			case InstallAppAction when !string.IsNullOrEmpty( arg ): InstallApp( arg ); break;
			case UninstallAppAction when !string.IsNullOrEmpty( arg ): UninstallApp( arg ); break;
			case UninstallPackageAction when !string.IsNullOrEmpty( arg ): UninstallPackage( arg ); break;
		}
	}

	// The work behind the events.

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

			apps = await navi.Apps.ListAsync();
			storeAppIds = (await navi.Apps.ListFromStoreAsync()).ToHashSet();

			var (installed, installing) = await navi.Store.GetStatusAsync();
			installedPackages = installed.ToHashSet();
			installingPackages = installing.ToHashSet();
			blacklistedPackages = (await navi.Store.GetBlacklistAsync()).ToHashSet( System.StringComparer.OrdinalIgnoreCase );

			Update( uiState => uiState with { AppRows = BuildAppRows(), StoreRows = BuildStoreRows() } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Packages: couldn't refresh the lists: {e.Message}" );
		}
		finally
		{
			polling = false;
		}
	}

	/// <summary>
	/// Fetch one page of the Store. A refresh (<paramref name="append"/> false) starts over from the first entry;
	/// loading more adds the page after the entries already shown. Ignores a call made while a page is being fetched.
	/// </summary>
	async Task FetchStoreAsync( bool append )
	{
		// One page at a time, whatever asked for it: two at once would race to fill the same list.
		if ( refreshing )
			return;

		refreshing = true;
		storeLoaded = true;
		Update( uiState => uiState with { Refreshing = true } );

		RealTimeSince elapsed = 0;

		var text = UiState.SearchText.Trim();
		var skip = append ? storePackages.Count : 0;

		// A failed search leaves the page that is already listed, so the total it came with stays too.
		var total = UiState.StoreTotal;
		string status;

		try
		{
			// The old entries stay on screen while the page loads: the status above says it's a refresh.
			var (packages, found) = await navi.Store.SearchAsync( text, skip, StorePageSize );

			storePackages = append ? storePackages.Concat( packages ).ToArray() : packages;
			total = found;

			// A new list starts over: nothing expanded, details fetched fresh, and back at the top.
			if ( !append )
			{
				expanded.Clear();
				details.Clear();
				search++;
			}
			status = found > 0 ? null
				: text.Length > 0 ? "No apps found matching your filter."
				: "No apps have been published yet.";
		}
		catch ( Navi.NoNetworkException )
		{
			// Nothing listed can be installed without the network, so the list goes.
			storePackages = [];
			total = 0;
			status = NoNetworkText;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Packages: Store search failed: {e.Message}" );
			status = "Couldn't reach the Store.";
		}

		// Hold the running state, and with it the "Connecting" status, for a moment: the search often answers within
		// a frame or two, and a refresh drawn only after it already finished looks like a click that did nothing.
		var remaining = RefreshVisibleTime - elapsed;
		if ( remaining > 0 )
			await Task.Delay( (int)(remaining * 1000) );

		refreshing = false;
		Update( uiState => uiState with
		{
			Refreshing = false, StoreStatus = status, StoreRows = BuildStoreRows(), StoreTotal = total, StoreSearch = search,
		} );
	}

	/// <summary>
	/// Fetch the full package an entry's details are built from. It goes straight to the engine rather than through
	/// <see cref="Navi"/>: it reads public package metadata and touches nothing on the computer, so there is nothing
	/// for the computer to authorize.
	/// </summary>
	async Task FetchDetailsAsync( string ident )
	{
		try
		{
			// What was installed when the app was downloaded, before the backend's word on the latest.
			installedVersions[ident] = NaviStore.InstalledVersion( ident );

			// The computer has no way out without a network card, so the backend isn't asked either.
			if ( await navi.Network.IsOnlineAsync() && await Package.FetchAsync( ident, false ) is { } full )
				details[ident] = full;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Packages: couldn't fetch {ident}: {e.Message}" );
		}

		Update( uiState => uiState with { StoreRows = BuildStoreRows(), AppRows = BuildAppRows() } );
	}

	/// <summary>
	/// Make a call that needs a permission, asking the player for it if this app doesn't have it. What the call
	/// changed reaches the panel through <see cref="PollAsync"/>: the computer's version goes up, and the lists are
	/// fetched again.
	/// </summary>
	/// <remarks>
	/// <c>async void</c> on purpose: its callers are click handlers, which can't await, and both outcomes are caught
	/// below and turned into a notice, so no exception escapes the task.
	/// </remarks>
	async void Run( string what, System.Func<Task<bool>> call )
	{
		DismissNotice();

		try
		{
			await navi.WithPermissionAsync( call );
		}
		catch ( System.UnauthorizedAccessException )
		{
			Update( uiState => uiState with { Notice = $"Packages isn't allowed to {what}." } );
		}
		catch ( Navi.NoNetworkException )
		{
			Update( uiState => uiState with { Notice = $"Couldn't {what}: no network connection." } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Packages: couldn't {what}: {e.Message}" );
			Update( uiState => uiState with { Notice = $"Couldn't {what}." } );
		}
	}

	/// <summary>
	/// Replace the state and make the panel redraw. Every change something is drawn from goes through here, which is
	/// why <see cref="Revision"/> is the only thing the panel's hash needs.
	/// </summary>
	void Update( System.Func<PackagesUiState, PackagesUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };

	// What the state is built from.

	IReadOnlyList<PackageRow> BuildStoreRows() => storePackages
		.Select( x => BuildStoreRow( x.Package, x.Official ) )
		.ToArray();

	PackageRow BuildStoreRow( Package package, bool official )
	{
		var ident = package.FullIdent;
		var isExpanded = expanded.Contains( ident );

		// Search results carry the short info; the revision and the long description come from the details fetch.
		var full = isExpanded ? details.GetValueOrDefault( ident ) ?? package : package;

		return new PackageRow
		{
			Ident = ident,
			Title = package.Title,
			Summary = package.Summary,
			Thumb = package.Thumb,
			Official = official,
			Action = installingPackages.Contains( ident ) ? PackageAction.Installing
				: installedPackages.Contains( ident ) ? (storeAppIds.Contains( ident ) ? PackageAction.Installed : PackageAction.Missing)
				: blacklistedPackages.Contains( ident ) ? PackageAction.Blacklisted
				: PackageAction.Install,
			Expanded = isExpanded,
			About = isExpanded ? About( full ) : "",
			Facts = isExpanded ? Facts( full ) : [],
		};
	}

	IReadOnlyList<AppRow> BuildAppRows( string filter = null )
	{
		var kind = filter ?? UiState.Filter;

		return apps
			.Select( x => (App: x, FromStore: storeAppIds.Contains( x.Id )) )
			.Where( x => Matches( kind, x.App.Installed, x.App.Kind, x.FromStore ) )
			.Select( x => BuildAppRow( x.App, x.FromStore ) )
			.ToArray();
	}

	AppRow BuildAppRow( Navi.AppInfo app, bool fromStore )
	{
		var shielded = app.Kind is Navi.AppKind.Core or Navi.AppKind.System;
		var isExpanded = expandedApps.Contains( app.Id );

		// A Store app's id is its package ident, so the same details as the Store tab's are there for it.
		var full = fromStore ? details.GetValueOrDefault( app.Id ) : null;

		return new AppRow
		{
			Id = app.Id,
			Name = app.Name,
			Description = app.Description,
			Icon = app.Icon,
			Color = app.Color,
			Shield = shielded,
			ShieldTooltip = app.Kind == Navi.AppKind.Core ? "Core app: part of the OS" : "System app: asks for full system access",
			Action = app.Kind == Navi.AppKind.Core ? AppAction.None
				: app.Installed ? AppAction.Uninstall
				: AppAction.Install,
			Expanded = isExpanded,
			About = isExpanded ? (full is not null ? About( full ) : app.Description) : "",
			Facts = isExpanded ? AppFacts( app, fromStore, full ) : [],
		};
	}

	static IReadOnlyList<FilterItem> BuildFilters( PackagesTab tab, string selected ) => KindsOf( tab )
		.Select( x => new FilterItem( x.Key, x.Name, x.Key == selected ) )
		.ToArray();

	/// <summary>
	/// The current kind's name, for the button's tooltip. Falls back to the tab's first kind if the filter is one this
	/// build doesn't know: a hot reload can leave an instance holding an older one, and a missing name shouldn't
	/// take the panel down with it.
	/// </summary>
	static string NameOf( PackagesTab tab, string filter ) =>
		KindsOf( tab ).FirstOrDefault( x => x.Key == filter ).Name ?? KindsOf( tab )[0].Name;

	/// <summary>
	/// Is the app in the list under <paramref name="filter"/>? From Store is the Store apps that are installed:
	/// pre-packaged apps have their own tab, and Store apps that aren't installed are left to the Store tab. Regular is
	/// the apps that ship with the game or a library but aren't part of the OS, whether or not they are installed,
	/// because the Store can't bring them back. Core apps are part of the OS, and system apps ask for full system
	/// access, whether they ship with the game or with a Store package, so an app can match more than one of these.
	/// An unknown filter (a hot reload can leave an older one) shows From Store.
	/// </summary>
	static bool Matches( string filter, bool installed, Navi.AppKind kind, bool fromStore ) => filter switch
	{
		"core" => !fromStore && kind is Navi.AppKind.Core,
		"system" => installed && kind is Navi.AppKind.Core or Navi.AppKind.System,
		"prepackaged" => !fromStore && kind is not Navi.AppKind.Core,
		_ => installed && fromStore,
	};

	/// <summary>
	/// Shown in an app list when no app matches the kind being shown.
	/// </summary>
	static string EmptyTextFor( string filter ) => filter switch
	{
		"core" => "No apps are part of the OS.",
		"system" => "No apps ask for full system access.",
		"prepackaged" => "No apps come with the game.",
		_ => "No Store apps are installed.",
	};

	// Formatting for the details, done here so the panel prints values rather than working them out.

	static string About( Package package )
	{
		if ( !string.IsNullOrWhiteSpace( package.Description ) ) return package.Description;
		if ( !string.IsNullOrWhiteSpace( package.Summary ) ) return package.Summary;
		return "No description yet.";
	}

	static IReadOnlyList<PackageFact> Facts( Package package ) =>
	[
		new("person", "Author", package.Org?.Title ?? "Unknown"),
		new("thumb_up", "Rating", Rating( package )),
		new("group", "Players", package.Usage.Total.Users.ToString( "N0" )),
		new("update", "Updated", package.Updated.ToLocalTime().ToString( "d MMM yyyy" )),
		new("sell", "Version", package.Revision?.VersionId.ToString() ?? "..."),
		new("save", "Size", Size( package )),
	];

	/// <summary>
	/// The facts of an Apps entry. A Store app gets the Store's, with the version it has installed beside the latest
	/// one, so an update that hasn't arrived shows. The others get what the computer knows.
	/// </summary>
	IReadOnlyList<PackageFact> AppFacts( Navi.AppInfo app, bool fromStore, Package full )
	{
		var kind = app.Kind switch
		{
			Navi.AppKind.Core => "Core app: part of the OS",
			Navi.AppKind.System => "System app: full system access",
			_ => fromStore ? "Store app" : "Comes with the game",
		};

		var facts = new List<PackageFact>
		{
			new("category", "Kind", kind),
			new("tag", "Id", app.Id),
			new("download_done", "Status", app.Installed ? "Installed" : "Not installed"),
		};

		if ( !fromStore )
			return facts;

		var installed = installedVersions.GetValueOrDefault( app.Id );
		var latest = full?.Revision?.VersionId.ToString();

		facts.Add( new("sell", "Installed version", installed ?? (installedVersions.ContainsKey( app.Id ) ? "Unknown" : "...") ) );
		facts.Add( new("new_releases", "Latest version", latest ?? "...") );

		if ( installed is not null && latest is not null && installed != latest )
			facts.Add( new("warning", "Update", "A newer version is published. Restart the game to get it.") );

		if ( full is null )
			return facts;

		facts.Add( new("person", "Author", full.Org?.Title ?? "Unknown") );
		facts.Add( new("thumb_up", "Rating", Rating( full )) );
		facts.Add( new("group", "Players", full.Usage.Total.Users.ToString( "N0" )) );
		facts.Add( new("update", "Updated", full.Updated.ToLocalTime().ToString( "d MMM yyyy" )) );
		facts.Add( new("save", "Size", Size( full )) );
		return facts;
	}

	static string Rating( Package package )
	{
		if ( package.Reviews.Total > 0 )
			return $"{package.Reviews.Score * 100:0}% positive ({package.Reviews.Total})";

		if ( package.VotesUp + package.VotesDown > 0 )
			return $"{package.VotesUp} up, {package.VotesDown} down";

		return "No ratings yet";
	}

	static string Size( Package package )
	{
		if ( package.Revision is not { } revision )
			return "...";

		return $"{System.Math.Max( 1, revision.TotalSize / 1024 )} KB";
	}
}
