using Sandbox.UI;

namespace NaviApps;

/// <summary>
/// Dino Run's pixel art, as maps of '#' for a filled cell, and its sizes in game units.
/// </summary>
public static class DinoSprites
{
	public static string[] Standing =>
	[
		"..........#######.",
		".........##.######",
		".........#########",
		".........#########",
		".........#####....",
		".........#######..",
		"#.......#####.....",
		"#......######.....",
		"##...#########....",
		"###.##########.#..",
		"##############....",
		".############.....",
		"..###########.....",
		"...#########......",
		"....#######.......",
		".....###.##.......",
		".....##...#.......",
		".....#....#.......",
		".....##...##......",
	];

	public static string[] RunningA => [.. Standing[..15], ".....###.##.......", ".....##...##......", ".....#............", ".....##..........."];

	public static string[] RunningB => [.. Standing[..15], ".....###.##.......", ".....#....#.......", "..........#.......", "..........##......"];

	public static string[] Crashed => [Standing[0], ".........#.#.#####", ".........##.######", ".........#.#.#####", .. Standing[4..]];

	public static string[] DuckingA =>
	[
		"#............########..",
		"##..........##.#######.",
		"####.......###########.",
		"################.####..",
		"##############.........",
		".#############.........",
		"..###########..........",
		"...##.##...............",
		"...#...##..............",
	];

	public static string[] DuckingB => [.. DuckingA[..7], "...###.#...............", "....#..##.............."];

	public static string[] Cactus =>
	[
		"...##...",
		"..####..",
		"..####.#",
		"#.####.#",
		"#.####.#",
		"#.######",
		"#.#####.",
		"######..",
		".#####..",
		"..####..",
		"..####..",
		"..####..",
		"..####..",
		"..####..",
		"..####..",
		"..####..",
		"..####..",
	];

	public static string[] BirdUp =>
	[
		".......#...............",
		".......##..............",
		".......###.............",
		"...##..####............",
		"..###..#####...........",
		".#####################.",
		"......#############....",
		"........#######........",
	];

	public static string[] BirdDown =>
	[
		"...##..................",
		"..###..................",
		".#####################.",
		"......#############....",
		"........#######........",
		"........####...........",
		"........###............",
		"........##.............",
	];

	public static string[] Cloud =>
	[
		"......#####.....",
		"....##.....##...",
		"..##.........#..",
		".#............##",
		"################",
	];

	/// <summary>How big a large cactus's cells are, against a small one's.</summary>
	public const float LargeCell = 1.5f;

	public static (float Width, float Height) DinoSize => SizeOf( Standing );
	public static (float Width, float Height) DuckSize => SizeOf( DuckingA );
	public static (float Width, float Height) SmallCactusSize => SizeOf( Cactus );
	public static (float Width, float Height) LargeCactusSize => SizeOf( Cactus, LargeCell );
	public static float BirdWidth => SizeOf( BirdUp ).Width;
	public static float BirdHeight => SizeOf( BirdUp ).Height;

	public static (float Width, float Height) SizeOf( string[] map, float cell = 1f ) =>
		(map.Max( x => x.Length ) * cell, map.Length * cell);
}

/// <summary>
/// Draws a <see cref="DinoRunFrame"/>. Its sprites are built once, as panels for each run of filled cells, and every frame
/// only moves panels and flips classes, so nothing is rebuilt while the game runs. It holds no game:
/// <see cref="DinoRunGame"/> does.
/// </summary>
public sealed class DinoRunBoard : Panel
{
	const int GroundBumps = 14;
	const int Clouds = 3;

	readonly Panel dino;
	readonly Panel standing, runningA, runningB, ducking, duckingB, crashed;
	readonly Panel[] bumps = new Panel[GroundBumps];
	readonly Panel[] clouds = new Panel[Clouds];
	readonly Label score;
	readonly Label message;
	readonly Dictionary<int, Panel> obstaclePanels = new();

	/// <summary>A click on the track, for the game to take as a jump.</summary>
	public System.Action Clicked { get; set; }

	public DinoRunBoard()
	{
		AddClass( "dino-run" );
		Style.Width = Length.Pixels( DinoRunGame.Width );
		Style.Height = Length.Pixels( DinoRunGame.Height );

		for ( var i = 0; i < Clouds; i++ )
			clouds[i] = Sprite( this, DinoSprites.Cloud, "cloud" );

		Add.Panel( "ground" ).Style.Top = Length.Pixels( DinoRunGame.Ground );

		for ( var i = 0; i < GroundBumps; i++ )
		{
			bumps[i] = Add.Panel( "bump" );
			bumps[i].Style.Width = Length.Pixels( 2 + i * 7 % 5 );
			bumps[i].Style.Top = Length.Pixels( DinoRunGame.Ground + 2 + i * 3 % 4 );
		}

		dino = Add.Panel( "dino" );
		standing = Sprite( dino, DinoSprites.Standing, "pose" );
		runningA = Sprite( dino, DinoSprites.RunningA, "pose" );
		runningB = Sprite( dino, DinoSprites.RunningB, "pose" );
		ducking = Sprite( dino, DinoSprites.DuckingA, "pose" );
		duckingB = Sprite( dino, DinoSprites.DuckingB, "pose" );
		crashed = Sprite( dino, DinoSprites.Crashed, "pose" );

		score = new Label { Parent = this };
		score.AddClass( "score" );
		message = new Label { Parent = this };
		message.AddClass( "message" );
	}

