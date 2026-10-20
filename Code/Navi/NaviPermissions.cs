namespace Sandbox;

/// <summary>
/// What an app may do beyond the basics. Every app can open apps, list them and control its own window. Anything else needs
/// a permission, which the player grants: when the app asks (<c>Navi.Permissions.RequestAsync</c>), or from the
/// taskbar with "Permissions...". An app lists the permissions it may ask for in its <see cref="NaviAppResource"/>.
/// System apps get theirs without asking. Grants last until the app closes.
/// </summary>
public static class NaviPermissions
{
	/// <summary>
	/// Install and uninstall apps.
	/// </summary>
	public const string InstallApps = "navi.apps.install";

	/// <summary>
	/// Download and install apps from the Store.
	/// </summary>
	public const string InstallFromStore = "navi.store.install";

	/// <summary>
	/// Close other apps' windows.
	/// </summary>
	public const string ManageApps = "navi.apps.manage";

	/// <summary>
	/// Change the computer's settings, such as the display scale.
	/// </summary>
	public const string ChangeSettings = "navi.settings.change";

	/// <summary>
	/// Send the player notifications.
	/// </summary>
	public const string Notify = "navi.notifications.post";

	/// <summary>
	/// Read, change and delete every file on the computer. Without it an app has only its own folder.
	/// </summary>
	public const string Files = "navi.files.access";

	/// <summary>
	/// Read and send the direct messages of the player at the computer.
	/// </summary>
	public const string Messages = "navi.messages.access";

	/// <summary>
	/// See through the camera on top of the computer's screen.
	/// </summary>
	public const string Camera = "navi.camera.access";

	/// <summary>
	/// How a permission is shown to the player.
	/// </summary>
	public record Info( string Id, string Title, string Description, string Icon );

	public static IReadOnlyList<Info> All { get; } = new Info[]
	{
		new( InstallApps, "Install apps", "Install and uninstall apps on this computer.", "apps" ),
		new( InstallFromStore, "Use the Store", "Download apps from the Store and install them.", "cloud_download" ),
		new( ManageApps, "Manage apps", "Close other apps' windows.", "tab" ),
		new( ChangeSettings, "Change settings", "Change this computer's settings, such as the display scale.", "settings" ),
		new( Notify, "Send notifications", "Show you notifications, which can play a sound.", "notifications" ),
		new( Files, "Files", "See, change and delete every file on this computer.", "folder" ),
		new( Messages, "Messages", "Read and send direct messages as the player using this computer.", "chat" ),
		new( Camera, "Camera", "See what the camera on top of this computer's screen sees.", "videocam" ),
	};

	public static bool IsKnown( string id ) => All.Any( x => x.Id == id );

	/// <summary>
	/// How to show a permission. An unknown one is shown by its id.
	/// </summary>
	public static Info Describe( string id ) => All.FirstOrDefault( x => x.Id == id ) ?? new Info( id, id, "", "help" );

	/// <summary>
	/// The error an app gets when it calls something it lacks the permission for. The app can catch it, ask for
	/// the permission, and try again. The message ends with the permission id, so the client, which has no Navi types, can
	/// read it and throw <see cref="Navi.MissingPermissionException"/>.
	/// </summary>
	public static System.UnauthorizedAccessException Missing( string permission ) =>
		new( $"Missing permission: {permission}" );
}
