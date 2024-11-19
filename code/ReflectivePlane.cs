
using System;

namespace SdfWorld;

public sealed class ReflectivePlane : Component
{
	[RequireComponent] public ModelRenderer Renderer { get; private set; } = null!;

	private SceneCamera? _camera;
	private Texture? _renderTexture;

	protected override void OnStart()
	{
		_camera ??= new SceneCamera( "Reflection" );
		_camera.World = Scene.SceneWorld;
	}

	protected override void OnDestroy()
	{
		_camera?.Dispose();
		_camera = null;

		_renderTexture?.Dispose();
		_renderTexture = null;
	}

	protected override void OnUpdate()
	{
		if ( Scene.Camera is not { } mainCamera ) return;

		_renderTexture ??= Texture.CreateRenderTarget()
			.WithScreenFormat()
			.WithSize( mainCamera.ScreenRect.Size )
			.Create( "Reflection" );

		var plane = new Plane( WorldPosition, WorldRotation.Up );
		var cameraToPlane = plane.GetDistance( mainCamera.WorldPosition );

		mainCamera.UpdateSceneCamera( _camera );

		_camera!.ExcludeTags.Add( "water" );

		_camera.Position = mainCamera.WorldPosition - cameraToPlane * 2f * plane.Normal;

		// TODO: this is assuming camera has no roll, and plane is aligned to ground!
		_camera.Rotation = Rotation.LookAt( mainCamera.WorldRotation.Forward * new Vector3( 1f, 1f, -1f ) );

		var clipNormal = plane.Normal * MathF.Sign( cameraToPlane );

		_camera.Attributes.Set( "_ClipNormal", clipNormal );
		_camera.Attributes.Set( "_ClipDist", Vector3.Dot( WorldPosition, clipNormal ) );

		Graphics.RenderToTexture( _camera, _renderTexture );

		Renderer.SceneObject.Attributes.Set( "ReflectionTexture", _renderTexture );
	}
}
