namespace Sandbox;

/// <summary>
/// Running scripts (the engine's experimental <c>Game.Scripting</c>) on the computer, for apps such as Code Edit. Off
/// unless the game turns it on. See <c>docs/scripting.md</c> for what a script can reach: all of the game's public
/// members, apart from the ones that ask <see cref="NaviScriptGuard"/>.
/// </summary>
public sealed partial class NaviComputer
{
	/// <summary>
	/// The most instructions one script may run. It stops a loop that never ends; it is not a budget for gameplay.
	/// </summary>
	public const int ScriptInstructionLimit = 1_000_000;

	bool scriptingEnabled;

	/// <summary>
	/// Whether apps may run scripts on this computer. Off by default. Only the game or the editor's inspector turns it
	/// on: no app, script or player action can (a script asking is ignored). Even when on, an app needs the
	/// <see cref="NaviPermissions.Scripts"/> permission.
	/// </summary>
	[Property, Group( "Other" ), Sync( SyncFlags.FromHost )]
	public bool ScriptingEnabled
	{
		get => scriptingEnabled;
		set
		{
			if ( !NaviScriptGuard.Active )
				scriptingEnabled = value;
		}
	}

	/// <summary>
	/// Run <paramref name="source"/> as a script and say how it went: whether it ran, what went wrong, and where in the
	/// source (a range of characters). Host only, and only while <see cref="ScriptingEnabled"/>. Internal, so a script
	/// can't reach it.
	/// </summary>
	internal (bool Ok, string Message, int Start, int Length) RunScript( string source )
	{
		if ( !IsAuthority )
			return (false, "Scripts run on the host's computer.", 0, 0);

		if ( !ScriptingEnabled )
			return (false, "Running scripts is disabled.", 0, 0);

		if ( NaviScriptGuard.Active )
			return (false, "A script can't run another script.", 0, 0);

		if ( string.IsNullOrWhiteSpace( source ) )
			return (false, "There is nothing to run.", 0, 0);

		try
		{
			var script = Game.Scripting.CreateScript( source );
			var ran = NaviScriptGuard.Run( () => script.Run( ScriptInstructionLimit ) );

			return ran || script.LastError is null
				? (true, "", 0, 0)
				: (false, script.LastError.Message, script.LastError.Start, script.LastError.Length);
		}
		catch ( System.Exception e )
		{
			return (false, e.Message, 0, 0);
		}
	}
}
