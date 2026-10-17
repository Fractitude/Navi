namespace Sandbox;

/// <summary>
/// Registers a <see cref="NaviApp"/> so it can be installed on a <see cref="NaviComputer"/>.
/// In a Razor app put it at the top of the file: <c>@attribute [NaviApp( "calculator", "Calculator" )]</c>
/// </summary>
[System.AttributeUsage( System.AttributeTargets.Class, Inherited = false )]
public sealed class NaviAppAttribute : System.Attribute
{
	/// <summary>
	/// Unique, stable id. Saved in <see cref="NaviComputer.InstalledApps"/>, so don't change it once used.
	/// </summary>
	public string Id { get; }

	/// <summary>
	/// Name shown on the taskbar, window title and package list.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// One line shown in the package list.
	/// </summary>
	public string Description { get; set; } = "";

	/// <summary>
	/// Material icon name, e.g. "calculate".
	/// </summary>
	public string Icon { get; set; } = "apps";

	/// <summary>
	/// Icon background, as a CSS color.
	/// </summary>
	public string Color { get; set; } = "#8a8472";

	/// <summary>
	/// System apps are always installed and can't be uninstalled.
	/// </summary>
	public bool IsSystem { get; set; }

	/// <summary>
	/// The window has a minimize button, and its taskbar icon minimizes it when it is in front.
	/// </summary>
	public bool CanMinimize { get; set; } = true;

	/// <summary>
	/// The window has a maximize button and maximizes when its title bar is double-clicked.
	/// </summary>
	public bool CanMaximize { get; set; } = true;

	/// <summary>
	/// The window can be resized by dragging its corners.
	/// </summary>
	public bool CanResize { get; set; } = true;

	/// <summary>
	/// While the window is in front, the camera moves to the screen and the player's keys go to the app instead of
	/// moving the player. Read them with <see cref="Input"/>, e.g. <c>Input.Down( "Forward" )</c>. Escape closes the app.
	/// </summary>
	public bool CapturesInput { get; set; }

	public NaviAppAttribute( string id, string name )
	{
		Id = id;
		Name = name;
	}
}
