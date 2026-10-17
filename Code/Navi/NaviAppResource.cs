namespace Sandbox;

/// <summary>
/// Defines a Navi app: a <see cref="Sandbox.UI.Panel"/> class plus how it appears and what it may ask for. Every app is
/// one of these. Built-in apps are in the library's assets; players publish theirs to the Store (sbox.game) from an addon.
/// The panel doesn't use Navi types. It talks to the computer through the <c>Navi</c> client (<c>Code/Navi/Client/Navi.cs</c>),
/// so an app published from an addon works in every game with the Navi library. The app's id isn't set here: see
/// <see cref="NaviAppRegistry.IdOf"/>.
/// </summary>
[AssetType( Name = "Navi App", Extension = NaviStore.PackageType, Category = "Navi", Flags = AssetTypeFlags.NoEmbedding )]
public sealed class NaviAppResource : GameResource
{
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

	/// <summary>
	/// Has a minimize button.
	/// </summary>
	[Property] public bool CanMinimize { get; set; } = true;

	/// <summary>
	/// Has a maximize button and maximizes on title bar double-click.
	/// </summary>
	[Property] public bool CanMaximize { get; set; } = true;

	/// <summary>
	/// Can be resized by its corners.
	/// </summary>
	[Property] public bool CanResize { get; set; } = true;

	/// <summary>
	/// Takes over the player's controls while its window is in front. See <see cref="NaviInputCapture"/>.
	/// </summary>
	[Property] public bool CapturesInput { get; set; }

	/// <summary>
	/// The permissions the app may ask for (<see cref="NaviPermissions"/>). It can't be granted any others.
	/// </summary>
	[Property] public List<string> Permissions { get; set; } = new();

	/// <summary>
	/// A system app: it asks for full system access (root), which allows everything, whatever <see cref="Permissions"/> lists.
	/// A Store app gets it only after the player accepts, when they install it. One that ships with the game or a library
	/// is part of the OS: it has root without asking, is always installed, and can't be uninstalled.
	/// </summary>
	[Property] public bool IsSystem { get; set; }

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
		if ( string.IsNullOrWhiteSpace( Title ) )
		{
			context.SetPublishingDisabled( "Set Title." );
			return;
		}

		if ( FindPanelType() is null )
		{
			context.SetPublishingDisabled( $"Panel Type '{PanelType}' isn't a panel class in this project's code." );
			return;
		}

		if ( Permissions?.FirstOrDefault( x => !NaviPermissions.IsKnown( x ) ) is { } unknown )
		{
			context.SetPublishingDisabled( $"'{unknown}' isn't a Navi permission." );
			return;
		}

		context.IncludeCode = true;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "💾", width, height, "#9ab0c4" );
	}
}
