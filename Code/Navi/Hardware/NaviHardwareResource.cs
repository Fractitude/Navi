namespace Sandbox;

/// <summary>
/// A piece of hardware a <see cref="NaviComputer"/> can have installed, such as a network card. What it does is the
/// capabilities it provides (<see cref="Capabilities"/>): the computer asks for a capability, never for a particular piece
/// of hardware, so a game can add its own hardware (a USB modem, a better sound card) that does the same as Navi's. A
/// hardware's id is its resource path, e.g. <c>navi/hardware/networkcard.nhw</c> (<see cref="NaviHardware.NetworkCard"/>).
/// Don't move or rename one once it is used: saves name hardware by its id.
/// </summary>
/// <remarks>
/// Only game code installs hardware (<see cref="NaviComputer.InstallHardware(string)"/>), on the host. No app call or
/// <c>User…</c> RPC does. Hardware isn't physical yet; a world item installed onto the computer would call the same method.
/// </remarks>
[AssetType( Name = "Navi Hardware", Extension = "nhw", Category = "Navi", Flags = AssetTypeFlags.NoEmbedding )]
public sealed class NaviHardwareResource : GameResource
{
	/// <summary>
	/// Name shown to the player, e.g. in Settings.
	/// </summary>
	[Property] public string Title { get; set; }

	/// <summary>
	/// One line about what it is.
	/// </summary>
	[Property] public string Description { get; set; } = "";

	/// <summary>
	/// Material icon name, e.g. "lan".
	/// </summary>
	[Property] public string Icon { get; set; } = "memory";

	/// <summary>
	/// What the computer can do with it installed: <see cref="NaviHardware.Network"/>, <see cref="NaviHardware.Audio"/>, or
	/// a capability of the game's own. Compared without case.
	/// </summary>
	[Property] public List<string> Capabilities { get; set; } = new();

	/// <summary>
	/// The hardware's id: its resource path.
	/// </summary>
	public string Id => ResourcePath;

	public string Name => string.IsNullOrWhiteSpace( Title ) ? System.IO.Path.GetFileNameWithoutExtension( ResourcePath ) : Title;

	/// <summary>
	/// Does it provide <paramref name="capability"/>?
	/// </summary>
	public bool Provides( string capability ) =>
		!string.IsNullOrWhiteSpace( capability )
		&& (Capabilities?.Any( x => string.Equals( x?.Trim(), capability, System.StringComparison.OrdinalIgnoreCase ) ) ?? false);

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "🔌", width, height, "#9ab0c4" );
	}
}

/// <summary>
/// The capabilities Navi itself knows, and the hardware it comes with.
/// </summary>
public static class NaviHardware
{
	/// <summary>
	/// Reaching other computers: the Store, Messages, the web, and other computers by IP address (<see cref="NaviNetwork"/>).
	/// Without it the computer is offline, and every call that would go out fails with <c>Navi.NoNetworkException</c>.
	/// </summary>
	public const string Network = "network";

	/// <summary>
	/// Playing sounds: app sounds (<c>Navi.Audio.PlayAsync</c>) and the notification sound. Without it the computer can only
	/// beep (<see cref="NaviComputer.Beep"/>), which it does for notifications too.
	/// </summary>
	public const string Audio = "audio";

	public const string Folder = "navi/hardware/";

	/// <summary>
	/// Navi's network card. Provides <see cref="Network"/>.
	/// </summary>
	public const string NetworkCard = Folder + "networkcard.nhw";

	/// <summary>
	/// Navi's sound card. Provides <see cref="Audio"/>.
	/// </summary>
	public const string SoundCard = Folder + "soundcard.nhw";

	/// <summary>
	/// The hardware with this id (resource path), or null.
	/// </summary>
	public static NaviHardwareResource Find( string id ) =>
		string.IsNullOrWhiteSpace( id ) ? null : ResourceLibrary.Get<NaviHardwareResource>( id );

	/// <summary>
	/// Every piece of hardware the game and its libraries define.
	/// </summary>
	public static IEnumerable<NaviHardwareResource> All => ResourceLibrary.GetAll<NaviHardwareResource>();
}
