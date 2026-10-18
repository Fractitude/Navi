namespace Sandbox;

/// <summary>
/// Defines a Navi plugin: which class to run, and how the plugin is listed. It is published to sbox.game from an addon,
/// with the plugin's code, and the host installs it by its package ident (see <see cref="NaviPluginManager"/>). Prefix
/// the ident with <c>nplug_</c>, e.g. <c>fractitude.nplug_themes</c>.
/// </summary>
[AssetType( Name = "Navi Plugin", Extension = NaviPluginManager.PackageType, Category = "Navi", Flags = AssetTypeFlags.NoEmbedding )]
public sealed class NaviPluginResource : GameResource
{
	/// <summary>
	/// Name shown in the plugin list.
	/// </summary>
	[Property] public string Title { get; set; }

	/// <summary>
	/// One line shown in the plugin list.
	/// </summary>
	[Property] public string Description { get; set; } = "";

	/// <summary>
	/// Full name of the plugin's class, e.g. "NaviThemes.ThemesPlugin". A plain class with a public constructor that takes
	/// no arguments: it needs no base class or interface, since an addon can't see Navi's types. Navi calls its
	/// <c>OnEnable</c> and <c>OnUpdate</c> methods by name. See <see cref="NaviPluginManager"/>.
	/// </summary>
	[Property] public string PluginType { get; set; }

	/// <summary>
	/// The class named by <see cref="PluginType"/>, or null if it isn't loaded or can't be created.
	/// </summary>
	public TypeDescription FindPluginType() => NaviPluginManager.FindPluginType( PluginType );

	public override void ConfigurePublishing( ResourcePublishContext context )
	{
		if ( string.IsNullOrWhiteSpace( Title ) )
		{
			context.SetPublishingDisabled( "Set Title." );
			return;
		}

		if ( FindPluginType() is null )
		{
			context.SetPublishingDisabled( $"Plugin Type '{PluginType}' isn't a class with a public parameterless constructor in this project's code." );
			return;
		}

		context.IncludeCode = true;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "🧩", width, height, "#b49ac4" );
	}
}
