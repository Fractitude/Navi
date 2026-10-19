namespace Sandbox;

/// <summary>
/// Holds a static value that has to survive a hot reload. A hot reload of this library resets a static collection of
/// only engine types (strings, numbers) to its initial value, but keeps one that holds this library's types. So a static
/// collection of strings that changes while the game runs goes in one of these.
/// </summary>
internal sealed class NaviHotloadKept<T>
{
	public NaviHotloadKept( T value ) => Value = value;

	public T Value { get; }
}
