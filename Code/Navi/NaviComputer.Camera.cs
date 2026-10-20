namespace Sandbox;

/// <summary>
/// The camera on top of the computer's screen. The computer owns it, and apps only get the picture it draws
/// (<c>camera.view</c>, which needs <see cref="NaviPermissions.Camera"/>), never the camera itself. Every client has its own,
/// since it only looks at the world and holds no state.
/// </summary>
public sealed partial class NaviComputer
{
	/// <summary>
	/// The camera draws only this long after the last app asked for its picture, so it costs nothing while nobody looks.
	/// </summary>
	const float CameraHold = 1f;

	/// <summary>
	/// How far in front of the screen the camera sits, in the GameObject's local units.
	/// </summary>
	const float CameraOffset = 2f;

	/// <summary>
	/// The largest picture an app can ask for, in pixels on a side.
	/// </summary>
	const int MaxCameraSize = 1024;

	GameObject cameraObject;
	CameraComponent cameraComponent;
	Texture cameraTexture;
	RealTimeSince sinceCameraAsked = float.MaxValue;

	/// <summary>
	/// The texture the camera draws into, at least a pixel and at most <see cref="MaxCameraSize"/> on a side. The same
	/// texture comes back while the size stays. Asking keeps the camera drawing for a moment.
	/// </summary>
	internal Texture GetCameraView( int width, int height )
	{
		width = System.Math.Clamp( width, 1, MaxCameraSize );
		height = System.Math.Clamp( height, 1, MaxCameraSize );

		sinceCameraAsked = 0;

		if ( cameraTexture is null || cameraTexture.Width != width || cameraTexture.Height != height )
		{
			cameraTexture?.Dispose();
			cameraTexture = Texture.CreateRenderTarget().WithSize( width, height ).Create();
		}

		return cameraTexture;
	}

	/// <summary>
	/// Keep the camera at the top center of the screen, looking out of it, and draw its picture while apps ask for it.
	/// It isn't saved or sent over the network, and isn't the scene's main camera.
	/// </summary>
	void UpdateCamera()
	{
		if ( sinceCameraAsked > CameraHold || cameraTexture is null || Components.Get<WorldPanel>() is not { } panel )
		{
			if ( cameraObject.IsValid() )
				cameraObject.Destroy();

			cameraObject = null;
			cameraComponent = null;
			return;
		}

		if ( !cameraObject.IsValid() )
		{
			cameraObject = new GameObject( GameObject, true, "Navi Camera" );
			cameraObject.Flags |= GameObjectFlags.NotSaved;
			cameraObject.NetworkMode = NetworkMode.Never;

			cameraComponent = cameraObject.Components.Create<CameraComponent>();
			cameraComponent.IsMainCamera = false;
			cameraComponent.Enabled = false;
			cameraComponent.FieldOfView = 75;
		}

		// The screen faces forward, so the camera looks the same way from the middle of its top edge.
		var (center, size) = ScreenRect( panel );
		var local = new Transform( center + Vector3.Up * (size.y * 0.5f) + Vector3.Forward * CameraOffset );

		if ( cameraObject.LocalTransform != local )
			cameraObject.LocalTransform = local;

		cameraComponent.RenderToTexture( cameraTexture );
	}

	void DestroyCamera()
	{
		if ( cameraObject.IsValid() )
			cameraObject.Destroy();

		cameraObject = null;
		cameraComponent = null;

		cameraTexture?.Dispose();
		cameraTexture = null;
	}
}
