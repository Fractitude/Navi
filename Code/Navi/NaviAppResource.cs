namespace Sandbox;

/// <summary>
/// Defines a Navi app whose panel is any <see cref="Sandbox.UI.Panel"/>, and publishes it to the Store (sbox.game).
/// The panel doesn't need to use any Navi types, so an app published from an addon works in every game with the Navi library.
/// Create one in an addon project that targets a game with Navi, set <see cref="PanelType"/>, and publish it from the asset browser.
/// The addon's code is published with it.
/// </summary>
[AssetType( Name = "Navi App", Extension = NaviStore.PackageType, Category = "Navi", Flags = AssetTypeFlags.NoEmbedding )]
public sealed class NaviAppResource : GameResource
{
	/// <summary>
	/// Unique, stable id. Saved in <see cref="NaviComputer.InstalledApps"/>, so don't change it once published.
	/// </summary>
	[Property] public string AppId { get; set; }

	/// <summary>
	/// Name shown on the taskbar tooltip, window title and package list.
	/// </summary>
	[Property] public string Title { get; set; }

	/// <summary>
	/// One line shown in the package list.
	/// </summary>
	[Property] public string Description { get; set; } = "";

	/// <summary>
	/// Material icon name, e.g. "calculate".
	/// </summary>
	[Property] public string Icon { get; set; } = "apps";

	/// <summary>
	/// Icon background, as a CSS color.
	/// </summary>
	[Property] public string Color { get; set; } = "#8a8472";

	/// <summary>
	/// Full name of the app's panel class, e.g. "NaviCounter.Counter". Give each app its own namespace so names don't clash.
	/// </summary>
	[Property] public string PanelType { get; set; }

	/// <inheritdoc cref="NaviAppAttribute.CanMinimize"/>
	[Property] public bool CanMinimize { get; set; } = true;

	/// <inheritdoc cref="NaviAppAttribute.CanMaximize"/>
	[Property] public bool CanMaximize { get; set; } = true;

	/// <inheritdoc cref="NaviAppAttribute.CanResize"/>
	[Property] public bool CanResize { get; set; } = true;

	/// <summary>
	/// The panel class named by <see cref="PanelType"/>, or null if it isn't loaded.
	/// </summary>
	public TypeDescription FindPanelType()
	{
		if ( string.IsNullOrWhiteSpace( PanelType ) )
			return null;

		var panels = TypeLibrary.GetTypes<Sandbox.UI.Panel>().Where( x => !x.IsAbstract ).ToList();
		return panels.FirstOrDefault( x => x.FullName == PanelType ) ?? panels.FirstOrDefault( x => x.ClassName == PanelType );
	}

	public override void ConfigurePublishing( ResourcePublishContext context )
	{
		if ( string.IsNullOrWhiteSpace( AppId ) || string.IsNullOrWhiteSpace( Title ) )
		{
			context.SetPublishingDisabled( "Set App Id and Title." );
			return;
		}

		if ( FindPanelType() is null )
		{
			context.SetPublishingDisabled( $"Panel Type '{PanelType}' isn't a panel class in this project's code." );
			return;
		}

		context.IncludeCode = true;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "💾", width, height, "#9ab0c4" );
	}
}
