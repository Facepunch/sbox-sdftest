using System;

namespace SdfWorld;

public sealed class UnderwaterPostProcessing : PostProcess, Component.ExecuteInEditor
{
	private IDisposable? _renderHook;
	private readonly RenderAttributes _attributes = new();

	protected override void OnEnabled()
	{
		_renderHook = Camera.AddHookBeforeOverlay( "Underwater Post Processing", 1000, RenderEffect );
	}

	protected override void OnDisabled()
	{
		_renderHook?.Dispose();
		_renderHook = null;
	}

	public void RenderEffect( SceneCamera camera )
	{
		if ( !camera.EnablePostProcessing )
			return;

		if ( Scene.GetAllComponents<ReflectivePlane>().FirstOrDefault() is not { } ocean )
			return;

		// Pass the Color property to the shader
		_attributes.Set( "SurfaceHeight", ocean.WorldPosition.z );

		// Pass the FrameBuffer to the shader
		Graphics.GrabFrameTexture( "ColorBuffer", _attributes );
		Graphics.GrabDepthTexture( "DepthBuffer", _attributes );

		// Blit a quad across the entire screen with our custom shader
		Graphics.Blit( Material.FromShader( "shaders/underwater.shader" ), _attributes );
	}
}
