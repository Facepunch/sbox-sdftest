using Sandbox;
using Sandbox.Worlds;

public sealed class DebugCellLoader : Component, ICellLoader
{
	[Property]
	public Model PlaneModel { get; set; }

	[Property]
	public Material Material { get; set; }

	public void LoadCell( WorldCell cell )
	{
		var size = cell.World.CellSize;

		var obj = new GameObject( true )
		{
			Parent = cell.GameObject,
			Transform =
			{
				LocalPosition = new Vector3( size, size, 0f ) * 0.5f - Vector3.Up * (16 << cell.World.Level),
				LocalScale = size / 100f
			}
		};

		var renderer = obj.Components.Create<ModelRenderer>();

		cell.OpacityChanged += OnOpacityChanged;

		renderer.Model = PlaneModel;
		renderer.MaterialOverride = Material;
		renderer.Tint = new ColorHsv( cell.World.Level * 30f, 1f, 1f, 0f );
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
