namespace Sandbox;

/// <summary>
/// The network between Navi computers. A computer is on it while it has hardware that provides
/// <see cref="NaviHardware.Network"/> (<see cref="NaviComputer.IsOnline"/>), and is known on it by its IP address
/// (<see cref="NaviComputer.IpAddress"/>). Everything that reaches beyond the computer goes through here: an app call that
/// goes out calls <see cref="Require"/> first, and game code finds another computer with <see cref="Reach"/>, which only
/// answers when both are online. Any new call that talks to another computer, a server or the internet must do the same.
/// </summary>
/// <remarks>
/// It blocks what goes through Navi. An app's code can still use the engine's own web panel or HTTP directly: see the known
/// limit in the security notes. The built-in apps check <c>Navi.Network</c> before they do.
/// </remarks>
public static class NaviNetwork
{
	/// <summary>
	/// Starts the message of the error an app gets for a call that needs the network on a computer that is offline. The
	/// client, which has no Navi types, reads it and throws <c>Navi.NoNetworkException</c>.
	/// </summary>
	public const string ErrorPrefix = Navi.NoNetworkPrefix;

	/// <summary>
	/// The error for a call that needs the network on a computer that has none.
	/// </summary>
	public static System.InvalidOperationException NotConnected() =>
		new( ErrorPrefix + "this computer has no network connection." );

	/// <summary>
	/// Throw <see cref="NotConnected"/> unless the computer is online. Call it before anything that goes out.
	/// </summary>
	public static void Require( NaviComputer computer )
	{
		if ( computer is null || !computer.IsOnline )
			throw NotConnected();
	}

	/// <summary>
	/// The computer in <paramref name="scene"/> with this IP address, online or not, or null. For game code; a computer that
	/// wants to talk to it uses <see cref="Reach"/>.
	/// </summary>
	public static NaviComputer Find( Scene scene, string address )
	{
		if ( scene is null || !IsAddress( address ) )
			return null;

		return scene.GetAllComponents<NaviComputer>().FirstOrDefault( x => x.IpAddress == address );
	}

	/// <summary>
	/// The computer at <paramref name="address"/> as <paramref name="from"/> sees it over the network: null if either is
	/// offline, there is no computer there, or it is <paramref name="from"/> itself.
	/// </summary>
	public static NaviComputer Reach( NaviComputer from, string address )
	{
		if ( from is null || !from.IsOnline )
			return null;

		var to = Find( from.Scene, address );
		return to is not null && to != from && to.IsOnline ? to : null;
	}

	/// <summary>
	/// Is this an IPv4 address written plainly: four numbers 0 to 255 with dots, no leading zeros?
	/// </summary>
	public static bool IsAddress( string address )
	{
		if ( string.IsNullOrEmpty( address ) || address.Length > 15 )
			return false;

		var parts = address.Split( '.' );
		return parts.Length == 4 && parts.All( x =>
			x.Length is > 0 and <= 3 && x.All( char.IsAsciiDigit ) && (x.Length == 1 || x[0] != '0') && int.Parse( x ) <= 255 );
	}

	/// <summary>
	/// A new address in 10.0.0.0/8, the private range, that <paramref name="taken"/> says is free. Never one ending in .0 or
	/// .255. Null if none was found, which only a nearly full network gives.
	/// </summary>
	internal static string NewAddress( System.Func<string, bool> taken )
	{
		var random = new System.Random();

		for ( var i = 0; i < 1000; i++ )
		{
			var address = $"10.{random.Next( 0, 256 )}.{random.Next( 0, 256 )}.{random.Next( 1, 255 )}";
			if ( !taken( address ) )
				return address;
		}

		return null;
	}
}
