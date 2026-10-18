using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// Everything the Files app does: which folder is open, what is selected, and which file call a click becomes. The panel
/// (<c>Files.razor</c>) draws <see cref="UiState"/>, and its controls are actions (<c>Navi.Act</c>) that end up in
/// <see cref="Act"/>. The app is shared, since only the host has the files: in multiplayer only the host's copy has this.
/// </summary>
public sealed class FilesViewModel
{
	readonly Navi navi;

	const float PollInterval = 0.25f;

	/// <summary>
	/// The most rows the list shows, so a huge folder can't outgrow the state sent to other players.
	/// </summary>
	const int MaxRows = 300;

	const string StartFolder = "/Documents";
	const string TrashName = ".Trash";

	public const string OpenPathAction = "open-path";
	public const string OpenAction = "open";
	public const string SelectAction = "select";
	public const string BackAction = "back";
	public const string ForwardAction = "forward";
	public const string UpAction = "up";
	public const string NewFolderAction = "new-folder";
	public const string NewFileAction = "new-file";
	public const string StartRenameAction = "start-rename";
	public const string SetRenameTextAction = "set-rename-text";
	public const string SubmitRenameAction = "submit-rename";
	public const string CancelRenameAction = "cancel-rename";
	public const string TrashAction = "trash";
	public const string RestoreAction = "restore";
	public const string EmptyTrashAction = "empty-trash";
	public const string MakeAliasAction = "make-alias";
	public const string MakeSymlinkAction = "make-symlink";
	public const string CopyAction = "copy";
	public const string CutAction = "cut";
	public const string PasteAction = "paste";
	public const string ToggleHiddenAction = "toggle-hidden";
	public const string EjectAction = "eject";
	public const string DismissNoticeAction = "dismiss-notice";
	public const string SelectAllAction = "select-all";
	public const string OpenMenuAction = "open-menu";
	public const string ChooseMenuAction = "choose-menu";
	public const string CloseMenuAction = "close-menu";

	/// <summary>
	/// How <see cref="SelectAction"/> treats what was selected, as its second argument: replace it (none, a plain click),
	/// add or take away the entry (Ctrl+click), or select from the last clicked entry to this one (Shift+click).
	/// </summary>
	public const string ToggleMode = "toggle", RangeMode = "range";

	// What the computer said.

	string path = StartFolder;

	/// <summary>Where the open folder really is, with symlinks followed. It says which drive it is on.</summary>
	string realPath = StartFolder;

	IReadOnlyList<Navi.FileInfo> entries = [];
	IReadOnlyList<Navi.DriveInfo> drives = [];

	/// <summary>Why the open folder couldn't be listed, or null.</summary>
	string listError;

	// What the player did.

	readonly Stack<string> back = new();
	readonly Stack<string> forward = new();

	/// <summary>
	/// The deepest folder on the open folder's path the player has been to: going up keeps it, so the path above the
	/// list still shows where they came from. Opening anywhere off that path starts it over.
	/// </summary>
	string trail = StartFolder;

	/// <summary>The selected entries, in the order they were selected.</summary>
	readonly List<string> selection = new();

	/// <summary>Where a Shift+click or Shift+arrow selection starts from: the entry last clicked on its own.</summary>
	string anchor;

	/// <summary>The entry the arrow keys move from: the one last clicked or reached with them.</summary>
	string cursor;

	/// <summary>An entry to select once the folder being opened is listed.</summary>
	string selectAfterRefresh;

	/// <summary>The paths of the entries as the list shows them, in order, for ranges and the arrow keys.</summary>
	List<string> shownPaths = new();

	bool showHidden;
	string notice;

	/// <summary>The right-click menu while it is open (see <see cref="OpenMenu"/>), else empty.</summary>
	IReadOnlyList<MenuItem> menu = [];

	string menuHeading;
	Vector2 menuAt;

	/// <summary>What Copy or Cut took, for Paste.</summary>
	(IReadOnlyList<string> Paths, bool Cut)? clipboard;

	string renamePath;

	int? knownVersion;
	bool polling;
	RealTimeSince sincePoll;

	public FilesViewModel( Navi navi )
	{
		this.navi = navi ?? throw new System.ArgumentNullException( nameof(navi) );
		Rebuild();
	}

