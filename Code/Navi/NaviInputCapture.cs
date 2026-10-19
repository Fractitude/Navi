namespace Sandbox;

/// <summary>
/// Takes the player to the screen while they have the computer's seat (<see cref="NaviComputer.IsSeated"/>). The camera flies
/// to the screen so it fills the view, and the player's movement and look stop, so the app in front can read keys like WASD
/// with <see cref="Input"/>. Once there, the mouse cursor is free to point and click anywhere on the screen. Escape stands the
/// player up, and everything goes back.
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
	/// aims with the cursor while it's visible. Off, clicks stop entirely while the player is seated.
	/// </summary>
	[Property] public bool FreeCursor { get; set; } = true;

	/// <summary>
	/// The input actions sent to the host while a shared app has the player's controls, so its host copy can read them
	/// with <c>Navi.Input.Down</c>. Games can add their own.
	/// </summary>
	public static HashSet<string> ForwardedActions { get; private set; } = new()
	{
		"Forward", "Backward", "Left", "Right", "Jump", "Run", "Walk", "Duck", "Attack1", "Attack2", "Reload", "Use",
	};

	/// <summary>
	/// The keys an app gets as keyboard shortcuts (<c>NaviApp.OnShortcut</c>) while it is in front: the engine's key name,
	/// and the name the app gets. Ctrl, Shift and Alt held with one go before it, e.g. "ctrl+shift+n". Escape isn't one: it
	/// stands the player up.
	/// </summary>
	public static IReadOnlyList<(string Key, string Name)> ShortcutKeys { get; } =
	[
		.."abcdefghijklmnopqrstuvwxyz0123456789".Select( x => (x.ToString(), x.ToString()) ),
		("ENTER", "enter"), ("BACKSPACE", "backspace"), ("DEL", "delete"), ("TAB", "tab"), ("SPACE", "space"),
		("UPARROW", "up"), ("DOWNARROW", "down"), ("LEFTARROW", "left"), ("RIGHTARROW", "right"),
		("HOME", "home"), ("END", "end"), ("PGUP", "pageup"), ("PGDN", "pagedown"),
		.."123456789".Select( x => ("F" + x, "f" + x) ),
		("F10", "f10"), ("F11", "f11"), ("F12", "f12"),
	];

	static readonly HashSet<string> shortcutNames = ShortcutKeys.Select( x => x.Name ).ToHashSet();

	/// <summary>
	/// Is <paramref name="keys"/> a shortcut as apps get them: "ctrl+", "shift+", "alt+" in that order, then a key name?
	/// </summary>
	public static bool IsShortcut( string keys )
	{
		if ( string.IsNullOrEmpty( keys ) || keys.Length > 32 )
			return false;

		var rest = keys;
		foreach ( var modifier in new[] { "ctrl+", "shift+", "alt+" } )
		{
			if ( rest.StartsWith( modifier, System.StringComparison.Ordinal ) )
				rest = rest[modifier.Length..];
		}

		return shortcutNames.Contains( rest );
	}

	/// <summary>
	/// The letters a text field keeps with Ctrl held: select all, copy, cut, paste, undo and redo.
	/// </summary>
	static readonly HashSet<string> textFieldLetters = ["a", "c", "x", "v", "z", "y"];

	/// <summary>
	/// Does a shortcut reach the app while a text field has the keys? Only Ctrl with a letter or digit the field doesn't
	/// use itself, like Ctrl+S: everything else is typing, or moving the caret.
	/// </summary>
	public static bool PassesTextField( string keys )
	{
		if ( !keys.StartsWith( "ctrl+", System.StringComparison.Ordinal ) )
			return false;

		var key = keys[(keys.LastIndexOf( '+' ) + 1)..];
		return key.Length == 1 && char.IsAsciiLetterOrDigit( key[0] ) && !textFieldLetters.Contains( key );
	}

	/// <summary>
	/// Send the shortcuts the player pressed this frame to the app in front, once the screen fills the view. While a
	/// text field has the keys they are typing, so only <see cref="PassesTextField"/> ones go.
	/// </summary>
	void ForwardShortcuts()
	{
		if ( state != State.Captured || Computer.InputCapture is not { } window )
			return;

		var typing = Sandbox.UI.InputFocus.Current is Sandbox.UI.TextEntry;
		var modifiers = (Input.Keyboard.Down( "CTRL" ) || Input.Keyboard.Down( "RCTRL" ) ? "ctrl+" : "")
			+ (Input.Keyboard.Down( "SHIFT" ) || Input.Keyboard.Down( "RSHIFT" ) ? "shift+" : "")
			+ (Input.Keyboard.Down( "ALT" ) || Input.Keyboard.Down( "RALT" ) ? "alt+" : "");

		foreach ( var (key, name) in ShortcutKeys )
		{
			if ( Input.Keyboard.Pressed( key ) && (!typing || PassesTextField( modifiers + name )) )
				Computer.SendShortcut( window, modifiers + name );
		}
	}

	/// <summary>
	/// What was last sent to the host, sorted and joined, so it is only sent again when it changes.
	/// </summary>
	string sentInput;

	/// <summary>
	/// Tell the host which <see cref="ForwardedActions"/> the player holds, while a shared app has their controls.
	/// </summary>
	void ForwardInput()
	{
		var down = state == State.Captured && Computer.InputCapture is { App.Shared: true }
			? ForwardedActions.Where( x => Input.Down( x ) ).OrderBy( x => x ).ToArray()
			: [];

		var joined = string.Join( ",", down );
		if ( joined == sentInput )
			return;

		sentInput = joined;
		Computer.UserHoldInput( down );
	}

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
	Transform playerView;
	MouseVisibility playerMouse;

	record ControllerState( PlayerController Controller, bool Input, bool Look, bool Camera );
	readonly List<ControllerState> controllers = new();
	readonly List<WorldInput> worldInputs = new();

	// The reach of the computer's world panels before they were widened for the seat. See WidenReach.
	readonly List<(WorldPanel Panel, float Range)> reaches = new();

	protected override void OnUpdate()
	{
		// Only the player using the computer gets pulled in. Everyone else watches from where they stand.
		var wanted = Computer.IsSeated;

		if ( wanted && state is State.Idle or State.FlyingOut )
			Begin();
		else if ( !wanted && state is State.FlyingIn or State.Captured )
			state = State.FlyingOut;

		if ( state == State.Idle )
			return;

		UpdateLeave();
		ForwardInput();
		ForwardShortcuts();

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
	/// An Escape press is swallowed, so the game menu doesn't open, and stands the player up from the seat. Escape never
	/// reaches <see cref="Input.Keyboard"/>: the engine keeps it for its menu.
	/// </summary>
	void UpdateLeave()
	{
		if ( !Input.EscapePressed )
			return;

		Input.EscapePressed = false;
		Computer.UserLeave();
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

	/// <summary>
	/// A world panel only takes clicks within its <see cref="WorldPanel.InteractionRange"/> of the camera, and the seat
	/// puts the camera where the whole screen fits the view: its corners are farther than its middle, and can be
	/// farther than the range a game set for walking up to it. Widen the reach of the computer's panels (the desktop and
	/// the secure layer) to cover the screen while the player sits, and put it back after.
	/// </summary>
	void WidenReach()
	{
		if ( Scene.Camera is not { } camera || !Components.TryGet<WorldPanel>( out var screen ) )
			return;

		var scale = Sandbox.UI.WorldPanel.ScreenToWorldScale * WorldScale.x;
		var halfDiagonal = (screen.PanelSize * scale).Length * 0.5f;
		var needed = ((ScreenView( camera ).Position - WorldPosition).Length + halfDiagonal) * 1.1f;

		foreach ( var panel in GameObject.Components.GetAll<WorldPanel>( FindMode.EnabledInSelfAndDescendants ) )
		{
			if ( panel.InteractionRange >= needed )
				continue;

			reaches.Add( (panel, panel.InteractionRange) );
			panel.InteractionRange = needed;
		}
	}

	void Begin()
	{
		if ( state == State.Idle )
		{
			if ( Scene.Camera is { } camera )
				playerView = camera.WorldTransform;

			playerMouse = Mouse.Visibility;
			WidenReach();

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

		foreach ( var (panel, range) in reaches )
		{
			if ( panel.IsValid() ) panel.InteractionRange = range;
		}

		reaches.Clear();
		Mouse.Visibility = playerMouse;
		controllers.Clear();
		worldInputs.Clear();
		flight = 0;
		state = State.Idle;
		ForwardInput();

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
