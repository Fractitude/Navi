namespace Sandbox;

/// <summary>
/// Everything other clients need to draw a <see cref="NaviComputer"/> the way the host has it; the others replace their
/// state with it. The host sends it as JSON in parts: <see cref="NaviComputer.NetworkApps"/>,
/// <see cref="NaviComputer.NetworkState"/>, <see cref="NaviComputer.NetworkWindows"/> and
/// <see cref="NaviComputer.NetworkSession"/> (<see cref="NaviSessionSnapshot"/>).
/// </summary>
public sealed class NaviComputerSnapshot
{
	/// <summary>
	/// Installed apps, permissions and settings: what a save holds.
	/// </summary>
	public NaviComputerSave State { get; set; } = new();

	/// <summary>
	/// The manifests of the apps the host has installed or open, except Store apps, which clients download themselves.
	/// A client may not have the asset files of the host's game or libraries, e.g. one that joins an editor session.
	/// </summary>
	public List<NaviAppSnapshot> Apps { get; set; } = new();

	/// <summary>
	/// The running apps, and their windows.
	/// </summary>
	public NaviLayoutSnapshot Layout { get; set; } = new();

	/// <summary>
	/// Open permission requests, oldest first.
	/// </summary>
	public List<NaviRequestSnapshot> Requests { get; set; } = new();

	/// <summary>
	/// Store packages the host is downloading.
	/// </summary>
	public List<string> Downloading { get; set; } = new();

	/// <summary>
	/// Connection id of the player using the computer, or empty. See <see cref="NaviComputer.UserId"/>.
	/// </summary>
	public System.Guid User { get; set; }
}

/// <summary>
/// The part of <see cref="NaviComputerSnapshot"/> sent in <see cref="NaviComputer.NetworkSession"/>. The user and the
/// requests travel together: a new user dismisses open requests, and clients must never see one without the other.
/// </summary>
public sealed class NaviSessionSnapshot
{
	public List<NaviRequestSnapshot> Requests { get; set; } = new();
	public List<string> Downloading { get; set; } = new();
	public System.Guid User { get; set; }
}

/// <summary>
/// An app's manifest, as in <see cref="NaviAppInfo"/>, with its panel class by name.
/// </summary>
public sealed class NaviAppSnapshot
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Description { get; set; }
	public string Icon { get; set; }
	public string Color { get; set; }
	public bool CanMinimize { get; set; }
	public bool CanMaximize { get; set; }
	public bool CanResize { get; set; }
	public float AspectRatio { get; set; }
	public bool Headless { get; set; }
	public List<NaviWindowDefinitionSnapshot> Windows { get; set; } = new();
	public bool Shared { get; set; }
	public bool IsPrepackaged { get; set; }
	public bool IsSystem { get; set; }
	public List<string> Permissions { get; set; } = new();
	public string PanelType { get; set; }
}

/// <summary>
/// A window an app can open, as in <see cref="NaviWindowInfo"/>, with its panel class by name.
/// </summary>
public sealed class NaviWindowDefinitionSnapshot
{
	public string Id { get; set; }
	public string Title { get; set; }
	public bool CanMinimize { get; set; }
	public bool CanMaximize { get; set; }
	public bool CanResize { get; set; }
	public float AspectRatio { get; set; }
	public string PanelType { get; set; }
}

/// <summary>
/// The part of <see cref="NaviComputerSnapshot"/> sent in <see cref="NaviComputer.NetworkWindows"/>: what is running, and
/// where its windows are. It changes whenever a window moves.
/// </summary>
public sealed class NaviLayoutSnapshot
{
	public List<NaviProcessSnapshot> Processes { get; set; } = new();
	public List<NaviWindowSnapshot> Windows { get; set; } = new();
}

/// <summary>
/// A running app, as in <see cref="NaviProcess"/>.
/// </summary>
public sealed class NaviProcessSnapshot
{
	public string AppId { get; set; }
	public bool IsRoot { get; set; }
	public List<string> Permissions { get; set; } = new();
	public NaviTraySnapshot Tray { get; set; }
}

/// <summary>
/// An app's tray icon, as in <see cref="NaviTray"/>.
/// </summary>
public sealed class NaviTraySnapshot
{
	public string Icon { get; set; }
	public string Tooltip { get; set; }
	public List<NaviTrayItem> Items { get; set; } = new();
}

/// <summary>
/// An open window, as in <see cref="NaviWindow"/>.
/// </summary>
public sealed class NaviWindowSnapshot
{
	public string AppId { get; set; }

	/// <summary>
	/// Empty for the app's main window.
	/// </summary>
	public string WindowId { get; set; } = "";

	public Vector2 Position { get; set; }
	public Vector2? Size { get; set; }
	public bool IsMinimized { get; set; }
	public bool IsMaximized { get; set; }
	public int ZIndex { get; set; }

	/// <summary>
	/// The title bar's text after the app's name, as the app has it now. See <see cref="NaviWindow.Title"/>.
	/// </summary>
	public string Title { get; set; } = "";
}

/// <summary>
/// An open permission request, as in <see cref="NaviPermissionRequest"/>.
/// </summary>
public sealed class NaviRequestSnapshot
{
	public int Id { get; set; }
	public string AppId { get; set; }
	public List<string> Permissions { get; set; } = new();
	public List<string> Ticked { get; set; } = new();
	public bool IsReview { get; set; }
	public bool IsRoot { get; set; }
	public bool Launch { get; set; }
}