	/// <summary>
	/// A sprite: a panel the size of <paramref name="map"/>, with a panel for each run of filled cells in a row. The runs are
	/// a little taller than a cell, so a scaled board shows no seams between the rows.
	/// </summary>
	static Panel Sprite( Panel parent, string[] map, string className, float cell = 1f )
	{
		var sprite = parent.Add.Panel( $"sprite {className}" );
		var (width, height) = DinoSprites.SizeOf( map, cell );
		sprite.Style.Width = Length.Pixels( width );
		sprite.Style.Height = Length.Pixels( height );

		for ( var y = 0; y < map.Length; y++ )
		{
			var row = map[y];
			for ( var x = 0; x < row.Length; x++ )
			{
				if ( row[x] != '#' )
					continue;

				var start = x;
				while ( x < row.Length && row[x] == '#' )
					x++;

				var run = sprite.Add.Panel( "px" );
				run.Style.Left = Length.Pixels( start * cell );
				run.Style.Top = Length.Pixels( y * cell );
				run.Style.Width = Length.Pixels( (x - start) * cell );
				run.Style.Height = Length.Pixels( cell + 0.3f );
			}
		}

		return sprite;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton == MouseButtons.Left )
			Clicked?.Invoke();
	}

	public void DrawFrame( DinoRunFrame frame )
	{
		SetClass( "night", frame.Night );

		// The dino. It stands while waiting and in the air, and runs or ducks on the ground.
		var grounded = frame.DinoHeight <= 0f;
		var running = frame.Phase == DinoRunPhase.Running && grounded;
		Show( standing, frame.Phase == DinoRunPhase.Waiting || frame.Phase == DinoRunPhase.Running && !grounded );
		Show( runningA, running && !frame.Ducking && frame.Step );
		Show( runningB, running && !frame.Ducking && !frame.Step );
		Show( ducking, running && frame.Ducking && frame.Step );
		Show( duckingB, running && frame.Ducking && !frame.Step );
		Show( crashed, frame.Phase == DinoRunPhase.Crashed );

		var (_, tall) = frame.Ducking && running ? DinoSprites.DuckSize : DinoSprites.DinoSize;
		Place( dino, DinoRunGame.DinoX, frame.DinoHeight, tall );

		// The ground and the clouds scroll with the distance; the clouds slower, as they are far away.
		for ( var i = 0; i < GroundBumps; i++ )
		{
			var x = Wrap( i * 47 % (DinoRunGame.Width + 20) - frame.Distance, DinoRunGame.Width + 20 ) - 10;
			bumps[i].Style.Left = Length.Pixels( x );
		}

		for ( var i = 0; i < Clouds; i++ )
		{
			var x = Wrap( i * 113 + 40 - frame.Distance * 0.15f, DinoRunGame.Width + 40 ) - 20;
			clouds[i].Style.Left = Length.Pixels( x );
			clouds[i].Style.Top = Length.Pixels( 10 + i * 9 % 20 );
		}

		DrawObstacles( frame );

		var points = frame.Score.ToString( "00000" );
		var high = frame.HighScore > 0 ? $"HI {frame.HighScore:00000}  " : "";
		score.Text = high + points;

		message.Text = frame switch
		{
			{ Phase: DinoRunPhase.Waiting } => "PRESS SPACE TO PLAY",
			{ Phase: DinoRunPhase.Crashed } => "GAME OVER",
			{ Paused: true } => "PAUSED",
			_ => "",
		};
	}

	void DrawObstacles( DinoRunFrame frame )
	{
		var ids = new HashSet<int>();

		foreach ( var obstacle in frame.Obstacles )
		{
			ids.Add( obstacle.Id );

			if ( !obstaclePanels.TryGetValue( obstacle.Id, out var panel ) )
			{
				panel = Add.Panel( "obstacle" );
				Build( panel, obstacle );
				obstaclePanels[obstacle.Id] = panel;
			}

			if ( obstacle.Kind == DinoObstacleKind.Bird )
			{
				Show( panel.Children.ElementAt( 0 ), frame.Flap );
				Show( panel.Children.ElementAt( 1 ), !frame.Flap );
			}

			Place( panel, obstacle.X, obstacle.Height, obstacle.Tall );
		}

		foreach ( var (id, panel) in obstaclePanels.Where( x => !ids.Contains( x.Key ) ).ToList() )
		{
			panel.Delete( true );
			obstaclePanels.Remove( id );
		}
	}

	static void Build( Panel panel, DinoObstacle obstacle )
	{
		if ( obstacle.Kind == DinoObstacleKind.Bird )
		{
			Sprite( panel, DinoSprites.BirdUp, "wings" );
			Sprite( panel, DinoSprites.BirdDown, "wings" );
			return;
		}

		var cell = obstacle.Kind == DinoObstacleKind.LargeCactus ? DinoSprites.LargeCell : 1f;
		var width = DinoSprites.SizeOf( DinoSprites.Cactus, cell ).Width;

		for ( var i = 0; i < obstacle.Count; i++ )
			Sprite( panel, DinoSprites.Cactus, "cactus", cell ).Style.Left = Length.Pixels( i * width );
	}

	/// <summary>
	/// Put a panel <paramref name="tall"/> high with its bottom <paramref name="height"/> above the ground.
	/// </summary>
	static void Place( Panel panel, float x, float height, float tall )
	{
		panel.Style.Left = Length.Pixels( x );
		panel.Style.Top = Length.Pixels( DinoRunGame.Ground - height - tall + 1 );
	}

	static void Show( Panel panel, bool shown ) => panel.SetClass( "hidden", !shown );

	static float Wrap( float value, float range ) => (value % range + range) % range;
}
