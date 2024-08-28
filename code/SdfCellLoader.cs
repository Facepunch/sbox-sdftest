using System;
using System.Threading.Tasks;
using Sandbox.Sdf;
using Sandbox.Utility;
using Sandbox.Worlds;

namespace Sandbox;

public sealed class SdfCellLoader : Component, ICellLoader
{
	[Property]
	public WorldParameters Parameters { get; set; }

	[Property]
	public string Seed { get; set; }

	void ICellLoader.LoadCell( WorldCell cell )
	{
		var level = cell.World.Level;
		var sdfObj = new GameObject( false )
		{
			Parent = cell.GameObject,
			Transform = { Local = new Transform( 0f, Rotation.Identity, 1 << level ) }
		};

		var sdfWorld = sdfObj.Components.Create<Sdf3DWorld>();
		var sdfSize = (int)cell.World.CellSize >> level;

		sdfWorld.IsFinite = true;
		sdfWorld.Size = new Vector3Int( sdfSize, sdfSize, (int)cell.World.CellHeight >> level );
		sdfWorld.HasPhysics = level == 0;

		sdfObj.Enabled = true;

		cell.MarkLoading();

		_ = GenerateAsync( cell, sdfWorld );
	}

	private async Task GenerateAsync( WorldCell cell, Sdf3DWorld sdfWorld )
	{
		if ( Parameters is null ) return;

		var cellSize = cell.World.CellSize;
		var level = cell.World.Level;

		await Task.WorkerThread();

		var voxelRes = (int)(sdfWorld.Size.x * Parameters.Ground.ChunkResolution / Parameters.Ground.ChunkSize);
		var res = voxelRes;

		var heightmap = new float[res * res];

		Parameters.SampleHeightmap( Seed.FastHash(), res, cellSize, cell.Transform.World, heightmap, level );

		var caveNoise = Noise.SimplexField( new Noise.FractalParameters(
			Frequency: 1f / 2048f ) );
		var caveSdf = new NoiseSdf3D( caveNoise, 0.65f, 256f / sdfWorld.Transform.Scale.x )
			.Transform( new Transform( -cell.Transform.Position / sdfWorld.Transform.Scale.x, Rotation.Identity,
				1f / sdfWorld.Transform.Scale.x ) );

		await Task.MainThread();
		await sdfWorld.AddAsync( new HeightmapSdf3D( heightmap, res, sdfWorld.Size.x ).Intersection( caveSdf ), Parameters.Ground );

		while ( sdfWorld.NeedsMeshUpdate )
		{
			await Task.DelayRealtime( 1 );
		}

		cell.MarkReady();
	}

	void ICellLoader.UnloadCell( WorldCell cell )
	{

	}
}
