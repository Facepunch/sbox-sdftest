using System;
using System.Threading.Tasks;
using Sandbox.Sdf;
using Sandbox.Utility;
using Sandbox.Worlds;

public sealed class CellProcGen : Component, Component.ExecuteInEditor
{
	[RequireComponent]
	public Sdf3DWorld SdfWorld { get; private set; }

	[Property]
	public Sdf3DVolume Ground { get; set; }

	[Property]
	public Curve TerrainBias { get; set; }

	[Property]
	public Curve PlainsCurve { get; set; }

	[Property]
	public Curve MountainsCurve { get; set; }

	protected override void OnStart()
	{
		_ = GenerateAsync();
	}

	private async Task GenerateAsync()
	{
		var margin = 1;
		var res = 32 + margin * 1;
		var size = Components.GetInAncestorsOrSelf<WorldCell>()?.World.CellSize ?? 1024f;

		SdfWorld.Size = new Vector3Int( (int) size, (int) size, 2048 );

		//await Task.WorkerThread();

		var heightmap = new float[res * res];

		for ( var y = 0; y < res; ++y )
		for ( var x = 0; x < res; ++x )
		{
			var worldPos = Transform.World.PointToWorld( new Vector3( x - margin, y - margin, 0f ) * size / (res - margin * 2) );

			var terrain = Noise.Fbm( 4, worldPos.x / 256f, worldPos.y / 256f );
			var height = Noise.Fbm( 8, worldPos.x / 64f, worldPos.y / 64f );

			terrain = TerrainBias.Evaluate( terrain );

			var plainsHeight = PlainsCurve.Evaluate( height );
			var mountainsHeight = MountainsCurve.Evaluate( height );

			height = plainsHeight + terrain * (mountainsHeight - plainsHeight);

			heightmap[x + y * res] = height;
		}

		//await Task.MainThread();
		await SdfWorld.AddAsync( new HeightmapSdf3D( heightmap, res, margin, size ), Ground );
	}
}
