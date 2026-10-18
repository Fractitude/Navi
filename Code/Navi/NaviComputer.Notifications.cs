namespace Sandbox;

/// <summary>
/// A notification an app sent (<c>Navi.Notifications.SendAsync</c>). The desktop draws it with the app's name and icon,
/// which the app can't change, so a notification can't pass for another app's or the system's. It stays in the
/// notification list until the player dismisses it, or the app dismisses its own.
/// </summary>
public sealed class NaviNotification
{
	public const int MaxTitle = Navi.MaxNotificationTitle, MaxText = Navi.MaxNotificationText;

	/// <summary>
	/// Goes up with each notification the computer takes, so a higher one is newer. Never 0.
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// The app that sent it.
	/// </summary>
	public string AppId { get; set; }

	public string Title { get; set; } = "";
	public string Text { get; set; } = "";

	/// <summary>
	/// The app asked for the notification sound. It plays unless the player muted the app.
	/// </summary>
	public bool Sound { get; set; }

	/// <summary>
	/// When the computer took it, in Unix seconds (UTC).
	/// </summary>
	public long PostedAt { get; set; }
}

/// <summary>
/// Notifications. Apps send them (<see cref="Notify"/>, with <see cref="NaviPermissions.Notify"/>); the host keeps them,
/// saves them and sends them to clients with the rest of the state. Each client shows new ones as banners and plays their
/// sound itself, from the computer, so everyone near the screen hears it. The player mutes an app in Settings or from a
/// notification's menu: its notifications still go to the list, but quietly, without a sound, a banner or an unread count.
/// </summary>
public sealed partial class NaviComputer
{
	/// <summary>
	/// The most notifications the list keeps, and the most it keeps from one app. Older ones make room.
	/// </summary>
	public const int MaxNotifications = 50, MaxNotificationsPerApp = 10;

	/// <summary>
	/// Apps whose notifications the player muted.
	/// </summary>
	[Property] public List<string> MutedNotificationApps { get; set; } = new();

	/// <summary>
	/// The sound a notification plays when its app asks for one. Empty plays the engine's message pop.
	/// </summary>
	[Property] public SoundEvent NotificationSound { get; set; }

	const string DefaultNotificationSound = "sounds/kenney/ui/ui.popup.message.open.sound";

	/// <summary>
	/// The shortest time between two notification sounds, in seconds, so an app that sends many can't make a racket.
	/// </summary>
	const float NotificationSoundInterval = 1f;

	readonly List<NaviNotification> notifications = new();

	/// <summary>
	/// The notifications in the list, oldest first.
	/// </summary>
	public IReadOnlyList<NaviNotification> Notifications => notifications;

	/// <summary>
	/// The id of the newest notification the computer took, even if it is gone from the list since.
	/// </summary>
	public int LastNotificationId { get; private set; }

	/// <summary>
	/// The player has seen the notifications up to this id, by opening the list.
	/// </summary>
	public int NotificationsReadUpTo { get; private set; }

	/// <summary>
	/// The notifications the player hasn't seen, from apps that aren't muted. The taskbar's bell shows how many.
	/// </summary>
	public int UnreadNotifications => notifications.Count( x => x.Id > NotificationsReadUpTo && !IsNotificationsMuted( x.AppId ) );

	public bool IsNotificationsMuted( string appId ) => MutedNotificationApps?.Contains( appId ) ?? false;

	/// <summary>
	/// Add a notification from an installed app to the list. Title and text are cut to size, and one of them must have
	/// something in it. Game code may call it for any installed app; apps get here through <see cref="NaviSystemCalls"/>,
	/// as themselves. Returns the notification's id, or 0 if it wasn't taken.
	/// </summary>
	public int Notify( string appId, string title, string text, bool sound = false )
	{
		if ( !IsAuthority || !IsInstalled( appId ) )
			return 0;

		title = NaviAppInfo.Cap( OneLine( title ), NaviNotification.MaxTitle );
		text = NaviAppInfo.Cap( text?.Trim(), NaviNotification.MaxText );
		if ( string.IsNullOrEmpty( title ) && string.IsNullOrEmpty( text ) )
			return 0;

		var notification = new NaviNotification
		{
			Id = ++LastNotificationId,
			AppId = appId,
			Title = title ?? "",
			Text = text ?? "",
			Sound = sound,
			PostedAt = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
		};

		notifications.Add( notification );
		TrimNotifications( appId );
		Version++;
		return notification.Id;
	}

