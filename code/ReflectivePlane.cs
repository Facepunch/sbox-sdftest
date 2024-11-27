using System;

using Matrix4x4 = System.Numerics.Matrix4x4;

namespace SdfWorld;

public sealed class ReflectivePlane : Component
{
	[RequireComponent] public ModelRenderer Renderer { get; private set; } = null!;

	private CameraComponent? _reflectionCamera;
	private UnderwaterPostProcessing? _reflectionPostProcessing;
	private Texture? _renderTexture;

	protected override void OnEnabled()
	{
		if ( _reflectionCamera.IsValid() ) return;

		var cameraObj = new GameObject( false, "Reflection Camera" );

		_reflectionCamera = cameraObj.AddComponent<CameraComponent>();
		_reflectionCamera.RenderExcludeTags.Add( "water" );
		_reflectionCamera.IsMainCamera = false;

		_reflectionPostProcessing = cameraObj.AddComponent<UnderwaterPostProcessing>();
	}

	protected override void OnDisabled()
	{
		CleanUp();
	}

	protected override void OnDestroy()
	{
		CleanUp();
	}

	private void CleanUp()
	{
		_reflectionCamera?.DestroyGameObject();
		_reflectionCamera = null;

		_renderTexture?.Dispose();
		_renderTexture = null;
	}

	protected override void OnUpdate()
	{
		if ( _reflectionCamera is { Active: true } )
		{
			// For some reason I can't set this during / before the first OnPreRender
			_reflectionCamera.Priority = -100;
		}
	}

	protected override void OnPreRender()
	{
		if ( Scene.Camera is not { } mainCamera ) return;

		var targetSize = mainCamera.ScreenRect.Size;

		if ( _renderTexture is null || !_renderTexture.Size.AlmostEqual( targetSize ) )
		{
			_renderTexture?.Dispose();
			_renderTexture = Texture.CreateRenderTarget()
				.WithScreenFormat()
				.WithSize( mainCamera.ScreenRect.Size )
				.Create( "Reflection" );
			
			_reflectionCamera!.RenderTarget = _renderTexture;
			_reflectionCamera.GameObject.Enabled = true;

			Renderer.SceneObject.Attributes.Set( "ReflectionTexture", _renderTexture );
		}

		var plane = new Plane( WorldPosition, WorldRotation.Up );
		var cameraPosition = mainCamera.WorldPosition;
		var cameraRotation = mainCamera.WorldRotation;

		var viewMatrix = Matrix.CreateWorld( cameraPosition, cameraRotation.Forward, cameraRotation.Up );
		var reflectMatrix = ReflectMatrix( viewMatrix, plane );

		var reflectionPosition = reflectMatrix.Transform( cameraPosition );
		var reflectionRotation = ReflectRotation( cameraRotation, plane.Normal );

		_reflectionCamera!.WorldPosition = reflectionPosition;
		_reflectionCamera.WorldRotation = reflectionRotation;
		_reflectionCamera.BackgroundColor = mainCamera.BackgroundColor;
		_reflectionCamera.ZNear = mainCamera.ZNear;
		_reflectionCamera.ZFar = mainCamera.ZFar;
		_reflectionCamera.FieldOfView = mainCamera.FieldOfView;

		_reflectionPostProcessing!.Enabled = mainCamera.WorldPosition.z <= WorldPosition.z;

		_reflectionCamera.CustomSize = Screen.Size;
		_reflectionCamera.CustomProjectionMatrix = _reflectionCamera.CalculateObliqueMatrix( plane );

		WorldPosition = mainCamera.WorldPosition.SnapToGrid( 256f ).WithZ( WorldPosition.z );
	}

	private static Matrix ReflectMatrix( Matrix4x4 m, Plane plane )
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
