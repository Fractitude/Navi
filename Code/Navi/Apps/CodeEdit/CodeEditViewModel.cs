using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// Everything Code Edit does: the open document, where it is saved, and which file call a click becomes. The panel
/// (<c>CodeEdit.razor</c>) draws <see cref="UiState"/>, and its controls are actions (<c>Navi.Act</c>) that end up in
/// <see cref="Act"/>. The app is shared, since only the host has the files: in multiplayer only the host's copy has this.
/// </summary>
/// <remarks>
/// The document is in the state every player is sent, so it has a size limit (<see cref="MaxChars"/>,
/// <see cref="MaxSharedText"/>), and typing reaches it as small edits (<see cref="EditAction"/>), never the whole text.
/// </remarks>
public sealed class CodeEditViewModel
{
	readonly Navi navi;

	const float PollInterval = 0.25f;

	/// <summary>
	/// How often the state is sent to other players while only the text changes, in seconds. Anything else is sent at once.
	/// </summary>
	const float TypingPublishInterval = 0.25f;

	/// <summary>
	/// The longest document, in characters. Bigger files aren't opened: the text goes to every player with the state.
	/// </summary>
	public const int MaxChars = 16000;

	/// <summary>
	/// The most the document can take in the state sent to other players, in characters of JSON, under the computer's
	/// <c>NaviComputer.MaxAppState</c> with room for the rest. Text that needs escaping (line breaks, other alphabets)
	/// takes more, so this is checked as well as <see cref="MaxChars"/>.
	/// </summary>
	const int MaxSharedText = 28000;

	/// <summary>
	/// The most an edit action inserts. A longer insert (a paste) comes as several, each well under <c>Navi.MaxActionArgs</c>.
	/// </summary>
	public const int MaxEditInsert = 3000;

	const string StartFolder = "/Documents";
	static readonly string[] OpenTypes = ["cs", "razor", "scss", "css", "json", "js", "lua", "xml", "html", "txt", "md", "cfg", "ini", "csv", "log"];
	const string DefaultExtension = ".cs";
	const string UntitledName = "Untitled";

	public const string EditAction = "edit";
	public const string NewAction = "new";
	public const string OpenAction = "open";
	public const string SaveAction = "save";
	public const string SaveAsAction = "save-as";
	public const string SetBarTextAction = "set-bar-text";
	public const string SubmitBarAction = "submit-bar";
	public const string CancelBarAction = "cancel-bar";
	public const string ConfirmAction = "confirm";
	public const string CancelConfirmAction = "cancel-confirm";
	public const string ConfirmOtherAction = "confirm-other";
	public const string DismissNoticeAction = "dismiss-notice";
	public const string ToggleThemeAction = "toggle-theme";
	public const string RunAction = "run";

	/// <summary>The file in the app's own folder that holds what the player chose, as JSON.</summary>
	const string PreferencesName = "preferences.json";

	const string ThemeKey = "theme";

	// The document.

	/// <summary>Where it is saved, with symlinks followed, or null if it never was.</summary>
	string path;

	/// <summary>What the file held when it was opened or last saved, to tell whether there are changes.</summary>
	string savedText = "";

	string notice;
	CodeEditBar bar;

	/// <summary>What the yes button of the open question does; null while there is none.</summary>
	System.Func<Task> confirmed;

	/// <summary>What the question's other answer does, if it has one.</summary>
	System.Func<Task> confirmedOther;

	string confirmText;
	string confirmLabel = "";
	string confirmOtherLabel;

	/// <summary>The window closes once the document is saved: the player chose Save when closing it.</summary>
	bool closeAfterSave;

	/// <summary>Whether the window was last set to ask before closing, so it is only set again when that changes.</summary>
	bool? guardingClose;

	bool polling;

	/// <summary>A script is running, or the player is being asked for the permission to run one.</summary>
	bool running;

	/// <summary>The File Picker is open for this app, so Open doesn't ask for a second one.</summary>
	bool picking;
	RealTimeSince sincePoll;

	CodeEditUiState published;
	RealTimeSince sincePublished;

	/// <summary>The window title last set, so it is only set again when it changes.</summary>
	string title;

	public CodeEditViewModel( Navi navi )
	{
		this.navi = navi ?? throw new System.ArgumentNullException( nameof(navi) );
		Rebuild();
		_ = LoadPreferencesAsync();
	}

	public CodeEditUiState UiState { get; private set; } = new();

	public int Revision => UiState.Revision;