	public FilesUiState UiState { get; private set; } = new();

	public int Revision => UiState.Revision;

	public void Tick()
	{
		if ( !polling && sincePoll > PollInterval )
			_ = PollAsync();
	}

	async Task PollAsync()
	{
		polling = true;
		try
		{
			var version = await navi.Files.GetVersionAsync();
			if ( version != knownVersion )
			{
				knownVersion = version;
				await RefreshAsync();
			}
		}
		catch ( System.Exception e )
		{
			listError = Describe( e );
			Rebuild();
		}
		finally
		{
			polling = false;
			sincePoll = 0;
		}
	}

	/// <summary>
	/// List the open folder again, and the drives. A folder that is gone (deleted, or its drive ejected) gives way to the
	/// nearest one above it that is still there.
	/// </summary>
	async Task RefreshAsync()
	{
		listError = null;
		try
		{
			drives = await navi.Files.GetDrivesAsync();

			var info = await navi.Files.GetInfoAsync( path );
			if ( info is null && IsTrash( path ) )
			{
				// A trash that hasn't been used yet doesn't exist; it is empty all the same.
				entries = [];
				realPath = path;
			}
			else
			{
				while ( info is not { IsFolder: true } && path != "/" )
				{
					path = FolderOf( path );
					info = await navi.Files.GetInfoAsync( path );
				}

				realPath = info?.Path ?? "/";
				entries = await navi.Files.ListAsync( path );
			}
		}
		catch ( System.Exception e )
		{
			entries = [];
			listError = Describe( e );
		}

		selection.RemoveAll( x => entries.All( y => y.Path != x ) );
		if ( selectAfterRefresh is { } wanted && entries.Any( x => x.Path == wanted ) )
			SelectOnly( wanted );

		selectAfterRefresh = null;
		Rebuild();
	}

	/// <summary>
	/// Run an action from the panel, one case per control. Arguments the action needs and doesn't get are ignored, as
	/// if nothing had been clicked.
	/// </summary>
	public void Act( string action, string[] args )
	{
		var arg = args is { Length: > 0 } ? args[0] : null;

		// Anything the player does closes the menu, the way a click anywhere else does.
		if ( action is not (OpenMenuAction or ChooseMenuAction or SetRenameTextAction) )
			menu = [];

		switch ( action )
		{
			case OpenMenuAction:
				OpenMenu( arg, Number( args, 1 ), Number( args, 2 ) );
				break;

			case ChooseMenuAction when arg is not null:
				ChooseMenu( arg );
				break;

			case CloseMenuAction:
				Rebuild();
				break;

			case OpenPathAction when arg is not null: OpenPath( arg ); break;
			case OpenAction when arg is not null: _ = OpenAsync( arg ); break;
			case SelectAction: Select( arg, args is { Length: > 1 } ? args[1] : "" ); break;
			case SelectAllAction: SelectAll(); break;
			case BackAction: GoBack(); break;
			case ForwardAction: GoForward(); break;
			case UpAction: GoUp(); break;
			case NewFolderAction: _ = NewFolderAsync(); break;
			case NewFileAction: _ = NewFileAsync(); break;
			case StartRenameAction: StartRename(); break;
			case SetRenameTextAction: SetRenameText( arg ); break;
			case SubmitRenameAction: _ = SubmitRenameAsync(); break;
			case CancelRenameAction: CancelRename(); break;
			case TrashAction: _ = TrashAsync(); break;
			case RestoreAction: _ = RestoreAsync(); break;
			case EmptyTrashAction: _ = EmptyTrashAsync(); break;
			case MakeAliasAction: _ = MakeLinkAsync( alias: true ); break;
			case MakeSymlinkAction: _ = MakeLinkAsync( alias: false ); break;
			case CopyAction: Copy( cut: false ); break;
			case CutAction: Copy( cut: true ); break;
			case PasteAction: _ = PasteAsync(); break;
			case ToggleHiddenAction: ToggleHidden(); break;
			case EjectAction when arg is not null: _ = EjectAsync( arg ); break;
			case DismissNoticeAction: Notify( null ); break;
		}
	}

	// Going places.

