using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Finds and downloads Navi apps that players published to sbox.game as <see cref="NaviAppResource"/>s.
/// </summary>
public static class NaviStore
{
	/// <summary>
	/// Package type of published apps. It is the file extension of <see cref="NaviAppResource"/>.
	/// </summary>
	public const string PackageType = "napp";

	/// <summary>
	/// Published apps matching <paramref name="text"/>, or all of them when it is empty.
	/// </summary>
	public static async Task<List<Package>> SearchAsync( string text = "" )
	{
		var result = await Package.FindAsync( $"type:{PackageType} {text}".Trim() );

		// The backend ignores a type filter until a package of that type exists, so check the type here too.
		return result?.Packages?.Where( x => x.TypeName == PackageType ).ToList() ?? new();
	}

	/// <summary>
	/// Download a published app and load its code. Returns the app, or null if the package isn't a working Navi app.
	/// </summary>
	public static async Task<NaviAppInfo> DownloadAsync( string packageIdent )
	{
		var resource = await Cloud.Load<NaviAppResource>( packageIdent, withCode: true );
		if ( resource is null )
		{
			Log.Warning( $"NaviStore: '{packageIdent}' is not a Navi app" );
			return null;
		}

		var app = NaviAppRegistry.Find( resource.AppId );
		if ( app is null )
		{
			Log.Warning( $"NaviStore: '{packageIdent}' has no panel class '{resource.PanelType}' in its code" );
		}

		return app;
	}
}