	static string OneLine( string value ) => value?.ReplaceLineEndings( " " ).Trim();

	/// <summary>
	/// Drop the oldest notifications past <see cref="MaxNotificationsPerApp"/> for the app, and past
	/// <see cref="MaxNotifications"/> in all.
	/// </summary>
	void TrimNotifications( string appId )
	{
		while ( notifications.Count( x => x.AppId == appId ) > MaxNotificationsPerApp )
			notifications.Remove( notifications.First( x => x.AppId == appId ) );

		while ( notifications.Count > MaxNotifications )
			notifications.RemoveAt( 0 );
	}

	public NaviNotification GetNotification( int id ) => notifications.FirstOrDefault( x => x.Id == id );

	/// <summary>
	/// Remove a notification from the list. With <paramref name="appId"/>, only if that app sent it: an app dismisses its
	/// own and no others. Returns false if there was none to remove.
	/// </summary>
	public bool DismissNotification( int id, string appId = null )
	{
		if ( !IsAuthority || GetNotification( id ) is not { } notification || (appId is not null && notification.AppId != appId) )
			return false;

		notifications.Remove( notification );
		Version++;
		return true;
	}

	/// <summary>
	/// Empty the notification list.
	/// </summary>
	public void ClearNotifications()
	{
		if ( !IsAuthority || notifications.Count == 0 )
			return;

		notifications.Clear();
		Version++;
	}

	/// <summary>
	/// Mute an app's notifications, or unmute them. Returns false if the app isn't installed.
	/// </summary>
	public bool SetNotificationsMuted( string appId, bool muted )
	{
		if ( !IsAuthority || !IsInstalled( appId ) )
			return false;

		MutedNotificationApps ??= new();
		if ( muted == IsNotificationsMuted( appId ) )
			return true;

		if ( muted )
			MutedNotificationApps.Add( appId );
		else
			MutedNotificationApps.Remove( appId );

		Version++;
		return true;
	}

	/// <summary>
	/// The player has seen every notification in the list.
	/// </summary>
	public void MarkNotificationsRead()
	{
		if ( !IsAuthority || NotificationsReadUpTo == LastNotificationId )
			return;

		NotificationsReadUpTo = LastNotificationId;
		Version++;
	}

	/// <summary>
	/// Installed apps that may show up in the notification settings: the ones that declare
	/// <see cref="NaviPermissions.Notify"/>, have a notification in the list, or are muted.
	/// </summary>
	public IEnumerable<NaviAppInfo> NotifyingApps => Installed.Where( x =>
		x.Permissions.Contains( NaviPermissions.Notify ) || IsNotificationsMuted( x.Id ) || notifications.Any( y => y.AppId == x.Id ) );

	/// <summary>
	/// Forget an uninstalled app's notifications, and that it was muted.
	/// </summary>
	void ForgetNotifications( string appId )
	{
		notifications.RemoveAll( x => x.AppId == appId );
		MutedNotificationApps?.Remove( appId );
	}

	List<NaviNotification> CaptureNotifications() => notifications.Select( x => new NaviNotification
	{
		Id = x.Id,
		AppId = x.AppId,
		Title = x.Title,
		Text = x.Text,
		Sound = x.Sound,
		PostedAt = x.PostedAt,
	} ).ToList();

