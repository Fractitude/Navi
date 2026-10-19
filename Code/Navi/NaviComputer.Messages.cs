namespace Sandbox;

/// <summary>
/// Direct messages (<see cref="NaviMessages"/>). The messages are the game's, not the computer's; what the computer does is
/// tell the player at its screen when one comes, as a notification from the Messages app.
/// </summary>
public sealed partial class NaviComputer
{
	/// <summary>
	/// The app a message notification comes from.
	/// </summary>
	public const string MessagesApp = NaviAppRegistry.BuiltInFolder + "messages.napp";

	/// <summary>
	/// The newest message this computer has already thought about, or -1 before it has looked.
	/// </summary>
	long messagesSeenUpTo = -1;

	/// <summary>
	/// On the host, once a frame: keep the message store going, and notify the player in the seat of the messages that came
	/// for them. A computer that was just started doesn't notify for what was there before.
	/// </summary>
	void UpdateMessages()
	{
		if ( !IsAuthority )
			return;

		NaviMessages.Update();

		if ( messagesSeenUpTo < 0 || NaviMessages.LastId < messagesSeenUpTo )
		{
			messagesSeenUpTo = NaviMessages.LastId;
			return;
		}

		if ( NaviMessages.LastId == messagesSeenUpTo )
			return;

		var arrived = NaviMessages.Since( messagesSeenUpTo );
		messagesSeenUpTo = NaviMessages.LastId;

		if ( NaviMessages.Identify( this ) is not { } me )
			return;

		// The window shows the message itself, so it doesn't need to be told.
		if ( Windows.Any( x => x.App.Id == MessagesApp && !x.IsMinimized ) )
			return;

		foreach ( var message in arrived.Where( x => x.To == me.Id ).TakeLast( 3 ) )
			Notify( MessagesApp, NaviMessages.NameOf( message.From ), message.Text, sound: true );
	}
}
