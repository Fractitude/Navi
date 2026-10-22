namespace Sandbox;

/// <summary>
/// What the player is dragging between windows (<c>Navi.Drag</c>), on this client. The desktop draws it at the cursor and
/// delivers it to the window it is let go over (<see cref="NaviDesktop"/>).
/// </summary>
public sealed class NaviDrag
{
	/// <summary>Counts the drags, so the desktop redraws when a new one starts.</summary>
	public int Serial { get; init; }

	/// <summary>The window it was dragged out of.</summary>
	public NaviWindow Source { get; init; }

	/// <summary><see cref="Navi.DragCalls.FilesKind"/>.</summary>
	public string Kind { get; init; }

	/// <summary>What the cursor carries: one line of text, cut short.</summary>
	public string Label { get; init; }

	/// <summary>A Material icon name.</summary>
	public string Icon { get; init; }

	/// <summary>The computer's files being dragged, as clean absolute paths.</summary>
	public IReadOnlyList<string> Paths { get; init; }
}

/// <summary>
/// Drag and drop between windows. It lives on the client of the player using the computer and is never synced: a drag is the
/// player's mouse, and what is dropped goes to an app's copy on that same client.
/// </summary>
public sealed partial class NaviComputer
{
	int dragSerial;

	/// <summary>
	/// The drag in progress on this client, or null. Only <see cref="BeginDrag"/> makes one, and the desktop ends it.
	/// </summary>
	internal NaviDrag CurrentDrag { get; private set; }

	/// <summary>
	/// An app's copy asked to start a drag (<c>Navi.Drag.BeginFilesAsync</c>). Only while the player really holds the left
	/// mouse button (<c>Input.Keyboard</c>, which game code can't fake the way it can a panel event), in a window of this
	/// computer that is theirs to use, and the files must be ones the app could name: it needs the Files permission. The
	/// paths are cleaned up here, so what the target gets is plain. False if nothing started.
	/// </summary>
	internal bool BeginDrag( NaviWindow source, object[] args )
	{
		if ( source is null || !Windows.Contains( source ) || !CanUse || !IsUser || IsLocked || SecureEntryActive )
			return false;

		if ( !Input.Keyboard.Down( "MOUSE1" ) )
			return false;

		if ( args is not [string kind, string label, string icon, string[] paths] || kind != Navi.DragCalls.FilesKind
			|| !source.Process.HasPermission( NaviPermissions.Files ) )
			return false;

		var clean = new List<string>();
		foreach ( var path in paths.Take( Navi.DragCalls.MaxPaths ) )
		{
			if ( path is not { Length: > 0 and <= Navi.DragCalls.MaxPath } || path[0] != '/' || path.Any( char.IsControl ) )
				continue;

			var normalized = NaviFileSystem.Normalize( path );
			if ( !clean.Contains( normalized ) )
				clean.Add( normalized );
		}

		if ( clean.Count == 0 )
			return false;

		label = new string( (label ?? "").Where( x => !char.IsControl( x ) ).ToArray() ).Trim();
		if ( label.Length == 0 )
			label = NaviFileSystem.NameOf( clean[0] );

		CurrentDrag = new NaviDrag
		{
			Serial = ++dragSerial,
			Source = source,
			Kind = kind,
			Label = NaviAppInfo.Cap( label, Navi.DragCalls.MaxLabel ),
			Icon = icon is { Length: > 0 and <= 32 } && icon.All( x => char.IsAsciiLetterOrDigit( x ) || x == '_' ) ? icon : "draft",
			Paths = clean,
		};

		return true;
	}

	/// <summary>
	/// The drag is over: the player let go, or it can't go on.
	/// </summary>
	internal void EndDrag() => CurrentDrag = null;
}
