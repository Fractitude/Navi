namespace Sandbox;

/// <summary>
/// The lock screen's password or pattern. Only the proof of what the player entered ever reaches the host
/// (<see cref="NaviLockCrypto"/>), and only the host's hash of it is kept.
/// </summary>
public sealed partial class NaviComputer
{
	/// <summary>
	/// What unlocks the screen. Saved with the computer. See <see cref="NaviLockKind"/>.
	/// </summary>
	public NaviLockKind LockKind { get; private set; }

	/// <summary>
	/// The salt of <see cref="LockKind"/>'s password, as hex. Not secret: every client gets it, because the player's client
	/// needs it to make the proof. Null if there is no password.
	/// </summary>
	public string LockSalt { get; private set; }

	/// <summary>
	/// The host's hash of the proof. Never sent to a client or shown, and left out of what clients are sent. A field, so
	/// the inspector doesn't show it.
	/// </summary>
	string lockVerifier;

	/// <summary>
	/// The screen is locked with a password or pattern: clicking isn't enough to unlock it.
	/// </summary>
	public bool HasLockCredential => LockKind != NaviLockKind.None;

	/// <summary>
	/// Wrong tries and the wait after them, for each connection, kept by the host: one player's wrong tries never make
	/// another wait. Cleared for a connection by its right one, and for all of them when the screen is unlocked another way.
	/// Offline there is only one, under the empty id.
	/// </summary>
	readonly Dictionary<System.Guid, (int Failed, System.DateTime RetryAt)> attempts = new();

	// What the host said about the player in the seat, on a client.
	int seenFailed;
	System.DateTime seenRetryAt;

	/// <summary>
	/// Wrong passwords or patterns the player in the seat has tried since their last right one, counted by the host for their
	/// connection. Sent to every client so the player sees it, but it says nothing about what was tried.
	/// </summary>
	public int FailedAttempts => IsAuthority ? Attempts( UserId ).Failed : seenFailed;

	/// <summary>
	/// Until when the host refuses to check anything from the player in the seat, after too many wrong tries, in UTC.
	/// Default if it isn't refusing.
	/// </summary>
	public System.DateTime RetryAt => IsAuthority ? Attempts( UserId ).RetryAt : seenRetryAt;

	(int Failed, System.DateTime RetryAt) Attempts( System.Guid connection ) =>
		attempts.TryGetValue( connection, out var entry ) ? entry : default;

	/// <summary>
	/// On the host, in an RPC: the connection that called. Offline, the empty id.
	/// </summary>
	System.Guid CallerId => Networking.IsActive ? (Rpc.Caller ?? Connection.Local)?.Id ?? default : default;

	/// <summary>
	/// Wrong tries allowed before the host starts making the player wait.
	/// </summary>
	public const int FreeAttempts = 3;

	/// <summary>
	/// The longest wait after wrong tries, in seconds. It starts at 5 and doubles with each wrong try.
	/// </summary>
	public const int MaxLockoutSeconds = 300;

	/// <summary>
	/// The host won't check a proof from the player in the seat yet. Only the host enforces it; clients show it.
	/// </summary>
	public bool IsLockedOut => RetryAt > System.DateTime.UtcNow;

	/// <summary>
	/// On this client only, never sent: the player asked to set up or change the lock (from the start menu). The secure
	/// layer shows the dialog, since the password is typed there and nowhere an app could see it.
	/// </summary>
	public bool LockSetupRequested { get; set; }

	/// <summary>
	/// On this client only, never sent: the secure layer is taking a password or pattern. The player's keys aren't forwarded
	/// to any app meanwhile (<see cref="InputCapture"/>), so they can't reach the host that way either.
	/// </summary>
	public bool SecureEntryActive { get; internal set; }

	/// <summary>
	/// On the host, in an RPC: is this the proof of the current password? Counts a wrong one against the caller's connection,
	/// and makes that player wait after <see cref="FreeAttempts"/> of them. Anything that isn't a proof isn't counted, and
	/// nothing is checked while the caller is waiting.
	/// </summary>
	bool CheckLockProof( string proof )
	{
		var caller = CallerId;
		var (failed, retryAt) = Attempts( caller );

		if ( !IsAuthority || !HasLockCredential || lockVerifier is null || retryAt > System.DateTime.UtcNow
			|| !NaviLockCrypto.IsHex( proof, NaviLockCrypto.ProofLength ) )
			return false;

		if ( NaviLockCrypto.Matches( proof, lockVerifier ) )
		{
			attempts.Remove( caller );
			Version++;
			return true;
		}

		failed++;
		if ( failed > FreeAttempts )
			retryAt = System.DateTime.UtcNow.AddSeconds( System.Math.Min( MaxLockoutSeconds, 5 * System.Math.Pow( 2, failed - FreeAttempts - 1 ) ) );

		attempts[caller] = (failed, retryAt);
		Version++;
		return false;
	}

	/// <summary>
	/// On the host: forget the wrong tries of connections that have left the game.
	/// </summary>
	void PruneAttempts()
	{
		foreach ( var id in attempts.Keys.Where( x => x != default && Connection.Find( x ) is null ).ToList() )
			attempts.Remove( id );
	}

	/// <summary>
	/// The user unlocks the screen with a password or pattern. <paramref name="proof"/> is what
	/// <see cref="NaviLockCrypto.Proof"/> made of it on the user's client: the password itself never gets here. Any player in
	/// the seat who has it may unlock the screen, not only the one who locked it.
	/// </summary>
	[Rpc.Host]
	public void UserUnlockWith( string proof )
	{
		if ( Authorize( whileLocked: true ) && IsLocked && HasLockCredential && CheckLockProof( proof ) )
			Unlock();
	}

	/// <summary>
	/// The user sets, changes or removes the lock's password or pattern (<paramref name="kind"/> is a
	/// <see cref="NaviLockKind"/>, None to remove it). Only while the screen isn't locked, and with the current one's proof
	/// if there is one. <paramref name="salt"/> and <paramref name="proof"/> are made on the user's client; the host keeps
	/// the salt and a hash of the proof.
	/// </summary>
	[Rpc.Host]
	public void UserSetLock( string currentProof, int kind, string salt, string proof )
	{
		if ( !Authorize() || (HasLockCredential && !CheckLockProof( currentProof )) )
			return;

		if ( kind == (int)NaviLockKind.None )
		{
			LockKind = NaviLockKind.None;
			LockSalt = null;
			lockVerifier = null;
		}
		else if ( kind is (int)NaviLockKind.Password or (int)NaviLockKind.Pattern
			&& NaviLockCrypto.IsHex( salt, NaviLockCrypto.SaltLength ) && NaviLockCrypto.IsHex( proof, NaviLockCrypto.ProofLength ) )
		{
			LockKind = (NaviLockKind)kind;
			LockSalt = salt.ToUpperInvariant();
			lockVerifier = NaviLockCrypto.Verifier( proof );
		}
		else
		{
			return;
		}

		Version++;
	}
}
