using Sandbox;
using Sandbox.Worlds;

public sealed class DebugCellLoader : Component, ICellLoader
{
	[Property]
	public Model PlaneModel { get; set; } = null!;

	[Property]
	public Material Material { get; set; } = null!;

	public void LoadCell( WorldCell cell )
	{
		var size = cell.Size;

		var obj = new GameObject( true )
		{
			Parent = cell.GameObject,
			LocalPosition = size * 0.5f - Vector3.Up * size.x / 256f,
			LocalScale = size / 100f
		};

		var renderer = obj.Components.Create<ModelRenderer>();

		cell.OpacityChanged += OnOpacityChanged;

		renderer.Model = PlaneModel;
		renderer.MaterialOverride = Material;
		renderer.Tint = new ColorHsv( cell.Index.Level * 30f, 1f, 1f, 0f );
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	private void OnOpacityChanged( WorldCell cell, float opacity )
	{
		var renderer = cell.Components.Get<ModelRenderer>( FindMode.EnabledInSelfAndDescendants );

		renderer.Tint = renderer.Tint.WithAlpha( opacity );
	}

	public void UnloadCell( WorldCell cell )
	{

	}
}