	public void OpenPath( string to )
	{
		if ( to == path )
			return;

		back.Push( path );
		forward.Clear();
		GoTo( to );
	}

	void GoTo( string to )
	{
		if ( !IsWithin( trail, to ) )
			trail = to;

		path = to;
		selection.Clear();
		anchor = cursor = null;
		renamePath = null;
		Rebuild();
		_ = RefreshAsync();
	}

	public void GoBack()
	{
		if ( back.Count == 0 )
			return;

		forward.Push( path );
		GoTo( back.Pop() );
	}

	public void GoForward()
	{
		if ( forward.Count == 0 )
			return;

		back.Push( path );
		GoTo( forward.Pop() );
	}

	public void GoUp()
	{
		if ( path != "/" )
			OpenPath( FolderOf( path ) );
	}

	/// <summary>
	/// What a double-click does: a folder (or a symlink to one) opens in the list, an alias opens what it points at, and a
	/// file waits for an app that can open it.
	/// </summary>
	public async Task OpenAsync( string entry )
	{
		try
		{
			var info = await navi.Files.GetInfoAsync( entry, followLinks: false );
			if ( info is null )
				return;

			if ( info.Kind == Navi.FileKind.Alias )
			{
				var target = await navi.Files.ResolveAliasAsync( entry );
				if ( target is null )
				{
					Notify( $"The original of \"{info.Name}\" can't be found." );
					return;
				}

				entry = target;
			}

			var to = await navi.Files.GetInfoAsync( entry );
			if ( to is null )
			{
				Notify( $"\"{NameOf( entry )}\" points at something that isn't there." );
				return;
			}

			if ( to.IsFolder )
			{
				OpenPath( entry );
				return;
			}

			// An alias to a file shows the file where it is.
			if ( info.Kind == Navi.FileKind.Alias )
			{
				if ( FolderOf( entry ) == path )
				{
					SelectOnly( entry );
					Rebuild();
				}
				else
				{
					OpenPath( FolderOf( entry ) );
					selectAfterRefresh = entry;
				}

				return;
			}

			Notify( $"There's no app to open \"{to.Name}\" yet." );
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
		}
	}

	/// <summary>
	/// A click on an entry, with <paramref name="mode"/> saying which keys were held (<see cref="ToggleMode"/>,
	/// <see cref="RangeMode"/>, or none). A plain click on nothing clears the selection.
	/// </summary>
	public void Select( string entry, string mode = "" )
	{
		if ( renamePath is not null && entry != renamePath )
			renamePath = null;

		if ( entry is null || !shownPaths.Contains( entry ) )
		{
			if ( mode == "" )
				selection.Clear();

			Rebuild();
			return;
		}

		switch ( mode )
		{
			case ToggleMode:
				if ( !selection.Remove( entry ) )
					selection.Add( entry );

				anchor = entry;
				break;

			case RangeMode:
				SelectRange( anchor ?? entry, entry );
				break;

			default:
				SelectOnly( entry );
				break;
		}

		cursor = entry;
		Rebuild();
	}

	void SelectOnly( string entry )
	{
		selection.Clear();
		selection.Add( entry );
		anchor = cursor = entry;
	}

	/// <summary>
	/// Select the entries from <paramref name="from"/> to <paramref name="to"/> as the list shows them, and nothing else.
	/// </summary>
	void SelectRange( string from, string to )
	{
		var (a, b) = (shownPaths.IndexOf( from ), shownPaths.IndexOf( to ));
		if ( a < 0 )
			a = b;

		selection.Clear();
		selection.AddRange( shownPaths.Skip( System.Math.Min( a, b ) ).Take( System.Math.Abs( a - b ) + 1 ) );
		anchor = from;
	}

	public void SelectAll()
	{
		selection.Clear();
		selection.AddRange( shownPaths );
		Rebuild();
	}

