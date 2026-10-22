using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// Everything the Image Viewer app does: it takes the files the player opened with it, reads their bytes from the
/// computer's files and decodes them into a texture. The panel (<c>ImageViewer.razor</c>) draws <see cref="UiState"/>
/// and <see cref="Picture"/>. The app is shared: this runs in the host's copy, and what the player does in any copy (Open,
/// zooming, panning) arrives in <see cref="Act"/>.
/// </summary>
public sealed class ImageViewerViewModel
{
	/// <summary>The player pressed Open...</summary>
	public const string OpenAction = "open";

	/// <summary>The player zoomed or panned: the zoom, then the pan across and down, as numbers.</summary>
	public const string ViewAction = "view";

	/// <summary>The biggest zoom, as a multiple of the size that fits the window.</summary>
	public const float MaxZoom = 16f;

	/// <summary>The furthest the pan is taken from the host, as a fraction of the window. The panel keeps it well inside.</summary>
	const float MaxPan = 8f;

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

	/// <summary>
	/// What the player did in the app, from any client. Arguments are text; what doesn't parse is ignored.
	/// </summary>
	public void Act( string action, string[] args )
	{
		switch ( action )
		{
			case OpenAction:
				ShowPicker();
				break;

			case ViewAction when args.Length == 3 && TryNumber( args[0], out var zoom ) && TryNumber( args[1], out var panX )
				&& TryNumber( args[2], out var panY ):
				SetView( zoom, panX, panY );
				break;
		}
	}

	static bool TryNumber( string text, out float value ) =>
		float.TryParse( text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
			out value ) && float.IsFinite( value );

	/// <summary>
	/// Zoom and pan the picture, for everyone. Replaces the state without going through <see cref="Update"/>: the panel
	/// puts the picture where the state says every frame, so a drag doesn't rebuild it.
	/// </summary>
	public void SetView( float zoom, float panX, float panY )
	{
		if ( !UiState.HasImage )
			return;

		UiState = UiState with
		{
			Zoom = System.Math.Clamp( zoom, 1f, MaxZoom ),
			PanX = System.Math.Clamp( panX, -MaxPan, MaxPan ),
			PanY = System.Math.Clamp( panY, -MaxPan, MaxPan ),
		};
	}

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

			// The computer makes the preview now, before the state says there is a new picture, so the clients that
			// draw it can ask for it at once. It is also what makes this the picture they may ask for.
			var preview = await PreviewOfAsync( info.Path );

			Picture = texture;
			Update( uiState => uiState with
			{
				HasImage = true,
				Loading = false,
				Name = info.Name,
				SizeText = $"{bitmap.Width} x {bitmap.Height} px",
				PathText = info.Path,
				PictureWidth = bitmap.Width,
				PictureHeight = bitmap.Height,
				PictureVersion = uiState.PictureVersion + 1,
				HasPreview = preview,
				Zoom = 1f,
				PanX = 0f,
				PanY = 0f,
				Notice = "",
			} );
		}
		catch ( System.Exception e )
		{
			Fail( Describe( e ) );
		}
	}

	/// <summary>Is there a preview of the picture to send to the clients?</summary>
	async Task<bool> PreviewOfAsync( string path )
	{
		try
		{
			return await navi.Files.GetPreviewAsync( path ) is { Length: > 0 };
		}
		catch ( System.Exception )
		{
			return false;
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
