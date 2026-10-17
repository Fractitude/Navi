using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Panel events an app can raise to control its window, e.g. <c>CreateEvent( "navi.close" )</c>.
/// They are plain strings, so apps that don't reference Navi can use them too.
/// </summary>
public static class NaviEvents
{
	public const string Close = "navi.close";
	public const string Minimize = "navi.minimize";
	public const string ToggleMaximize = "navi.maximize";
}

/// <summary>
/// Window body. Creates the panel of the app in <see cref="Process"/> and keeps it as its only child.
/// Razor can't create a panel whose type is only known at runtime, so this does it in code.
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

			if ( app is NaviApp naviApp )
			{
				naviApp.Computer = Computer;
				naviApp.Info = Process.App;
			}

			AddChild( app );
		}

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