	/// <summary>
	/// What other players draw: <see cref="UiState"/>, but while only the text changes, at most every
	/// <see cref="TypingPublishInterval"/>, so typing doesn't send the whole document each key.
	/// </summary>
	public CodeEditUiState Published
	{
		get
		{
			if ( published is null || published.Revision != UiState.Revision || sincePublished > TypingPublishInterval )
			{
				published = UiState;
				sincePublished = 0;
			}

			return published;
		}
	}

	public void Tick()
	{
		if ( !polling && sincePoll > PollInterval )
			_ = PollAsync();

		UpdateTitle();
		UpdateCloseGuard();
	}

	/// <summary>
	/// Have the player asked before closing the window while there are unsaved changes (<see cref="CloseRequested"/>).
	/// </summary>
	void UpdateCloseGuard()
	{
		if ( guardingClose == UiState.Dirty )
			return;

		guardingClose = UiState.Dirty;
		_ = navi.Window.SetCloseGuardAsync( UiState.Dirty );
	}

	/// <summary>
	/// The player is closing the window with unsaved changes (<c>NaviApp.OnCloseRequested</c>): save them, throw them
	/// away, or keep the window. Closing it again closes it anyway; the computer sees to that.
	/// </summary>
	public void CloseRequested()
	{
		if ( !UiState.Dirty )
		{
			_ = navi.Window.CloseAsync();
			return;
		}

		Ask( $"Save the changes to \"{UiState.Name}\" before closing?", "Save", SaveAndCloseAsync,
			"Don't save", () => navi.Window.CloseAsync() );
	}

	async Task SaveAndCloseAsync()
	{
		closeAfterSave = true;
		await SaveAsync();
	}

	/// <summary>
	/// Take the files the player opened with this app, from the file explorer. The last one is opened.
	/// </summary>
	async Task PollAsync()
	{
		polling = true;
		try
		{
			var opened = await navi.Files.TakeOpenedAsync();
			if ( opened.Count > 0 )
				AskToDiscard( () => LoadAsync( opened[^1] ) );

			// The game can turn scripting on or off for the computer, so it is asked again every time.
			var scripting = await navi.Scripts.IsEnabledAsync();
			if ( scripting != UiState.ScriptingEnabled )
			{
				UiState = UiState with { ScriptingEnabled = scripting };
				Rebuild();
			}
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
		}
		finally
		{
			polling = false;
			sincePoll = 0;
		}
	}

	void UpdateTitle()
	{
		var name = UiState.Name.Length > 28 ? UiState.Name[..27] + "…" : UiState.Name;
		var wanted = UiState.Dirty ? name + " •" : name;
		if ( wanted == title )
			return;

		title = wanted;
		_ = navi.Window.SetTitleAsync( wanted );
	}

	/// <summary>
	/// Run an action from the panel, one case per control. Arguments the action needs and doesn't get are ignored, as
	/// if nothing had been clicked.
	/// </summary>
	public void Act( string action, string[] args )
	{
		args ??= [];
		var arg = args.Length > 0 ? args[0] : null;

		// While a question is open it covers the window: only its answers do anything. Typing still lands, so the text
		// field and the document never part.
		if ( confirmed is not null && action is not (ConfirmAction or ConfirmOtherAction or CancelConfirmAction or EditAction) )
			return;

		switch ( action )
		{
			case EditAction when args.Length == 4: Edit( args[0], args[1], args[2], args[3] ); break;
			case NewAction: AskToDiscard( NewDocument ); break;
			case OpenAction: AskToDiscard( PickAsync ); break;
			case SaveAction: _ = SaveAsync(); break;
			case SaveAsAction: OpenBar( CodeEditBar.SaveAs ); break;
			case CancelBarAction: CloseBar(); break;
			case SetBarTextAction: SetBarText( arg ); break;
			case SubmitBarAction: _ = SubmitBarAsync(); break;
			case ConfirmAction: _ = AnswerAsync( confirmed ); break;
			case ConfirmOtherAction when confirmedOther is not null: _ = AnswerAsync( confirmedOther ); break;
			case CancelConfirmAction: _ = AnswerAsync( null ); break;
			case DismissNoticeAction: Notify( null ); break;
			case ToggleThemeAction: _ = SetThemeAsync( !UiState.LightTheme ); break;
			case RunAction: _ = RunAsync(); break;
		}
	}

