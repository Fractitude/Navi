using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// The open picture on a client that only draws the Image Viewer: the preview the host makes of it
/// (<c>Navi.Files.GetPreviewAsync</c>), fetched again whenever the state says another picture was opened. A texture, so
/// never part of the state: every copy that draws it makes its own.
/// </summary>
public sealed class ImageViewerPreview
{
	/// <summary>How long to wait before asking again when the host didn't send it, in seconds.</summary>
	const float UnavailableDelay = 2f;

	readonly Navi navi;

	bool loading;
	int shownVersion;
	int failedVersion;
	RealTimeSince sinceUnavailable;
	bool unavailable;

	public ImageViewerPreview( Navi navi ) => this.navi = navi;

	/// <summary>The picture, or null while it is loading or if there is none.</summary>
	public Texture Texture { get; private set; }

	/// <summary>Goes up whenever the picture changes, so the panel draws it.</summary>
	public int Version { get; private set; }

	/// <summary>The picture of the state is the one that was fetched.</summary>
	public bool IsCurrent( ImageViewerUiState uiState ) => !uiState.HasImage || shownVersion == uiState.PictureVersion;

	public void Tick( ImageViewerUiState uiState )
	{
		if ( !uiState.HasImage || !uiState.HasPreview )
		{
			if ( Texture is not null )
			{
				Texture = null;
				Version++;
			}

			shownVersion = 0;
			return;
		}

		if ( loading || shownVersion == uiState.PictureVersion || failedVersion == uiState.PictureVersion )
			return;

		if ( unavailable && sinceUnavailable < UnavailableDelay )
			return;

		_ = LoadAsync( uiState.PathText, uiState.PictureVersion );
	}

	async Task LoadAsync( string path, int version )
	{
		loading = true;
		try
		{
			var bytes = await navi.Files.GetPreviewAsync( path );
			using var bitmap = bytes is { Length: > 0 } ? Bitmap.CreateFromBytes( bytes ) : null;
			var texture = bitmap is { Width: > 0, Height: > 0 } ? bitmap.ToTexture() : null;

			unavailable = false;
			if ( texture is null )
			{
				failedVersion = version;
				return;
			}

			Texture = texture;
			shownVersion = version;
			Version++;
		}
		catch ( Navi.FileException e ) when ( e.Error == Navi.FileError.Unavailable )
		{
			// The host didn't send it (too much was asked at once, or the screen is locked): try again in a moment.
			unavailable = true;
			sinceUnavailable = 0;
		}
		catch ( System.Exception )
		{
			failedVersion = version;
		}
		finally
		{
			loading = false;
		}
	}
}
