using System.Threading.Tasks;

namespace NaviApps;

/// <summary>
/// The thumbnails of the pictures in the open folder, one at a time, for the rows that ask for one
/// (<see cref="FileRow.Thumbnail"/>). The computer makes each picture small (<c>Navi.Files.GetThumbnailAsync</c>), so the
/// seated player's copy on a client is sent the thumbnail by the host, never the whole picture. Textures, so never part
/// of the state: every copy that draws them makes its own.
/// </summary>
public sealed class FilesThumbnails
{
	/// <summary>How long to wait before trying again when the host didn't send a file, in seconds.</summary>
	const float UnavailableDelay = 2f;

	readonly Navi navi;

	/// <summary>Thumbnails by path, with the row's key they were made from.</summary>
	readonly Dictionary<string, (string Key, Texture Texture)> made = new();

	/// <summary>Pictures that got no thumbnail, by path and key, so they aren't tried again.</summary>
	readonly Dictionary<string, string> failed = new();

	bool loading;
	IReadOnlyList<FileRow> lastRows;
	RealTimeSince sinceUnavailable = UnavailableDelay;

	public FilesThumbnails( Navi navi ) => this.navi = navi;

	/// <summary>Goes up whenever a thumbnail is made, so the panel draws it.</summary>
	public int Version { get; private set; }

	/// <summary>The thumbnail of a picture in the open folder, or null while it is loading or if it has none.</summary>
	public Texture Of( string path ) => made.TryGetValue( path, out var thumb ) ? thumb.Texture : null;

	/// <summary>
	/// Let go of the thumbnails of rows no longer shown, and start making the next one missing.
	/// </summary>
	public void Tick( IReadOnlyList<FileRow> rows )
	{
		// The rows are replaced whole when anything in them changes.
		if ( !ReferenceEquals( rows, lastRows ) )
		{
			lastRows = rows;
			var shown = rows.Select( x => x.Path ).ToHashSet();
			foreach ( var gone in made.Keys.Where( x => !shown.Contains( x ) ).ToList() )
				made.Remove( gone );
		}

		if ( loading || sinceUnavailable < UnavailableDelay )
			return;

		var next = rows.FirstOrDefault( x => !string.IsNullOrEmpty( x.Thumbnail )
			&& (!made.TryGetValue( x.Path, out var thumb ) || thumb.Key != x.Thumbnail)
			&& (!failed.TryGetValue( x.Path, out var key ) || key != x.Thumbnail) );

		if ( next is not null )
			_ = LoadAsync( next.Path, next.Thumbnail );
	}

	async Task LoadAsync( string path, string key )
	{
		loading = true;
		try
		{
			var bytes = await navi.Files.GetThumbnailAsync( path );
			using var bitmap = bytes is { Length: > 0 } ? Bitmap.CreateFromBytes( bytes ) : null;
			var texture = bitmap is { Width: > 0, Height: > 0 } ? bitmap.ToTexture() : null;

			if ( texture is null )
			{
				failed[path] = key;
			}
			else
			{
				made[path] = (key, texture);
				Version++;
			}
		}
		catch ( Navi.FileException e ) when ( e.Error == Navi.FileError.Unavailable )
		{
			// The host didn't send it (the seat changed hands, or too much was asked at once): try again in a moment.
			sinceUnavailable = 0;
		}
		catch ( System.Exception )
		{
			failed[path] = key;
		}
		finally
		{
			loading = false;
		}
	}
}