	/// <summary>
	/// A keyboard shortcut (<c>NaviApp.OnShortcut</c>), as in Notepad. Ctrl ones come even while the text has the caret.
	/// </summary>
	public void Shortcut( string keys )
	{
		if ( confirmed is not null )
		{
			if ( keys == "enter" )
				_ = AnswerAsync( confirmed );

			return;
		}

		switch ( keys )
		{
			case "ctrl+n": AskToDiscard( NewDocument ); break;
			case "ctrl+o": AskToDiscard( PickAsync ); break;
			case "ctrl+s": _ = SaveAsync(); break;
			case "ctrl+shift+s": OpenBar( CodeEditBar.SaveAs ); break;
		}
	}

	// Running.

	/// <summary>
	/// Run the document as a script, if the game allows it. The first time the player is asked for the permission. What
	/// went wrong is shown with its line; a script that ran says so.
	/// </summary>
	async Task RunAsync()
	{
		if ( running || !UiState.ScriptingEnabled )
			return;

		running = true;
		var text = UiState.Text;
		try
		{
			var result = await navi.WithPermissionAsync( () => navi.Scripts.RunAsync( text ) );
			Notify( result.Ok ? "The script ran." : Where( text, result ) + result.Message );
		}
		catch ( Navi.MissingPermissionException )
		{
			Notify( "Code Edit isn't allowed to run scripts. Allow it \"Run scripts\" in the taskbar's \"Permissions...\"." );
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
		}
		finally
		{
			running = false;
		}
	}

	/// <summary>
	/// "Line 3: " for the place in the text an error is about, or nothing when it isn't about a place.
	/// </summary>
	static string Where( string text, Navi.ScriptResult result )
	{
		if ( result.Length <= 0 && result.Start <= 0 )
			return "";

		var line = 1;
		for ( var i = 0; i < System.Math.Min( result.Start, text.Length ); i++ )
		{
			if ( text[i] == '\n' )
				line++;
		}

		return $"Line {line}: ";
	}

	// Preferences.

	/// <summary>
	/// Take the theme the player chose last from the app's folder. The first time there is nothing there, so the
	/// editor follows the computer's color scheme (a dark computer gets the dark editor).
	/// </summary>
	async Task LoadPreferencesAsync()
	{
		var light = false;
		try
		{
			var file = Join( await navi.Files.GetAppFolderAsync(), PreferencesName );
			var chosen = await navi.Files.GetInfoAsync( file ) is { IsFolder: false }
				? Json.Deserialize<Dictionary<string, string>>( await navi.Files.ReadTextAsync( file ) )
				: null;

			if ( chosen is not null && chosen.TryGetValue( ThemeKey, out var theme ) && theme is "light" or "dark" )
				light = theme == "light";
			else
				light = (await navi.Settings.GetThemeAsync()).Scheme == Navi.ColorScheme.Light;
		}
		catch ( System.Exception )
		{
			// Unreadable preferences: the dark editor, and the next choice writes them again.
		}

		UiState = UiState with { LightTheme = light, ThemeKnown = true };
		Rebuild();
	}

	async Task SetThemeAsync( bool light )
	{
		UiState = UiState with { LightTheme = light, ThemeKnown = true };
		Rebuild();

		try
		{
			var folder = await navi.Files.GetAppFolderAsync();
			if ( await navi.Files.GetInfoAsync( folder ) is null )
			{
				// A folder at a time: Navi.Files makes one level.
				var made = "";
				foreach ( var part in folder.Split( '/', System.StringSplitOptions.RemoveEmptyEntries ) )
				{
					made += "/" + part;
					if ( await navi.Files.GetInfoAsync( made ) is null )
						await navi.Files.CreateFolderAsync( made );
				}
			}

			await navi.Files.WriteTextAsync( Join( folder, PreferencesName ),
				Json.Serialize( new Dictionary<string, string> { [ThemeKey] = light ? "light" : "dark" } ) );
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
		}
	}

	// Typing.

