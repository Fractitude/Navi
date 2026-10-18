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
	string selected;
	bool showHidden;
	string notice;

	/// <summary>What Copy or Cut took, for Paste.</summary>
	(string Path, bool Cut)? clipboard;

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

		if ( selected is not null && entries.All( x => x.Path != selected ) )
			selected = null;

		Rebuild();
	}

	/// <summary>
	/// Run an action from the panel, one case per control. Arguments the action needs and doesn't get are ignored, as
	/// if nothing had been clicked.
	/// </summary>
	public void Act( string action, string[] args )
	{
		var arg = args is { Length: > 0 } ? args[0] : null;

		switch ( action )
		{
			case OpenPathAction when arg is not null: OpenPath( arg ); break;
			case OpenAction when arg is not null: _ = OpenAsync( arg ); break;
			case SelectAction: Select( arg ); break;
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
		path = to;
		selected = null;
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
				OpenPath( FolderOf( entry ) );
				selected = entry;
				Rebuild();
				return;
			}

			Notify( $"There's no app to open \"{to.Name}\" yet." );
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
		}
	}

	public void Select( string entry )
	{
		if ( renamePath is not null && entry != renamePath )
			renamePath = null;

		selected = entries.Any( x => x.Path == entry ) ? entry : null;
		Rebuild();
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
		selected = made;
		StartRename();
	}

	public void StartRename()
	{
		if ( !CanChange( selected ) )
			return;

		renamePath = selected;
		UiState = UiState with { RenameText = NameOf( selected ), RenameSession = UiState.RenameSession + 1 };
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
			selected = Join( FolderOf( from ), name );

		await RefreshAsync();
	}

	public void CancelRename()
	{
		renamePath = null;
		Rebuild();
	}

	/// <summary>
	/// Move the selection to the trash, or in the trash delete it for good.
	/// </summary>
	async Task TrashAsync()
	{
		if ( !CanChange( selected ) )
			return;

		var entry = selected;
		if ( IsTrash( path ) )
			await RunAsync( async () => { await navi.Files.DeleteAsync( entry ); return entry; } );
		else
			await RunAsync( () => navi.Files.TrashAsync( entry ) );

		await RefreshAsync();
	}

	async Task RestoreAsync()
	{
		if ( !CanChange( selected ) )
			return;

		var entry = selected;
		await RunAsync( () => navi.Files.RestoreAsync( entry ) );
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
	/// Make an alias ("name alias") or a symlink ("name link") to the selection, next to it.
	/// </summary>
	async Task MakeLinkAsync( bool alias )
	{
		if ( selected is not { } target )
			return;

		var at = Join( path, FreeName( $"{NameOf( target )} {(alias ? "alias" : "link")}" ) );
		var made = await RunAsync( () => alias ? navi.Files.CreateAliasAsync( at, target ) : navi.Files.CreateSymlinkAsync( at, target ) );
		await RefreshAsync();

		if ( made is not null )
		{
			selected = made;
			Rebuild();
		}
	}

	void Copy( bool cut )
	{
		if ( selected is null || (cut && !CanChange( selected )) )
			return;

		clipboard = (selected, cut);
		Rebuild();
	}

	/// <summary>
	/// Copy what Copy took into the open folder, or move what Cut took. A name that is taken gets a number.
	/// </summary>
	async Task PasteAsync()
	{
		if ( clipboard is not { } taken )
			return;

		// Cut and pasted where it already is: nothing to move.
		if ( taken.Cut && FolderOf( taken.Path ) == path )
			return;

		var to = Join( path, FreeName( NameOf( taken.Path ) ) );

		var pasted = await RunAsync( () => taken.Cut ? navi.Files.MoveAsync( taken.Path, to ) : navi.Files.CopyAsync( taken.Path, to ) );
		if ( taken.Cut && pasted is not null )
			clipboard = null;

		await RefreshAsync();
		if ( pasted is not null )
		{
			selected = to;
			Rebuild();
		}
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

	bool CanChange( string entry ) =>
		entry is not null && entries.FirstOrDefault( x => x.Path == entry ) is { IsDrive: false };

	bool IsTrash( string folder ) => NameOf( folder ) == TrashName;

	/// <summary>
	/// <paramref name="name"/>, or "name 2.txt" and so on if the open folder has one.
	/// </summary>
	string FreeName( string name )
	{
		var taken = entries.Select( x => NameOf( x.Path ) ).ToHashSet( System.StringComparer.Ordinal );
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

		var rows = shown.Take( MaxRows ).Select( x => new FileRow(
			x.Path, x.Name, IconOf( x ), DetailOf( x ), x.Modified.ToLocalTime().ToString( "yyyy-MM-dd HH:mm" ),
			x.Kind switch { Navi.FileKind.Symlink => "link", Navi.FileKind.Alias => "alias", _ => "" },
			x.Path == selected, x.Name.StartsWith( '.' ) ) ).ToList();

		var drive = CurrentDrive;
		var count = shown.Count == 1 ? "1 item" : $"{shown.Count} items";
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
			CanChange = CanChange( selected ),
			HasSelection = selected is not null,
			CanPaste = clipboard is not null && !inTrash,
			ShowHidden = showHidden,
			RenamePath = renamePath,
		};
	}

	List<Crumb> BuildCrumbs()
	{
		var crumbs = new List<Crumb> { new( "/", drives.FirstOrDefault( x => x.Path == "/" )?.Label ?? "/" ) };
		var at = "";
		foreach ( var part in path.Split( '/', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			at += "/" + part;
			crumbs.Add( new Crumb( at, part ) );
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
