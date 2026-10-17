using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// An app that exists in the game and can be installed, from a loaded <see cref="NaviAppResource"/>.
/// </summary>
public sealed class NaviAppInfo
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public string Icon { get; init; }
	public string Color { get; init; }
	public bool CanMinimize { get; init; }
	public bool CanMaximize { get; init; }
	public bool CanResize { get; init; }
	public bool CapturesInput { get; init; }

	/// <summary>
	/// Shipped with the game or a library, rather than downloaded from the Store.
	/// </summary>
	public bool IsPrepackaged { get; init; }

	/// <summary>
	/// Always installed, can't be uninstalled, and has all its <see cref="Permissions"/> without asking.
	/// Only a prepackaged app can be one. See <see cref="NaviAppResource.IsSystem"/>.
	/// </summary>
	public bool IsSystem { get; init; }

	/// <summary>
	/// The permissions the app may ask for. See <see cref="NaviPermissions"/>.
	/// </summary>
	public IReadOnlyList<string> Permissions { get; init; }

	/// <summary>
	/// The app's panel class.
	/// </summary>
	public TypeDescription PanelType { get; init; }

	/// <summary>
	/// The resource that defines this app.
	/// </summary>
	public NaviAppResource Resource { get; init; }

	/// <summary>
	/// Create a new instance of the app's panel.
	/// </summary>
	public Panel Create() => PanelType.Create<Panel>();

	internal static NaviAppInfo FromResource( NaviAppResource resource, TypeDescription panelType )
	{
		var prepackaged = !NaviStore.IsDownloaded( resource );

		return new()
		{
			Id = resource.AppId,
			Name = resource.Title ?? resource.AppId,
			Description = resource.Description ?? "",
			Icon = resource.Icon,
			Color = resource.Color,
			CanMinimize = resource.CanMinimize,
			CanMaximize = resource.CanMaximize,
			CanResize = resource.CanResize,
			CapturesInput = resource.CapturesInput,
			IsPrepackaged = prepackaged,
			IsSystem = prepackaged && resource.IsSystem,
			Permissions = resource.Permissions?.Where( NaviPermissions.IsKnown ).Distinct().ToArray() ?? [],
			PanelType = panelType,
			Resource = resource,
		};
	}
}

/// <summary>
/// Finds every app in the game: the loaded <see cref="NaviAppResource"/>s, from the game's and libraries' assets or downloaded
/// from the Store. Adding an app needs no registration code.
/// </summary>
public static class NaviAppRegistry
{
	/// <summary>
	/// Every app, sorted by name. If two apps share an id, a prepackaged one wins over a Store one.
	/// </summary>
	public static IEnumerable<NaviAppInfo> All => ResourceLibrary.GetAll<NaviAppResource>()
		.Where( x => !string.IsNullOrWhiteSpace( x.AppId ) )
		.Select( x => (Resource: x, Panel: x.FindPanelType()) )
		.Where( x => x.Panel is not null )
		.Select( x => NaviAppInfo.FromResource( x.Resource, x.Panel ) )
		.OrderBy( x => x.IsPrepackaged ? 0 : 1 )
		.DistinctBy( x => x.Id )
		.OrderBy( x => x.Name );

	/// <summary>
	/// The app with this id, or null.
	/// </summary>
	public static NaviAppInfo Find( string id ) => All.FirstOrDefault( x => x.Id == id );
}