	/// <summary>
	/// The player typed: replace <paramref name="removed"/> characters at <paramref name="start"/> with
	/// <paramref name="inserted"/>. The panel works out the edit from what its field held before and after, so only the
	/// change crosses the network. An edit made for an older <see cref="CodeEditUiState.TextSession"/> (the text was
	/// replaced since) is dropped. One that doesn't fit the text, or makes it too long, is refused, and the field is
	/// given the document again.
	/// </summary>
	public void Edit( string session, string start, string removed, string inserted )
	{
		if ( !int.TryParse( session, out var forSession ) || forSession != UiState.TextSession )
			return;

		var text = UiState.Text;
		if ( !int.TryParse( start, out var at ) || !int.TryParse( removed, out var count ) || inserted is null
			|| at < 0 || at > text.Length || count < 0 || count > text.Length - at || inserted.Length > MaxEditInsert )
		{
			Resync( null );
			return;
		}

		var edited = text[..at] + inserted + text[(at + count)..];
		if ( TooLong( edited ) )
		{
			Resync( $"The document can't be longer than {MaxChars:N0} characters here." );
			return;
		}

		var dirty = edited != savedText;
		if ( dirty == UiState.Dirty )
		{
			// Typing is no reason to redraw: the field already shows it.
			UiState = UiState with { Text = edited };
			return;
		}

		UiState = UiState with { Text = edited };
		Rebuild();
	}

	/// <summary>
	/// Give the text field the document as it is here, dropping edits on their way.
	/// </summary>
	void Resync( string why )
	{
		notice = why ?? notice;
		UiState = UiState with { TextSession = UiState.TextSession + 1 };
		Rebuild();
	}

	static bool TooLong( string text ) =>
		text.Length > MaxChars || (text.Length * 6 > MaxSharedText && Json.Serialize( text ).Length > MaxSharedText);

	// Documents.

	Task NewDocument()
	{
		path = null;
		savedText = "";
		bar = CodeEditBar.None;
		notice = null;
		Replace( "" );
		return Task.CompletedTask;
	}

	/// <summary>
	/// Put <paramref name="text"/> in the document as the field's new start.
	/// </summary>
	void Replace( string text )
	{
		UiState = UiState with { Text = text, TextSession = UiState.TextSession + 1 };
		Rebuild();
	}

	/// <summary>
	/// Ask the computer for a file: it opens the File Picker, a window of its own, in Documents. Nothing happens if the
	/// player closes it.
	/// </summary>
	async Task PickAsync()
	{
		if ( picking )
			return;

		picking = true;
		try
		{
			var file = await navi.Files.PickAsync( OpenTypes, StartFolder );
			if ( file is not null )
				await LoadAsync( file );
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
		}
		finally
		{
			picking = false;
		}
	}

	/// <summary>
	/// Open a file. One that isn't there, is a folder, or is too big to edit here leaves the document as it was.
	/// </summary>
	public async Task LoadAsync( string file )
	{
		try
		{
			var info = await navi.Files.GetInfoAsync( file );
			if ( info is null )
			{
				Notify( $"\"{NameOf( file )}\" isn't there." );
				return;
			}

			if ( info.IsFolder )
			{
				Notify( $"\"{info.Name}\" is a folder." );
				return;
			}

			var text = await navi.Files.ReadTextAsync( info.Path );
			if ( TooLong( text ) )
			{
				Notify( $"\"{info.Name}\" is too big to edit here: the most is {MaxChars:N0} characters." );
				return;
			}

			path = info.Path;
			savedText = text;
			bar = CodeEditBar.None;
			notice = null;
			Replace( text );
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
		}
	}

	/// <summary>
	/// Save to the file the document came from, or ask where for one that was never saved.
	/// </summary>
	public async Task SaveAsync()
	{
		if ( path is null )
		{
			OpenBar( CodeEditBar.SaveAs );
			return;
		}

		await WriteAsync( path );
	}

	async Task WriteAsync( string to )
	{
		var text = UiState.Text;
		try
		{
			await CreateFoldersAsync( FolderOf( to ) );
			var written = await navi.Files.WriteTextAsync( to, text );
			path = (await navi.Files.GetInfoAsync( written ))?.Path ?? written;
			savedText = text;
			bar = CodeEditBar.None;
			notice = null;
			Rebuild();

			if ( closeAfterSave )
				await navi.Window.CloseAsync();
		}
		catch ( System.Exception e )
		{
			// Not saved, so the window stays, with what went wrong.
			closeAfterSave = false;
			Notify( Describe( e ) );
		}
	}

