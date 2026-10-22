using System;

namespace NaviApps;

/// <summary>
/// The part of the Web Browser every player near the screen shares, run by the host's copy: whether the computer is
/// online, and Dino Run while it isn't or the seated player is on <c>browser://dino</c>. The seated player plays it with their keys (which reach the host's copy) and by
/// clicking the track (<see cref="ClickAction"/>), and everyone else watches. Browsing is not here: it is the seated
/// player's own (<see cref="WebBrowserViewModel"/>).
/// </summary>
public sealed class WebBrowserSharedViewModel
{
	/// <summary>The player clicked the track, which counts as a jump.</summary>
	public const string ClickAction = "dino.click";

	/// <summary>
	/// The seated player opened (<c>"1"</c>) or left (<c>"0"</c>) <c>browser://dino</c>. All the host learns of their
	/// browsing, and only so the game can be shared.
	/// </summary>
	public const string ShowAction = "dino.show";

	readonly Navi navi;
	readonly DinoRunGame dino;

	bool? online;
	bool shown;

	/// <summary>Is the game on: offline, or the player opened it.</summary>
	bool Playing => online is false || shown;

	public WebBrowserSharedViewModel( Navi navi )
	{
		this.navi = navi;
		dino = new DinoRunGame( navi );
		UiState = new WebBrowserSharedState { Dino = dino.Frame };
		_ = CheckNetworkAsync();
	}

	/// <summary>
	/// What the host's copy publishes. Replaced whole on every change.
	/// </summary>
	public WebBrowserSharedState UiState { get; private set; }

	async System.Threading.Tasks.Task CheckNetworkAsync()
	{
		try
		{
			NetworkChanged( await navi.Network.IsOnlineAsync() );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Web Browser: couldn't ask whether the computer is online: {e.Message}" );
			NetworkChanged( false );
		}
	}

	/// <summary>
	/// The computer went online or offline.
	/// </summary>
	public void NetworkChanged( bool online )
	{
		if ( this.online == online )
			return;

		this.online = online;
		UiState = UiState with { Online = online };
	}

	/// <summary>
	/// Advance the game while the computer is offline. <paramref name="hasInput"/> is whether the window has the seated
	/// player's keys.
	/// </summary>
	public void Tick( float delta, bool hasInput )
	{
		if ( !Playing )
			return;

		dino.Tick( delta, hasInput );

		if ( !ReferenceEquals( UiState.Dino, dino.Frame ) )
			UiState = UiState with { Dino = dino.Frame };
	}

	/// <summary>
	/// What the seated player did, from whichever client they are on. Anything else is ignored.
	/// </summary>
	public void Act( string action, string[] args )
	{
		if ( action == ClickAction && Playing )
			dino.Click();
		else if ( action == ShowAction && args is [var value] && value is "0" or "1" && (value == "1") != shown )
		{
			shown = value == "1";
			UiState = UiState with { DinoShown = shown };
		}
	}
}
