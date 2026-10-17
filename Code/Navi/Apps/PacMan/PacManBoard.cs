using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// The Pac-Man maze and game. Builds its tiles once, then moves the actor panels every tick, so the game doesn't
/// rebuild any UI while it runs. Input comes from the Forward/Backward/Left/Right actions (WASD).
/// </summary>
public sealed class PacManBoard : Panel
{
	public const int Cell = 12;

	// # wall, . dot, o power pellet, - ghost door, P Pac-Man start, G ghost start (first outside the house, the rest inside).
	static readonly string[] Layout =
	{
		"###################",
		"#........#........#",
		"#o##.###.#.###.##o#",
		"#.................#",
		"#.##.#.#####.#.##.#",
		"#....#...#...#....#",
		"####.### # ###.####",
		"   #.#   G   #.#   ",
		"####.# ##-## #.####",
		"    .  #GGG#  .    ",
		"####.# ##### #.####",
		"   #.#       #.#   ",
		"####.# ##### #.####",
		"#........#........#",
		"#.##.###.#.###.##.#",
		"#o.#.....P.....#.o#",
		"##.#.#.#####.#.#.##",
		"#....#...#...#....#",
		"#.######.#.######.#",
		"#.................#",
		"###################",
	};

	public static int Width => Layout[0].Length;
	public static int Height => Layout.Length;

	public enum GameState { Ready, Playing, Dying, LevelClear, GameOver }

	public GameState State { get; private set; } = GameState.Ready;
	public int Score { get; private set; }
	public int Lives { get; private set; } = 3;
	public int Level { get; private set; } = 1;

	/// <summary>
	/// False pauses the game. The app sets it while it has the player's input.
	/// </summary>
	public bool HasInput { get; set; }

	static readonly Vector2Int Up = new( 0, -1 );
	static readonly Vector2Int Down = new( 0, 1 );
	static readonly Vector2Int Left = new( -1, 0 );
	static readonly Vector2Int Right = new( 1, 0 );
	static readonly Vector2Int[] Directions = { Up, Left, Down, Right }; // tie-break order of the arcade game

	// Scatter, chase, scatter, chase... in seconds. Chase lasts forever after the last entry.
	static readonly float[] ModeTimes = { 7, 20, 7, 20, 5, 20, 5 };

	class Actor
	{
		public Vector2Int Tile;
		public Vector2Int Dir;
		public float Progress; // 0..1 towards Tile + Dir
		public Panel Panel;

		public Vector2 Position => new( Tile.x + Dir.x * Progress, Tile.y + Dir.y * Progress );
	}

	enum GhostMode { InHouse, Leaving, Active, Eaten }

	sealed class Ghost : Actor
	{
		public string Name;
		public Vector2Int Corner;
		public Vector2Int Home;
		public GhostMode Mode;
		public float ReleaseTimer;
		public bool Frightened;
	}

	readonly bool[,] dots = new bool[Width, Height];
	readonly bool[,] pellets = new bool[Width, Height];
	readonly Panel[,] dotPanels = new Panel[Width, Height];
	readonly Actor pac = new();
	readonly List<Ghost> ghosts = new();
	Panel pacMouth;

	Vector2Int pacStart;
	Vector2Int doorAbove; // tile just outside the ghost house door
	int dotsLeft;
	Vector2Int wanted;

	float stateTimer;
	float modeTimer;
	int modeIndex;
	float frightTimer;
	int ghostsEatenThisFright;
	float clock;

	public PacManBoard()
	{
		AddClass( "board" );
		Style.Width = Length.Pixels( Width * Cell );
		Style.Height = Length.Pixels( Height * Cell );

		BuildMaze();
		NewGame();
	}

