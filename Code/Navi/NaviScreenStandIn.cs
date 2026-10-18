namespace Sandbox;

/// <summary>
/// Stands in for a computer's screen where the computer is out of network range (<see cref="NaviComputer.NetworkRange"/>):
/// the engine turns the computer off there, so players would see nothing. The host spawns one for each computer. It draws
/// its renderers only while the computer is off or missing on this client, or out of range of the host's camera
/// (<see cref="NaviComputer.IsOutOfRange"/>). Only the drawing is switched, which stays on this client; the components
/// stay as the host spawned them.
/// </summary>
public sealed class NaviScreenStandIn : Component
{
	/// <summary>
	/// The computer's GameObject. Looked up every frame: a player who joins out of range gets it only once in range.
	/// </summary>
	[Property] public System.Guid Computer { get; set; }

	protected override void OnUpdate()
	{
		var computer = Scene.Directory.FindByGuid( Computer );
		var show = computer is null
			|| !computer.Active
			|| computer.Components.Get<NaviComputer>() is { IsOutOfRange: true };

		foreach ( var renderer in Components.GetAll<ModelRenderer>( FindMode.EverythingInSelf ) )
		{
			if ( renderer.SceneObject is { } sceneObject && sceneObject.RenderingEnabled != show )
				sceneObject.RenderingEnabled = show;
		}
	}
}
