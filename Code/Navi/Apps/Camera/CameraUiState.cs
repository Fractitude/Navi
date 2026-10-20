namespace NaviApps;

/// <summary>
/// Everything the Camera panel draws besides the picture, which is a texture and so is held by the view model.
/// </summary>
public sealed record CameraUiState
{
	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>The app doesn't have the camera permission yet, and shows a button to ask for it.</summary>
	public bool NeedsPermission { get; init; }

	/// <summary>The player said no, or the camera could not be reached.</summary>
	public string Notice { get; init; } = "";

	/// <summary>A picture is being saved, so the button waits.</summary>
	public bool Saving { get; init; }

	/// <summary>What became of the last picture taken: where it was saved, or why not. Stays until the next one.</summary>
	public string PhotoNotice { get; init; } = "";

	/// <summary>The picture has arrived, so the panel shows it instead of the waiting text.</summary>
	public bool HasPicture { get; init; }
}