	void BuildMaze()
	{
		for ( var y = 0; y < Height; y++ )
		{
			for ( var x = 0; x < Width; x++ )
			{
				var c = Layout[y][x];

				if ( c == '#' )
				{
					var wall = AddTile( "wall", x, y );
					if ( !IsWall( x, y - 1 ) ) wall.AddClass( "edge-top" );
					if ( !IsWall( x, y + 1 ) ) wall.AddClass( "edge-bottom" );
					if ( !IsWall( x - 1, y ) ) wall.AddClass( "edge-left" );
					if ( !IsWall( x + 1, y ) ) wall.AddClass( "edge-right" );
				}
				else if ( c == '-' )
				{
					AddTile( "door", x, y );
					doorAbove = new Vector2Int( x, y - 1 );
				}
				else if ( c is '.' or 'o' )
				{
					dotPanels[x, y] = AddTile( c == 'o' ? "pellet" : "dot", x, y );
				}
				else if ( c == 'P' )
				{
					pacStart = new Vector2Int( x, y );
				}
			}
		}

		pac.Panel = Add.Panel( "pacman" );
		pacMouth = pac.Panel.Add.Panel( "mouth" );

		var names = new[] { "blinky", "pinky", "inky", "clyde" };
		var corners = new[] { new Vector2Int( Width - 2, 0 ), new Vector2Int( 1, 0 ), new Vector2Int( Width - 2, Height - 1 ), new Vector2Int( 1, Height - 1 ) };
		var homes = new List<Vector2Int>();

		for ( var y = 0; y < Height; y++ )
			for ( var x = 0; x < Width; x++ )
				if ( Layout[y][x] == 'G' ) homes.Add( new Vector2Int( x, y ) );

		// The first G (outside the house) is Blinky's.
		homes = homes.OrderBy( h => h.y ).ThenBy( h => System.Math.Abs( h.x - doorAbove.x ) ).ToList();

		for ( var i = 0; i < names.Length && i < homes.Count; i++ )
		{
			var panel = Add.Panel( $"ghost {names[i]}" );
			panel.Add.Panel( "eye left" );
			panel.Add.Panel( "eye right" );

			ghosts.Add( new Ghost { Name = names[i], Corner = corners[i], Home = homes[i], Panel = panel } );
		}
	}

	Panel AddTile( string className, int x, int y )
	{
		var tile = Add.Panel( className );
		tile.Style.Left = Length.Pixels( x * Cell );
		tile.Style.Top = Length.Pixels( y * Cell );
		return tile;
	}

	static bool IsWall( int x, int y ) => x >= 0 && y >= 0 && x < Width && y < Height && Layout[y][x] == '#';

	/// <summary>
	/// Can an actor stand on this tile? Off the left/right edge wraps (the tunnel). The door is only open to ghosts going in or out.
	/// </summary>
	static bool IsOpen( Vector2Int tile, bool throughDoor = false )
	{
		if ( tile.y < 0 || tile.y >= Height ) return false;
		var c = Layout[tile.y][Wrap( tile.x )];
		return c != '#' && (c != '-' || throughDoor);
	}

	static int Wrap( int x ) => ((x % Width) + Width) % Width;
	static Vector2Int Wrap( Vector2Int tile ) => new( Wrap( tile.x ), tile.y );
	static float DistanceSquared( Vector2Int a, Vector2Int b ) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

	void NewGame()
	{
		Score = 0;
		Lives = 3;
		Level = 1;
		ResetDots();
		ResetActors();
	}

	void ResetDots()
	{
		dotsLeft = 0;

		for ( var y = 0; y < Height; y++ )
		{
			for ( var x = 0; x < Width; x++ )
			{
				var c = Layout[y][x];
				dots[x, y] = c is '.' or 'o';
				pellets[x, y] = c == 'o';
				if ( dots[x, y] ) dotsLeft++;
				dotPanels[x, y]?.SetClass( "eaten", false );
			}
		}
	}

	void ResetActors()
	{
		pac.Tile = pacStart;
		pac.Dir = Left;
		pac.Progress = 0;
		wanted = Left;

		for ( var i = 0; i < ghosts.Count; i++ )
		{
			var g = ghosts[i];
			g.Tile = g.Home;
			g.Progress = 0;
			g.Frightened = false;
			g.Mode = i == 0 ? GhostMode.Active : GhostMode.InHouse;
			g.Dir = i == 0 ? Left : Up;
			g.ReleaseTimer = i * 3f;
		}

		modeTimer = 0;
		modeIndex = 0;
		frightTimer = 0;
		State = GameState.Ready;
		stateTimer = 2f;
		Render();
	}

	public override void Tick()
	{
		base.Tick();

		if ( !HasInput || !IsVisible )
			return;

		var dt = System.MathF.Min( Time.Delta, 0.05f );
		clock += dt;

		ReadInput();

		switch ( State )
		{
			case GameState.Ready:
				if ( (stateTimer -= dt) <= 0 ) State = GameState.Playing;
				break;

			case GameState.Playing:
				Step( dt );
				break;

			case GameState.Dying:
				if ( (stateTimer -= dt) <= 0 )
				{
					if ( --Lives <= 0 ) State = GameState.GameOver;
					else ResetActors();
				}
				break;

			case GameState.LevelClear:
				SetClass( "flash", (int)(stateTimer * 4) % 2 == 0 );
				if ( (stateTimer -= dt) <= 0 )
				{
					SetClass( "flash", false );
					Level++;
					ResetDots();
					ResetActors();
				}
				break;

			case GameState.GameOver:
				if ( restartPressed )
					NewGame();
				break;
		}

		Render();
	}

