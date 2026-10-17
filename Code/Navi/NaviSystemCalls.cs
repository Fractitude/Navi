using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// The calls apps make to the computer, through the <c>Navi</c> client (<c>Code/Navi/Client/Navi.cs</c>).
/// A call is a <c>navi.call</c> panel event that bubbles up from the app to its window, with a value of
/// <c>(string Method, object[] Args, TaskCompletionSource&lt;object&gt; Reply)</c>. Everything in it is a .NET or engine type,
/// so apps don't need Navi types and Store apps work in every game. The call runs as the app's window: a call it lacks
/// the permission for fails with <see cref="System.UnauthorizedAccessException"/> (<see cref="NaviPermissions.Missing"/>).
/// Keep method names, arguments and results stable: published apps depend on them.
/// </summary>
public static class NaviSystemCalls
{
	public const string EventName = "navi.call";

	/// <summary>
	/// Run a call for <paramref name="process"/> and answer it on <paramref name="reply"/>.
	/// </summary>
	public static async Task RunAsync( NaviComputer computer, NaviProcess process, string method, object[] args, TaskCompletionSource<object> reply )
	{
		try
		{
			reply.TrySetResult( await CallAsync( computer, process, method, args ?? [] ) );
		}
		catch ( System.Exception e )
		{
			reply.TrySetException( e );
		}
	}

	static async Task<object> CallAsync( NaviComputer computer, NaviProcess process, string method, object[] args )
	{
		var self = process.App.Id;

		switch ( method )
		{
			// The app's own window.
			case "window.close":
				computer.Close( self );
				return null;

			case "window.minimize":
				return computer.Minimize( self );

			case "window.maximize":
				return computer.ToggleMaximized( self );

			// Permissions.
			case "permissions.get":
				return process.Permissions.ToArray();

			case "permissions.request":
			{
				var asked = Arg<string[]>( args, 0, method );
				var granted = await computer.RequestPermissionsAsync( process, asked );
				return (granted, asked.Except( granted ).ToArray());
			}

			// Apps.
			case "system.version":
				return computer.Version;

			case "apps.list":
				return NaviAppRegistry.All
					.Select( x => (x.Id, x.Name, x.Description, x.Icon, x.Color, computer.IsInstalled( x ), x.IsSystem) )
					.ToArray();

			case "apps.launch":
				return computer.Launch( Arg<string>( args, 0, method ) );

			case "apps.install":
				Require( process, NaviPermissions.InstallApps );
				return computer.Install( Arg<string>( args, 0, method ) );

			case "apps.uninstall":
				Require( process, NaviPermissions.InstallApps );
				return computer.Uninstall( Arg<string>( args, 0, method ) );

			case "apps.close":
			{
				var id = Arg<string>( args, 0, method );
				if ( id != self )
					Require( process, NaviPermissions.ManageApps );

				computer.Close( id );
				return null;
			}

			// The Store.
			case "store.search":
			{
				var packages = await NaviStore.SearchAsync( args.Length > 0 ? args[0] as string ?? "" : "" );
				return packages.Select( x => (x, NaviStore.IsOfficial( x )) ).ToArray();
			}

			case "store.status":
				return (computer.InstalledPackages?.ToArray() ?? [], computer.DownloadingPackages.ToArray());

			case "store.install":
				Require( process, NaviPermissions.InstallFromStore );
				return await computer.InstallPackageAsync( Arg<string>( args, 0, method ) );
		}

		throw new System.NotSupportedException( $"Navi has no call '{method}'." );
	}

	static void Require( NaviProcess process, string permission )
	{
		if ( !process.HasPermission( permission ) )
			throw NaviPermissions.Missing( permission );
	}

	static T Arg<T>( object[] args, int index, string method )
	{
		if ( index < args.Length && args[index] is T value )
			return value;

		throw new System.ArgumentException( $"'{method}' needs a {typeof( T ).Name} as argument {index + 1}." );
	}
}
