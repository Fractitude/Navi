using System.Security.Cryptography;
using System.Text;

namespace Sandbox;

/// <summary>
/// What unlocks the computer's lock screen.
/// </summary>
public enum NaviLockKind
{
	/// <summary>
	/// Nothing: the player who locked the screen clicks to unlock it.
	/// </summary>
	None,

	/// <summary>
	/// Letters and digits, 4 to <see cref="NaviLockCrypto.MaxPasswordLength"/> of them. Digits only makes it a PIN.
	/// </summary>
	Password,

	/// <summary>
	/// At least <see cref="NaviLockCrypto.MinPatternNodes"/> dots of a 3 by 3 grid, joined by dragging the mouse.
	/// </summary>
	Pattern,
}

/// <summary>
/// How a password or pattern becomes something the host can check without ever having it. The player's client turns it
/// into a <b>proof</b> with a slow, salted hash (PBKDF2): that is all that is sent. The host keeps only a hash of the proof,
/// the <b>verifier</b>, so neither what the player typed nor the proof is stored, and a leaked save can't be replayed: it
/// has to be guessed against the slow hash, one password at a time.
/// </summary>
/// <remarks>
/// The salt isn't secret and is sent to every client, so the player's client can use it. The proof only ever goes from the
/// player's client to the host; the host never sends it, nor the verifier, to anyone. The password and the pattern never
/// leave the secure layer's panel on the client where they were entered.
/// </remarks>
public static class NaviLockCrypto
{
	public const int MinPasswordLength = 4;
	public const int MaxPasswordLength = 64;
	public const int MinPatternNodes = 4;

	/// <summary>
	/// The pattern grid is this many dots across and down.
	/// </summary>
	public const int PatternSize = 3;

	public const int SaltBytes = 16;
	const int ProofBytes = 32;
	const int Iterations = 100000;

	/// <summary>
	/// Length of a salt as hex text.
	/// </summary>
	public const int SaltLength = SaltBytes * 2;

	/// <summary>
	/// Length of a proof, and of a verifier, as hex text.
	/// </summary>
	public const int ProofLength = ProofBytes * 2;

	/// <summary>
	/// A new random salt, from a version 4 GUID: the operating system's random numbers, 122 bits of them. (The sandbox doesn't
	/// allow the random number generator itself. A salt only has to be different each time, not secret.)
	/// </summary>
	public static string NewSalt() => System.Convert.ToHexString( System.Guid.NewGuid().ToByteArray() );

	/// <summary>
	/// The proof for a password, or a pattern written as its dots' numbers in order (0 to 8, left to right, top to bottom).
	/// Slow on purpose.
	/// </summary>
	public static string Proof( NaviLockKind kind, string secret, string salt )
	{
		return System.Convert.ToHexString( Pbkdf2( Encoding.UTF8.GetBytes( $"{kind}:{secret}" ), System.Convert.FromHexString( salt ) ) );
	}

	/// <summary>
	/// PBKDF2 with HMAC-SHA256 (RFC 8018), one 32 byte block, made from SHA-256 itself: the sandbox allows that, not the
	/// framework's own PBKDF2, HMAC or random number generator.
	/// </summary>
	static byte[] Pbkdf2( byte[] password, byte[] salt )
	{
		var key = new byte[64];
		( password.Length > 64 ? SHA256.HashData( password ) : password ).CopyTo( key, 0 );

		// HMAC(key, message) = H( key^opad + H( key^ipad + message ) ).
		var inner = new byte[64 + System.Math.Max( 32, salt.Length + 4 )];
		var outer = new byte[64 + 32];
		for ( var i = 0; i < 64; i++ )
		{
			inner[i] = (byte)(key[i] ^ 0x36);
			outer[i] = (byte)(key[i] ^ 0x5c);
		}

		byte[] Hmac( int messageLength )
		{
			var hash = SHA256.HashData( System.MemoryExtensions.AsSpan( inner, 0, 64 + messageLength ).ToArray() );
			hash.CopyTo( outer, 64 );
			return SHA256.HashData( outer );
		}

		// U1 = HMAC( salt + the block number, 1 ).
		salt.CopyTo( inner, 64 );
		inner[64 + salt.Length + 3] = 1;
		var u = Hmac( salt.Length + 4 );
		var result = new byte[u.Length];
		u.CopyTo( result, 0 );

		for ( var i = 1; i < Iterations; i++ )
		{
			u.CopyTo( inner, 64 );
			u = Hmac( 32 );

			for ( var j = 0; j < result.Length; j++ )
				result[j] ^= u[j];
		}

		return result;
	}

	/// <summary>
	/// What the host keeps in place of the proof.
	/// </summary>
	public static string Verifier( string proof ) => System.Convert.ToHexString( SHA256.HashData( System.Convert.FromHexString( proof ) ) );

	/// <summary>
	/// Does this proof go with this verifier? Takes the same time whatever differs.
	/// </summary>
	public static bool Matches( string proof, string verifier )
	{
		if ( !IsHex( proof, ProofLength ) || !IsHex( verifier, ProofLength ) )
			return false;

		var a = System.Convert.FromHexString( Verifier( proof ) );
		var b = System.Convert.FromHexString( verifier );

		var difference = 0;
		for ( var i = 0; i < a.Length; i++ )
			difference |= a[i] ^ b[i];

		return difference == 0;
	}

	/// <summary>
	/// Is this hex text of exactly <paramref name="length"/> characters? Anything from the network is checked before use.
	/// </summary>
	public static bool IsHex( string text, int length ) =>
		text is not null && text.Length == length && text.All( System.Uri.IsHexDigit );

	/// <summary>
	/// Does this fit the rules for a password of this kind: 4 to 64 letters and digits, or a pattern of at least 4 distinct
	/// dots? What is entered is checked by the player's client; the host only ever sees the proof.
	/// </summary>
	public static bool IsAcceptable( NaviLockKind kind, string secret ) => kind switch
	{
		NaviLockKind.Password => secret is { Length: >= MinPasswordLength and <= MaxPasswordLength } && secret.All( char.IsAsciiLetterOrDigit ),
		NaviLockKind.Pattern => secret is { Length: >= MinPatternNodes and <= PatternSize * PatternSize }
			&& secret.All( x => x >= '0' && x < '0' + PatternSize * PatternSize ) && secret.Distinct().Count() == secret.Length,
		_ => false,
	};
}
