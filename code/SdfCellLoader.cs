using System.Threading.Tasks;
using Sandbox.Sdf;
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
		sdfWorld.Size = new Vector3Int( sdfSize, sdfSize, 2048 >> level );
		sdfWorld.HasPhysics = level == 0;

		sdfObj.Enabled = true;

		_ = GenerateAsync( cell, sdfWorld );
	}

	private async Task GenerateAsync( WorldCell cell, Sdf3DWorld sdfWorld )
	{
		if ( Parameters is null ) return;

		await Task.WorkerThread();

		var voxelRes = (int)(sdfWorld.Size.x * Parameters.Ground.ChunkResolution / Parameters.Ground.ChunkSize);
		var res = voxelRes;

		var heightmap = new float[res * res];

		Parameters.SampleHeightmap( Seed.FastHash(), res, cell.World.CellSize, cell.Transform.World, heightmap, cell.World.Level );

		await Task.MainThread();
		await sdfWorld.AddAsync( new HeightmapSdf3D( heightmap, res, sdfWorld.Size.x ), Parameters.Ground );
	}

	void ICellLoader.UnloadCell( WorldCell cell )
	{

	}
}