	/// <summary>
	/// The arrow keys, Home and End: move to the entry <paramref name="by"/> rows away (or to the first or last), and
	/// select it, or with <paramref name="extend"/> (Shift) everything from the anchor to it.
	/// </summary>
	void MoveCursor( int by, bool extend )
	{
		if ( shownPaths.Count == 0 )
			return;

		var at = cursor is null ? -1 : shownPaths.IndexOf( cursor );
		var to = by switch
		{
			int.MinValue => 0,
			int.MaxValue => shownPaths.Count - 1,
			_ when at < 0 => by > 0 ? 0 : shownPaths.Count - 1,
			_ => int.Clamp( at + by, 0, shownPaths.Count - 1 ),
		};

		var entry = shownPaths[to];
		if ( extend )
			SelectRange( anchor ?? cursor ?? entry, entry );
		else
			SelectOnly( entry );

		cursor = entry;
		renamePath = null;
		Rebuild();
	}

	/// <summary>
	/// A keyboard shortcut (<c>NaviApp.OnShortcut</c>), as in Windows' File Explorer, with Ctrl+Shift+N and Ctrl+H from
	/// Linux file managers. Keys it doesn't know do nothing.
	/// </summary>
	public void Shortcut( string keys )
	{
		if ( menu.Count > 0 )
		{
			menu = [];
			Rebuild();
		}

		// The rename field has the keys while it is open; these are the ones that reach the app around it.
		if ( renamePath is not null && keys is not ("enter" or "f2") )
			return;

		switch ( keys )
		{
			case "up": MoveCursor( -1, false ); break;
			case "down": MoveCursor( 1, false ); break;
			case "shift+up": MoveCursor( -1, true ); break;
			case "shift+down": MoveCursor( 1, true ); break;
			case "home": MoveCursor( int.MinValue, false ); break;
			case "end": MoveCursor( int.MaxValue, false ); break;
			case "shift+home": MoveCursor( int.MinValue, true ); break;
			case "shift+end": MoveCursor( int.MaxValue, true ); break;
			case "ctrl+a": SelectAll(); break;
			case "enter" when renamePath is not null: _ = SubmitRenameAsync(); break;
			case "enter" when selection.Count == 1: _ = OpenAsync( selection[0] ); break;
			case "f2" when renamePath is null: StartRename(); break;
			case "delete": _ = TrashAsync(); break;
			case "ctrl+c": Copy( cut: false ); break;
			case "ctrl+x": Copy( cut: true ); break;
			case "ctrl+v": _ = PasteAsync(); break;
			case "ctrl+shift+n": _ = NewFolderAsync(); break;
			case "ctrl+h": ToggleHidden(); break;
			case "backspace" or "alt+up": GoUp(); break;
			case "alt+left": GoBack(); break;
			case "alt+right": GoForward(); break;
		}
	}

	// The right-click menu.

	static float Number( string[] args, int index ) =>
		args is not null && index < args.Length
		&& float.TryParse( args[index], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value )
		&& float.IsFinite( value )
			? value
			: 0f;

	/// <summary>
	/// A right-click at (<paramref name="x"/>, <paramref name="y"/>) in the panel, on <paramref name="entry"/> or on the
	/// empty list (null). An entry that isn't selected becomes the selection, as in Windows; one that is keeps the whole
	/// selection, so the menu acts on all of it. On the empty list nothing stays selected, and the menu is about the folder.
	/// </summary>
	public void OpenMenu( string entry, float x, float y )
	{
		renamePath = null;

		if ( entry is not null && shownPaths.Contains( entry ) )
		{
			if ( !selection.Contains( entry ) )
				SelectOnly( entry );

			cursor = entry;
		}
		else
		{
			selection.Clear();
		}

		menuAt = new Vector2( x, y );
		(menu, menuHeading) = BuildMenu();
		Rebuild();
	}

