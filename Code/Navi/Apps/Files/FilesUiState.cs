namespace NaviApps;

/// <summary>
/// A place in the sidebar: a folder, the trash, or a drive. <see cref="Ejectable"/> drives show an eject button.
/// </summary>
public sealed record PlaceItem( string Path, string Label, string Icon, bool Selected, bool Ejectable = false );

/// <summary>
/// A part of the path above the list, which opens that folder. <see cref="Ahead"/> parts are below the open folder:
/// where the player came from before going up, drawn grayed out so they can go back down.
/// </summary>
public sealed record Crumb( string Path, string Label, bool Ahead = false );

/// <summary>
/// An entry in the open folder. <see cref="Detail"/> is a file's size or a folder's item count; <see cref="Badge"/> marks
/// a link ("link" for a symlink, "alias" for an alias, empty otherwise); <see cref="Faded"/> is a hidden entry shown
/// anyway; <see cref="Cursor"/> is where the arrow keys move from. <see cref="Thumbnail"/> is a picture's contents' hash
/// (<c>Navi.Files.GetHashesAsync</c>), which grid view shows small (<see cref="FilesThumbnails"/>): it changes when the
/// contents do, so the thumbnail is asked for again.
/// </summary>
public sealed record FileRow(
	string Path,
	string Name,
	string Icon,
	string Detail,
	string Modified,
	string Badge,
	bool Selected,
	bool Faded,
	bool Cursor,
	string Thumbnail = "" );

/// <summary>
/// A question over the window before something that can't be undone, such as emptying the trash. <see cref="Button"/>
/// is the label of the button that goes ahead.
/// </summary>
public sealed record ConfirmDialog( string Title, string Text, string Button );

/// <summary>
/// Everything the Files panel draws. Replaced whole by <see cref="FilesViewModel"/> on every change.
/// </summary>
public sealed record FilesUiState
{
	/// <summary>What a viewer draws until the host's state arrives.</summary>
	public static FilesUiState Empty { get; } = new();

	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>The open folder, as the player got there (through symlinks, it may differ from where it really is).</summary>
	public string Path { get; init; } = "/";

	public IReadOnlyList<Crumb> Crumbs { get; init; } = [];
	public IReadOnlyList<PlaceItem> Places { get; init; } = [];
	public IReadOnlyList<PlaceItem> Drives { get; init; } = [];
	public IReadOnlyList<FileRow> Rows { get; init; } = [];

	/// <summary>Shown in place of the list when it is empty, or when the folder couldn't be opened.</summary>
	public string EmptyText { get; init; }

	/// <summary>The bottom line: how many entries, and how full the drive is.</summary>
	public string Status { get; init; } = "";

	/// <summary>What went wrong last, until the player dismisses it.</summary>
	public string Notice { get; init; }

	public bool CanBack { get; init; }
	public bool CanForward { get; init; }
	public bool CanUp { get; init; }

	/// <summary>The open folder is a trash folder: entries there are restored or deleted for good.</summary>
	public bool InTrash { get; init; }

	/// <summary>Entries are selected, and they can all be moved or deleted.</summary>
	public bool CanChange { get; init; }

	/// <summary>One entry is selected, and it can be renamed.</summary>
	public bool CanRename { get; init; }

	public bool HasSelection { get; init; }
	public bool CanPaste { get; init; }
	public bool ShowHidden { get; init; }

	/// <summary>Entries are drawn as a grid of icons instead of a list of rows.</summary>
	public bool GridView { get; init; }

	/// <summary>The entry being renamed (or just made), or null. The rename bar shows while it is set.</summary>
	public string RenamePath { get; init; }

	/// <summary>
	/// What the rename field holds. Typing replaces it without bumping <see cref="Revision"/>; see
	/// <see cref="FilesViewModel.SetRenameText"/>.
	/// </summary>
	public string RenameText { get; init; } = "";

	/// <summary>Counts rename bars opened, so the panel focuses the field once for each.</summary>
	public int RenameSession { get; init; }

	/// <summary>The right-click menu's items, or empty while it is closed.</summary>
	public IReadOnlyList<NaviMenuItem> Menu { get; init; } = [];

	/// <summary>
	/// What the open menu is about: the selected entries, or the open folder when none are. "Copy path" copies these.
	/// </summary>
	public IReadOnlyList<string> MenuPaths { get; init; } = [];

	/// <summary>Over the menu when several entries are selected: how many. Null otherwise.</summary>
	public string MenuHeading { get; init; }

	/// <summary>Where the menu was opened, in the panel's pixels from its top left. The panel keeps it on screen.</summary>
	public float MenuX { get; init; }

	public float MenuY { get; init; }

	/// <summary>The question waiting for the player's answer, or null. It covers the window until answered.</summary>
	public ConfirmDialog Confirm { get; init; }

	public bool MenuOpen => Menu.Count > 0;

	public bool Renaming => RenamePath is not null;
	public bool ListEmpty => Rows.Count == 0;
}
