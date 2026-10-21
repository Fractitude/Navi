namespace Sandbox;

/// <summary>
/// A seat where a player can watch a <see cref="NaviComputer"/> without working it. The player presses Use on it (it is a
/// <see cref="Component.IPressable"/>, like the engine's chairs, so it needs a collider to look at) and
/// <see cref="NaviInputCapture"/> flies their camera to frame the screen, as for the driver. The seat's own position doesn't
/// matter to the camera. They can't click, type or launch anything. Escape stands them up. One viewer per seat, and the
/// computer's own seat (the driver's) is separate: it is still taken by clicking the screen.
/// </summary>
public sealed class NaviSpectatorSeat : Component, Component.IPressable
{
	/// <summary>
	/// The computer this seat watches.
	/// </summary>
	[Property] public NaviComputer Computer { get; set; }

	/// <summary>
	/// Shown when the player looks at the seat.
	/// </summary>
	[Property] public string Label { get; set; } = "Watch";

	/// <summary>
	/// Somebody is watching from this seat.
	/// </summary>
	public bool IsTaken => Computer.IsValid() && Computer.IsSpectatorSeatTaken( this );

	bool Component.IPressable.CanPress( Component.IPressable.Event e ) => Computer.IsValid() && Computer.CanSpectate( this );

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		if ( !Computer.IsValid() || !Computer.CanSpectate( this ) )
			return false;

		Computer.UserSpectate( Id );
		return true;
	}

	Component.IPressable.Tooltip? Component.IPressable.GetTooltip( Component.IPressable.Event e )
	{
		if ( !Computer.IsValid() || !Computer.CanSpectate( this ) )
			return null;

		return new Component.IPressable.Tooltip( Label, "visibility", "", true, this );
	}
}
