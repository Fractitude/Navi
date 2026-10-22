using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// One form a drag carries (<see cref="Navi.DragOffer"/>), cleaned up by the computer.
/// </summary>
public sealed record NaviDragOffer( string Kind, IReadOnlyList<string> Items );

/// <summary>
/// What the player is dragging between windows (<c>Navi.Drag</c>), on this client. The desktop draws it at the cursor, finds
/// the drop zone under it and tells that zone's app (<see cref="NaviDesktop"/>); the source hears how it ended.
/// </summary>
public sealed class NaviDrag
{
	/// <summary>Counts the drags, so the desktop redraws when a new one starts.</summary>
	public int Serial { get; init; }

	/// <summary>The window it was dragged out of.</summary>
	public NaviWindow Source { get; init; }

	/// <summary>What it carries, in the forms the source offered.</summary>
	public IReadOnlyList<NaviDragOffer> Offers { get; init; }

	/// <summary>What a drop may do with it: the source's say.</summary>
	public Navi.DragEffects Allowed { get; init; }

	/// <summary>What the cursor carries: one line of text, cut short.</summary>
	public string Label { get; init; }

	/// <summary>A Material icon name.</summary>
	public string Icon { get; init; }

	/// <summary>The drop zone the mouse is over that takes it, or null.</summary>
	public Panel Zone { get; set; }

	/// <summary>The window the zone is in.</summary>
	public NaviAppHost ZoneHost { get; set; }

	/// <summary>What a drop would do now, or none.</summary>
	public Navi.DragEffects Effect { get; set; }

	/// <summary>The mouse is over a window that is not the source and has no zone that takes this: the cursor shows "no".</summary>
	public bool Refused { get; set; }

	/// <summary>The offer of this kind, or null.</summary>
	public NaviDragOffer Find( string kind ) => Offers.FirstOrDefault( x => x.Kind == kind );
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
	/// An app's copy asked to start a drag (<c>Navi.Drag.BeginAsync</c>). Only while the player really holds the left mouse
	/// button (<c>Input.Keyboard</c>, which game code can't fake the way it can a panel event), in a window of this computer
	/// that is theirs to use. Files in the payload must be ones the app could name, so it needs the Files permission. The
	/// paths, text and label are cleaned up here, so what the target gets is plain. False if nothing started.
	/// </summary>
	internal bool BeginDrag( NaviWindow source, object[] args )
	{
		if ( source is null || !Windows.Contains( source ) || !CanUse || !IsUser || IsLocked || SecureEntryActive )
			return false;

		if ( !Input.Keyboard.Down( "MOUSE1" ) )
			return false;

		if ( args is not [string label, string icon, int allowed, string[] kinds, string[][] items] || kinds.Length != items.Length
			|| kinds.Length is 0 or > Navi.DragCalls.MaxOffers )
			return false;

		var effects = (Navi.DragEffects)allowed & Navi.DragEffects.All;
		if ( effects == Navi.DragEffects.None )
			return false;

		var offers = new List<NaviDragOffer>();
		for ( var i = 0; i < kinds.Length; i++ )
		{
			if ( offers.Any( x => x.Kind == kinds[i] ) || CleanOffer( source, kinds[i], items[i] ) is not { } offer )
				return false;

			offers.Add( offer );
		}

		label = new string( (label ?? "").Where( x => !char.IsControl( x ) ).ToArray() ).Trim();
		if ( label.Length == 0 )
			label = offers[0].Kind == Navi.DragCalls.FilesKind ? NaviFileSystem.NameOf( offers[0].Items[0] ) : "";

		CurrentDrag = new NaviDrag
		{
			Serial = ++dragSerial,
			Source = source,
			Offers = offers,
			Allowed = effects,
			Label = NaviAppInfo.Cap( label, Navi.DragCalls.MaxLabel ),
			Icon = icon is { Length: > 0 and <= 32 } && icon.All( x => char.IsAsciiLetterOrDigit( x ) || x == '_' ) ? icon : "draft",
		};

		return true;
	}

	/// <summary>
	/// One offer of a drag, cleaned up, or null if it isn't one an app may make.
	/// </summary>
	static NaviDragOffer CleanOffer( NaviWindow source, string kind, string[] items )
	{
		if ( items is null )
			return null;

		switch ( kind )
		{
			case Navi.DragCalls.FilesKind:
			{
				if ( !source.Process.HasPermission( NaviPermissions.Files ) )
					return null;

				var clean = new List<string>();
				foreach ( var path in items.Take( Navi.DragCalls.MaxPaths ) )
				{
					if ( path is not { Length: > 0 and <= Navi.DragCalls.MaxPath } || path[0] != '/' || path.Any( char.IsControl ) )
						continue;

					var normalized = NaviFileSystem.Normalize( path );
					if ( !clean.Contains( normalized ) )
						clean.Add( normalized );
				}

				return clean.Count == 0 ? null : new NaviDragOffer( kind, clean );
			}

			case Navi.DragCalls.TextKind:
			{
				if ( items.Length != 1 || items[0] is not { Length: > 0 } text )
					return null;

				text = text.Length > Navi.DragCalls.MaxText ? text[..Navi.DragCalls.MaxText] : text;
				return new NaviDragOffer( kind, [new string( text.Where( x => !char.IsControl( x ) || x == '\n' ).ToArray() )] );
			}

			default:
				return null;
		}
	}

	/// <summary>
	/// The drag is over: the player let go, or it can't go on.
	/// </summary>
	internal void EndDrag() => CurrentDrag = null;
}
