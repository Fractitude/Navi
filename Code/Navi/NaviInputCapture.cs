namespace Sandbox;

/// <summary>
/// Gives the player's input to an app that captures input (<see cref="NaviAppResource.CapturesInput"/>) while its window is in front.
/// The camera flies to the screen so it fills the view, and the player's movement and look stop, so apps can read keys like WASD
/// with <see cref="Input"/>. Once there, the mouse cursor is free to point and click anywhere on the screen. Pressing Escape several
/// times quickly force-quits the app. When the window closes, minimizes or loses focus, everything goes back.
/// </summary>
public sealed class NaviInputCapture : Component
{
	/// <summary>
	/// The computer on this GameObject. Looked up rather than [RequireComponent], because this component is itself
	/// usually added by <see cref="NaviDesktop"/>'s [RequireComponent], and then its own required properties aren't filled in.
	/// </summary>
	public NaviComputer Computer => computer.IsValid() ? computer : computer = Components.GetOrCreate<NaviComputer>();
	NaviComputer computer;

	/// <summary>
	/// Seconds the camera takes to fly to the screen and back.
	/// </summary>
	[Property] public float FlyTime { get; set; } = 0.45f;

	/// <summary>
	/// How much of the view the screen fills. 1 fits it exactly; less leaves a border.
	/// </summary>
	[Property, Range( 0.5f, 1f )] public float Fill { get; set; } = 1f;

	/// <summary>
	/// Show the mouse cursor once the screen fills the view, so the player can point and click on it. <see cref="WorldInput"/>
	/// aims with the cursor while it's visible. Off, clicks stop entirely while an app has the controls.
	/// </summary>
	[Property] public bool FreeCursor { get; set; } = true;

	/// <summary>
	/// Escape presses in a row that force-quit the app. Not a hold: s&box tells games when Escape goes down,
	/// but not whether it's still held.
	/// </summary>
	[Property, Range( 2, 10 )] public int QuitPresses { get; set; } = 5;

	/// <summary>
	/// Seconds allowed between presses. A longer gap starts the count over.
	/// </summary>
	[Property, Range( 0.3f, 3f )] public float QuitWindow { get; set; } = 1f;

	/// <summary>
	/// Presses still needed to force-quit the app.
	/// </summary>
	public int QuitPressesLeft => QuitPresses - escapePresses;

	/// <summary>
	/// How far through <see cref="QuitPresses"/> the player is, 0 to 1.
	/// </summary>
	public float QuitProgress => QuitPresses > 0 ? (float)escapePresses / QuitPresses : 0f;

	/// <summary>
	/// Show the "press Escape to quit" hint, from the first press until the count runs out.
	/// </summary>
	public bool ShowQuitHint => Computer.InputCapture is not null && escapePresses > 0;

	/// <summary>
	/// The capture that has the player's input right now, or null. Game code can check it, e.g. to hide a crosshair.
	/// </summary>
	public static NaviInputCapture Current { get; private set; }

	/// <summary>
	/// True while this computer has the player's input, including while the camera flies to the screen and back.
	/// </summary>
	public bool IsCapturing => state != State.Idle;

	enum State { Idle, FlyingIn, Captured, FlyingOut }

	State state;
	float flight; // 0 at the player's view, 1 at the screen
	RealTimeSince sinceEscapePressed = 100;
	int escapePresses;
	Transform playerView;
	MouseVisibility playerMouse;

	record ControllerState( PlayerController Controller, bool Input, bool Look, bool Camera );
	readonly List<ControllerState> controllers = new();
	readonly List<WorldInput> worldInputs = new();

	protected override void OnUpdate()
	{
		var wanted = Computer.InputCapture is not null;

		if ( wanted && state is State.Idle or State.FlyingOut )
			Begin();
		else if ( !wanted && state is State.FlyingIn or State.Captured )
			state = State.FlyingOut;

		if ( state == State.Idle )
			return;

		UpdateQuit();

		foreach ( var c in controllers )
		{
			if ( c.Controller.IsValid() )
				c.Controller.WishVelocity = 0;
		}

		var step = FlyTime > 0 ? Time.Delta / FlyTime : 1f;

		if ( state == State.FlyingIn )
		{
			flight = System.MathF.Min( 1f, flight + step );
			if ( flight >= 1f ) Arrive();
		}
		else if ( state == State.FlyingOut )
		{
			flight = System.MathF.Max( 0f, flight - step );
			if ( flight <= 0f ) End();
		}

		UpdateCursor();
	}

	/// <summary>
	/// The camera reached the screen: centre the cursor on it.
	/// </summary>
	void Arrive()
	{
		state = State.Captured;

		if ( FreeCursor )
			Mouse.Position = Screen.Size * 0.5f;
	}

	/// <summary>
	/// The cursor and world clicks only work once the screen fills the view, so nothing is clicked by accident mid-flight.
	/// Set every frame, because game code (like a crosshair) may hide the cursor every frame too.
	/// </summary>
	void UpdateCursor()
	{
		var free = FreeCursor && state == State.Captured;

		Mouse.Visibility = free ? MouseVisibility.Visible : playerMouse;

		foreach ( var wi in worldInputs )
		{
			if ( wi.IsValid() ) wi.Enabled = free;
		}
	}

