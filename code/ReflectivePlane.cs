
using System;
using Sandbox.Rendering;

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

	protected override void OnPreRender()
	{
		if ( Scene.Camera is not { } mainCamera ) return;

		var targetSize = mainCamera.ScreenRect.Size;

		if ( _renderTexture?.Size.AlmostEqual( targetSize ) is false )
		{
			_renderTexture.Dispose();
			_renderTexture = null;
		}

		_renderTexture ??= Texture.CreateRenderTarget()
			.WithScreenFormat()
			.WithSize( mainCamera.ScreenRect.Size )
			.Create( "Reflection" );

		var plane = new Plane( WorldPosition, WorldRotation.Up );
		var cameraPosition = mainCamera.WorldPosition;
		var cameraRotation = mainCamera.WorldRotation;

		var viewMatrix = Matrix.CreateWorld( cameraPosition, cameraRotation.Forward, cameraRotation.Up );
		var reflectMatrix = ReflectMatrix( viewMatrix, plane );

		var reflectionPosition = reflectMatrix.Transform( cameraPosition );
		var reflectionRotation = ReflectRotation( cameraRotation, plane.Normal );

		mainCamera.UpdateSceneCamera( _camera );

		_camera!.ExcludeTags.Add( "water" );

		_camera.Position = reflectionPosition;
		_camera.Rotation = reflectionRotation;

		var clipNormal = plane.Normal * MathF.Sign( plane.GetDistance( mainCamera.WorldPosition ) );

		_camera.Attributes.Set( "_ClipNormal", clipNormal );
		_camera.Attributes.Set( "_ClipDist", Vector3.Dot( WorldPosition, clipNormal ) );

		Graphics.RenderToTexture( _camera, _renderTexture );

		Renderer.SceneObject.Attributes.Set( "ReflectionTexture", _renderTexture );

		WorldPosition = mainCamera.WorldPosition.SnapToGrid( 256f ).WithZ( WorldPosition.z );
	}

	private static Matrix ReflectMatrix( System.Numerics.Matrix4x4 m, Plane plane )
	{
		m.M11 = (1.0f - 2.0f * plane.Normal.x * plane.Normal.x);
		m.M21 = (-2.0f * plane.Normal.x * plane.Normal.y);
		m.M31 = (-2.0f * plane.Normal.x * plane.Normal.z);
		m.M41 = (-2.0f * -plane.Distance * plane.Normal.x);

		m.M12 = (-2.0f * plane.Normal.y * plane.Normal.x);
		m.M22 = (1.0f - 2.0f * plane.Normal.y * plane.Normal.y);
		m.M32 = (-2.0f * plane.Normal.y * plane.Normal.z);
		m.M42 = (-2.0f * -plane.Distance * plane.Normal.y);

		m.M13 = (-2.0f * plane.Normal.z * plane.Normal.x);
		m.M23 = (-2.0f * plane.Normal.z * plane.Normal.y);
		m.M33 = (1.0f - 2.0f * plane.Normal.z * plane.Normal.z);
		m.M43 = (-2.0f * -plane.Distance * plane.Normal.z);

		m.M14 = 0.0f;
		m.M24 = 0.0f;
		m.M34 = 0.0f;
		m.M44 = 1.0f;

		return m;
	}

	private static Rotation ReflectRotation( Rotation source, Vector3 normal )
	{
		return Rotation.LookAt( Vector3.Reflect( source * Vector3.Forward, normal ), Vector3.Reflect( source * Vector3.Up, normal ) );
	}

}
