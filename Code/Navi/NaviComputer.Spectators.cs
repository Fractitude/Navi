namespace Sandbox;

/// <summary>
/// Spectator seats (<see cref="NaviSpectatorSeat"/>). A spectator only has their camera taken to the seat. They are not the
/// user: <see cref="Authorize"/> refuses everything they send, so they can't work the computer or answer a prompt. The host
/// keeps who sits where, and the seat is only the camera's place, so nothing a spectator's client says gives it more.
/// </summary>
public sealed partial class NaviComputer
{
	/// <summary>
	/// Who is watching from which seat: connection id to <see cref="NaviSpectatorSeat"/> component id, as text. Only the host
	/// writes it.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public NetDictionary<string, string> Spectators { get; set; } = new();

	/// <summary>
	/// Offline, the seat the local player watches from.
	/// </summary>
	System.Guid spectatingOffline;

	/// <summary>
	/// The seat the local player watches from, or null.
	/// </summary>
	public NaviSpectatorSeat SpectatorSeat
	{
		get
		{
			var id = spectatingOffline;

			if ( Networking.IsActive )
			{
				id = default;
				if ( Connection.Local is { } local && Spectators.TryGetValue( local.Id.ToString(), out var text ) )
					System.Guid.TryParse( text, out id );
			}

			return id == default ? null : FindSpectatorSeat( id );
		}
	}

	/// <summary>
	/// The local player is watching from a spectator seat.
	/// </summary>
	public bool IsSpectating => SpectatorSeat.IsValid();

	NaviSpectatorSeat FindSpectatorSeat( System.Guid id ) =>
		Scene.GetAllComponents<NaviSpectatorSeat>().FirstOrDefault( x => x.Id == id && x.Computer == this );

	/// <summary>
	/// Somebody is watching from <paramref name="seat"/>.
	/// </summary>
	public bool IsSpectatorSeatTaken( NaviSpectatorSeat seat )
	{
		if ( !Networking.IsActive )
			return spectatingOffline == seat.Id;

		var id = seat.Id.ToString();
		return Spectators.Values.Contains( id );
	}

	/// <summary>
	/// The local player may take <paramref name="seat"/>: it is free, and they are neither in the driver's seat nor watching
	/// from another one.
	/// </summary>
	public bool CanSpectate( NaviSpectatorSeat seat ) => !IsSeated && !IsSpectating && !IsSpectatorSeatTaken( seat );

	bool IsSpectator( System.Guid id ) => Spectators.ContainsKey( id.ToString() );

	/// <summary>
	/// The player sits in a spectator seat, if it is free and they aren't the user. Allowed while the screen is locked.
	/// </summary>
	[Rpc.Host]
	public void UserSpectate( System.Guid seatId )
	{
		if ( !IsAuthority || FindSpectatorSeat( seatId ) is not { } seat )
			return;

		if ( !Networking.IsActive )
		{
			if ( !seatedOffline && spectatingOffline == default )
				spectatingOffline = seat.Id;

			return;
		}

		var caller = Rpc.Caller ?? Connection.Local;
		if ( caller is null || caller.Id == UserId || IsSpectator( caller.Id ) || IsSpectatorSeatTaken( seat ) )
			return;

		Spectators[caller.Id.ToString()] = seat.Id.ToString();
	}

	/// <summary>
	/// On the host, in <see cref="UserLeave"/>: stand the caller up if they are watching. Returns whether they were.
	/// </summary>
	bool LeaveSpectating()
	{
		if ( !IsAuthority )
			return false;

		if ( !Networking.IsActive )
		{
			var was = spectatingOffline != default;
			spectatingOffline = default;
			return was;
		}

		var caller = Rpc.Caller ?? Connection.Local;
		return caller is not null && Spectators.Remove( caller.Id.ToString() );
	}

	/// <summary>
	/// On the host: free the seats of players who left the game or whose seat is gone.
	/// </summary>
	void PruneSpectators()
	{
		if ( Spectators.Count == 0 )
			return;

		var gone = Spectators
			.Where( x => !System.Guid.TryParse( x.Key, out var id ) || Connection.Find( id ) is null
				|| !System.Guid.TryParse( x.Value, out var seat ) || FindSpectatorSeat( seat ) is null )
			.Select( x => x.Key )
			.ToList();

		foreach ( var key in gone )
			Spectators.Remove( key );
	}
}
