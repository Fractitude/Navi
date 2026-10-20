using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// Everything the Camera app does: it asks the computer for the picture of the camera on top of the screen, and keeps it
/// coming while the window is open. The panel (<c>Camera.razor</c>) draws <see cref="UiState"/> and <see cref="Picture"/>.
/// </summary>
public sealed class CameraViewModel
{
	readonly Navi navi;

	/// <summary>The picture's size in pixels: 16 by 9. The panel scales it to the window.</summary>
	public const int PictureWidth = 640, PictureHeight = 360;

	/// <summary>
	/// How often the computer is asked for the picture, in seconds. It draws for a second after each ask, so this keeps it
	/// drawing without a call every frame.
	/// </summary>
	const float AskInterval = 0.5f;

	RealTimeSince sinceAsked = float.MaxValue;
	bool asking;
	bool denied;

	public CameraViewModel( Navi navi )
	{
		this.navi = navi;
	}

	public CameraUiState UiState { get; private set; } = new();
	public int Revision => UiState.Revision;

	/// <summary>
	/// The texture the camera draws into. Not part of the state: it is the same object for as long as the window is open,
	/// and the computer fills it in.
	/// </summary>
	public Texture Picture { get; private set; }

	void Update( System.Func<CameraUiState, CameraUiState> change ) =>
		UiState = change( UiState ) with { Revision = UiState.Revision + 1 };

	/// <summary>Called by the panel each frame.</summary>
	public void Tick()
	{
		if ( asking || denied || sinceAsked < AskInterval )
			return;

		_ = AskAsync();
	}

	/// <summary>The "Take picture" button: save the picture as a JPEG in the app's folder.</summary>
	public void TakePhoto()
	{
		if ( UiState.Saving || !UiState.HasPicture )
			return;

		_ = TakePhotoAsync();
	}

	async Task TakePhotoAsync()
	{
		Update( uiState => uiState with { Saving = true, PhotoNotice = "" } );

		try
		{
			var photo = await navi.Camera.TakePhotoAsync();
			var folder = await navi.Files.GetAppFolderAsync();
			var path = await navi.Files.WriteBytesAsync( $"{folder}/photo-{System.DateTime.Now:yyyyMMdd-HHmmss}.jpg", photo );

			Update( uiState => uiState with { Saving = false, PhotoNotice = $"Saved {path}" } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Camera: couldn't save the picture: {e.Message}" );
			Update( uiState => uiState with { Saving = false, PhotoNotice = "The picture couldn't be saved." } );
		}
	}

	/// <summary>The "Allow" button: ask the player for the camera permission.</summary>
	public void Allow() => _ = AllowAsync();

	async Task AskAsync()
	{
		asking = true;

		try
		{
			Picture = await navi.Camera.GetViewAsync( PictureWidth, PictureHeight );
			sinceAsked = 0;

			if ( UiState.NeedsPermission || !UiState.HasPicture || UiState.Notice != "" )
				Update( uiState => uiState with { NeedsPermission = false, HasPicture = true, Notice = "" } );
		}
		catch ( Navi.MissingPermissionException )
		{
			// Wait for the player to press Allow rather than asking again every half second.
			denied = true;
			Update( uiState => uiState with { NeedsPermission = true, HasPicture = false } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Camera: couldn't get the picture: {e.Message}" );
			sinceAsked = 0;
			Update( uiState => uiState with { Notice = "The camera can't be reached." } );
		}
		finally
		{
			asking = false;
		}
	}

	async Task AllowAsync()
	{
		try
		{
			var request = await navi.Permissions.RequestAsync( Navi.Permission.Camera );

			if ( request.Granted.Contains( Navi.Permission.Camera ) )
				denied = false;
			else
				Update( uiState => uiState with { Notice = "The camera can't work without that permission." } );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Camera: couldn't ask for the permission: {e.Message}" );
		}
	}
}
