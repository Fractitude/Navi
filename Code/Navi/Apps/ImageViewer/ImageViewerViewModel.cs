using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// Everything the Image Viewer app does: it takes the files the player opened with it, reads their bytes from the
/// computer's files and decodes them into a texture. The panel (<c>ImageViewer.razor</c>) draws <see cref="UiState"/>
/// and <see cref="Picture"/>.
/// </summary>
public sealed class ImageViewerViewModel
{
	readonly Navi navi;

	/// <summary>How often the files opened from the file explorer are asked for, in seconds.</summary>
	const float PollInterval = 0.25f;

	/// <summary>The biggest file opened, in bytes. Bigger ones are refused before they are decoded.</summary>
	const int MaxBytes = 32 * 1024 * 1024;

	const string StartFolder = "/Pictures";

	bool polling;
	RealTimeSince sincePoll;
	string title;

	public ImageViewerViewModel( Navi navi )
	{
		this.navi = navi;
		UiState = new();
	}

	public ImageViewerUiState UiState { get; private set; }

	public int Revision => UiState.Revision;

	/// <summary>
	/// The open image. Not part of the state: it is a texture, replaced whole when another file is opened.
	/// </summary>
	public Texture Picture { get; private set; }

	void Update( System.Func<ImageViewerUiState, ImageViewerUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };

	/// <summary>Called by the panel each frame.</summary>
	public void Tick()
	{
		if ( !polling && sincePoll > PollInterval )
			_ = PollAsync();

		var wanted = UiState.HasImage ? UiState.Name : "Image Viewer";
		if ( wanted != title )
		{
			title = wanted;
			_ = navi.Window.SetTitleAsync( wanted );
		}
	}

	/// <summary>
	/// The "Open..." button: ask the computer for a picture. It opens the File Picker, a window of its own, in Pictures.
	/// </summary>
	public void ShowPicker()
	{
		if ( UiState.Loading || UiState.Picking )
			return;

		_ = PickAsync();
	}

	async Task PickAsync()
	{
		Update( uiState => uiState with { Picking = true, Notice = "" } );

		string file;
		try
		{
			file = await navi.Files.PickAsync( ["jpg", "jpeg", "png"], StartFolder );
		}
		catch ( System.Exception e )
		{
			Update( uiState => uiState with { Picking = false } );
			Fail( Describe( e ) );
			return;
		}

		Update( uiState => uiState with { Picking = false } );
		if ( file is not null )
			await LoadAsync( file );
	}

	/// <summary>
	/// Take the files the player opened with this app, from the file explorer. The last one is shown.
	/// </summary>
	async Task PollAsync()
	{
		polling = true;
		try
		{
			var opened = await navi.Files.TakeOpenedAsync();
			if ( opened.Count > 0 )
				await LoadAsync( opened[^1] );
		}
		catch ( System.Exception e )
		{
			Fail( Describe( e ) );
		}
		finally
		{
			polling = false;
			sincePoll = 0;
		}
	}

	/// <summary>
	/// Open an image file. One that isn't there, isn't a picture or is too big leaves the open image as it was.
	/// </summary>
	public async Task LoadAsync( string file )
	{
		Update( uiState => uiState with { Loading = true, Notice = "" } );

		try
		{
			var info = await navi.Files.GetInfoAsync( file );
			if ( info is null )
			{
				Fail( $"\"{NameOf( file )}\" isn't there." );
				return;
			}

			if ( info.IsFolder )
			{
				Fail( $"\"{info.Name}\" is a folder." );
				return;
			}

			var bytes = await navi.Files.ReadBytesAsync( info.Path );
			if ( bytes is null || bytes.Length == 0 )
			{
				Fail( $"\"{info.Name}\" is empty." );
				return;
			}

			if ( bytes.Length > MaxBytes )
			{
				Fail( $"\"{info.Name}\" is too big to show here." );
				return;
			}

			using var bitmap = Bitmap.CreateFromBytes( bytes );
			var texture = bitmap is { Width: > 0, Height: > 0 } ? bitmap.ToTexture() : null;
			if ( texture is null )
			{
				Fail( $"\"{info.Name}\" isn't a picture that can be shown." );
				return;
			}

			Picture = texture;
			Update( uiState => uiState with
			{
				HasImage = true,
				Loading = false,
				Name = info.Name,
				SizeText = $"{bitmap.Width} x {bitmap.Height} px",
				PathText = info.Path,
				Notice = "",
			} );
		}
		catch ( System.Exception e )
		{
			Fail( Describe( e ) );
		}
	}

	void Fail( string notice ) => Update( uiState => uiState with { Loading = false, Notice = notice } );

	static string NameOf( string path )
	{
		var trimmed = path.TrimEnd( '/' );
		return trimmed[(trimmed.LastIndexOf( '/' ) + 1)..];
	}

	static string Describe( System.Exception e ) => e switch
	{
		Navi.MissingPermissionException =>
			"Image Viewer isn't allowed to reach that file. Allow it Files access in the taskbar's \"Permissions...\".",
		Navi.FileException f => f.Error switch
		{
			Navi.FileError.NotFound => $"\"{NameOf( f.Path )}\" isn't there.",
			Navi.FileError.NotAFolder => $"\"{NameOf( f.Path )}\" isn't a folder.",
			Navi.FileError.IsAFolder => $"\"{NameOf( f.Path )}\" is a folder.",
			Navi.FileError.LinkLoop => "Those links point at each other in a loop.",
			Navi.FileError.Unavailable => "The files are on the host's computer.",
			_ => f.Message,
		},
		_ => e.Message,
	};
}