	/// <summary>
	/// What the menu offers for what is selected: one entry, several, or (nothing selected) the open folder.
	/// </summary>
	(IReadOnlyList<MenuItem> Items, string Heading) BuildMenu()
	{
		var inTrash = IsTrash( path );
		var canChange = CanChange( selection );

		if ( selection.Count == 0 )
		{
			if ( inTrash )
			{
				return ([
					new( EmptyTrashAction, "Empty trash", "delete_sweep", "", entries.Count > 0 ),
					new( SelectAllAction, "Select all", "select_all", "Ctrl+A", shownPaths.Count > 0, Separator: true ),
				], null);
			}

			return ([
				new( NewFolderAction, "New folder", "create_new_folder", "Ctrl+Shift+N" ),
				new( NewFileAction, "New text file", "note_add" ),
				new( PasteAction, "Paste", "content_paste", "Ctrl+V", clipboard is not null, Separator: true ),
				new( SelectAllAction, "Select all", "select_all", "Ctrl+A", shownPaths.Count > 0, Separator: true ),
				new( ToggleHiddenAction, showHidden ? "Hide hidden files" : "Show hidden files",
					showHidden ? "visibility_off" : "visibility", "Ctrl+H" ),
			], null);
		}

		if ( inTrash )
		{
			return ([
				new( RestoreAction, "Put back", "restore_from_trash", "", canChange ),
				new( TrashAction, "Delete for good", "delete_forever", "Delete", canChange ),
			], selection.Count > 1 ? $"{selection.Count} items" : null);
		}

		if ( selection.Count > 1 )
		{
			return ([
				new( MakeAliasAction, "Make aliases", "shortcut" ),
				new( MakeSymlinkAction, "Make symlinks", "link" ),
				new( CopyAction, "Copy", "content_copy", "Ctrl+C", Separator: true ),
				new( CutAction, "Cut", "content_cut", "Ctrl+X", canChange ),
				new( TrashAction, "Move to trash", "delete", "Delete", canChange, Separator: true ),
			], $"{selection.Count} items");
		}

		var entry = entries.FirstOrDefault( x => x.Path == selection[0] );
		if ( entry is { IsDrive: true } )
		{
			return ([
				new( OpenAction, "Open", "folder_open", "Enter" ),
				new( EjectAction, "Eject", "eject", Separator: true ),
			], null);
		}

		return ([
			new( OpenAction, entry?.Kind == Navi.FileKind.Alias ? "Show original" : "Open", "folder_open", "Enter" ),
			new( StartRenameAction, "Rename", "edit", "F2", canChange, Separator: true ),
			new( MakeAliasAction, "Make alias", "shortcut" ),
			new( MakeSymlinkAction, "Make symlink", "link" ),
			new( CopyAction, "Copy", "content_copy", "Ctrl+C", Separator: true ),
			new( CutAction, "Cut", "content_cut", "Ctrl+X", canChange ),
			new( TrashAction, "Move to trash", "delete", "Delete", canChange, Separator: true ),
		], null);
	}

	/// <summary>
	/// The player chose a menu item. Only an item the menu offers, enabled, does anything: the id came over the network.
	/// </summary>
	public void ChooseMenu( string id )
	{
		var item = menu.FirstOrDefault( x => x.Id == id );
		menu = [];
		Rebuild();

		if ( item is not { Enabled: true } )
			return;

		var first = selection.FirstOrDefault();
		switch ( id )
		{
			case OpenAction when first is not null: _ = OpenAsync( first ); break;
			case EjectAction when first is not null: _ = EjectAsync( first ); break;
			default: Act( id, [] ); break;
		}
	}

	// Making and changing.

	async Task NewFolderAsync()
	{
		var made = await RunAsync( () => navi.Files.CreateFolderAsync( Join( path, FreeName( "untitled folder" ) ) ) );
		if ( made is not null )
			await ShowAndRenameAsync( made );
	}

	async Task NewFileAsync()
	{
		var made = await RunAsync( () => navi.Files.WriteTextAsync( Join( path, FreeName( "untitled.txt" ) ), "" ) );
		if ( made is not null )
			await ShowAndRenameAsync( made );
	}

	/// <summary>
	/// Select what was just made and start renaming it, as Finder does.
	/// </summary>
	async Task ShowAndRenameAsync( string made )
	{
		await RefreshAsync();
		SelectOnly( made );
		StartRename();
	}

	/// <summary>
	/// Open the rename bar for the selected entry. Only one can be renamed at a time.
	/// </summary>
	public void StartRename()
	{
		if ( selection.Count != 1 || !CanChange( selection ) )
			return;

		renamePath = selection[0];
		UiState = UiState with { RenameText = NameOf( renamePath ), RenameSession = UiState.RenameSession + 1 };
		Rebuild();
	}

