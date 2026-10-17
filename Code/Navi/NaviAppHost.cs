using Sandbox.UI;
using System.Threading.Tasks;

namespace Sandbox;

/// <summary>
/// Panel event names and classes shared with apps. They are plain strings, so apps that don't reference Navi can use them.
/// </summary>
public static class NaviEvents
{
	/// <summary>
	/// A call to the computer. See <see cref="NaviSystemCalls"/>.
	/// </summary>
	public const string Call = NaviSystemCalls.EventName;

	// Older ways to control the window, from before NaviSystemCalls. Kept for published apps.
	public const string Close = "navi.close";
	public const string Minimize = "navi.minimize";
	public const string ToggleMaximize = "navi.maximize";

	/// <summary>
	/// Class on an app's panel while it has the player's input (see <see cref="NaviInputCapture"/>), e.g. <c>HasClass( "navi-input" )</c>.
	/// </summary>
	public const string HasInputClass = "navi-input";
}

/// <summary>
/// Window body. Creates the panel of the app in <see cref="Process"/> and keeps it as its only child, and answers the
/// calls the app makes to the computer. Razor can't create a panel whose type is only known at runtime, so this does it in code.
/// </summary>
public sealed class NaviAppHost : Panel
{
	public NaviComputer Computer { get; set; }
	public NaviProcess Process { get; set; }

	Panel app;
	string appId;

	public override void Tick()
	{
		base.Tick();

		if ( app is null || !app.IsValid || appId != Process?.App.Id )
		{
			app?.Delete();
			app = null;
			appId = null;

			if ( Process is null )
				return;

			app = Process.App.Create();
			appId = Process.App.Id;
			AddChild( app );
		}

		app.SetClass( NaviEvents.HasInputClass, Computer?.InputCapture == Process );

		// A window with a set size decides the app's size. Otherwise the window fits the app.
		var fill = Process.Size is not null || Process.IsMaximized;
		app.Style.Width = fill ? Length.Percent( 100 ) : null;
		app.Style.Height = fill ? Length.Percent( 100 ) : null;
	}

	/// <summary>
	/// Handle the <see cref="NaviEvents"/> that bubble up from the app.
	/// </summary>
	protected override void OnEvent( PanelEvent e )
	{
		var id = Process?.App.Id;

		if ( Computer is not null && id is not null )
		{
			switch ( e.Name )
			{
				case NaviEvents.Call when e.Value is System.ValueTuple<string, object[], TaskCompletionSource<object>> call:
					_ = NaviSystemCalls.RunAsync( Computer, Process, call.Item1, call.Item2, call.Item3 );
					e.StopPropagation();
					return;

				case NaviEvents.Close:
					Computer.Close( id );
					e.StopPropagation();
					return;

				case NaviEvents.Minimize:
					Computer.Minimize( id );
					e.StopPropagation();
					return;

				case NaviEvents.ToggleMaximize:
					Computer.ToggleMaximized( id );
					e.StopPropagation();
					return;
			}
		}

		base.OnEvent( e );
	}
}
