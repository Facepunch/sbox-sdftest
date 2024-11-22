using System;

using Matrix4x4 = System.Numerics.Matrix4x4;

namespace SdfWorld;

public sealed class ReflectivePlane : Component
{
	[RequireComponent] public ModelRenderer Renderer { get; private set; } = null!;

	private CameraComponent? _reflectionCamera;
	private Texture? _renderTexture;

	protected override void OnEnabled()
	{
		if ( _reflectionCamera.IsValid() ) return;

		var cameraObj = new GameObject( false, "Reflection Camera" );

		_reflectionCamera = cameraObj.AddComponent<CameraComponent>( true );
		_reflectionCamera.RenderExcludeTags.Add( "water" );
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

		var projectionMatrix = CreateProjection( _reflectionCamera );
		var cameraSpaceClipNormal = _reflectionCamera.WorldRotation.Inverse * WorldRotation.Up;

		// Swizzle so +x is right, +z is forward etc
		cameraSpaceClipNormal = new Vector3(
			cameraSpaceClipNormal.y,
			-cameraSpaceClipNormal.z,
			cameraSpaceClipNormal.x ).Normal;

		projectionMatrix = ModifyProjectionMatrix( projectionMatrix,
			new Vector4( cameraSpaceClipNormal, Vector3.Dot( reflectionPosition - WorldPosition, WorldRotation.Up ) ) );

		_reflectionCamera.CustomProjectionMatrix = projectionMatrix;

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

	private static Matrix CreateProjection( CameraComponent camera )
	{
		var tanAngleHorz = MathF.Tan( camera.FieldOfView * 0.5f * MathF.PI / 180f );
		var tanAngleVert = tanAngleHorz * camera.ScreenRect.Height / camera.ScreenRect.Width;

		return CreateProjection( tanAngleHorz, tanAngleVert, camera.ZNear, camera.ZFar );
	}
	private static float Dot( Vector4 a, Vector4 b )
	{
		return System.Numerics.Vector4.Dot( a, b );
	}

	private static Matrix CreateProjection( float tanAngleHorz, float tanAngleVert, float nearZ, float farZ )
	{
		var invReverseDepth = 1f / (nearZ - farZ);

		var result = new Matrix4x4(
			1f / tanAngleHorz, 0f, 0f, 0f,
			0f, 1f / tanAngleVert, 0f, 0f,
			0f, 0f, farZ * invReverseDepth, farZ * nearZ * invReverseDepth,
			0f, 0f, -1f, 0f
		);

		return result;
	}

	/// <summary>
	/// Pinched from <see href="https://terathon.com/blog/oblique-clipping.html">here</see>
	/// and <see href="https://forum.beyond3d.com/threads/oblique-near-plane-clipping-reversed-depth-buffer.52827/">here</see>.
	/// </summary>
	private static Matrix ModifyProjectionMatrix( Matrix matrix, Vector4 clipPlane )
	{
		Matrix4x4 m = matrix;

		// Calculate the clip-space corner point opposite the clipping plane
		// as (sgn(clipPlane.x), sgn(clipPlane.y), 1, 1) and
		// transform it into camera space by multiplying it
		// by the inverse of the projection matrix

		Vector4 q = default;

		q.x = (MathF.Sign( clipPlane.x ) - m.M13) / m.M11;
		q.y = (MathF.Sign( clipPlane.y ) - m.M23) / m.M22;
		q.z = 1f;
		q.w = (1f - m.M33) / m.M34;

		// Calculate the scaled plane vector
		var c = clipPlane * (1f / Dot( clipPlane, q ));

		// Replace the third row of the projection matrix
		m.M31 = -c.x;
		m.M32 = -c.y;
		m.M33 = -c.z;
		m.M34 = c.w;

		return m;
	}

	private static Rotation ReflectRotation( Rotation source, Vector3 normal )
	{
		return Rotation.LookAt( Vector3.Reflect( source * Vector3.Forward, normal ), Vector3.Reflect( source * Vector3.Up, normal ) );
	}

}
