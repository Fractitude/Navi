namespace NaviApps;

/// <summary>
/// What a Dino Run obstacle is.
/// </summary>
public enum DinoObstacleKind
{
	SmallCactus,
	LargeCactus,
	Bird,
}

/// <summary>
/// An obstacle on the track. <see cref="X"/> is its left edge and <see cref="Height"/> how far its bottom is above the
/// ground, in game units. A cactus obstacle is a group of <see cref="Count"/> cacti side by side.
/// </summary>
public sealed record DinoObstacle( int Id, DinoObstacleKind Kind, float X, float Height, int Count, float Width, float Tall );

public enum DinoRunPhase
{
	/// <summary>Not started: the dino stands at the start, waiting for a jump.</summary>
	Waiting,
	Running,
	Crashed,
}

/// <summary>
/// One frame of Dino Run, for <see cref="DinoRunBoard"/> to draw. Replaced whole every tick.
/// </summary>
public sealed record DinoRunFrame
{
	public static DinoRunFrame Empty { get; } = new();

	public DinoRunPhase Phase { get; init; }

	/// <summary>The dino's feet above the ground, in game units.</summary>
	public float DinoHeight { get; init; }

	public bool Ducking { get; init; }

	/// <summary>Which of the two running poses the legs are in.</summary>
	public bool Step { get; init; }

	/// <summary>Which of the two poses the birds' wings are in.</summary>
	public bool Flap { get; init; }

	public IReadOnlyList<DinoObstacle> Obstacles { get; init; } = [];

	/// <summary>How far the track has scrolled, in game units. The ground and the clouds are drawn from it.</summary>
	public float Distance { get; init; }

	public int Score { get; init; }
	public int HighScore { get; init; }

	/// <summary>Every 700 points the track turns to night, and back.</summary>
	public bool Night { get; init; }

	/// <summary>The game is running but the window doesn't have the player's keys, so it waits.</summary>
	public bool Paused { get; init; }
}

/// <summary>
/// Dino Run, the game a web browser offers while the computer is offline: jump the cacti, duck the birds, and go faster
/// the longer it runs. The rules follow Chrome's, in game units of half its pixels. All the logic is here; the board
/// (<see cref="DinoRunBoard"/>) only draws <see cref="Frame"/>.
/// </summary>
public sealed class DinoRunGame
{
	/// <summary>The size of the track, in game units.</summary>
	public const float Width = 300, Height = 80;

	/// <summary>How far down the ground line is, from the top of the track.</summary>
	public const float Ground = 72;

	/// <summary>Where the dino's left edge is.</summary>
	public const float DinoX = 20;

	const float StartSpeed = 180, MaxSpeed = 390, Acceleration = 1.8f;
	const float Gravity = 1080, JumpVelocity = -300, DuckFallFactor = 3f;

	/// <summary>Birds only come once the track is this fast.</summary>
	const float BirdSpeed = 255;

	/// <summary>Points per unit of distance.</summary>
	const float PointsPerUnit = 0.05f;

	const int NightEvery = 700;

	/// <summary>How long after a crash before a jump starts a new run, in seconds, so a held key doesn't restart it.</summary>
	const float RestartDelay = 0.5f;

	/// <summary>
	/// The best score of this game session, on the host (whose copy of the browser runs the game). Not saved. Static, so it survives the window being closed; not
	/// readonly, so a hot reload keeps it.
	/// </summary>
	static int highScore;

	readonly Navi navi;
	readonly System.Random random = new();
	readonly List<DinoObstacle> obstacles = new();

	DinoRunPhase phase;
	float height, velocity, speed = StartSpeed, distance, animation, sinceCrash;
	float nextGap;
	bool ducking, jumpHeld;
	int nextId, lastHundred;

	/// <summary>A click on the track counts as a jump, for a player without a keyboard to hand.</summary>
	bool clicked;

	public DinoRunGame( Navi navi )
	{
		this.navi = navi;
		Frame = MakeFrame( paused: false );
	}

	public DinoRunFrame Frame { get; private set; }

	/// <summary>
	/// The player clicked the track.
	/// </summary>
	public void Click() => clicked = true;

	/// <summary>
	/// Advance the game by <paramref name="delta"/> seconds. <paramref name="hasInput"/> is whether the window has the
	/// player's keys; without them a run waits where it is.
	/// </summary>
	public void Tick( float delta, bool hasInput )
	{
		delta = System.Math.Clamp( delta, 0f, 0.05f );

		var jump = clicked || hasInput && (navi.Input.Down( "Jump" ) || navi.Input.Down( "Forward" ));
		var jumpPressed = clicked || hasInput && (navi.Input.Pressed( "Jump" ) || navi.Input.Pressed( "Forward" ));
		var duck = hasInput && (navi.Input.Down( "Duck" ) || navi.Input.Down( "Backward" ));
		clicked = false;

		switch ( phase )
		{
			case DinoRunPhase.Waiting:
				if ( jumpPressed )
				{
					phase = DinoRunPhase.Running;
					Jump();
				}

				break;

			case DinoRunPhase.Crashed:
				sinceCrash += delta;
				if ( jumpPressed && sinceCrash > RestartDelay )
					Restart();

				break;

			case DinoRunPhase.Running when hasInput || jump:
				Run( delta, jump, jumpPressed, duck );
				break;
		}

		jumpHeld = jump;
		Frame = MakeFrame( paused: phase == DinoRunPhase.Running && !hasInput );
	}

