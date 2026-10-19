namespace Sandbox;

/// <summary>
/// A window an app can open besides its main one (<see cref="NaviAppResource.Windows"/>). An app can only open windows its
/// manifest lists, each with a panel class from its own code.
/// </summary>
public sealed class NaviWindowResource
{
	/// <summary>
	/// Names the window to <c>Navi.Window.OpenAsync</c>: 1 to 32 lowercase letters, digits, '-' or '_'. Unique within the app.
	/// </summary>
	[Property] public string Id { get; set; }

	/// <summary>
	/// Shown in the title bar after the app's name. Optional.
	/// </summary>
	[Property] public string Title { get; set; } = "";

	/// <summary>
	/// Full name of the window's panel class, e.g. "NaviCounter.CounterSettings".
	/// </summary>
	[Property] public string PanelType { get; set; }

	[Property] public bool CanMinimize { get; set; } = true;
	[Property] public bool CanMaximize { get; set; } = true;
	[Property] public bool CanResize { get; set; } = true;

	/// <summary>
	/// Shape the window's content keeps when it is resized, as width and height, e.g. 4 by 3. Zero for any shape.
	/// See <see cref="NaviAppResource.AspectRatio"/>.
	/// </summary>
	[Property] public Vector2 AspectRatio { get; set; }
}

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
	/// The app starts without a window, in the system tray. <see cref="PanelType"/> is then its service: a panel that runs
	/// while the app does and is never drawn. It sets its tray icon and menu (<c>Navi.Tray.SetAsync</c>) and opens the windows
	/// listed in <see cref="Windows"/> (<c>Navi.Window.OpenAsync</c>). It runs until the player quits it from the tray menu.
	/// </summary>
	[Property] public bool Headless { get; set; }

	/// <summary>
	/// More windows the app can open, each with a panel of its own. A normal app's <see cref="PanelType"/> is its main
	/// window, and closing that closes the app and these. A headless app has only these.
	/// </summary>
	[Property] public List<NaviWindowResource> Windows { get; set; } = new();

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
	/// Shape the window's content (below the title bar) keeps when the player resizes it, as width and height, e.g. 4 by 3
	/// or the app's own size in pixels. Zero for any shape. The desktop holds it while a corner is dragged, as macOS does
	/// with a window's content aspect ratio, so the app doesn't fight the drag. The app still scales what it draws to fill
	/// its window, and centers it when the shape can't be kept, as when maximized.
	/// </summary>
	[Property] public Vector2 AspectRatio { get; set; }

	/// <summary>
	/// In multiplayer, every player sees the same app: it runs on the host, and other clients only draw what it publishes.
	/// The app must be written for it, with <c>Navi.Publish</c>, <c>Navi.GetState</c> and <c>Navi.Act</c>. Without it, each
	/// client runs its own copy, so players see different things in the window.
	/// </summary>
	[Property] public bool Shared { get; set; }

	/// <summary>
	/// The permissions the app may ask for (<see cref="NaviPermissions"/>). It can't be granted any others.
	/// </summary>
	[Property] public List<string> Permissions { get; set; } = new();

	/// <summary>
	/// The kinds of file the app opens, as extensions without the dot, e.g. "txt". Opening one in the file explorer
	/// (<c>Navi.Files.OpenAsync</c>) launches the app with it. The computer picks the app, an app that ships with the game
	/// first; one from the Store is only picked if it declares <see cref="NaviPermissions.Files"/>, since without it the
	/// file is out of its reach.
	/// </summary>
	[Property] public List<string> FileTypes { get; set; } = new();

	/// <summary>
	/// A system app: it asks for full system access (root), which allows everything, whatever <see cref="Permissions"/> lists.
	/// A Store app gets it only after the player accepts, when they install it. One that ships with the game or a library
	/// is part of the OS: it has root without asking, is always installed, and can't be uninstalled.
	/// </summary>
	[Property] public bool IsSystem { get; set; }

	/// <summary>
	/// The panel class named by <see cref="PanelType"/>, or null if it isn't loaded.
	/// </summary>
	public TypeDescription FindPanelType() => FindPanelType( PanelType );

	internal static TypeDescription FindPanelType( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		var panels = TypeLibrary.GetTypes<Sandbox.UI.Panel>().Where( x => !x.IsAbstract ).ToList();
		return panels.FirstOrDefault( x => x.FullName == name ) ?? panels.FirstOrDefault( x => x.ClassName == name );
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

		var windowIds = new HashSet<string>();
		foreach ( var window in Windows ?? [] )
		{
			if ( !NaviWindowInfo.IsValidId( window?.Id ) || !windowIds.Add( window.Id ) )
			{
				context.SetPublishingDisabled( $"Window id '{window?.Id}' must be 1 to 32 lowercase letters, digits, '-' or '_', and unique." );
				return;
			}

			if ( FindPanelType( window.PanelType ) is null )
			{
				context.SetPublishingDisabled( $"Window '{window.Id}': Panel Type '{window.PanelType}' isn't a panel class in this project's code." );
				return;
			}
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