	/// <summary>
	/// Make the folders of <paramref name="folder"/> that don't exist yet, from the top down, so a path typed in Save as
	/// can lead somewhere new.
	/// </summary>
	async Task CreateFoldersAsync( string folder )
	{
		var at = "";
		foreach ( var part in folder.Split( '/', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			at = Join( at == "" ? "/" : at, part );
			if ( await navi.Files.GetInfoAsync( at ) is null )
				await navi.Files.CreateFolderAsync( at );
		}
	}

	// The path bar and the question.

	void OpenBar( CodeEditBar which )
	{
		bar = which;
		var text = path ?? Join( StartFolder, UntitledName.ToLowerInvariant() + DefaultExtension );

		UiState = UiState with { BarText = text, BarSession = UiState.BarSession + 1 };
		Rebuild();
	}

	void CloseBar()
	{
		bar = CodeEditBar.None;
		closeAfterSave = false;
		Rebuild();
	}

	/// <summary>
	/// What the path field holds, as the field reports it. Typing replaces the state without a redraw.
	/// </summary>
	public void SetBarText( string text ) =>
		UiState = UiState with { BarText = text is { Length: > 1024 } ? text[..1024] : text ?? "" };

	async Task SubmitBarAsync()
	{
		var to = PathOf( UiState.BarText );
		if ( to is null )
			return;

		if ( bar != CodeEditBar.SaveAs )
			return;

		try
		{
			var existing = await navi.Files.GetInfoAsync( to );
			if ( existing is { IsFolder: true } )
			{
				Notify( $"\"{existing.Name}\" is a folder." );
				return;
			}

			if ( existing is not null && existing.Path != path )
			{
				Ask( $"\"{existing.Name}\" already exists. Replace what is in it?", "Replace", () => WriteAsync( to ) );
				return;
			}
		}
		catch ( System.Exception e )
		{
			Notify( Describe( e ) );
			return;
		}

		await WriteAsync( to );
	}

	/// <summary>
	/// What the player typed in the path field as a full path: a name alone is in the document's folder (or Documents),
	/// and a name with no extension gets ".cs". Null for nothing.
	/// </summary>
	string PathOf( string typed )
	{
		typed = typed?.Trim();
		if ( string.IsNullOrEmpty( typed ) || typed.EndsWith( '/' ) )
			return null;

		var full = typed.StartsWith( '/' ) ? typed : Join( FolderOf( path ?? Join( StartFolder, "x" ) ), typed );
		return NameOf( full ).Contains( '.' ) ? full : full + DefaultExtension;
	}

	/// <summary>
	/// Run <paramref name="then"/>, after asking whether to throw away the changes if there are any.
	/// </summary>
	void AskToDiscard( System.Func<Task> then )
	{
		if ( !UiState.Dirty )
		{
			_ = then();
			return;
		}

		Ask( $"Discard the changes to \"{UiState.Name}\"?", "Discard", then );
	}

	void Ask( string text, string yes, System.Func<Task> then, string other = null, System.Func<Task> otherThen = null )
	{
		confirmText = text;
		confirmLabel = yes;
		confirmed = then;
		confirmOtherLabel = other;
		confirmedOther = otherThen;
		Rebuild();
	}

	/// <summary>
	/// Close the question and run <paramref name="then"/>, the answer chosen, or nothing for Cancel.
	/// </summary>
	async Task AnswerAsync( System.Func<Task> then )
	{
		confirmed = confirmedOther = null;
		confirmText = confirmOtherLabel = null;
		Rebuild();

		// Cancel, e.g. on replacing a file while saving to close, keeps the window after all.
		if ( then is null )
			closeAfterSave = false;
		else
			await then();
	}

	void Notify( string text )
	{
		notice = text;
		Rebuild();
	}

	static string Describe( System.Exception e ) => e switch
	{
		Navi.MissingPermissionException => "Code Edit isn't allowed to reach that file. Allow it Files access in the taskbar's \"Permissions...\".",
		Navi.FileException f => f.Error switch
		{
			Navi.FileError.NotFound => $"\"{NameOf( f.Path )}\" isn't there.",
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

	void Rebuild()
	{
		var name = path is null ? UntitledName : NameOf( path );
		var dirty = UiState.Text != savedText;

		UiState = UiState with
		{
			Revision = UiState.Revision + 1,
			Name = name,
			Path = path,
			Dirty = dirty,
			Status = (path ?? "Not saved yet") + (dirty ? "  ·  Edited" : path is null ? "" : "  ·  Saved"),
			Notice = notice,
			Bar = bar,
			ConfirmText = confirmText,
			ConfirmLabel = confirmLabel,
			ConfirmOtherLabel = confirmOtherLabel,
		};
	}

	static string Join( string folder, string name ) => folder == "/" ? "/" + name : folder + "/" + name;

	static string NameOf( string entry ) => entry is null or "/" ? "" : entry[(entry.LastIndexOf( '/' ) + 1)..];

	static string FolderOf( string entry )
	{
		var slash = entry.LastIndexOf( '/' );
		return slash <= 0 ? "/" : entry[..slash];
	}
}
