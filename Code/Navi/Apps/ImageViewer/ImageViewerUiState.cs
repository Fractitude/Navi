namespace NaviApps;

/// <summary>
/// Everything the Image Viewer panel draws besides the picture, which is a texture and so is held by the view model (on
/// a client, <see cref="ImageViewerPreview"/> makes its own from the host's preview). The app is shared, so this is sent
/// to every player: the open image, and how it is zoomed and panned.
/// </summary>
public sealed record ImageViewerUiState
{
	/// <summary>What a viewer draws until the host's state arrives.</summary>
	public static ImageViewerUiState Empty { get; } = new();

	/// <summary>Goes up whenever anything else here changes. The panel's BuildHash is this.</summary>
	public int Revision { get; init; }

	/// <summary>An image is open, so the panel shows it instead of the empty state.</summary>
	public bool HasImage { get; init; }

	/// <summary>The open image's file name.</summary>
	public string Name { get; init; } = "";

	/// <summary>The open image's size, like "640 x 480 px".</summary>
	public string SizeText { get; init; } = "";

	/// <summary>The open image's full path.</summary>
	public string PathText { get; init; } = "";

	/// <summary>The open image's size in pixels, which the zoom and pan are worked out from.</summary>
	public int PictureWidth { get; init; }

	public int PictureHeight { get; init; }

	/// <summary>Goes up whenever another picture is opened, even the same file again, so a client fetches it again.</summary>
	public int PictureVersion { get; init; }

	/// <summary>The host could make a preview small enough to send to the clients. When not, only the host sees the picture.</summary>
	public bool HasPreview { get; init; }

	/// <summary>
	/// How far the picture is zoomed in, as a multiple of the size that fits the window (1 to 16). Not part of
	/// <see cref="Revision"/>: the panel puts the picture where this says every frame.
	/// </summary>
	public float Zoom { get; init; } = 1f;

	/// <summary>
	/// How far the picture is moved from the centre, as a fraction of the window's width and height, so it lands in the
	/// same place on a window of another size. Not part of <see cref="Revision"/>, like <see cref="Zoom"/>.
	/// </summary>
	public float PanX { get; init; }

	public float PanY { get; init; }

	/// <summary>Why the last file couldn't be opened. Empty when there is nothing to say.</summary>
	public string Notice { get; init; } = "";

	/// <summary>The File Picker is open for this app, so Open waits.</summary>
	public bool Picking { get; init; }

	/// <summary>A file is being read, so Open waits.</summary>
	public bool Loading { get; init; }
}
