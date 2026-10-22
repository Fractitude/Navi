namespace Sandbox;

/// <summary>
/// The time the taskbar's clock shows. By default it is the player's own local time. A game with a clock of its own (a day
/// and night cycle, say) points <see cref="TimeOfDay"/> and <see cref="Date"/> at it. The clock is read on each client every
/// frame, so the source must run on every client by itself: nothing about it is sent over the network.
/// </summary>
/// <remarks>
/// Not <c>readonly</c>: a hot reload would reset them (see "Static state and hot reload" in AGENTS.md). A game sets them
/// again every frame, so a reload of its own code doesn't leave the clock on a stale source.
/// </remarks>
public static class NaviClock
{
	/// <summary>
	/// The time of day now, or null for the player's local time.
	/// </summary>
	public static System.Func<System.TimeSpan> TimeOfDay { get; set; }

	/// <summary>
	/// The day as the clock's tooltip says it, e.g. "Monday, Day 1", or null for the local date.
	/// </summary>
	public static System.Func<string> Date { get; set; }

	/// <summary>
	/// The time of day now, from <see cref="TimeOfDay"/>, between 00:00 and 23:59:59. A source that throws falls back to the
	/// local time, so a game's bug can't take the desktop down.
	/// </summary>
	public static System.TimeSpan Now
	{
		get
		{
			var time = System.DateTime.Now.TimeOfDay;

			try
			{
				if ( TimeOfDay is { } source )
					time = source();
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"NaviClock: the game's time source failed: {e.Message}" );
				TimeOfDay = null;
			}

			var ticks = time.Ticks % System.TimeSpan.TicksPerDay;
			return System.TimeSpan.FromTicks( ticks < 0 ? ticks + System.TimeSpan.TicksPerDay : ticks );
		}
	}

	/// <summary>
	/// The time as the taskbar shows it: 24 hours, e.g. "09:05".
	/// </summary>
	public static string Text
	{
		get
		{
			var now = Now;
			return $"{now.Hours:00}:{now.Minutes:00}";
		}
	}

	/// <summary>
	/// The day, from <see cref="Date"/>.
	/// </summary>
	public static string DateText
	{
		get
		{
			try
			{
				if ( Date?.Invoke() is { } date )
					return date;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"NaviClock: the game's date source failed: {e.Message}" );
				Date = null;
			}

			return System.DateTime.Now.ToString( "dddd, d MMMM" );
		}
	}
}
