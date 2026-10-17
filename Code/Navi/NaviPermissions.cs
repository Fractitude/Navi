namespace Sandbox;

/// <summary>
/// What an app may do beyond the basics. Every app can open apps, list them and control its own window. Anything else needs
/// a permission, which the player grants: when the app asks (<c>Navi.RequestPermissions</c>), or by opening it from the
/// taskbar with "Open with permissions...". An app lists the permissions it may ask for in its <see cref="NaviAppResource"/>.
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
	/// How a permission is shown to the player.
	/// </summary>
	public record Info( string Id, string Title, string Description, string Icon );

	public static IReadOnlyList<Info> All { get; } = new Info[]
	{
		new( InstallApps, "Install apps", "Install and uninstall apps on this computer.", "apps" ),
		new( InstallFromStore, "Use the Store", "Download apps from the Store and install them.", "cloud_download" ),
		new( ManageApps, "Manage apps", "Close other apps' windows.", "tab" ),
	};

	public static bool IsKnown( string id ) => All.Any( x => x.Id == id );

	/// <summary>
	/// How to show a permission. An unknown one is shown by its id.
	/// </summary>
	public static Info Describe( string id ) => All.FirstOrDefault( x => x.Id == id ) ?? new Info( id, id, "", "help" );

	/// <summary>
	/// The error an app gets when it calls something it lacks the permission for. The app can catch it, ask for
	/// the permission, and try again. The message ends with the permission id, so apps without Navi types can read it.
	/// </summary>
	public static System.UnauthorizedAccessException Missing( string permission ) =>
		new( $"Missing permission: {permission}" );
}