	/// <summary>
	/// What the rename field holds, as the field reports it. Typing is no reason to redraw the list, so this replaces the
	/// state without going through <see cref="Rebuild"/>.
	/// </summary>
	public void SetRenameText( string text ) =>
		UiState = UiState with { RenameText = text is { Length: > 255 } ? text[..255] : text ?? "" };

	async Task SubmitRenameAsync()
	{
		if ( renamePath is not { } from )
			return;

		var name = UiState.RenameText.Trim();
		renamePath = null;

		if ( name == NameOf( from ) || name == "" )
		{
			Rebuild();
			return;
		}

		var moved = await RunAsync( () => navi.Files.MoveAsync( from, Join( FolderOf( from ), name ) ) );
		if ( moved is not null )
			selectAfterRefresh = Join( FolderOf( from ), name );

		await RefreshAsync();
	}

	public void CancelRename()
	{
		renamePath = null;
		Rebuild();
	}

	/// <summary>
	/// Move the selection to the trash, or in the trash delete it for good. One that fails stops the rest.
	/// </summary>
	async Task TrashAsync()
	{
		if ( !CanChange( selection ) )
			return;

		var inTrash = IsTrash( path );
		foreach ( var entry in selection.ToList() )
		{
			var done = inTrash
				? await RunAsync( async () => { await navi.Files.DeleteAsync( entry ); return entry; } )
				: await RunAsync( () => navi.Files.TrashAsync( entry ) );

			if ( done is null )
				break;
		}

		await RefreshAsync();
	}

	async Task RestoreAsync()
	{
		if ( !CanChange( selection ) )
			return;

		foreach ( var entry in selection.ToList() )
		{
			if ( await RunAsync( () => navi.Files.RestoreAsync( entry ) ) is null )
				break;
		}

		await RefreshAsync();
	}

	async Task EmptyTrashAsync()
	{
		if ( !IsTrash( path ) )
			return;

		foreach ( var entry in entries.ToList() )
			await RunAsync( async () => { await navi.Files.DeleteAsync( entry.Path ); return entry.Path; } );

		await RefreshAsync();
	}

	/// <summary>
	/// Make an alias ("name alias") or a symlink ("name link") to each selected entry, next to it. The new ones end up
	/// selected.
	/// </summary>
	async Task MakeLinkAsync( bool alias )
	{
		if ( selection.Count == 0 )
			return;

		var taken = new HashSet<string>( System.StringComparer.Ordinal );
		var made = new List<string>();
		foreach ( var target in selection.ToList() )
		{
			var at = Join( path, FreeName( $"{NameOf( target )} {(alias ? "alias" : "link")}", taken ) );
			taken.Add( NameOf( at ) );

			if ( await RunAsync( () => alias ? navi.Files.CreateAliasAsync( at, target ) : navi.Files.CreateSymlinkAsync( at, target ) ) is not { } link )
				break;

			made.Add( link );
		}

		await RefreshAsync();
		SelectMade( made );
	}

	void Copy( bool cut )
	{
		if ( selection.Count == 0 || (cut && !CanChange( selection )) )
			return;

		clipboard = (selection.ToList(), cut);
		Rebuild();
	}

	/// <summary>
	/// Copy what Copy took into the open folder, or move what Cut took. A name that is taken gets a number. What was
	/// pasted ends up selected.
	/// </summary>
	async Task PasteAsync()
	{
		if ( clipboard is not { } taken || IsTrash( path ) )
			return;

		var names = new HashSet<string>( System.StringComparer.Ordinal );
		var pasted = new List<string>();
		var failed = false;

		foreach ( var from in taken.Paths )
		{
			// Cut and pasted where it already is: nothing to move.
			if ( taken.Cut && FolderOf( from ) == path )
				continue;

			var to = Join( path, FreeName( NameOf( from ), names ) );
			names.Add( NameOf( to ) );

			if ( await RunAsync( () => taken.Cut ? navi.Files.MoveAsync( from, to ) : navi.Files.CopyAsync( from, to ) ) is null )
			{
				failed = true;
				break;
			}

			pasted.Add( to );
		}

		// What was cut is gone from where it was, so it can't be pasted again.
		if ( taken.Cut && !failed )
			clipboard = null;

		await RefreshAsync();
		SelectMade( pasted );
	}

