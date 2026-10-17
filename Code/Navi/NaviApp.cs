using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Base class for apps that run on a <see cref="NaviComputer"/>. To make an app, create a Razor panel
/// that inherits this and tag it with <see cref="NaviAppAttribute"/>. It is found automatically.
/// </summary>
public abstract class NaviApp : Panel
{
	/// <summary>
	/// The computer this app is running on. Set before the app's first render.
	/// </summary>
	public NaviComputer Computer { get; internal set; }

	/// <summary>
	/// This app's registration.
	/// </summary>
	public NaviAppInfo Info { get; internal set; }

	/// <summary>
	/// Close this app's window.
	/// </summary>
	public void Close() => Computer?.Close( Info.Id );
}
