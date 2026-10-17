// The Navi client: how an app talks to the Navi computer it runs on. Every Navi app uses it, built-in or from the Store.
//
// It uses no Navi types, only .NET and engine ones, so a Store app copies this file into its own project and namespace
// instead of installing the Navi library (which would get published inside the app). Copy it as it is; only the
// namespace line changes.
//
// Calls go up to the computer as a "navi.call" panel event and are answered a frame or so later, so they are async.
// A call the app lacks the permission for throws UnauthorizedAccessException. Catch it, ask for the permission with
// RequestPermissions, and try again; WithPermission does all three.

using System;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.UI;

namespace Sandbox;

public static class Navi
{
	/// <summary>
	/// Permissions an app can ask for. List the ones an app may ask for in its .napp, under Permissions.
	/// </summary>
	public static class Permission
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
	}

	/// <summary>
	/// Make a call to the computer. Prefer the typed methods below.
	/// </summary>
	public static Task<object> Call( Panel app, string method, params object[] args )
	{
		var reply = new TaskCompletionSource<object>();
		app.CreateEvent( new PanelEvent( "navi.call", app ) { Value = (method, args, reply) } );
		return reply.Task;
	}

	static async Task<T> Call<T>( Panel app, string method, params object[] args ) => (T)await Call( app, method, args );

	// The app's own window.

	public static Task Close( Panel app ) => Call( app, "window.close" );

	public static Task<bool> Minimize( Panel app ) => Call<bool>( app, "window.minimize" );

	/// <summary>
	/// Maximize the window, or restore it if it is maximized. False if the app doesn't allow maximizing.
	/// </summary>
	public static Task<bool> ToggleMaximize( Panel app ) => Call<bool>( app, "window.maximize" );

	/// <summary>
	/// True while the app's window has the player's controls (apps with CapturesInput). Pause when it doesn't.
	/// </summary>
	public static bool HasInput( Panel app ) => app.HasClass( "navi-input" );

	// Permissions.

	/// <summary>
	/// The permissions this app has been granted.
	/// </summary>
	public static Task<string[]> GetPermissions( Panel app ) => Call<string[]>( app, "permissions.get" );

	/// <summary>
	/// Ask the player for permissions. They see a list and can grant all, some or none. Returns which were granted and which
	/// denied. Only permissions the app's .napp lists can be granted; already granted ones aren't asked again.
	/// </summary>
	public static Task<(string[] Granted, string[] Denied)> RequestPermissions( Panel app, params string[] permissions ) =>
		Call<(string[], string[])>( app, "permissions.request", new object[] { permissions } );

	/// <summary>
	/// True if this app has full system access (root): it is a system app the player accepted, or part of the OS.
	/// Root has every permission.
	/// </summary>
	public static Task<bool> HasRoot( Panel app ) => Call<bool>( app, "permissions.root" );

	/// <summary>
	/// The permission a call failed for, or null if it failed for another reason.
	/// </summary>
	public static string MissingPermission( Exception e )
	{
		const string prefix = "Missing permission: ";
		return e is UnauthorizedAccessException && e.Message.StartsWith( prefix ) ? e.Message[prefix.Length..] : null;
	}

	/// <summary>
	/// Make a call. If it fails for a missing permission, ask the player for it and, if they grant it, try once more.
	/// Still throws UnauthorizedAccessException if they deny it.
	/// </summary>
	public static async Task<T> WithPermission<T>( Panel app, Func<Task<T>> call )
	{
		string permission;

		try
		{
			return await call();
		}
		catch ( UnauthorizedAccessException e ) when ( MissingPermission( e ) is not null )
		{
			permission = MissingPermission( e );
		}

		var (granted, _) = await RequestPermissions( app, permission );
		if ( !granted.Contains( permission ) )
			throw new UnauthorizedAccessException( $"Missing permission: {permission}" );

		return await call();
	}

	// Apps.

	/// <summary>
	/// Every app on the computer, sorted by name. Type is "core" for apps that are part of the OS (always installed,
	/// can't be uninstalled), "system" for other apps with full system access, or "app".
	/// </summary>
	public static Task<(string Id, string Name, string Description, string Icon, string Color, bool Installed, string Type)[]> GetApps( Panel app ) =>
		Call<(string, string, string, string, string, bool, string)[]>( app, "apps.list" );

	/// <summary>
	/// Open an installed app, or bring it to the front.
	/// </summary>
	public static Task<bool> Launch( Panel app, string id ) => Call<bool>( app, "apps.launch", id );

	/// <summary>
	/// Install an app. Needs <see cref="Permission.InstallApps"/>.
	/// </summary>
	public static Task<bool> Install( Panel app, string id ) => Call<bool>( app, "apps.install", id );

	/// <summary>
	/// Uninstall an app. Needs <see cref="Permission.InstallApps"/>.
	/// </summary>
	public static Task<bool> Uninstall( Panel app, string id ) => Call<bool>( app, "apps.uninstall", id );

	/// <summary>
	/// Close an app's window. Needs <see cref="Permission.ManageApps"/>, unless it is this app.
	/// </summary>
	public static Task CloseApp( Panel app, string id ) => Call( app, "apps.close", id );

	/// <summary>
	/// Goes up whenever apps are installed, opened or closed, or packages change. Poll it to know when to refresh.
	/// </summary>
	public static Task<int> GetVersion( Panel app ) => Call<int>( app, "system.version" );

	// The Store.

	/// <summary>
	/// One page of apps published to the Store that match <paramref name="text"/>, and how many match in all. The page
	/// leaves out the first <paramref name="skip"/> matches and holds at most <paramref name="take"/> of them. Official
	/// ones are by the Navi makers.
	/// </summary>
	public static Task<((Package Package, bool Official)[] Packages, int Total)> SearchStore( Panel app, string text = "", int skip = 0, int take = 20 ) =>
		Call<((Package, bool)[], int)>( app, "store.search", text, skip, take );

	/// <summary>
	/// Idents of the Store packages installed on this computer, and of those downloading now.
	/// </summary>
	public static Task<(string[] Installed, string[] Installing)> GetStoreStatus( Panel app ) =>
		Call<(string[], string[])>( app, "store.status" );

	/// <summary>
	/// Download a Store package and install its app. Needs <see cref="Permission.InstallFromStore"/>. False if it failed.
	/// </summary>
	public static Task<bool> InstallPackage( Panel app, string ident ) => Call<bool>( app, "store.install", ident );
}
