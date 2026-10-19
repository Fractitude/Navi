namespace Sandbox;

/// <summary>
/// The <c>messages.*</c> calls (<c>Navi.Messages</c>). They run on the host, as the player in the computer's seat, and need
/// <see cref="NaviPermissions.Messages"/>. Who "me" is comes from the seat, never from the app, and every question is asked
/// as that player (<see cref="NaviMessages"/>), so an app can't read or send as anyone else. Nobody in the seat means no
/// one to be, and the calls answer with nothing.
/// Keep method names, arguments and results stable: published apps depend on them.
/// </summary>
static class NaviMessageCalls
{
	public static object Run( NaviComputer computer, NaviProcess process, string method, object[] args )
	{
		if ( !process.HasPermission( NaviPermissions.Messages ) )
			throw NaviPermissions.Missing( NaviPermissions.Messages );

		// The messages are the host's. A client's own copy of an app that isn't shared has none.
		if ( !computer.IsAuthority )
			throw new System.InvalidOperationException( "Messages are on the host. Make the app Shared to use them in multiplayer." );

		var me = NaviMessages.Identify( computer );

		switch ( method )
		{
			// Goes up when anything about the player's messages may have changed, including who is in the seat.
			case "messages.version":
				return System.HashCode.Combine( NaviMessages.Version, me?.Id );

			case "messages.me":
				return (me?.Id ?? "", me?.Name ?? "");

			case "messages.contacts":
				return me is null
					? []
					: NaviMessages.GetContacts( me.Value.Id )
						.Select( x => (x.Id, x.Name, x.Online, x.LastText, x.LastAt, x.Unread, x.LastMine) )
						.ToArray();

			case "messages.history":
				return me is null
					? []
					: NaviMessages.GetHistory( me.Value.Id, Arg<string>( args, 0, method ), int.Clamp( Arg<int>( args, 1, method ), 1, 100 ) )
						.Select( x => (x.Id, x.From == me.Value.Id, x.Text, x.At) )
						.ToArray();

			case "messages.send":
				return me is not null && NaviMessages.Send( me.Value.Id, Arg<string>( args, 0, method ), Arg<string>( args, 1, method ) );

			case "messages.read":
				if ( me is not null )
					NaviMessages.MarkRead( me.Value.Id, Arg<string>( args, 0, method ) );

				return null;
		}

		throw new System.NotSupportedException( $"Navi has no call '{method}'." );
	}

	static T Arg<T>( object[] args, int index, string method )
	{
		if ( index < args.Length && args[index] is T value )
			return value;

		throw new System.ArgumentException( $"'{method}' needs a {typeof( T ).Name} as argument {index + 1}." );
	}
}
