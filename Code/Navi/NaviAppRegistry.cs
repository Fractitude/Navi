using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// An app that exists in the game and can be installed. It comes from either a <see cref="NaviApp"/> class with a
/// <see cref="NaviAppAttribute"/>, or a <see cref="NaviAppResource"/> that names a panel class.
/// </summary>
public sealed class NaviAppInfo
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public string Icon { get; init; }
	public string Color { get; init; }
	public bool IsSystem { get; init; }
	public bool CanMinimize { get; init; }
	public bool CanMaximize { get; init; }
	public bool CanResize { get; init; }
	public bool CapturesInput { get; init; }

	/// <summary>
	/// The app's panel class.
	/// </summary>
	public TypeDescription PanelType { get; init; }

	/// <summary>
	/// The resource that defines this app, or null for an app defined by <see cref="NaviAppAttribute"/>.
	/// </summary>
	public NaviAppResource Resource { get; init; }

	/// <summary>
	/// Create a new instance of the app's panel.
	/// </summary>
	public Panel Create() => PanelType.Create<Panel>();

	internal static NaviAppInfo FromAttribute( TypeDescription type, NaviAppAttribute attribute ) => new()
	{
		Id = attribute.Id,
		Name = attribute.Name,
		Description = attribute.Description,
		Icon = attribute.Icon,
		Color = attribute.Color,
		IsSystem = attribute.IsSystem,
		CanMinimize = attribute.CanMinimize,
		CanMaximize = attribute.CanMaximize,
		CanResize = attribute.CanResize,
		CapturesInput = attribute.CapturesInput,
		PanelType = type,
	};

	internal static NaviAppInfo FromResource( NaviAppResource resource, TypeDescription panelType ) => new()
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
		PanelType = panelType,
		Resource = resource,
	};
}

/// <summary>
/// Finds every app in the game: <see cref="NaviApp"/> classes with a <see cref="NaviAppAttribute"/>, and loaded
/// <see cref="NaviAppResource"/>s (from the game's assets, or downloaded from the Store). Adding an app needs no registration code.
/// </summary>
public static class NaviAppRegistry
{
	/// <summary>
	/// Every app, sorted by name. If two apps share an id, the one defined in code wins.
	/// </summary>
	public static IEnumerable<NaviAppInfo> All => CodeApps()
		.Concat( ResourceApps() )
		.DistinctBy( x => x.Id )
		.OrderBy( x => x.Name );

	/// <summary>
	/// The app with this id, or null.
	/// </summary>
	public static NaviAppInfo Find( string id ) => All.FirstOrDefault( x => x.Id == id );

	static IEnumerable<NaviAppInfo> CodeApps() => TypeLibrary.GetTypes<NaviApp>()
		.Where( x => !x.IsAbstract )
		.Select( x => (Type: x, Attribute: x.GetAttribute<NaviAppAttribute>()) )
		.Where( x => x.Attribute is not null )
		.Select( x => NaviAppInfo.FromAttribute( x.Type, x.Attribute ) );

	static IEnumerable<NaviAppInfo> ResourceApps() => ResourceLibrary.GetAll<NaviAppResource>()
		.Where( x => !string.IsNullOrWhiteSpace( x.AppId ) )
		.Select( x => (Resource: x, Panel: x.FindPanelType()) )
		.Where( x => x.Panel is not null )
		.Select( x => NaviAppInfo.FromResource( x.Resource, x.Panel ) );
}
