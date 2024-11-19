
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
		_camera.RenderTags.Add( "world" );
		_camera.RenderTags.Add( "skybox" );
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

		_camera!.ZNear = mainCamera.ZNear;
		_camera.ZFar = mainCamera.ZFar;
		_camera.FieldOfView = mainCamera.FieldOfView;

		_camera.Position = mainCamera.WorldPosition - cameraToPlane * 2f * plane.Normal;

		// TODO: this is assuming camera has no roll, and plane is aligned to ground!
		_camera.Rotation = Rotation.LookAt( mainCamera.WorldRotation.Forward * new Vector3( 1f, 1f, -1f ) );

		Graphics.RenderToTexture( _camera, _renderTexture );

		Renderer.SceneObject.Attributes.Set( "ReflectionTexture", _renderTexture );

		_camera.Attributes.Set( "_ClipNormal", plane.Normal * MathF.Sign( cameraToPlane ) );
		_camera.Attributes.Set( "_ClipDist", Vector3.Dot( WorldPosition, plane.Normal ) );
	}
}