	/// <summary>
	/// Select entries that were just made here, once they are listed.
	/// </summary>
	void SelectMade( List<string> made )
	{
		if ( made.Count == 0 )
			return;

		selection.Clear();
		selection.AddRange( made.Where( shownPaths.Contains ) );
		anchor = cursor = selection.LastOrDefault();
		Rebuild();
	}

	void ToggleHidden()
	{
		showHidden = !showHidden;
		Rebuild();
	}

	async Task EjectAsync( string drivePath )
	{
		await RunAsync( async () => { await navi.Files.EjectAsync( drivePath ); return drivePath; } );
		await RefreshAsync();
	}

	void Notify( string text )
	{
		notice = text;
		Rebuild();
	}

	/// <summary>
	/// Make a call, and show what went wrong if it fails. Returns the call's result, or null if it failed.
	/// </summary>
	async Task<string> RunAsync( System.Func<Task<string>> call )
	{
		try
		{
			var result = await call();
			notice = null;
			return result;
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
			return null;
		}
	}

	static string Describe( System.Exception e ) => e switch
	{
		Navi.MissingPermissionException => "Files isn't allowed to do that.",
		Navi.FileException f => f.Error switch
		{
			Navi.FileError.NotFound => $"\"{NameOf( f.Path )}\" isn't there.",
			Navi.FileError.AlreadyExists => $"There is already an item named \"{NameOf( f.Path )}\".",
			Navi.FileError.NotAFolder => $"\"{NameOf( f.Path )}\" isn't a folder.",
			Navi.FileError.IsAFolder => $"\"{NameOf( f.Path )}\" is a folder.",
			Navi.FileError.NoSpace => "There isn't enough room on the drive.",
			Navi.FileError.InvalidName => "That name can't be used. A name can't be . or .., or have a / in it.",
			Navi.FileError.LinkLoop => "Those links point at each other in a loop.",
			Navi.FileError.Protected => $"\"{NameOf( f.Path )}\" can't be changed.",
			Navi.FileError.Unavailable => "The files are on the host's computer.",
			_ => f.Message,
		},
		_ => e.Message,
	};

	// Building the state.

	/// <summary>
	/// Something is selected, and all of it can be moved, renamed or deleted: no drives.
	/// </summary>
	bool CanChange( IReadOnlyCollection<string> paths ) =>
		paths.Count > 0 && paths.All( p => entries.FirstOrDefault( x => x.Path == p ) is { IsDrive: false } );

	bool IsTrash( string folder ) => NameOf( folder ) == TrashName;

	/// <summary>
	/// <paramref name="name"/>, or "name 2.txt" and so on if the open folder has one, or <paramref name="alsoTaken"/>
	/// does (names about to be used).
	/// </summary>
	string FreeName( string name, IEnumerable<string> alsoTaken = null )
	{
		var taken = entries.Select( x => NameOf( x.Path ) ).Concat( alsoTaken ?? [] ).ToHashSet( System.StringComparer.Ordinal );
		var dot = name.LastIndexOf( '.' );
		var (stem, extension) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");

		var candidate = name;
		for ( var i = 2; taken.Contains( candidate ); i++ )
			candidate = $"{stem} {i}{extension}";

		return candidate;
	}

	/// <summary>
	/// The drive the open folder is on: the one mounted deepest above where it really is.
	/// </summary>
	Navi.DriveInfo CurrentDrive =>
		drives.Where( x => IsWithin( realPath, x.Path ) ).OrderByDescending( x => x.Path.Length ).FirstOrDefault();

