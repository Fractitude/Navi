using System.Collections.Generic;

namespace NaviApps;

/// <summary>
/// What a Store entry's install control offers, as the row shows it.
/// </summary>
public enum PackageAction
{
	/// <summary>Not installed: the row offers Install.</summary>
	Install,

	/// <summary>Downloading now: the row says Installing...</summary>
	Installing,

	/// <summary>On this computer: the row says Installed.</summary>
	Installed,

	/// <summary>Installed, but its app didn't load, so the Installed tab has nothing to uninstall: the row offers Remove.</summary>
	Missing,

	/// <summary>The game blocks it: the row says Blacklisted, and it can't be installed.</summary>
	Blacklisted,
}

/// <summary>
/// What an app's install control offers, as the row shows it.
/// </summary>
public enum AppAction
{
	/// <summary>No control: a core app is part of the OS, so it is always installed and can't be removed.</summary>
	None,

	Install,
	Uninstall,
}

/// <summary>
/// The tabs of the Packages app, in the order they are drawn.
/// </summary>
public enum PackagesTab
{
	/// <summary>The apps the player installed: Store apps, not the ones that ship with the game.</summary>
	Installed,

	Store,

	/// <summary>The apps that ship with the game or a library, installed or not.</summary>
	Prepackaged,
}

/// <summary>
/// One line of an expanded Store entry: an icon, a key and a value, the value already formatted for display.
/// </summary>
public sealed record PackageFact( string Icon, string Key, string Value );

/// <summary>
/// One entry of the Store list, ready to draw: no state to check and nothing to format. The view model decides all
/// of it, including whether the details are out and which words the facts read in.
/// </summary>
public sealed record PackageRow
{
	/// <summary>The package's full ident: what the entry installs and what the details are keyed by.</summary>
	public string Ident { get; init; }

	public string Title { get; init; }
	public string Summary { get; init; }
	public string Thumb { get; init; }

	/// <summary>Published by one of the Store's official organisations.</summary>
	public bool Official { get; init; }

	public PackageAction Action { get; init; }

	/// <summary>Its details are showing.</summary>
	public bool Expanded { get; init; }

	/// <summary>The long description. Empty unless <see cref="Expanded"/>.</summary>
	public string About { get; init; }

	/// <summary>The facts under the description. Empty unless <see cref="Expanded"/>, and while they are still loading.</summary>
	public IReadOnlyList<PackageFact> Facts { get; init; } = [];
}

/// <summary>
/// One entry of the Apps list, ready to draw.
/// </summary>
public sealed record AppRow
{
	/// <summary>The app's id: what the row installs or uninstalls.</summary>
	public string Id { get; init; }

	public string Name { get; init; }
	public string Description { get; init; }
	public string Icon { get; init; }
	public string Color { get; init; }

	/// <summary>Draw the shield: the app has, or asks for, full system access.</summary>
	public bool Shield { get; init; }

	/// <summary>What the shield means for this app, for its tooltip.</summary>
	public string ShieldTooltip { get; init; }

	public AppAction Action { get; init; }

	/// <summary>Its details are showing.</summary>
	public bool Expanded { get; init; }

	/// <summary>The long description. Empty unless <see cref="Expanded"/>.</summary>
	public string About { get; init; }

	/// <summary>The facts under the description. Empty unless <see cref="Expanded"/>.</summary>
	public IReadOnlyList<PackageFact> Facts { get; init; } = [];
}

/// <summary>
/// One kind of app an app tab can show, with whether it is the one being shown.
/// </summary>
public sealed record FilterItem( string Key, string Label, bool Selected );

/// <summary>
/// Everything the Packages panel draws, and nothing it works out: the panel reads these fields and prints them rune
/// for rune. It is replaced as a whole on every change, so a page drawn from one snapshot is never half of another.
/// </summary>
/// <remarks>
/// A UI state like this is the app-side half of Navi's design: the panel is a stateless view, the view model
/// (<see cref="PackagesViewModel"/>) is the logic, and this is the one thing that crosses between them. A Store app
/// does the same with its own two classes; nothing here is a Navi type, so the pattern works for any app.
/// </remarks>
public sealed record PackagesUiState
{
	/// <summary>
	/// Goes up whenever anything else here changes. The panel's <c>BuildHash</c> is this, so it is what makes it redraw.
	/// </summary>
	public int Revision { get; init; }

	/// <summary>What a viewer draws until the host's state arrives.</summary>
	public static PackagesUiState Empty { get; } = new();

	/// <summary>The tab that is up.</summary>
	public PackagesTab Tab { get; init; }

	/// <summary>The Store tab is up, rather than one of the app lists.</summary>
	public bool ShowStore => Tab == PackagesTab.Store;

	/// <summary>The filter menu is up.</summary>
	public bool FilterOpen { get; init; }

	/// <summary>The key of the kind the current app tab is showing. One of the <see cref="Filters"/> keys.</summary>
	public string Filter { get; init; }

	/// <summary>The kinds the current app tab can show, in menu order.</summary>
	public IReadOnlyList<FilterItem> Filters { get; init; } = [];

	/// <summary>The current kind's name, for the filter button's tooltip.</summary>
	public string FilterName { get; init; }

	/// <summary>Shown in the Apps list when no app matches the kind being shown.</summary>
	public string EmptyText { get; init; }

	/// <summary>Message shown above the list, e.g. when the player denied a permission. Null when there is none.</summary>
	public string Notice { get; init; }

	/// <summary>What the Store search field holds.</summary>
	public string SearchText { get; init; } = "";

	/// <summary>A Store page is being fetched. The view shows its own status while this is on.</summary>
	public bool Refreshing { get; init; }

	/// <summary>The result of the last Store search, or null. Never set while <see cref="Refreshing"/> is on.</summary>
	public string StoreStatus { get; init; }

	/// <summary>
	/// Why the last Store search couldn't go out (<see cref="Navi.NetworkStatus.OfflineHint"/>), or null if the computer was
	/// online. The view shows it in place of the Store.
	/// </summary>
	public string OfflineHint { get; init; }

	/// <summary>The Store entries fetched so far. The backend pages them, so this grows with "More".</summary>
	public IReadOnlyList<PackageRow> StoreRows { get; init; } = [];

	/// <summary>How many entries match the current search in all, not how many are listed.</summary>
	public int StoreTotal { get; init; }

	/// <summary>
	/// Goes up with every search that starts the Store list over (a new query or a refresh, not "More"). The panel
	/// scrolls the list back to the top when it changes.
	/// </summary>
	public int StoreSearch { get; init; }

	public int StoreShown => StoreRows.Count;

	/// <summary>There is another page of the Store behind the ones listed.</summary>
	public bool HasMore => !Refreshing && StoreShown < StoreTotal;

	/// <summary>The Apps entries matching the current kind.</summary>
	public IReadOnlyList<AppRow> AppRows { get; init; } = [];

	/// <summary>No app matches the kind being shown.</summary>
	public bool AppListEmpty => AppRows.Count == 0;
}
