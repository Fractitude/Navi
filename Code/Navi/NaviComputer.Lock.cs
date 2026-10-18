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
	/// Wrong passwords or patterns since the last right one, or since the screen was unlocked another way. Sent to every
	/// client so the player sees it, but it says nothing about what was tried.
	/// </summary>
	public int FailedAttempts { get; private set; }

	/// <summary>
	/// Until when the host refuses to check anything, after too many wrong tries, in UTC. Default if it isn't refusing.
	/// </summary>
	public System.DateTime RetryAt { get; private set; }

	/// <summary>
	/// Wrong tries allowed before the host starts making the player wait.
	/// </summary>
	public const int FreeAttempts = 3;

	/// <summary>
	/// The longest wait after wrong tries, in seconds. It starts at 5 and doubles with each wrong try.
	/// </summary>
	public const int MaxLockoutSeconds = 300;

	/// <summary>
	/// The host won't check a proof yet. Only the host enforces it; clients show it.
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
	/// On the host: is this the proof of the current password? Counts a wrong one, and makes the player wait after
	/// <see cref="FreeAttempts"/> of them. Anything that isn't a proof isn't counted.
	/// </summary>
	bool CheckLockProof( string proof )
	{
		if ( !IsAuthority || !HasLockCredential || lockVerifier is null || IsLockedOut || !NaviLockCrypto.IsHex( proof, NaviLockCrypto.ProofLength ) )
			return false;

		if ( NaviLockCrypto.Matches( proof, lockVerifier ) )
		{
			ClearFailedAttempts();
			return true;
		}

		FailedAttempts++;
		if ( FailedAttempts > FreeAttempts )
			RetryAt = System.DateTime.UtcNow.AddSeconds( System.Math.Min( MaxLockoutSeconds, 5 * System.Math.Pow( 2, FailedAttempts - FreeAttempts - 1 ) ) );

		Version++;
		return false;
	}

	void ClearFailedAttempts()
	{
		FailedAttempts = 0;
		RetryAt = default;
		Version++;
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
