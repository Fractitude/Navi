namespace Sandbox;

/// <summary>
/// What a <see cref="NaviComputer"/> remembers between game launches: everything the player changed on it. Open windows
/// aren't in it; a loaded computer starts at an empty desktop.
/// </summary>
public sealed class NaviComputerSave
{
	public List<string> InstalledApps { get; set; } = new();
	public List<string> InstalledPackages { get; set; } = new();
	public List<string> GrantedPermissions { get; set; } = new();
	public List<string> ReviewedApps { get; set; } = new();
	public List<string> RootApps { get; set; } = new();
	public float DisplayScale { get; set; } = 1f;
}

/// <summary>
/// Saves Navi computers to the game's local data (<see cref="FileSystem.Data"/>), one file per computer in a folder per
/// save slot: <c>navi/saves/{slot}/{computer}.json</c>. A computer loads its save when it starts and saves itself when
/// it changes (<see cref="NaviComputer.AutoSave"/>). A game with save slots sets <see cref="Slot"/> and calls
/// <see cref="LoadAll"/> when the player loads a save.
/// </summary>
/// <remarks>
/// The files are plain JSON on the player's disk, and every package's code in the game can write them, the way it can
/// change a <see cref="NaviComputer"/> directly. They keep the player's choices; they don't add security. Loading still
/// goes through the computer's rules: root needs a system app, permissions are only ones the app declares.
/// </remarks>
public static class NaviSaveData
{
	/// <summary>
	/// The save slot computers load from and save to. A plain name; anything else falls back to "default".
	/// </summary>
	public static string Slot
	{
		get;
		set => field = IsPlainName( value ) ? value : "default";
	} = "default";

	static string PathFor( string id ) => $"navi/saves/{Slot}/{id}.json";

	/// <summary>
	/// The computer's save in the current slot, or null if it has none.
	/// </summary>
	public static NaviComputerSave Read( string id )
	{
		if ( !IsPlainName( id ) || FileSystem.Data is not { } data || !data.FileExists( PathFor( id ) ) )
			return null;

		try
		{
			return data.ReadJson<NaviComputerSave>( PathFor( id ) );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't read the save of computer '{id}': {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Write the computer's save to the current slot.
	/// </summary>
	public static void Write( string id, NaviComputerSave save )
	{
		if ( !IsPlainName( id ) || FileSystem.Data is not { } data )
			return;

		try
		{
			data.CreateDirectory( $"navi/saves/{Slot}" );
			data.WriteJson( PathFor( id ), save );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Navi: couldn't save computer '{id}': {e.Message}" );
		}
	}

	/// <summary>
	/// Load every computer in the scene from the current slot. Computers without a save there keep what they have.
	/// </summary>
	public static void LoadAll( Scene scene )
	{
		foreach ( var computer in scene?.GetAllComponents<NaviComputer>() ?? [] )
			computer.Load();
	}

	/// <summary>
	/// Save every computer in the scene to the current slot.
	/// </summary>
	public static void SaveAll( Scene scene )
	{
		foreach ( var computer in scene?.GetAllComponents<NaviComputer>() ?? [] )
			computer.Save();
	}

	/// <summary>
	/// Letters, digits, '-' and '_' only, so a name can't reach outside the saves folder.
	/// </summary>
	static bool IsPlainName( string name ) =>
		!string.IsNullOrEmpty( name ) && name.Length <= 64 && name.All( x => char.IsAsciiLetterOrDigit( x ) || x is '-' or '_' );
}