	/// <summary>
	/// An Escape press is swallowed, so the game menu doesn't open. <see cref="QuitPresses"/> presses in a row, each within
	/// <see cref="QuitWindow"/> of the last, force-quit the app. Escape never reaches <see cref="Input.Keyboard"/>: the
	/// engine keeps it for its menu.
	/// </summary>
	void UpdateQuit()
	{
		if ( escapePresses > 0 && sinceEscapePressed > QuitWindow )
			escapePresses = 0;

		if ( !Input.EscapePressed )
			return;

		Input.EscapePressed = false;
		sinceEscapePressed = 0;
		escapePresses++;

		if ( escapePresses >= QuitPresses && Computer.InputCapture is { } capture )
		{
			escapePresses = 0;
			Computer.Close( capture.App.Id );
		}
	}

	protected override void OnPreRender()
	{
		if ( state == State.Idle || Scene.Camera is not { } camera )
			return;

		var screen = ScreenView( camera );
		var t = Sandbox.Utility.Easing.EaseInOut( flight );

		camera.WorldPosition = Vector3.Lerp( playerView.Position, screen.Position, t );
		camera.WorldRotation = Rotation.Slerp( playerView.Rotation, screen.Rotation, t );
	}

	protected override void OnDisabled()
	{
		if ( state != State.Idle )
			End();
	}

	void Begin()
	{
		if ( state == State.Idle )
		{
			if ( Scene.Camera is { } camera )
				playerView = camera.WorldTransform;

			playerMouse = Mouse.Visibility;

			foreach ( var pc in Scene.GetAllComponents<PlayerController>().Where( x => !x.IsProxy ) )
			{
				controllers.Add( new ControllerState( pc, pc.UseInputControls, pc.UseLookControls, pc.UseCameraControls ) );
				pc.UseInputControls = false;
				pc.UseLookControls = false;
				pc.UseCameraControls = false;
			}

			// Off while the camera flies, or clicks go to whatever the view's centre passes over. UpdateCursor turns them back on.
			foreach ( var wi in Scene.GetAllComponents<WorldInput>().Where( x => x.Enabled ) )
			{
				worldInputs.Add( wi );
				wi.Enabled = false;
			}
		}

		state = State.FlyingIn;
		Current = this;
	}

	void End()
	{
		foreach ( var c in controllers )
		{
			if ( !c.Controller.IsValid() ) continue;
			c.Controller.UseInputControls = c.Input;
			c.Controller.UseLookControls = c.Look;
			c.Controller.UseCameraControls = c.Camera;
		}

		foreach ( var wi in worldInputs )
		{
			if ( wi.IsValid() ) wi.Enabled = true;
		}

		Mouse.Visibility = playerMouse;
		controllers.Clear();
		worldInputs.Clear();
		flight = 0;
		escapePresses = 0;
		state = State.Idle;

		if ( Current == this )
			Current = null;
	}

	/// <summary>
	/// Where the camera goes so the computer's screen fills the view: centred in front of it, far enough back to fit it.
	/// </summary>
	internal Transform ScreenView( CameraComponent camera )
	{
		if ( !Components.TryGet<WorldPanel>( out var panel ) )
			return playerView;

		// Same maths as the engine's world panel: panel pixels × 0.05 × object scale, with x along Right and y along Down.
		var scale = Sandbox.UI.WorldPanel.ScreenToWorldScale * WorldScale.x;
		var size = panel.PanelSize * scale;

		var offset = Vector2.Zero;
		if ( panel.HorizontalAlign == WorldPanel.HAlignment.Left ) offset.x = size.x * 0.5f;
		if ( panel.HorizontalAlign == WorldPanel.HAlignment.Right ) offset.x = -size.x * 0.5f;
		if ( panel.VerticalAlign == WorldPanel.VAlignment.Top ) offset.y = size.y * 0.5f;
		if ( panel.VerticalAlign == WorldPanel.VAlignment.Bottom ) offset.y = -size.y * 0.5f;

		var rotation = WorldRotation;
		var center = WorldPosition + rotation.Right * offset.x + rotation.Down * offset.y;

		var tanFov = System.MathF.Tan( MathX.DegreeToRadian( camera.FieldOfView ) * 0.5f );
		var aspect = Screen.Aspect;
		var tanH = camera.FovAxis == CameraComponent.Axis.Vertical ? tanFov * aspect : tanFov;
		var tanV = camera.FovAxis == CameraComponent.Axis.Vertical ? tanFov : tanFov / aspect;
		var distance = System.MathF.Max( size.x * 0.5f / tanH, size.y * 0.5f / tanV ) / Fill;

		// The screen's front faces along the object's forward.
		var position = center + rotation.Forward * distance;
		return new Transform( position, Rotation.LookAt( -rotation.Forward, rotation.Up ) );
	}
}
