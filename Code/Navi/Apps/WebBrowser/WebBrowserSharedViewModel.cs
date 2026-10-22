using System;

namespace NaviApps;

/// <summary>
/// The part of the Web Browser every player near the screen shares, run by the host's copy: whether the computer is
/// online, and Dino Run while it isn't. The seated player plays it with their keys (which reach the host's copy) and by
/// clicking the track (<see cref="ClickAction"/>), and everyone else watches. Browsing is not here: it is the seated
/// player's own (<see cref="WebBrowserViewModel"/>).
/// </summary>
public sealed class WebBrowserSharedViewModel
{
	/// <summary>The player clicked the track, which counts as a jump.</summary>
	public const string ClickAction = "dino.click";

	readonly Navi navi;
	readonly DinoRunGame dino;

	bool? online;

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
		if ( online is not false )
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
		if ( action == ClickAction && online is false )
			dino.Click();
	}
}
