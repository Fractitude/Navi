using System.Threading.Tasks;

namespace NaviApps;

/// <summary>One line of the file picker's list.</summary>
public sealed record FilePickerRow( string Name, string Path, bool IsFolder, string Icon );

/// <summary>
/// Everything the file picker panel (<c>FilePicker.razor</c>) draws.
/// </summary>
public sealed record FilePickerUiState
{
	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>The picker is showing. When it isn't, the panel draws nothing.</summary>
	public bool IsOpen { get; init; }

	/// <summary>The folder being listed.</summary>
	public string Path { get; init; } = "/";

	public IReadOnlyList<FilePickerRow> Rows { get; init; } = [];

	/// <summary>The path of the row the player clicked, or empty.</summary>
	public string Selected { get; init; } = "";

	/// <summary>Why a folder couldn't be listed, or what is wrong with the choice. Empty when there is nothing to say.</summary>
	public string Notice { get; init; } = "";

	public bool Loading { get; init; }
}

/// <summary>
/// What the File Picker window does: it lists the computer's folders and tells the computer which file the player chose.
/// The File Picker is a system app, so it has the files; an app that wants a file asks the computer
/// (<c>Navi.Files.PickAsync</c>) and never sees the folders. The panel is <c>FilePickerWindow.razor</c>.
/// </summary>
public sealed class FilePickerViewModel
{
	readonly Navi navi;
	System.Action<string> onPicked;
	System.Action onCancelled;
	string[] extensions = [];

	public FilePickerViewModel( Navi navi )
	{
		this.navi = navi;
	}

	public FilePickerUiState UiState { get; private set; } = new();
	public int Revision => UiState.Revision;

	void Update( System.Func<FilePickerUiState, FilePickerUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };

	/// <summary>
	/// Start listing. <paramref name="startFolder"/> is where it starts (the root when null or not a folder),
	/// <paramref name="fileTypes"/> the extensions shown without the dot (every file when empty),
	/// <paramref name="picked"/> is called with the path of the file the player chose and <paramref name="cancelled"/>
	/// when they gave up.
	/// </summary>
	public void Show( System.Action<string> picked, System.Action cancelled, string startFolder, params string[] fileTypes )
	{
		onPicked = picked;
		onCancelled = cancelled;
		extensions = fileTypes?.Select( x => x.ToLowerInvariant() ).ToArray() ?? [];
		Update( uiState => uiState with { IsOpen = true, Selected = "", Notice = "" } );
		_ = ListAsync( startFolder ?? "/" );
	}

	public void Cancel()
	{
		Update( uiState => uiState with { IsOpen = false, Rows = [], Selected = "" } );
		onCancelled?.Invoke();
	}

	public void Up()
	{
		var path = UiState.Path.TrimEnd( '/' );
		if ( path == "" )
			return;

		_ = ListAsync( path[..path.LastIndexOf( '/' )] is { Length: > 0 } parent ? parent : "/" );
	}

	public void Select( string path ) => Update( uiState => uiState with { Selected = path, Notice = "" } );

	/// <summary>Double click on a row: go into a folder, choose a file.</summary>
	public void Activate( string path )
	{
		var row = UiState.Rows.FirstOrDefault( x => x.Path == path );
		if ( row is null )
			return;

		if ( row.IsFolder )
			_ = ListAsync( row.Path );
		else
			Choose( row.Path );
	}

	/// <summary>The "Open" button: open the selected folder, or choose the selected file.</summary>
	public void Confirm()
	{
		if ( UiState.Selected == "" )
		{
			Update( uiState => uiState with { Notice = "Pick a file first." } );
			return;
		}

		Activate( UiState.Selected );
	}

	void Choose( string path )
	{
		Update( uiState => uiState with { IsOpen = false, Rows = [], Selected = "" } );
		onPicked?.Invoke( path );
	}

	async Task ListAsync( string folder )
	{
		Update( uiState => uiState with { Loading = true, Notice = "" } );

		try
		{
			var info = await navi.Files.GetInfoAsync( folder );
			if ( info is not { IsFolder: true } && folder != "/" )
			{
				folder = "/";
				info = await navi.Files.GetInfoAsync( folder );
			}

			var entries = await navi.Files.ListAsync( folder );
			var rows = entries
				.Where( x => !x.Name.StartsWith( '.' ) && (x.IsFolder || Matches( x.Name )) )
				.OrderBy( x => x.IsFolder ? 0 : 1 )
				.ThenBy( x => x.Name, System.StringComparer.OrdinalIgnoreCase )
				.Select( x => new FilePickerRow( x.Name, x.Path, x.IsFolder, IconOf( x ) ) )
				.ToList();

			Update( uiState => uiState with
			{
				Loading = false,
				Path = info?.Path ?? folder,
				Rows = rows,
				Selected = "",
				Notice = rows.Count == 0 ? "This folder is empty." : "",
			} );
		}
		catch ( System.Exception e )
		{
			Update( uiState => uiState with
			{
				Loading = false,
				Notice = e is Navi.MissingPermissionException ? "Allow Files access in the taskbar's \"Permissions...\"." : e.Message,
			} );
		}
	}

	bool Matches( string name )
	{
		if ( extensions.Length == 0 )
			return true;

		var dot = name.LastIndexOf( '.' );
		return dot >= 0 && extensions.Contains( name[(dot + 1)..].ToLowerInvariant() );
	}

	static string IconOf( Navi.FileInfo file )
	{
		if ( file.IsFolder )
			return "folder";

		return file.Name.ToLowerInvariant() switch
		{
			var x when x.EndsWith( ".jpg" ) || x.EndsWith( ".jpeg" ) || x.EndsWith( ".png" ) => "image",
			_ => "description",
		};
	}
}
