namespace NaviApps;

/// <summary>
/// Everything the Image Viewer panel draws besides the picture, which is a texture and so is held by the view model.
/// </summary>
public sealed record ImageViewerUiState
{
	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>An image is open, so the panel shows it instead of the empty state.</summary>
	public bool HasImage { get; init; }

	/// <summary>The open image's file name.</summary>
	public string Name { get; init; } = "";

	/// <summary>The open image's size, like "640 x 480 px".</summary>
	public string SizeText { get; init; } = "";

	/// <summary>What the path field starts with. Typing changes the field, not this.</summary>
	public string PathText { get; init; } = "";

	/// <summary>Why the last file couldn't be opened. Empty when there is nothing to say.</summary>
	public string Notice { get; init; } = "";

	/// <summary>A file is being read, so Open waits.</summary>
	public bool Loading { get; init; }
}
