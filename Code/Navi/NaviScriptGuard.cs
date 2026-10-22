namespace Sandbox;

/// <summary>
/// Tells public methods that a script is running them. The engine's scripts (<c>Game.Scripting</c>) can call every
/// public member of every type in the game, and nothing else hides one (see <c>docs/scripting.md</c>), so a public
/// method that must not be reachable from a script asks <see cref="Active"/> and refuses. Only
/// <see cref="NaviComputer.RunScript"/> may run a script, and it does so through <see cref="Run"/>.
/// </summary>
/// <remarks>
/// <see cref="Enter"/> and <see cref="Exit"/> are internal and the depth is private, which the scripts can't reach.
/// A script is synchronous and can't start a task or a lambda, so the flag covers everything it calls.
/// </remarks>
public static class NaviScriptGuard
{
	static int depth;

	/// <summary>
	/// A script is running. Methods that change the computer, or give away what it keeps secret, return without doing
	/// anything while this is true.
	/// </summary>
	public static bool Active => depth > 0;

	internal static void Enter() => depth++;

	internal static void Exit() => depth--;

	/// <summary>
	/// Run <paramref name="body"/> (which runs a script) with <see cref="Active"/> set.
	/// </summary>
	internal static T Run<T>( System.Func<T> body )
	{
		Enter();
		try
		{
			return body();
		}
		finally
		{
			Exit();
		}
	}
}