	bool restartPressed;

	void ReadInput()
	{
		if ( Input.Down( "Forward" ) ) wanted = Up;
		else if ( Input.Down( "Backward" ) ) wanted = Down;
		else if ( Input.Down( "Left" ) ) wanted = Left;
		else if ( Input.Down( "Right" ) ) wanted = Right;

		// A fresh press, so a key still held from the last game doesn't restart straight away.
		restartPressed = Input.Pressed( "Jump" ) || Input.Pressed( "Forward" ) || Input.Pressed( "Backward" ) || Input.Pressed( "Left" ) || Input.Pressed( "Right" );
	}

	void Step( float dt )
	{
		UpdateGhostMode( dt );
		MovePac( dt );

		foreach ( var g in ghosts )
			MoveGhost( g, dt );

		CheckCollisions();

		if ( dotsLeft == 0 )
		{
			State = GameState.LevelClear;
			stateTimer = 2f;
		}
	}

	float PacSpeed => 6f + Level * 0.2f;
	float GhostSpeed => 5f + Level * 0.25f;
	float FrightTime => System.MathF.Max( 2f, 7f - Level );

	void MovePac( float dt )
	{
		// Turning around is allowed mid-tile.
		if ( pac.Dir != Vector2Int.Zero && wanted == -pac.Dir )
		{
			pac.Tile = Wrap( pac.Tile + pac.Dir );
			pac.Dir = wanted;
			pac.Progress = 1 - pac.Progress;
		}

		if ( pac.Dir == Vector2Int.Zero )
		{
			if ( IsOpen( pac.Tile + wanted ) ) pac.Dir = wanted;
			else return;
		}

		pac.Progress += PacSpeed * dt;

		while ( pac.Progress >= 1 )
		{
			pac.Progress -= 1;
			pac.Tile = Wrap( pac.Tile + pac.Dir );
			Eat( pac.Tile );

			if ( IsOpen( pac.Tile + wanted ) )
			{
				pac.Dir = wanted;
			}
			else if ( !IsOpen( pac.Tile + pac.Dir ) )
			{
				pac.Dir = Vector2Int.Zero;
				pac.Progress = 0;
				break;
			}
		}
	}

	void Eat( Vector2Int tile )
	{
		if ( !dots[tile.x, tile.y] )
			return;

		dots[tile.x, tile.y] = false;
		dotPanels[tile.x, tile.y]?.SetClass( "eaten", true );
		dotsLeft--;

		if ( pellets[tile.x, tile.y] )
		{
			Score += 50;
			frightTimer = FrightTime;
			ghostsEatenThisFright = 0;

			foreach ( var g in ghosts.Where( g => g.Mode == GhostMode.Active ) )
			{
				g.Frightened = true;
				Reverse( g );
			}
		}
		else
		{
			Score += 10;
		}
	}

	void UpdateGhostMode( float dt )
	{
		if ( frightTimer > 0 )
		{
			frightTimer -= dt;

			if ( frightTimer <= 0 )
				foreach ( var g in ghosts ) g.Frightened = false;

			return; // the scatter/chase clock pauses while ghosts are frightened
		}

		if ( modeIndex >= ModeTimes.Length )
			return;

		modeTimer += dt;
		if ( modeTimer < ModeTimes[modeIndex] )
			return;

		modeTimer = 0;
		modeIndex++;

		foreach ( var g in ghosts.Where( g => g.Mode == GhostMode.Active ) )
			Reverse( g );
	}

	bool Chasing => modeIndex % 2 == 1 || modeIndex >= ModeTimes.Length;

	static void Reverse( Actor a )
	{
		if ( a.Dir == Vector2Int.Zero ) return;
		a.Tile = Wrap( a.Tile + a.Dir );
		a.Dir = -a.Dir;
		a.Progress = 1 - a.Progress;
	}

	void MoveGhost( Ghost g, float dt )
	{
		if ( g.Mode == GhostMode.InHouse )
		{
			if ( (g.ReleaseTimer -= dt) <= 0 ) g.Mode = GhostMode.Leaving;
			return;
		}

		var speed = g.Mode switch
		{
			GhostMode.Eaten => GhostSpeed * 2f,
			GhostMode.Leaving => GhostSpeed * 0.6f,
			_ when g.Frightened => GhostSpeed * 0.6f,
			_ => GhostSpeed,
		};

		if ( g.Dir == Vector2Int.Zero )
			g.Dir = ChooseDirection( g );

		g.Progress += speed * dt;

		while ( g.Progress >= 1 )
		{
			g.Progress -= 1;
			g.Tile = Wrap( g.Tile + g.Dir );

			if ( g.Mode == GhostMode.Leaving && g.Tile == doorAbove )
			{
				g.Mode = GhostMode.Active;
				g.Frightened = false;
			}
			else if ( g.Mode == GhostMode.Eaten && g.Tile == g.Home )
			{
				g.Mode = GhostMode.Leaving;
				g.Frightened = false;
			}

			g.Dir = ChooseDirection( g );
		}
	}