	void Run( float delta, bool jump, bool jumpPressed, bool duck )
	{
		speed = System.Math.Min( MaxSpeed, speed + Acceleration * delta );

		var step = speed * delta;
		distance += step;
		animation += delta;

		// The dino. A jump starts on the ground; ducking in the air drops it faster.
		var grounded = height <= 0f && velocity >= 0f;
		if ( grounded && jumpPressed && !duck )
			Jump();

		if ( !grounded || velocity < 0f )
		{
			velocity += Gravity * (duck ? DuckFallFactor : 1f) * delta;
			height -= velocity * delta;

			if ( height <= 0f )
			{
				height = 0f;
				velocity = 0f;
			}
		}

		ducking = duck && height <= 0f;

		// The track.
		for ( var i = obstacles.Count - 1; i >= 0; i-- )
		{
			var obstacle = obstacles[i];
			var moved = obstacle with { X = obstacle.X - step * (obstacle.Kind == DinoObstacleKind.Bird ? 1.1f : 1f) };

			if ( moved.X + moved.Width < 0 )
				obstacles.RemoveAt( i );
			else
				obstacles[i] = moved;
		}

		if ( obstacles.Count == 0 || obstacles[^1].X + obstacles[^1].Width < Width - nextGap )
			Spawn();

		// A sound at every hundred points. Chrome blinks the score too; here it stays put.
		var hundred = Score / 100;
		if ( hundred > lastHundred )
		{
			lastHundred = hundred;
			_ = navi.Audio.PlayAsync( "sounds/kenney/ui/ui.upvote.sound", 0.6f );
		}

		if ( obstacles.Any( Hits ) )
			Crash();
	}

	int Score => (int)(distance * PointsPerUnit);

	void Jump()
	{
		velocity = JumpVelocity;
		height = 0.01f;
		_ = navi.Audio.PlayAsync( "sounds/kenney/ui/ui.button.press.sound", 0.5f );
	}

	void Crash()
	{
		phase = DinoRunPhase.Crashed;
		sinceCrash = 0;
		highScore = System.Math.Max( highScore, Score );
		_ = navi.Audio.PlayAsync( "sounds/kenney/ui/ui.button.deny.sound" );
	}

	void Restart()
	{
		obstacles.Clear();
		phase = DinoRunPhase.Running;
		speed = StartSpeed;
		distance = 0;
		lastHundred = 0;
		nextGap = 0;
		ducking = false;
		Jump();
	}

	/// <summary>
	/// Put a new obstacle at the right edge, and pick the gap before the next one: wider the faster the track goes.
	/// </summary>
	void Spawn()
	{
		var birds = speed > BirdSpeed;
		var roll = random.Next( birds ? 3 : 2 );

		// Never two birds in a row, as in Chrome.
		if ( roll == 2 && obstacles.Count > 0 && obstacles[^1].Kind == DinoObstacleKind.Bird )
			roll = random.Next( 2 );

		DinoObstacle obstacle;
		float minGap;

		if ( roll == 2 )
		{
			float[] heights = [1, 10, 22];
			obstacle = new DinoObstacle( nextId++, DinoObstacleKind.Bird, Width, heights[random.Next( heights.Length )], 1,
				DinoSprites.BirdWidth, DinoSprites.BirdHeight );
			minGap = 75;
		}
		else
		{
			var large = roll == 1;
			var most = speed > 240 ? 3 : speed > 200 ? 2 : 1;
			var count = random.Next( 1, most + 1 );
			var (width, tall) = large ? DinoSprites.LargeCactusSize : DinoSprites.SmallCactusSize;

			obstacle = new DinoObstacle( nextId++, large ? DinoObstacleKind.LargeCactus : DinoObstacleKind.SmallCactus,
				Width, 0, count, width * count, tall );
			minGap = 60;
		}

		obstacles.Add( obstacle );

		// Chrome's gap: the obstacle's width times the speed in its pixels a frame, plus the kind's minimum.
		var gap = obstacle.Width * (speed / 30f) + minGap * 0.6f;
		nextGap = gap + (float)random.NextDouble() * gap * 0.5f;
	}

	/// <summary>
	/// Does the dino touch <paramref name="obstacle"/>? Both are taken a little smaller than drawn, so a near miss is a miss.
	/// </summary>
	bool Hits( DinoObstacle obstacle )
	{
		var (dinoWidth, dinoTall) = ducking ? DinoSprites.DuckSize : DinoSprites.DinoSize;
		var dino = new Rect( DinoX + 3, height + 1, dinoWidth - 6, dinoTall - (ducking ? 2 : 4) );
		var box = new Rect( obstacle.X + 1.5f, obstacle.Height + 1, obstacle.Width - 3, obstacle.Tall - 2 );

		return dino.Left < box.Right && box.Left < dino.Right && dino.Top < box.Bottom && box.Top < dino.Bottom;
	}

	DinoRunFrame MakeFrame( bool paused ) => new()
	{
		Phase = phase,
		DinoHeight = height,
		Ducking = ducking,
		Step = (int)(animation * 10) % 2 == 0,
		Flap = (int)(animation * 4) % 2 == 0,
		Obstacles = obstacles.ToArray(),
		Distance = distance,
		Score = Score,
		HighScore = highScore,
		Night = Score / NightEvery % 2 == 1,
		Paused = paused,
	};
}
