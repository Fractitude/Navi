using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// A file an app asked the player to choose (<c>Navi.Files.PickAsync</c>), waiting for the answer.
/// </summary>
public sealed class NaviFilePickRequest
{
	public NaviProcess Caller { get; init; }

	/// <summary>The extensions that can be chosen, lowercase without the dot. Empty for any file.</summary>
	public IReadOnlyList<string> Types { get; init; }

	/// <summary>The folder the dialog opens in. The root if it isn't a folder.</summary>
	public string StartFolder { get; init; }

	internal TaskCompletionSource<string> Answer { get; } = new();
}

/// <summary>
/// The system file picker. An app asks for a file and the computer opens the File Picker, a window of its own that is part
/// of the OS, not of the app. The player chooses there, and the app gets that one file: no permission, no look at the rest
/// of the files. Host only, like every file call.
/// </summary>
public sealed partial class NaviComputer
{
	const int MaxPickTypes = 16, MaxPickType = 16, MaxPickFolder = 512;

	NaviFilePickRequest pickRequest;

	/// <summary>
	/// What the File Picker window is answering, or null. Only the picker's own calls read it (<c>picker.request</c>).
	/// </summary>
	internal NaviFilePickRequest PickRequest => pickRequest;

	/// <summary>
	/// Ask the player for a file for <paramref name="caller"/>. Returns its path, which the app may then read (and nothing
	/// else it hasn't been given), or null if the player cancelled or closed the window. One picker is open at a time.
	/// </summary>
	public async Task<string> PickFileAsync( NaviProcess caller, IEnumerable<string> types, string startFolder )
	{
		if ( !IsAuthority || Files is null )
			throw new NaviFileException( NaviFileError.Unavailable, "" );

		if ( caller.App.Id == NaviAppRegistry.FilePickerApp || NaviAppRegistry.Find( NaviAppRegistry.FilePickerApp ) is not { } picker )
			throw new System.NotSupportedException( "There is no file picker." );

		if ( pickRequest is not null )
			throw new System.InvalidOperationException( "A file picker is already open." );

		var request = new NaviFilePickRequest
		{
			Caller = caller,
			Types = (types ?? []).Where( x => !string.IsNullOrEmpty( x ) && x.Length <= MaxPickType && x.All( char.IsLetterOrDigit ) )
				.Select( x => x.ToLowerInvariant() ).Distinct().Take( MaxPickTypes ).ToList(),
			StartFolder = startFolder is { Length: > 0 and <= MaxPickFolder } ? startFolder : "/",
		};

		pickRequest = request;

		if ( GetProcess( picker.Id ) is null )
			Open( picker );
		else
			FocusApp( picker.Id );

		return await request.Answer.Task;
	}

	/// <summary>
	/// The File Picker's answer: the player chose <paramref name="path"/>. The caller may then use that file. False if
	/// nothing is waiting or the file isn't one that can be chosen.
	/// </summary>
	internal bool AnswerPick( string path )
	{
		if ( !IsAuthority || pickRequest is not { } request || Files is null )
			return false;

		if ( Files.TryResolve( path, true ) is not { } at || at.Node.Kind != NaviFileKind.File )
			return false;

		var name = NaviFileSystem.NameOf( at.Path );
		var dot = name.LastIndexOf( '.' );
		if ( request.Types.Count > 0 && (dot < 0 || !request.Types.Contains( name[(dot + 1)..].ToLowerInvariant() )) )
			return false;

		request.Caller.PickedFiles.Add( at.Path );
		EndPick( at.Path );
		return true;
	}

	/// <summary>
	/// The File Picker's answer: the player chose nothing.
	/// </summary>
	internal void CancelPick() => EndPick( null );

	/// <summary>
	/// Answer the waiting app and close the picker, unless it is what is closing.
	/// </summary>
	void EndPick( string path, bool closePicker = true )
	{
		if ( pickRequest is not { } request )
			return;

		pickRequest = null;
		request.Answer.TrySetResult( path );

		if ( closePicker )
			Close( NaviAppRegistry.FilePickerApp );
	}
}
