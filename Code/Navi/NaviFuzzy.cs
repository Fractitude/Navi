namespace Sandbox;

/// <summary>
/// Fuzzy matching for the start menu's search. Scores how well a query fits a text, so the best matches sort first and
/// near misses still show up: a typo ("calcualtor"), the first letters of the words ("wb" for Web Browser), or letters that
/// are in order but not next to each other ("pcmn" for Pac-Man).
/// </summary>
public static class NaviFuzzy
{
	/// <summary>
	/// How well <paramref name="query"/> fits the name of something, from 0 (not at all) up to <see cref="Exact"/>. Case and
	/// spaces don't matter.
	/// </summary>
	public static int ScoreName( string query, string name )
	{
		var q = Normalize( query );
		var text = (name ?? "").ToLowerInvariant();
		if ( q.Length == 0 || text.Length == 0 )
			return 0;

		var compact = Normalize( text );
		var words = Words( text );

		if ( compact == q )
			return Exact;

		if ( compact.StartsWith( q ) )
			return 900 - (compact.Length - q.Length);

		if ( words.Any( x => x.StartsWith( q ) ) )
			return 800;

		var index = compact.IndexOf( q, System.StringComparison.Ordinal );
		if ( index >= 0 )
			return 700 - index;

		if ( q.Length >= 2 && string.Concat( words.Select( x => x[0] ) ).StartsWith( q ) )
			return 650;

		var best = 0;

		if ( q.Length >= 2 )
			best = Subsequence( q, compact, words );

		// A typo in the whole name, in its start, or in one word.
		if ( q.Length >= 3 )
		{
			var allowed = q.Length >= 7 ? 2 : 1;
			var candidates = words.Append( compact ).Append( compact[..System.Math.Min( compact.Length, q.Length )] );
			var distance = candidates.Min( x => Distance( q, x.Length > q.Length + allowed ? x[..( q.Length + allowed )] : x ) );

			if ( distance <= allowed )
				best = System.Math.Max( best, 450 - distance * 60 );
		}

		return best;
	}

	/// <summary>
	/// How well <paramref name="query"/> fits a longer text, such as a description: only whole words that start with it, or
	/// the query inside the text. Always below any match on a name.
	/// </summary>
	public static int ScoreText( string query, string text )
	{
		var q = (query ?? "").Trim().ToLowerInvariant();
		var lower = (text ?? "").ToLowerInvariant();
		if ( q.Length == 0 )
			return 0;

		if ( Words( lower ).Any( x => x.StartsWith( q ) ) )
			return 120;

		return q.Length >= 3 && lower.Contains( q ) ? 100 : 0;
	}

	/// <summary>
	/// The score of a name that is the same as the query.
	/// </summary>
	public const int Exact = 1000;

	/// <summary>
	/// Lowercase, with everything but letters and digits left out.
	/// </summary>
	static string Normalize( string text ) =>
		new( (text ?? "").Where( char.IsLetterOrDigit ).Select( char.ToLowerInvariant ).ToArray() );

	/// <summary>
	/// The runs of letters and digits in the text, lowercase: "Pac-Man" is "pac" and "man".
	/// </summary>
	static List<string> Words( string text )
	{
		var words = new List<string>();
		var current = new System.Text.StringBuilder();

		foreach ( var c in text.ToLowerInvariant() )
		{
			if ( char.IsLetterOrDigit( c ) )
			{
				current.Append( c );
			}
			else if ( current.Length > 0 )
			{
				words.Add( current.ToString() );
				current.Clear();
			}
		}

		if ( current.Length > 0 )
			words.Add( current.ToString() );

		return words;
	}

	/// <summary>
	/// The query's letters found in order in the text, below the other kinds of match: 300 at most, plus a little for each
	/// letter that follows the last or starts a word, less for each letter skipped. 0 if they aren't all there.
	/// </summary>
	static int Subsequence( string q, string compact, List<string> words )
	{
		// Where each word starts in the compact text.
		var starts = new HashSet<int>();
		var offset = 0;
		foreach ( var word in words )
		{
			starts.Add( offset );
			offset += word.Length;
		}

		var score = 0;
		var from = 0;
		var last = -2;

		foreach ( var c in q )
		{
			var at = compact.IndexOf( c, from );
			if ( at < 0 )
				return 0;

			score += 10;
			if ( at == last + 1 )
				score += 15;

			if ( starts.Contains( at ) )
				score += 20;

			score -= at - from;
			last = at;
			from = at + 1;
		}

		return System.Math.Clamp( 200 + score, 1, 599 );
	}

	/// <summary>
	/// The number of single-letter changes (add, remove, replace, or swap two neighbours) between the texts.
	/// </summary>
	static int Distance( string a, string b )
	{
		var d = new int[a.Length + 1, b.Length + 1];

		for ( var i = 0; i <= a.Length; i++ )
			d[i, 0] = i;

		for ( var j = 0; j <= b.Length; j++ )
			d[0, j] = j;

		for ( var i = 1; i <= a.Length; i++ )
		{
			for ( var j = 1; j <= b.Length; j++ )
			{
				var cost = a[i - 1] == b[j - 1] ? 0 : 1;
				d[i, j] = System.Math.Min( System.Math.Min( d[i - 1, j] + 1, d[i, j - 1] + 1 ), d[i - 1, j - 1] + cost );

				if ( i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1] )
					d[i, j] = System.Math.Min( d[i, j], d[i - 2, j - 2] + 1 );
			}
		}

		return d[a.Length, b.Length];
	}
}