	Vector2Int ChooseDirection( Ghost g )
	{
		var throughDoor = g.Mode is GhostMode.Leaving or GhostMode.Eaten;
		var options = Directions
			.Where( d => IsOpen( g.Tile + d, throughDoor ) )
			.ToList();

		// Ghosts never turn back, unless it's the only way.
		var forward = options.Where( d => d != -g.Dir ).ToList();
		if ( forward.Count > 0 ) options = forward;
		if ( options.Count == 0 ) return Vector2Int.Zero;

		if ( g.Mode == GhostMode.Active && g.Frightened )
			return options[Game.Random.Int( 0, options.Count - 1 )];

		var target = Target( g );
		return options.OrderBy( d => DistanceSquared( Wrap( g.Tile + d ), target ) ).First();
	}

	Vector2Int Target( Ghost g )
	{
		if ( g.Mode == GhostMode.Leaving ) return doorAbove;
		if ( g.Mode == GhostMode.Eaten ) return g.Home;
		if ( !Chasing ) return g.Corner;

		var facing = pac.Dir == Vector2Int.Zero ? wanted : pac.Dir;

		switch ( g.Name )
		{
			case "pinky":
				return pac.Tile + facing * 4;

			case "inky":
				var blinky = ghosts[0].Tile;
				var ahead = pac.Tile + facing * 2;
				return ahead + (ahead - blinky);

			case "clyde":
				return DistanceSquared( g.Tile, pac.Tile ) > 64 ? pac.Tile : g.Corner;

			default:
				return pac.Tile;
		}
	}

	void CheckCollisions()
	{
		foreach ( var g in ghosts )
		{
			if ( g.Mode is GhostMode.InHouse or GhostMode.Eaten )
				continue;

			if ( (g.Position - pac.Position).Length > 0.6f )
				continue;

			if ( g.Frightened )
			{
				g.Mode = GhostMode.Eaten;
				g.Frightened = false;
				Score += 200 << ghostsEatenThisFright;
				ghostsEatenThisFright = System.Math.Min( ghostsEatenThisFright + 1, 3 );
			}
			else
			{
				State = GameState.Dying;
				stateTimer = 1.5f;
				return;
			}
		}
	}

	void Render()
	{
		Place( pac );
		pac.Panel.SetClass( "dying", State == GameState.Dying );

		var facing = pac.Dir == Vector2Int.Zero ? wanted : pac.Dir;
		pac.Panel.SetClass( "face-up", facing == Up );
		pac.Panel.SetClass( "face-down", facing == Down );
		pac.Panel.SetClass( "face-left", facing == Left );
		pac.Panel.SetClass( "face-right", facing == Right );
		pacMouth.SetClass( "closed", State == GameState.Playing && pac.Dir != Vector2Int.Zero && (int)(clock * 12) % 2 == 0 );

		var blinking = frightTimer > 0 && frightTimer < 2f && (int)(clock * 6) % 2 == 0;

		foreach ( var g in ghosts )
		{
			Place( g );
			g.Panel.SetClass( "frightened", g.Frightened );
			g.Panel.SetClass( "blink", g.Frightened && blinking );
			g.Panel.SetClass( "eaten", g.Mode == GhostMode.Eaten );
			g.Panel.SetClass( "hidden", State is GameState.Dying or GameState.LevelClear );
		}

		var pelletsOn = (int)(clock * 3) % 2 == 0;

		for ( var y = 0; y < Height; y++ )
			for ( var x = 0; x < Width; x++ )
				if ( pellets[x, y] ) dotPanels[x, y]?.SetClass( "blink", pelletsOn );
	}

	void Place( Actor a )
	{
		var pos = a.Position;

		// Inside the tunnel, the half-step across the wrap would draw on the wrong side.
		if ( pos.x < -0.5f ) pos.x += Width;
		if ( pos.x > Width - 0.5f ) pos.x -= Width;

		a.Panel.Style.Left = Length.Pixels( pos.x * Cell );
		a.Panel.Style.Top = Length.Pixels( pos.y * Cell );
	}
}