	void Rebuild()
	{
		var shown = entries.Where( x => showHidden || !x.Name.StartsWith( '.' ) )
			.OrderBy( x => x.IsFolder ? 0 : 1 )
			.ThenBy( x => x.Name, System.StringComparer.OrdinalIgnoreCase )
			.ThenBy( x => x.Name, System.StringComparer.Ordinal )
			.ToList();

		shownPaths = shown.Take( MaxRows ).Select( x => x.Path ).ToList();
		selection.RemoveAll( x => !shownPaths.Contains( x ) );

		var rows = shown.Take( MaxRows ).Select( x => new FileRow(
			x.Path, x.Name, IconOf( x ), DetailOf( x ), x.Modified.ToLocalTime().ToString( "yyyy-MM-dd HH:mm" ),
			x.Kind switch { Navi.FileKind.Symlink => "link", Navi.FileKind.Alias => "alias", _ => "" },
			selection.Contains( x.Path ), x.Name.StartsWith( '.' ), x.Path == cursor ) ).ToList();

		var drive = CurrentDrive;
		var count = shown.Count == 1 ? "1 item" : $"{shown.Count} items";
		if ( selection.Count > 1 )
			count = $"{selection.Count} of {shown.Count} selected";

		if ( shown.Count > MaxRows )
			count += $", {MaxRows} shown";

		var trash = drives.FirstOrDefault( x => x.Path == "/" ) is not null ? "/" + TrashName : null;
		var inTrash = IsTrash( path );

		UiState = UiState with
		{
			Revision = UiState.Revision + 1,
			Path = path,
			Crumbs = BuildCrumbs(),
			Places = new List<PlaceItem>
			{
				new( "/Documents", "Documents", "description", path == "/Documents" ),
				new( "/Downloads", "Downloads", "download", path == "/Downloads" ),
				new( trash ?? "/" + TrashName, "Trash", "delete", path == (trash ?? "/" + TrashName) ),
			},
			Drives = drives.Select( x => new PlaceItem( x.Path, x.Label, x.Removable ? "usb" : "storage", path == x.Path, x.Removable ) ).ToList(),
			Rows = rows,
			EmptyText = listError ?? (inTrash ? "The trash is empty." : "This folder is empty."),
			Status = drive is null ? count : $"{count}  ·  {FormatSize( drive.Used )} of {FormatSize( drive.Capacity )} used on {drive.Label}",
			Notice = notice,
			CanBack = back.Count > 0,
			CanForward = forward.Count > 0,
			CanUp = path != "/",
			InTrash = inTrash,
			CanChange = CanChange( selection ),
			CanRename = selection.Count == 1 && CanChange( selection ),
			HasSelection = selection.Count > 0,
			CanPaste = clipboard is not null && !inTrash,
			ShowHidden = showHidden,
			RenamePath = renamePath,
			Menu = menu,
			MenuHeading = menu.Count > 0 ? menuHeading : null,
			MenuX = menuAt.x,
			MenuY = menuAt.y,
		};
	}

	List<Crumb> BuildCrumbs()
	{
		var crumbs = new List<Crumb> { new( "/", drives.FirstOrDefault( x => x.Path == "/" )?.Label ?? "/" ) };
		var at = "";
		foreach ( var part in trail.Split( '/', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			at += "/" + part;
			crumbs.Add( new Crumb( at, part, !IsWithin( path, at ) ) );
		}

		return crumbs;
	}

	static string IconOf( Navi.FileInfo entry ) => entry.Kind switch
	{
		Navi.FileKind.Folder when entry.IsDrive => "usb",
		Navi.FileKind.Folder => "folder",
		Navi.FileKind.Symlink => "link",
		Navi.FileKind.Alias => "shortcut",
		_ when entry.Name.EndsWith( ".txt", System.StringComparison.OrdinalIgnoreCase ) => "description",
		_ => "draft",
	};

	static string DetailOf( Navi.FileInfo entry ) => entry.Kind switch
	{
		Navi.FileKind.Folder => entry.Items == 1 ? "1 item" : $"{entry.Items} items",
		Navi.FileKind.File => FormatSize( entry.Size ),
		_ => "→ " + entry.Target,
	};

	public static string FormatSize( long bytes ) => bytes switch
	{
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
		_ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
	};

	static string Join( string folder, string name ) => folder == "/" ? "/" + name : folder + "/" + name;

	static string NameOf( string entry ) => entry is null or "/" ? "" : entry[(entry.LastIndexOf( '/' ) + 1)..];

	static string FolderOf( string entry )
	{
		var slash = entry.LastIndexOf( '/' );
		return slash <= 0 ? "/" : entry[..slash];
	}

	static bool IsWithin( string entry, string folder ) =>
		entry == folder || folder == "/" || entry.StartsWith( folder + "/", System.StringComparison.Ordinal );
}
