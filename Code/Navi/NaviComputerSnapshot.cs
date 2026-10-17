namespace Sandbox;

/// <summary>
/// Everything other clients need to draw a <see cref="NaviComputer"/> the way the host has it. The host sends it as JSON
/// in <see cref="NaviComputer.NetworkSnapshot"/>; the others replace their state with it.
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

	public List<NaviWindowSnapshot> Windows { get; set; } = new();

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
	public bool CapturesInput { get; set; }
	public bool Shared { get; set; }
	public bool IsPrepackaged { get; set; }
	public bool IsSystem { get; set; }
	public List<string> Permissions { get; set; } = new();
	public string PanelType { get; set; }
}

/// <summary>
/// An open window, as in <see cref="NaviProcess"/>.
/// </summary>
public sealed class NaviWindowSnapshot
{
	public string AppId { get; set; }
	public Vector2 Position { get; set; }
	public Vector2? Size { get; set; }
	public bool IsMinimized { get; set; }
	public bool IsMaximized { get; set; }
	public int ZIndex { get; set; }
	public bool IsRoot { get; set; }
	public List<string> Permissions { get; set; } = new();
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