	/// <summary>
	/// Take on a save's notifications, cut to size as if they were sent now.
	/// </summary>
	void ApplyNotifications( NaviComputerSave save )
	{
		notifications.Clear();
		foreach ( var x in save.Notifications ?? [] )
		{
			if ( x is null || x.Id <= 0 || string.IsNullOrEmpty( x.AppId ) || notifications.Any( y => y.Id == x.Id ) )
				continue;

			notifications.Add( new NaviNotification
			{
				Id = x.Id,
				AppId = x.AppId,
				Title = NaviAppInfo.Cap( OneLine( x.Title ), NaviNotification.MaxTitle ) ?? "",
				Text = NaviAppInfo.Cap( x.Text?.Trim(), NaviNotification.MaxText ) ?? "",
				Sound = x.Sound,
				PostedAt = x.PostedAt,
			} );
		}

		notifications.Sort( ( a, b ) => a.Id.CompareTo( b.Id ) );
		while ( notifications.Count > MaxNotifications )
			notifications.RemoveAt( 0 );

		MutedNotificationApps = save.MutedNotificationApps?.Where( x => !string.IsNullOrEmpty( x ) ).Distinct().ToList() ?? new();
		LastNotificationId = System.Math.Max( save.LastNotificationId, notifications.Count > 0 ? notifications[^1].Id : 0 );
		NotificationsReadUpTo = int.Clamp( save.NotificationsReadUpTo, 0, LastNotificationId );
	}

	// On every client: when each notification arrived here, for its banner and its sound.

	/// <summary>
	/// This client has the computer's notifications: the host once it has started (and loaded its save), a client once
	/// the host's state came.
	/// </summary>
	bool hasNotifications;

	/// <summary>
	/// The newest notification this client has seen arrive, or -1 before it has noted what was already there.
	/// </summary>
	int arrivedUpTo = -1;

	readonly Dictionary<int, RealTimeSince> notificationArrivals = new();
	RealTimeSince sinceNotificationSound = NotificationSoundInterval;

	/// <summary>
	/// The notification arrived on this client within the last <paramref name="seconds"/>. The ones that were there when
	/// the computer started, or when this client joined, never count as arrived.
	/// </summary>
	public bool ArrivedWithin( int id, float seconds ) =>
		notificationArrivals.TryGetValue( id, out var since ) && since < seconds;

	/// <summary>
	/// Note the notifications that arrived since the last frame, and play the sound if one of them asks for it.
	/// </summary>
	void UpdateNotificationArrivals()
	{
		if ( !hasNotifications )
			return;

		// What was there already, or what a loaded save put there, didn't arrive.
		if ( arrivedUpTo < 0 || LastNotificationId < arrivedUpTo )
		{
			arrivedUpTo = LastNotificationId;
			return;
		}

		if ( notificationArrivals.Count > 0 )
		{
			foreach ( var id in notificationArrivals.Keys.Where( x => GetNotification( x ) is null ).ToList() )
				notificationArrivals.Remove( id );
		}

		if ( LastNotificationId == arrivedUpTo )
			return;

		var arrived = notifications.Where( x => x.Id > arrivedUpTo ).ToList();
		arrivedUpTo = LastNotificationId;

		foreach ( var notification in arrived )
			notificationArrivals[notification.Id] = 0;

		if ( arrived.Any( x => x.Sound && !IsNotificationsMuted( x.AppId ) ) && sinceNotificationSound > NotificationSoundInterval )
		{
			sinceNotificationSound = 0;
			PlayNotificationSound();
		}
	}

	void PlayNotificationSound()
	{
		if ( NotificationSound is not null )
			Sound.Play( NotificationSound, WorldPosition );
		else
			Sound.Play( DefaultNotificationSound, WorldPosition );
	}

	// What the player at the screen does, from the desktop. Each runs on the host, for the user only.

	[Rpc.Host]
	public void UserDismissNotification( int id )
	{
		if ( Authorize() )
			DismissNotification( id );
	}

	[Rpc.Host]
	public void UserClearNotifications()
	{
		if ( Authorize() )
			ClearNotifications();
	}

	[Rpc.Host]
	public void UserSetNotificationsMuted( string appId, bool muted )
	{
		if ( Authorize() )
			SetNotificationsMuted( appId, muted );
	}

	[Rpc.Host]
	public void UserMarkNotificationsRead()
	{
		if ( Authorize() )
			MarkNotificationsRead();
	}

	/// <summary>
	/// The user clicked a notification: it opens its app, or brings it to the front, and leaves the list.
	/// </summary>
	[Rpc.Host]
	public void UserOpenNotification( int id )
	{
		if ( !Authorize() || GetNotification( id ) is not { } notification )
			return;

		DismissNotification( id );
		Launch( notification.AppId );
	}
}
